using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections.Concurrent;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private async Task<MilestoneRunResult> BuildHqDecisionReportAsync(
        string jobId, string workingDirectory, MilestoneDefinition milestone,
        string reasonCode, string detail, IReadOnlySet<string> initialChangedPaths,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? mechanicalReports = null,
        IReadOnlyList<string>? changedPaths = null)
    {
        var current = await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
            workingDirectory, cancellationToken);
        var diagnostics = (mechanicalReports ?? Array.Empty<string>())
            .Concat(new[] { "RECOVERY_DECISION: " + reasonCode + " · " + detail })
            .ToArray();
        var report = MilestoneDefinitionContract.BuildHqReport(
            milestone, "HQ_DECISION_REQUIRED:" + reasonCode,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            diagnostics, string.Empty,
            MilestoneGitResult.NotStarted(milestone.TargetBranch),
            initialChangedPaths,
            changedPaths ?? Array.Empty<string>(),
            current,
            _formatRecoveryJobs.ContainsKey(jobId),
            GetUnreadRecoveryElements(jobId),
            workingDirectory, jobId);
        SaveMilestoneExecutionGraph(workingDirectory, jobId, milestone,
            "HQ_DECISION_REQUIRED", hqFinalState: "READY");
        AddTaskMessage("HQ RECOVERY DECISION",
            reasonCode + ": " + detail, status: "FORWARDED");
        return new(false, report + Environment.NewLine +
            "RECOVERY_DECISION_REQUIRED: Worker가 가능한 직접 복구를 먼저 시도했다. " +
            "미완료 결과를 완료로 간주하지 마라. HQ는 대체 구현/진단 마일스톤을 " +
            "선택하거나, 사용자 판단 없이는 진행할 수 없을 때만 PAUSE를 선택하라. " +
            "동일한 실패 작업을 아무 변경 없이 재실행하지 마라.");
    }

    /// <summary>
    /// First attempt a native Game View capture with no Windows foreground
    /// activation. Native capture is only file evidence until inspected.
    /// An inactive Editor that cannot render is not forcibly activated.
    /// </summary>
    private async Task<UnityBackgroundVisualEvidence> CaptureBestAvailableVisualAsync(
        UnityBackgroundVisualPlan plan, UnityBackgroundFocusGuard guard,
        CancellationToken cancellationToken)
    {
        // Defer accidental self-activation recovery until the screenshot file
        // is complete or the attempt fails. This lease NEVER focuses Unity.
        var captureScope = guard.BeginBackgroundCapture();
        UnityBackgroundVisualEvidence native;
        try
        {
            native = await UnityBackgroundVisualTools.CaptureNativeAsync(
                plan, cancellationToken);
        }
        finally
        {
            captureScope.Dispose();
            AddTaskMessage("UNITY NATIVE FOCUS",
                captureScope.Diagnostic, status: "OBSERVED",
                includeHistory: false);
        }

        AddTaskMessage("UNITY NATIVE GAME VIEW",
            native.Detail, status: native.Captured ? "OBSERVED" : "UNVERIFIED",
            includeHistory: false);
        if (native.Captured)
            return native;

        // The offscreen camera is supplementary, not equivalent to native
        // Game View or pointer-input evidence. Never activate a GUI as fallback.
        var offscreen = await UnityBackgroundVisualTools.CaptureAsync(
            plan, cancellationToken);
        return offscreen with
        {
            Detail = "NATIVE_UNVERIFIED=" + native.Detail +
                     "; OFFSCREEN_SUPPLEMENT_ONLY=" + offscreen.Detail
        };
    }

    private async Task<MilestoneRunResult> RunSingleMilestoneAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        WorkerAiRoleSettings implementer,
        WorkerAiRoleSettings high,
        WorkerAiRoleSettings designRole,
        Func<CancellationToken, Task<bool>> recheckUnityEditorAsync,
        UnityBackgroundFocusGuard unityFocusGuard,
        IReadOnlySet<string> initialChangedPaths,
        CancellationToken cancellationToken)
    {
        var milestoneStartedAt = DateTimeOffset.UtcNow;
        AddDataFlowHistory(
            WorkerRoleState.Unknown,
            "Worker 작업",
            $"마일스톤 사전 점검\nID: {milestone.Id}\nBRANCH: {milestone.TargetBranch}\nPOLICY: {(milestone.ReadOnlyNoFileChanges ? "READ_ONLY_NO_FILE_CHANGES" : "DEFAULT")}",
            status: "PROCESSING",
            persistenceSource: "WORKER ACTION");

        var milestoneChangedPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        // RESOURCE image extraction is disabled in UnityWorker. DESIGN #0
        // instead executes as an ordered, local, source-safe stage below.
        var resourceTasks = Array.Empty<(string Id, Task<ResourceExecutionReport> Task)>();

        // RESOURCE is an independent sidecar: never gate WORK, HIGH or Git
        // on generation, failure, artifact validation or transport completion.
        var gitPreflight = await MilestoneMechanicalExecutor.CheckGitReadyAsync(
            workingDirectory,
            milestone.TargetBranch,
            milestone.InitializeGitIfMissing,
            _targetSettings.ManualRepositoryUrl?.Trim(),
            cancellationToken);
        if (!gitPreflight.Success)
        {
            // Preflight is mechanical. Do not dispatch HIGH with write rights
            // before a valid Git workspace exists.
            return await BuildHqDecisionReportAsync(jobId,
                workingDirectory, milestone, "GIT_PREFLIGHT_FAILED",
                gitPreflight.Summary, initialChangedPaths, cancellationToken);
        }

        WorkerPaths.EnsureProjectHubLocalExclude(workingDirectory);
        // On a new job the Unity ignore template is applied before HQ; include
        // this repo policy change in Worker Git finalization, even though the
        // initial dirty snapshot naturally already includes it.
        var unityIgnorePath = Path.Combine(workingDirectory, ".gitignore");
        if (File.Exists(unityIgnorePath) &&
            File.ReadAllText(unityIgnorePath).Contains(
                "# ProjectHub Unity generated files", StringComparison.Ordinal))
            milestoneChangedPaths.Add(".gitignore");

        var workReports = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var resourceReports = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var mechanicalReports = new List<string>();
        var milestoneEditorReady = await recheckUnityEditorAsync(cancellationToken);
        mechanicalReports.Add("UNITY_EDITOR_MILESTONE_RECHECK: " +
            (milestoneEditorReady ? "CLI_CONNECTED" : "CLI_UNVERIFIED") +
            "; individual commands and Native image remain separately unverified.");
        var highReport = string.Empty;
        var gitResult = MilestoneGitResult.NotStarted(milestone.TargetBranch);
        var outcome = "COMPLETED";

        SaveMilestoneExecutionGraph(
            workingDirectory,
            jobId,
            milestone,
            "RUNNING",
            workReports,
            resourceReports,
            highReport);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The initial design is a separate Worker-owned commit.
            if (!string.IsNullOrWhiteSpace(milestone.PlanDocument))
            {
                const string planPath = "docs/projecthub/initial-plan.md";
                var fullPlanPath = Path.Combine(
                    workingDirectory, "docs", "projecthub", "initial-plan.md");
                if (!File.Exists(fullPlanPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPlanPath)!);
                    await File.WriteAllTextAsync(fullPlanPath,
                        "# Project design" + Environment.NewLine +
                        milestone.PlanDocument.Trim() + Environment.NewLine,
                        cancellationToken);
                    var planGit = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
                        workingDirectory, "ProjectHub initial project design",
                        new[] { planPath }, _targetSettings.ManualRepositoryUrl?.Trim(),
                        cancellationToken);
                    if (!planGit.Success)
                    {
                        var diagnosis = await ExecuteGitHighAsync(
                            jobId, workingDirectory, milestone, high, "INITIAL_PLAN",
                            planGit.Summary, cancellationToken);
                        planGit = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
                            workingDirectory, "ProjectHub initial project design",
                            new[] { planPath }, _targetSettings.ManualRepositoryUrl?.Trim(),
                            cancellationToken);
                        if (!planGit.Success)
                            return await BuildHqDecisionReportAsync(jobId,
                                workingDirectory, milestone, "INITIAL_PLAN_GIT_FAILED",
                                planGit.Summary + Environment.NewLine + "HIGH_GIT: " +
                                MilestoneDefinitionContract.Limit(diagnosis, 1200),
                                initialChangedPaths, cancellationToken);
                    }
                }
            }

            // Asset Manager errors are reported to HQ/HIGH, but independent
            // WORKs still run; only simultaneous duplicate file writes conflict.
            var assetManagerFailed = false;
            if (milestone.AssetManager is { } assetDefinition)
            {
                var preexistingAssetChanges = initialChangedPaths
                    .Where(path => AssetManagerPathPolicy.IsDeclaredScope(
                        path, assetDefinition, "INTEGRATE"))
                    .ToArray();
                if (preexistingAssetChanges.Length > 0)
                {
                    var sceneDirty = preexistingAssetChanges.Where(path =>
                        path.Equals(assetDefinition.ScenePath,
                            StringComparison.OrdinalIgnoreCase) ||
                        path.Equals(assetDefinition.ScenePath + ".meta",
                            StringComparison.OrdinalIgnoreCase)).ToArray();
                    var reusable = AssetManagerFileEvidence.Inspect(
                        workingDirectory, assetDefinition.TargetPath,
                        assetDefinition.SourcePolicy == "CC0_AUTO",
                        requireModel: assetDefinition.SourcePolicy != "PROJECT_ONLY");
                    // Previously dirty files are valid project state, not
                    // proof of concurrent duplicate file writers.
                    AddTaskMessage("ASSET MANAGER",
                        "기존 프로젝트 변경 " + preexistingAssetChanges.Length +
                        "개 보존 · sceneDirty=" + sceneDirty.Length +
                        " · " + reusable.Detail, status: "RECOVERED");
                }
                var assetRun = await ExecuteMilestoneAssetManagerAsync(
                    jobId, workingDirectory, milestone, assetDefinition,
                    implementer, cancellationToken);
                mechanicalReports.Add("ASSET_MANAGER #1: " +
                    MilestoneDefinitionContract.Limit(assetRun.Detail, 4000));
                foreach (var path in assetRun.ChangedPaths)
                    milestoneChangedPaths.Add(path);
                if (!assetRun.Success)
                {
                    assetManagerFailed = true;
                    AddTaskMessage("ASSET MANAGER",
                        "미완료 증거를 HQ/HIGH에 전달하고 독립 WORK는 계속 실행 · " +
                        MilestoneDefinitionContract.Limit(assetRun.Detail, 600),
                        status: "BLOCKED");
                }
            }


            if (milestone.Design is { } design)
            {
                RunOnUi(() =>
                {
                    TaskDirection.Text = "Design";
                    TaskTitle.Text = "#0 DESIGN · 시각적 아트 디렉션";
                    ResultTitle.Text = "#0 DESIGN";
                    SetFlowState(codexActive: true, workerActive: true,
                        webActive: false, explicitStage: TaskStage.Resource);
                });
                var designReference = milestone.Id + ":DESIGN:0";
                AddDataFlowHistory(WorkerRoleState.Design, "#0 DESIGN",
                    "Design 문서 및 실제 시각 증거 검토: " + design.Path,
                    status: "DISPATCHED", workItemId: "0",
                    referenceId: designReference,
                    persistenceSource: "WORKER DESIGN");
                var beforeDesign =
                    await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        workingDirectory, cancellationToken);
                string designReport;
                UnityBackgroundVisualPlan? visualPlan = null;
                try
                {
                    visualPlan = UnityBackgroundVisualTools.Prepare(
                        workingDirectory, jobId, milestone.Id + "-initial");
                }
                catch (Exception visualToolError) when (visualToolError is IOException or
                    UnauthorizedAccessException or ArgumentException)
                {
                    mechanicalReports.Add("INTERNAL_VISUAL_TOOLS_UNAVAILABLE: " +
                        visualToolError.Message);
                }
                try
                {
                    using var designEditorLease = await UnityEditorOperationGate.EnterAsync(
                        workingDirectory, cancellationToken);
                    var run = await RunCoordinatorRoleAsync(
                        jobId, "DESIGN",
                        RoleContractLoader.BuildDesignPrompt(design, workingDirectory) +
                        (visualPlan is null ? string.Empty :
                            Environment.NewLine + visualPlan.AgentGuidance),
                        designRole with { ThreadSessionId = null, ThreadProjectPath = null },
                        workingDirectory, null, null, cancellationToken,
                        CodexSandboxMode.WorkspaceWrite,
                        historyWorkItemId: "0",
                        historyReferenceId: designReference);
                    var parsed = RoleTextProtocol.ParseWork(run.FinalMessage);
                    var afterProduction =
                        await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                            workingDirectory, cancellationToken);
                    var producedPaths = MilestoneMechanicalExecutor.DiffChangeStates(
                        beforeDesign, afterProduction).ToArray();
                    var actualGraphics = producedPaths.Any(UnityDesignPromptPolicy.IsVisualArtifact);
                    var verified = run.ExitCode == 0 && parsed.IsValid &&
                        parsed.Status == "completed" && actualGraphics;
                    // Worker obtains its own evidence after the agent; this
                    // must not overwrite screenshots captured during DESIGN.
                    UnityBackgroundVisualEvidence offscreen;
                    if (visualPlan is null)
                        offscreen = new(false,
                            "OFFSCREEN_CAPTURE_UNVERIFIED: tool preparation unavailable", null);
                    else if (await recheckUnityEditorAsync(cancellationToken))
                        offscreen = await CaptureBestAvailableVisualAsync(
                            visualPlan, unityFocusGuard, cancellationToken);
                    else
                        offscreen = new(false,
                            "OFFSCREEN_CAPTURE_UNVERIFIED: latest Editor CLI probe not ready",
                            null);
                    designReport = (verified ? "DESIGN_STATUS: COMPLETED" :
                        "DESIGN_STATUS: BLOCKED_OR_IN_PROGRESS") +
                        Environment.NewLine +
                        MilestoneDefinitionContract.Limit(run.FinalMessage, 1600) +
                        Environment.NewLine +
                        "ACTUAL_GRAPHIC_CHANGES: " +
                        string.Join(", ", producedPaths.Where(UnityDesignPromptPolicy.IsVisualArtifact)) +
                        Environment.NewLine +
                        "VISUAL_E2E: UNVERIFIED_UNLESS_INSPECTED" +
                        Environment.NewLine + "INTERNAL_VISUAL_CAPTURE: " +
                        offscreen.Detail;
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    designReport = "DESIGN_STATUS: BLOCKED" +
                        Environment.NewLine + ex.GetType().Name + ": " + ex.Message;
                }
                var afterDesign =
                    await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        workingDirectory, cancellationToken);
                foreach (var changedPath in
                    MilestoneMechanicalExecutor.DiffChangeStates(
                        beforeDesign, afterDesign))
                    milestoneChangedPaths.Add(changedPath);
                mechanicalReports.Add("DESIGN #0: " + designReport);
                AddRoleResponseHistory(WorkerRoleState.Design,
                    "#0 DESIGN 결과", designReport,
                    status: designReport.StartsWith("DESIGN_STATUS: COMPLETED",
                        StringComparison.Ordinal) ? "COMPLETED" : "BLOCKED",
                    workItemId: "0", referenceId: designReference,
                    providerWireId: designRole.Provider);
                // The DESIGN turn is terminal even if this milestone continues
                // to WORK/HIGH. Deactivate its pipeline card immediately.
                RunOnUi(() => SetFlowState(codexActive: false, workerActive: false,
                    webActive: false, explicitStage: TaskStage.Idle));
            }

            void CaptureResourceState()
            {
                foreach (var execution in resourceTasks)
                {
                    if (execution.Task.IsCompletedSuccessfully)
                    {
                        var result = execution.Task.Result;
                        resourceReports[result.Id] = result.Report;
                        foreach (var changedPath in result.ChangedPaths)
                            milestoneChangedPaths.Add(changedPath);
                    }
                    else if (execution.Task.IsCompleted)
                    {
                        resourceReports[execution.Id] =
                            "RESOURCE_STATUS: BLOCKED" +
                            Environment.NewLine +
                            "RESOURCE_BACKGROUND_EXECUTION_FAILED";
                    }
                    else
                    {
                        resourceReports[execution.Id] =
                            "RESOURCE_STATUS: PENDING" +
                            Environment.NewLine +
                            "RESOURCE sidecar still running independently";
                    }
                }
            }

            var definitions = milestone.WorkItems.Values
                .OrderBy(work =>
                    int.TryParse(work.Id, out var number)
                        ? number
                        : int.MaxValue)
                .ToArray();

            var overlappingWorkIds =
                MilestoneMechanicalExecutor.FindOverlappingWorkItemIds(
                    definitions
                        .Where(work => !work.ReadOnly)
                        .Select(work => (
                            work.Id,
                            (IReadOnlyList<string>)work.WritePaths)));

            foreach (var workId in overlappingWorkIds)
            {
                workReports[workId] =
                    MilestoneDefinitionContract.BuildRoleResult(
                        null,
                        "blocked",
                        "WORK_WRITE_PATH_OVERLAP",
                        issues: new[]
                        {
                            "conflictingWorkItems=" +
                            string.Join(
                                ",",
                                overlappingWorkIds.OrderBy(
                                    id => int.TryParse(id, out var number)
                                        ? number
                                        : int.MaxValue))
                        });
            }

            var executableDefinitions = definitions
                .Where(work => !overlappingWorkIds.Contains(work.Id))
                .ToArray();
            var executableWorkIds = executableDefinitions
                .Select(work => work.Id)
                .ToArray();

            foreach (var workId in executableWorkIds)
            {
                AddDataFlowHistory(
                    WorkerRoleState.Work,
                    "Worker 분배",
                    $"WORK_ITEM_ID: {workId}",
                    status: "DISPATCHED",
                    referenceId: milestone.Id + ":" + workId,
                    workItemId: workId,
                    persistenceSource: "WORKER DISPATCH");
            }

            if (executableWorkIds.Length > 0)
            {
                var allRunnableScopes = executableDefinitions
                    .SelectMany(work => work.WritePaths)
                    .ToArray();
                var maxConcurrency = UnityWorkSchedulingPolicy.MaxConcurrentWork(
                    _targetSettings.EffectiveMaxConcurrentWork,
                    executableWorkIds.Length);
                var editorLaneCount = executableDefinitions.Count(
                    UnityWorkSchedulingPolicy.RequiresEditorLane);

                AddDataFlowHistory(
                    WorkerRoleState.Unknown,
                    "Worker 작업",
                    $"Unity WORK 병렬 실행 시작 · {executableWorkIds.Length}건 · 최대 {maxConcurrency}개 · Editor 독점 {editorLaneCount}건",
                    status: "PROCESSING",
                    persistenceSource: "WORKER ACTION");

                var before =
                    await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        workingDirectory,
                        cancellationToken);
                WorkExecutionReport[] completed;
                try
                {
                    completed = await UnityWorkSchedulingPolicy.RunAsync(
                        executableDefinitions,
                        _targetSettings.EffectiveMaxConcurrentWork,
                        (work, token) => ExecuteMilestoneWorkAsync(
                            jobId,
                            workingDirectory,
                            milestone,
                            work.Id,
                            implementer,
                            token),
                        cancellationToken,
                        activeCount => RunOnUi(() =>
                            ImplementerWorkGaugeText.Text =
                                FormatActiveWorkItemGauge(activeCount)),
                        projectRoot: workingDirectory);
                }
                finally
                {
                    RunOnUi(() =>
                        ImplementerWorkGaugeText.Text =
                            FormatActiveWorkItemGauge(0));
                }

                foreach (var item in completed)
                    workReports[item.Id] = item.Report;

                var after =
                    await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        workingDirectory,
                        cancellationToken);
                foreach (var changedPath in
                         MilestoneMechanicalExecutor.DiffChangeStates(
                             before,
                             after))
                {
                    // All changes made during WORK belong to this milestone.
                    // WRITE_PATH does not filter the eventual Git finalization.
                    milestoneChangedPaths.Add(changedPath);
                }

                AddDataFlowHistory(
                    WorkerRoleState.Unknown,
                    "Worker 작업",
                    $"Unity WORK 병렬 실행 종료 · {completed.Length}/{executableWorkIds.Length} 응답 수집",
                    status: "COMPLETED",
                    persistenceSource: "WORKER ACTION");
            }

            var blockedWorkIds = workReports
                .Where(pair =>
                    milestone.WorkItems.ContainsKey(pair.Key) &&
                    string.Equals(
                        MilestoneDefinitionContract.ReadWorkStatus(pair.Value),
                        "blocked",
                        StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray();
            var pendingWorkIds = workReports
                .Where(pair =>
                    milestone.WorkItems.ContainsKey(pair.Key) &&
                    string.Equals(
                        MilestoneDefinitionContract.ReadWorkStatus(pair.Value),
                        "in_progress",
                        StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray();
            if (blockedWorkIds.Length > 0)
            {
                outcome =
                    "WORK_BLOCKED:" +
                    string.Join(",", blockedWorkIds.OrderBy(
                        id => int.TryParse(id, out var number)
                            ? number
                            : int.MaxValue));
            }

            else if (pendingWorkIds.Length > 0)
            {
                outcome = "WORK_IN_PROGRESS:" + string.Join(",", pendingWorkIds);
            }
            if (assetManagerFailed && outcome == "COMPLETED")
                outcome = "ASSET_MANAGER_BLOCKED";

            CaptureResourceState();

            var plannedWorkScopes = executableDefinitions
                .Where(work => !work.ReadOnly)
                .SelectMany(work => work.WritePaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();


            // Worker owns BUILD; source-fix WORK #8 is attempted only once
            // for an actual compiler/build failure, never for EPERM or timeout.
            var buildRuns = await MilestoneBuildRunner.RunAsync(
                workingDirectory, jobId, executableDefinitions, cancellationToken,
                await recheckUnityEditorAsync(cancellationToken));
            foreach (var build in buildRuns)
                mechanicalReports.Add(
                    MilestoneDefinitionContract.FormatMechanicalResult(build));
            var buildFailed = buildRuns.Any(run => !run.Success);
            if (buildFailed && pendingWorkIds.Length == 0 &&
                !MilestoneBuildRunner.IsEnvironmentFailure(buildRuns))
            {
                var beforeRepair = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    workingDirectory, cancellationToken);
                var repairReport = await ExecuteBuildRepairAsync(
                    jobId, workingDirectory, milestone, plannedWorkScopes,
                    buildRuns.Last(run => !run.Success), implementer, cancellationToken);
                mechanicalReports.Add("BUILD_REPAIR: " +
                    MilestoneDefinitionContract.Limit(repairReport, 1200));
                var afterRepair = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    workingDirectory, cancellationToken);
                foreach (var path in MilestoneMechanicalExecutor.DiffChangeStates(
                    beforeRepair, afterRepair))
                    milestoneChangedPaths.Add(path);
                buildRuns = await MilestoneBuildRunner.RunAsync(
                    workingDirectory, jobId, executableDefinitions, cancellationToken,
                await recheckUnityEditorAsync(cancellationToken));
                foreach (var build in buildRuns)
                    mechanicalReports.Add("REBUILD: " +
                        MilestoneDefinitionContract.FormatMechanicalResult(build));
                buildFailed = buildRuns.Any(run => !run.Success);
            }

            // Preserve original WORK/BUILD failures instead of hiding them during HIGH.
            if (blockedWorkIds.Length == 0 && pendingWorkIds.Length == 0 && buildFailed)
                outcome = UnityCliBuildRunner.IsUnityProject(workingDirectory) &&
                    MilestoneBuildRunner.IsEnvironmentFailure(buildRuns)
                    ? "BUILD_UNVERIFIED" : "BUILD_FAILED";

            // Worker BUILD/TEST evidence is passed directly to HIGH.
            // No independent validation role is dispatched.

            // HIGH also receives unfinished WORK reports. They must
            // preserve unresolved checkpoints and report partial verification;
            // only HQ can decide whether to continue those WORK IDs.
            var beforeHigh = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            highReport = await ExecuteMilestoneHighAsync(
                jobId, workingDirectory, milestone, workReports, resourceReports,
                mechanicalReports, implementer, high, cancellationToken);
            var highChangedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var afterHigh = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            foreach (var path in MilestoneMechanicalExecutor.DiffChangeStates(
                beforeHigh, afterHigh))
            {
                milestoneChangedPaths.Add(path);
                highChangedPaths.Add(path);
            }

            if (outcome == "COMPLETED" &&
                !string.Equals(MilestoneDefinitionContract.ReadHighStatus(highReport),
                    "completed", StringComparison.OrdinalIgnoreCase))
                outcome = "HIGH_BLOCKED_OR_UNVERIFIED";

            CaptureResourceState();

            // HIGH may edit compilation/scene inputs after earlier evidence.
            // Validate its changes once before finalization; never treat a
            // failed recompile as a passed milestone.
            if (HighFinalBuildPolicy.RequiresBuild(highChangedPaths))
            {
                var highBuilds = await MilestoneBuildRunner.RunAsync(
                    workingDirectory, jobId, executableDefinitions, cancellationToken,
                await recheckUnityEditorAsync(cancellationToken));
                foreach (var build in highBuilds)
                    mechanicalReports.Add("HIGH_FINAL_BUILD: " +
                        MilestoneDefinitionContract.FormatMechanicalResult(build));
                if (highBuilds.Any(build => !build.Success))
                {
                    if (UnityCliBuildRunner.IsUnityProject(workingDirectory) &&
                        MilestoneBuildRunner.IsEnvironmentFailure(highBuilds))
                    {
                        // Preserve HIGH's real source-review result. Missing
                        // Pipeline is unverified compilation, not failed code.
                        mechanicalReports.Add(
                            "HIGH_FINAL_BUILD_UNVERIFIED: no connected Editor; " +
                            "do not credit the unavailable build as PASS.");
                        if (outcome == "COMPLETED")
                            outcome = "HIGH_FINAL_BUILD_UNVERIFIED";
                    }
                    else
                    {
                        mechanicalReports.Add("HIGH_BEFORE_FAILED_FINAL_BUILD: " +
                            MilestoneDefinitionContract.Limit(highReport, 1200));
                        highReport = HighFinalBuildPolicy.InvalidateCompletion(
                            highReport, "HIGH_FINAL_BUILD_FAILED");
                        outcome = "HIGH_FINAL_BUILD_FAILED";
                    }
                }
            }

            var stoppedRun = await MilestoneManagedRunRegistry.StopAsync(
                jobId,
                CancellationToken.None);
            if (stoppedRun is not null)
            {
                mechanicalReports.Add(
                    MilestoneDefinitionContract.FormatMechanicalResult(
                        stoppedRun));
            }

            var finalDirtyPaths =
                await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                    workingDirectory,
                    cancellationToken);
            foreach (var dirtyPath in finalDirtyPaths)
            {
                if (!initialChangedPaths.Contains(dirtyPath) ||
                    milestoneChangedPaths.Contains(dirtyPath))
                    milestoneChangedPaths.Add(dirtyPath);
            }

            // Paths completed in this or any earlier milestone of the same
            // user task are added without waiting for outstanding RESOURCEs.
            var pendingResourcePaths = _resourcePendingGit.Snapshot(workingDirectory);
            foreach (var path in pendingResourcePaths.Keys)
                milestoneChangedPaths.Add(path);

            if (milestone.ReadOnlyNoFileChanges)
            {
                gitResult = MilestoneGitResult.SkippedReadOnly(
                    milestone.TargetBranch);
            }
            else
            {
                AddDataFlowHistory(
                    WorkerRoleState.Unknown,
                    "Worker 작업",
                    "GIT_FINALIZE 강제 실행",
                    status: "EXECUTING",
                    persistenceSource: "WORKER ACTION");

                var gitProgress = new Progress<string>(message =>
                    RunOnUi(() => AddDataFlowHistory(
                        WorkerRoleState.Unknown,
                        "Worker 작업",
                        message,
                        status: "EXECUTING",
                        persistenceSource: "WORKER ACTION")));

                gitResult = await MilestoneMechanicalExecutor.FinalizeGitAsync(
                    workingDirectory,
                    milestone,
                    milestoneChangedPaths,
                    _targetSettings.ManualRepositoryUrl?.Trim(),
                    cancellationToken,
                    gitProgress);

                if (!gitResult.Success)
                {
                    var originalGitError = gitResult.Summary;
                    var gitHigh = await ExecuteGitHighAsync(
                        jobId, workingDirectory, milestone, high,
                        "FINALIZE", originalGitError, cancellationToken);
                    mechanicalReports.Add("GIT_HIGH: " +
                        MilestoneDefinitionContract.Limit(gitHigh, 1200));
                    mechanicalReports.Add("GIT_INITIAL_FAILURE: " +
                        MilestoneDefinitionContract.Limit(originalGitError, 1200));
                    // HIGH can fix .gitignore or source configuration. Include
                    // its actual changes in the single controlled Worker retry.
                    var changedAfterGitHigh =
                        await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                            workingDirectory, cancellationToken);
                    foreach (var path in changedAfterGitHigh)
                        if (!initialChangedPaths.Contains(path) ||
                            milestoneChangedPaths.Contains(path))
                            milestoneChangedPaths.Add(path);
                    gitResult = await MilestoneMechanicalExecutor.FinalizeGitAsync(
                        workingDirectory, milestone, milestoneChangedPaths,
                        _targetSettings.ManualRepositoryUrl?.Trim(),
                        cancellationToken, gitProgress);

                }

                if (gitResult.Success)
                    _resourcePendingGit.Acknowledge(workingDirectory, pendingResourcePaths);

                AddDataFlowHistory(
                    WorkerRoleState.Unknown,
                    "Git 최종화",
                    gitResult.Summary,
                    status: gitResult.Success ? "COMPLETED" : "FAILED",
                    persistenceSource: "WORKER ACTION");
            }

            var currentLocalChanges =
                await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                    workingDirectory,
                    cancellationToken);

            SaveMilestoneExecutionGraph(
                workingDirectory,
                jobId,
                milestone,
                gitResult.Success ? "FINALIZED" : "FINALIZE_FAILED",
                workReports,
                resourceReports,
                    highReport,
                hqFinalState: "READY",
                assetManagerFailed: assetManagerFailed);

            // Read-only progress reporting after mechanical finalization.
            // A missing observation must never block the next milestone or
            // change HIGH/HQ decisions.
            try
            {
                var checkpointStates = WorkExecutionJournal.ReadAll(
                        workingDirectory, jobId)
                    .Where(checkpoint => milestone.WorkItems.ContainsKey(
                        checkpoint.WorkItemId))
                    .ToDictionary(checkpoint => checkpoint.WorkItemId,
                        checkpoint => checkpoint.State,
                        StringComparer.OrdinalIgnoreCase);
                var facts = MilestoneProgressFacts.Format(
                    milestone.Id,
                    DateTimeOffset.UtcNow - milestoneStartedAt,
                    milestone.WorkItems.Keys.ToArray(),
                    checkpointStates,
                    milestoneChangedPaths,
                    gitResult.Success,
                    null,
                    MilestoneDefinitionContract.ReadHighStatus(highReport));
                AddTaskMessage("MILESTONE PROGRESS FACTS", facts,
                    status: "OBSERVED");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine(
                    "MILESTONE_PROGRESS_FACTS_UNAVAILABLE: " + exception.Message);
            }

            return new(
                false,
                MilestoneDefinitionContract.BuildHqReport(
                    milestone,
                    outcome,
                    workReports,
                    resourceReports,
                    mechanicalReports,
                            highReport,
                    gitResult,
                    initialChangedPaths,
                    milestoneChangedPaths,
                    currentLocalChanges,
                    _formatRecoveryJobs.ContainsKey(jobId),
                    GetUnreadRecoveryElements(jobId),
                    workingDirectory,
                    jobId));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var currentLocalChanges =
                await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                    workingDirectory,
                    CancellationToken.None);
            return new(
                false,
                MilestoneDefinitionContract.BuildHqReport(
                    milestone,
                    "MILESTONE_EXECUTION_ERROR: " +
                    exception.GetType().Name +
                    ": " +
                    exception.Message,
                    workReports,
                    resourceReports,
                    mechanicalReports,
                            highReport,
                    gitResult,
                    initialChangedPaths,
                    milestoneChangedPaths,
                    currentLocalChanges,
                    _formatRecoveryJobs.ContainsKey(jobId),
                    GetUnreadRecoveryElements(jobId),
                    workingDirectory,
                    jobId));
        }
    }

}
