using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections.Concurrent;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private sealed record MilestoneRunResult(
        bool PauseRequired,
        string Body);

    private sealed record WorkExecutionReport(
        string Id,
        string Report);

    private sealed record ResourceExecutionReport(
        string Id,
        string Report,
        IReadOnlyList<string> ChangedPaths);

    private readonly ConcurrentDictionary<string, byte>
        _formatRecoveryJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string[]>
        _formatRecoveryUnreadElements =
            new(StringComparer.OrdinalIgnoreCase);

    private void RecordUnreadRecoveryElements(
        string jobId,
        IEnumerable<string> elements)
    {
        var additions = elements
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (additions.Length == 0)
            return;

        _formatRecoveryUnreadElements.AddOrUpdate(
            jobId,
            additions,
            (_, existing) => existing
                .Concat(additions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private IReadOnlyList<string> GetUnreadRecoveryElements(string jobId) =>
        _formatRecoveryUnreadElements.TryGetValue(jobId, out var elements)
            ? elements
            : Array.Empty<string>();

    private async Task RunMilestoneCoordinatorFirstJobAsync(
        string request,
        CodexThreadOption? selectedThread,
        string workingDirectory,
        WorkerAiRoleSettings coordinator,
        WorkerAiRoleSettings implementer,
        CoordinatorContinuationState? continuation = null,
        IReadOnlyList<UserAttachmentInput>? attachments = null)
    {
        var continuing = continuation is not null;
        var jobId = continuation?.JobId ?? Guid.NewGuid().ToString("N");
        var normalizedRoot = Path.GetFullPath(workingDirectory);
        var qa = NormalizeRoleSessionForWorkspace(
            _targetSettings.EffectiveQa,
            normalizedRoot);
        var high = NormalizeRoleSessionForWorkspace(
            _targetSettings.EffectiveHighLevel,
            normalizedRoot);

        _activeWorkingDirectory = normalizedRoot;
        _activeProjectJobId = jobId;
        RunOnUi(() => SetDashboardBodyMode(DashboardBodyMode.TaskHistory));
        using var cts = new CancellationTokenSource();
        _activeTaskCts = cts;
        UpdateTaskConfigurationLockState();
        _activeCoordinatorFirst = true;
        SetFollowupComposerVisible(false);
        RunButton.Content = "■   취소";
        _userCanceledTask = false;
        _jobTimedOut = false;
        _lastActivityAt = DateTimeOffset.UtcNow;

        IReadOnlyList<AiInputAttachment> stagedAttachments =
            Array.Empty<AiInputAttachment>();

        try
        {
            if (!continuing)
            {
                _historyEvents.Clear();
                StartTaskTranscript(selectedThread, request, string.Empty);
                AddTaskMessage(
                    "TASK REQUEST",
                    request,
                    sizeBytes: Encoding.UTF8.GetByteCount(request),
                    itemCount: 1,
                    fileCount: attachments?.Count);
            }
            else
            {
                StartCommandTranscript();
                AddTaskMessage(
                    "USER FOLLOWUP",
                    request,
                    sizeBytes: Encoding.UTF8.GetByteCount(request),
                    itemCount: 1,
                    fileCount: attachments?.Count,
                    includeHistory: false);
            }

            stagedAttachments = UserAttachmentTransport.StageForWorkerRuntime(
                attachments,
                normalizedRoot,
                jobId + "-hq");
            var webAttachments = attachments?
                .Select(UserAttachmentTransport.CreateBridgeAttachment)
                .ToList() ?? new List<BridgeAttachment>();

            var hqSession = CodexCliRunner.NormalizeSessionId(
                continuation?.CoordinatorSessionId ??
                coordinator.ThreadSessionId);
            var configuredRepositoryUrl =
                _targetSettings.ManualRepositoryUrl?.Trim();
            var hqInbound = continuing
                ? BuildMilestoneFollowupInput(continuation!, request)
                : request;

            for (var milestoneIndex = 1;
                 milestoneIndex <= 64;
                 milestoneIndex++)
            {
                cts.Token.ThrowIfCancellationRequested();
                _formatRecoveryJobs.TryRemove(jobId, out _);
                _formatRecoveryUnreadElements.TryRemove(jobId, out _);

                RunOnUi(() =>
                {
                    TaskDirection.Text = "설계 관제";
                    TaskTitle.Text = continuing && milestoneIndex == 1
                        ? "HQ 재개 판단"
                        : $"HQ 마일스톤 설계 · {milestoneIndex}";
                    ResultTitle.Text = "HQ";
                    SetFlowState(
                        codexActive: true,
                        workerActive: false,
                        webActive: IsWebTransport(coordinator.Transport),
                        explicitStage: TaskStage.Coordinator);
                });

                var inboundType = continuing && milestoneIndex == 1
                    ? "USER_FOLLOWUP"
                    : milestoneIndex == 1
                        ? "USER_REQUEST"
                        : "MILESTONE_REPORT";

                var remoteReference =
                    await MilestoneMechanicalExecutor
                        .RefreshConfiguredOriginMainAsync(
                            normalizedRoot,
                            configuredRepositoryUrl,
                            cts.Token);
                if (!remoteReference.Success)
                {
                    throw new InvalidOperationException(
                        "HQ_REMOTE_REFERENCE_FAILED: " +
                        remoteReference.Summary);
                }

                AddDataFlowHistory(
                    WorkerRoleState.Unknown,
                    "Worker 작업",
                    "HQ 원격 참조 갱신" +
                    Environment.NewLine +
                    remoteReference.Summary,
                    status: "COMPLETED",
                    persistenceSource: "WORKER ACTION");

                var hqPrompt = BuildMilestoneHqPrompt(
                    inboundType,
                    hqInbound,
                    normalizedRoot,
                    configuredRepositoryUrl,
                    remoteReference.CommitSha);

                var hqResult = await RunHqRoleAsync(
                    jobId,
                    "HQ_MILESTONE",
                    hqPrompt,
                    coordinator,
                    normalizedRoot,
                    hqSession,
                    cts.Token,
                    sessionStarted: session =>
                        hqSession = CodexCliRunner.NormalizeSessionId(session),
                    inputAttachments:
                        milestoneIndex == 1 ? stagedAttachments : null,
                    webAttachments:
                        milestoneIndex == 1 ? webAttachments : null);

                hqSession = CodexCliRunner.NormalizeSessionId(
                    hqResult.SessionId) ?? hqSession;

                if (hqResult.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(hqResult.StandardError)
                            ? "HQ_EXECUTION_FAILED"
                            : hqResult.StandardError);
                }

                var hqMessage = hqResult.FinalMessage?.Trim() ?? string.Empty;
                AddRoleResponseHistory(
                    WorkerRoleState.Hq,
                    "마일스톤 설계",
                    hqMessage,
                    hqResult.Usage,
                    hqResult.Files,
                    status: "RECEIVED",
                    providerWireId: IsWebTransport(coordinator.Transport)
                            ? null : coordinator.Provider,
                    fullMessage: hqMessage);

                var hqText = HqTextProtocol.Parse(
                    hqMessage,
                    completionValidatedByTransport:
                        IsWebTransport(coordinator.Transport));
                if (!hqText.IsValid)
                {
                    _formatRecoveryJobs[jobId] = 1;

                    var recoveryError =
                        string.Join(", ", hqText.Errors);
                    var recoveryBody =
                        HqResponseRecoveryContract.BuildBody(
                            hqMessage,
                            recoveryError);
                    var recoveryPrompt = BuildMilestoneHqPrompt(
                        "HQ_RESPONSE_RECOVERY",
                        recoveryBody,
                        normalizedRoot,
                        configuredRepositoryUrl,
                        remoteReference.CommitSha);

                    AddDataFlowHistory(
                        WorkerRoleState.Hq,
                        "Worker 작업",
                        "HQ 전체 응답 복구 1회 실행" +
                        Environment.NewLine +
                        recoveryError,
                        status: "RECOVERY",
                        persistenceSource: "WORKER ACTION");

                    var recoveryResult = await RunHqRoleAsync(
                        jobId,
                        "HQ_RESPONSE_RECOVERY",
                        recoveryPrompt,
                        coordinator,
                        normalizedRoot,
                        hqSession,
                        cts.Token,
                        sessionStarted: session =>
                            hqSession = CodexCliRunner.NormalizeSessionId(session));

                    hqSession = CodexCliRunner.NormalizeSessionId(
                        recoveryResult.SessionId) ?? hqSession;

                    if (recoveryResult.ExitCode != 0)
                    {
                        throw new InvalidOperationException(
                            string.IsNullOrWhiteSpace(
                                recoveryResult.StandardError)
                                ? "HQ_RESPONSE_RECOVERY_EXECUTION_FAILED"
                                : recoveryResult.StandardError);
                    }

                    hqMessage =
                        recoveryResult.FinalMessage?.Trim() ??
                        string.Empty;
                    AddRoleResponseHistory(
                        WorkerRoleState.Hq,
                        "HQ 전체 응답 복구",
                        hqMessage,
                        recoveryResult.Usage,
                        recoveryResult.Files,
                        status: "RECOVERED",
                        providerWireId: IsWebTransport(coordinator.Transport)
                            ? null : coordinator.Provider,
                        fullMessage: hqMessage);

                    hqText = HqTextProtocol.Parse(
                        hqMessage,
                        completionValidatedByTransport:
                            IsWebTransport(coordinator.Transport));
                    if (!hqText.IsValid)
                    {
                        throw new InvalidOperationException(
                            "HQ_RESPONSE_RECOVERY_FAILED: " +
                            string.Join(", ", hqText.Errors));
                    }
                }

                var hqParse = hqText.Parse;
                MilestoneDefinition? milestone = null;
                var milestoneError = string.Empty;
                var hqAction = hqParse.ValidActions.Single();

                if (string.Equals(
                        hqAction.Name,
                        "PAUSE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    SaveMilestoneContinuation(
                        "PAUSED",
                        hqMessage,
                        jobId,
                        normalizedRoot,
                        coordinator,
                        implementer,
                        hqSession,
                        high);
                    RunOnUi(() =>
                    {
                        ResultTitle.Text = "PAUSED";
                        ResultBody.Text = hqAction.Body;
                        TaskTitle.Text = "사용자 직접 개입 필요";
                        AddTaskMessage(
                            "TASK PAUSED",
                            hqAction.Body,
                            status: "PAUSED");
                        SetFlowState(false, false, false);
                        SetFollowupComposerVisible(true);
                        SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
                    });
                    return;
                }

                if (string.Equals(
                        hqAction.Name,
                        "END",
                        StringComparison.OrdinalIgnoreCase))
                {
                    SaveMilestoneContinuation(
                        "DONE",
                        hqMessage,
                        jobId,
                        normalizedRoot,
                        coordinator,
                        implementer,
                        hqSession,
                        high);
                    ProjectWorkspacePersistence.ClearContinuation(normalizedRoot);
                    RunOnUi(() =>
                    {
                        ResultTitle.Text = "DONE";
                        ResultBody.Text = hqAction.Body;
                        TaskTitle.Text = "프로젝트 작업 완료";
                        AddTaskMessage(
                            "TASK RESULT",
                            hqAction.Body,
                            status: "COMPLETED");
                        SetFlowState(false, false, false);
                        SetFollowupComposerVisible(true);
                        SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
                    });
                    return;
                }

                if (!MilestoneDefinitionContract.TryBuild(
                        hqText.CompatibilityMessage,
                        hqParse,
                        out milestone,
                        out milestoneError) ||
                    milestone is null)
                {
                    throw new InvalidOperationException(
                        "HQ 텍스트 프로토콜을 마일스톤으로 구성하지 못했습니다: " +
                        milestoneError);
                }

                milestone = milestone with
                {
                    RawHqMessage = hqMessage
                };

                if (hqText.UnknownSections.Count > 0)
                {
                    AddDataFlowHistory(
                        WorkerRoleState.Hq,
                        "Worker 작업",
                        "HQ 미등록 section을 HIGH 판단 대상으로 보존함" +
                        Environment.NewLine +
                        "COUNT: " + hqText.UnknownSections.Count,
                        status: "FORWARDED",
                        persistenceSource: "WORKER ACTION");
                }

                RunOnUi(() =>
                {
                    _currentMilestoneQaReserved = milestone!.QaReserved;
                    _currentMilestoneResourceReserved =
                        milestone.Resources.Count > 0;
                    if (_currentMilestoneResourceReserved != true)
                        _resourceSidecarStatus = "ChatGPT Web";
                    UpdatePipelineVisuals();
                    AddDataFlowHistory(
                        WorkerRoleState.Unknown,
                        "Worker 작업",
                        $"HQ 응답 파싱 완료\nMILESTONE: {milestone.Id}\nWORK: {milestone.WorkItems.Count}건\nRESOURCE: {milestone.Resources.Count}건\nTEST: {(milestone.QaReserved ? "ON" : "OFF")}",
                        status: "PARSED",
                        persistenceSource: "WORKER ACTION");
                });

                SaveMilestoneExecutionGraph(
                    normalizedRoot,
                    jobId,
                    milestone!,
                    "PLANNED");

                var initialChangedPaths =
                    await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                        normalizedRoot,
                        cts.Token);

                MilestoneRunResult milestoneResult;
                try
                {
                    milestoneResult = await RunSingleMilestoneAsync(
                        jobId,
                        normalizedRoot,
                        milestone!,
                        implementer,
                        qa,
                        high,
                        initialChangedPaths,
                        cts.Token);
                }
                catch (OperationCanceledException)
                    when (cts.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception milestoneException)
                {
                    var currentLocalChanges =
                        await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                            normalizedRoot,
                            cts.Token);
                    var failureMessage =
                        milestoneException.GetType().Name +
                        ": " +
                        milestoneException.Message;

                    SaveMilestoneExecutionGraph(
                        normalizedRoot,
                        jobId,
                        milestone!,
                        "FAILED",
                        hqFinalState: "BLOCKED");

                    milestoneResult = new(
                        false,
                        MilestoneDefinitionContract.BuildHqReport(
                            milestone!,
                            "MILESTONE_EXECUTION_ERROR" +
                            Environment.NewLine +
                            failureMessage,
                            new Dictionary<string, string>(
                                StringComparer.OrdinalIgnoreCase),
                            new Dictionary<string, string>(
                                StringComparer.OrdinalIgnoreCase),
                            Array.Empty<string>(),
                            string.Empty,
                            string.Empty,
                            MilestoneGitResult.NotStarted(
                                milestone!.TargetBranch),
                            initialChangedPaths,
                            Array.Empty<string>(),
                            currentLocalChanges,
                            _formatRecoveryJobs.ContainsKey(jobId),
                            GetUnreadRecoveryElements(jobId),
                            normalizedRoot,
                            jobId));

                    AddTaskMessage(
                        "MILESTONE ERROR",
                        failureMessage,
                        status: "BLOCKED");
                }

                if (milestoneResult.PauseRequired)
                {
                    SaveMilestoneExecutionGraph(
                        normalizedRoot,
                        jobId,
                        milestone!,
                        "PAUSED",
                        hqFinalState: "BLOCKED");
                    SaveMilestoneContinuation(
                        "PAUSED",
                        hqMessage,
                        jobId,
                        normalizedRoot,
                        coordinator,
                        implementer,
                        hqSession,
                        high);
                    RunOnUi(() =>
                    {
                        ResultTitle.Text = "PAUSED";
                        ResultBody.Text = milestoneResult.Body;
                        TaskTitle.Text = "사용자 직접 개입 필요";
                        AddTaskMessage(
                            "TASK PAUSED",
                            milestoneResult.Body,
                            status: "PAUSED");
                        SetFlowState(false, false, false);
                        SetFollowupComposerVisible(true);
                        SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
                    });
                    return;
                }

                hqInbound = milestoneResult.Body;
                continuing = false;
            }

            throw new InvalidOperationException("HQ_MILESTONE_LIMIT_EXCEEDED");
        }
        catch (OperationCanceledException)
            when (cts.IsCancellationRequested)
        {
            if (_userCanceledTask)
                CompleteFullCancellationUi();
            else
                throw;
        }
        catch (Exception exception)
        {
            var detail =
                exception.GetType().Name + ": " + exception.Message;
            AddTaskMessage(
                "TASK ERROR",
                detail,
                status: "ERROR");
            ResultTitle.Text = "DONE · 오류 기록 있음";
            ResultBody.Text = detail;
            TaskTitle.Text = "신규 마일스톤 실행 오류";
            SetFlowState(false, false, false);
            SaveMilestoneContinuation(
                "DONE_WITH_ERROR",
                detail,
                jobId,
                normalizedRoot,
                coordinator,
                implementer,
                null,
                high);
            SetFollowupComposerVisible(true);
            SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
        }
        finally
        {
            try
            {
                await MilestoneManagedRunRegistry.StopAsync(
                    jobId,
                    CancellationToken.None);
            }
            catch
            {
                WorkerChildProcessJob.TerminateAllActiveJobs();
            }

            UserAttachmentTransport.CleanupStagedWorkerRuntime(
                stagedAttachments,
                normalizedRoot);
            _formatRecoveryJobs.TryRemove(jobId, out _);
            _formatRecoveryUnreadElements.TryRemove(jobId, out _);
            _activeCoordinatorFirst = false;
            _activeTaskCts = null;
            _activeProjectJobId = null;
            RunOnUi(() =>
            {
                RunButton.Content = "▶   실행";
                UpdatePipelineVisuals();
            });
            UpdateTaskConfigurationLockState();
            UpdateDashboardRunButtonState();
        }
    }

    private async Task<MilestoneRunResult> RunSingleMilestoneAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        WorkerAiRoleSettings implementer,
        WorkerAiRoleSettings qa,
        WorkerAiRoleSettings high,
        IReadOnlySet<string> initialChangedPaths,
        CancellationToken cancellationToken)
    {
        AddDataFlowHistory(
            WorkerRoleState.Unknown,
            "Worker 작업",
            $"마일스톤 사전 점검\nID: {milestone.Id}\nBRANCH: {milestone.TargetBranch}\nPOLICY: {(milestone.ReadOnlyNoFileChanges ? "READ_ONLY_NO_FILE_CHANGES" : "DEFAULT")}",
            status: "PROCESSING",
            persistenceSource: "WORKER ACTION");

        var milestoneChangedPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        // Launch RESOURCE immediately; Git preflight and design commit must not
        // delay its own transport or lifetime.
        var resourceTasks = milestone.Resources.Values
            .Select(resource =>
            {
                AddDataFlowHistory(
                    WorkerRoleState.Resource,
                    "Worker 분배",
                    $"RESOURCE_ID: {resource.Id}" +
                    Environment.NewLine +
                    $"TARGET_PATH: {resource.TargetPath}",
                    status: "DISPATCHED",
                    referenceId: milestone.Id + ":RESOURCE:" + resource.Id,
                    workItemId: "0",
                    persistenceSource: "WORKER RESOURCE QUEUE");

                return (
                    Id: resource.Id,
                    Task: _resourceTaskLifecycle.RunAsync(
                        jobId,
                        workingDirectory,
                        resourceCancellation =>
                            ExecuteMilestoneResourceBackgroundAsync(
                                workingDirectory,
                                milestone,
                                resource.Id,
                                resourceCancellation)));
            })
            .ToArray();


        var gitPreflight = await MilestoneMechanicalExecutor.CheckGitReadyAsync(
            workingDirectory,
            milestone.TargetBranch,
            milestone.InitializeGitIfMissing,
            _targetSettings.ManualRepositoryUrl?.Trim(),
            cancellationToken);
        if (!gitPreflight.Success)
        {
            var beforeGitHigh = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            var diagnostic = await ExecuteGitHighAsync(
                jobId, workingDirectory, milestone, high, "PREFLIGHT",
                gitPreflight.Summary, cancellationToken);
            var afterGitHigh = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            foreach (var path in MilestoneMechanicalExecutor.DiffChangeStates(
                beforeGitHigh, afterGitHigh))
                milestoneChangedPaths.Add(path);
            gitPreflight = await MilestoneMechanicalExecutor.CheckGitReadyAsync(
                workingDirectory, milestone.TargetBranch,
                milestone.InitializeGitIfMissing,
                _targetSettings.ManualRepositoryUrl?.Trim(), cancellationToken);
            if (!gitPreflight.Success)
                return new(true, "GIT_PREFLIGHT_FAILED: " + gitPreflight.Summary +
                    Environment.NewLine + "HIGH_GIT: " +
                    MilestoneDefinitionContract.Limit(diagnostic, 1200));
        }

        WorkerPaths.EnsureProjectHubLocalExclude(workingDirectory);

        var workReports = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var resourceReports = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var mechanicalReports = new List<string>();
        var qaReport = string.Empty;
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
            qaReport,
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
                            return new(true, "INITIAL_PLAN_GIT_FAILED: " +
                                planGit.Summary + Environment.NewLine + "HIGH_GIT: " +
                                MilestoneDefinitionContract.Limit(diagnosis, 1200));
                    }
                }
            }

            void CaptureResourceStateWithoutWaiting()
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
                            "독립 RESOURCE sidecar 실행 중 · 다음 단계 진행을 차단하지 않음";
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
                var maxConcurrency = Math.Clamp(
                    _targetSettings.EffectiveMaxConcurrentWork,
                    WorkerTargetConfiguration.MinimumConcurrentWork,
                    WorkerTargetConfiguration.MaximumConcurrentWork);

                AddDataFlowHistory(
                    WorkerRoleState.Unknown,
                    "Worker 작업",
                    $"GENERAL WORK 병렬 묶음 시작 · {executableWorkIds.Length}건",
                    status: "PROCESSING",
                    persistenceSource: "WORKER ACTION");

                var before =
                    await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        workingDirectory,
                        cancellationToken);
                using var gate = new SemaphoreSlim(maxConcurrency);
                var tasks = executableWorkIds.Select(async workId =>
                {
                    await gate.WaitAsync(cancellationToken);
                    try
                    {
                        return await ExecuteMilestoneWorkAsync(
                            jobId,
                            workingDirectory,
                            milestone,
                            workId,
                            implementer,
                            cancellationToken);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }).ToArray();

                RunOnUi(() =>
                    ImplementerWorkGaugeText.Text =
                        FormatActiveWorkItemGauge(
                            Math.Min(executableWorkIds.Length, maxConcurrency)));

                var completed = await Task.WhenAll(tasks);

                RunOnUi(() =>
                    ImplementerWorkGaugeText.Text =
                        FormatActiveWorkItemGauge(0));

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
                    if (MilestoneMechanicalExecutor.IsPathWithinScopes(
                            changedPath,
                            allRunnableScopes))
                    {
                        milestoneChangedPaths.Add(changedPath);
                    }
                }

                AddDataFlowHistory(
                    WorkerRoleState.Unknown,
                    "Worker 작업",
                    $"GENERAL WORK 병렬 묶음 종료 · {completed.Length}/{executableWorkIds.Length} terminal",
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
            if (blockedWorkIds.Length > 0)
            {
                outcome =
                    "WORK_BLOCKED:" +
                    string.Join(",", blockedWorkIds.OrderBy(
                        id => int.TryParse(id, out var number)
                            ? number
                            : int.MaxValue));
            }

            CaptureResourceStateWithoutWaiting();

            var plannedWorkScopes = executableDefinitions
                .Where(work => !work.ReadOnly)
                .SelectMany(work => work.WritePaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Worker owns BUILD; source-fix WORK #8 is attempted only once
            // for an actual compiler/build failure, never for EPERM or timeout.
            var buildRuns = await MilestoneBuildRunner.RunAsync(
                workingDirectory, jobId, executableDefinitions, cancellationToken);
            foreach (var build in buildRuns)
                mechanicalReports.Add(
                    MilestoneDefinitionContract.FormatMechanicalResult(build));
            var buildFailed = buildRuns.Any(run => !run.Success);
            if (buildFailed && !MilestoneBuildRunner.IsEnvironmentFailure(buildRuns))
            {
                var beforeRepair = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    workingDirectory, cancellationToken);
                var repairReport = await ExecuteBuildRepairAsync(
                    jobId, workingDirectory, milestone, plannedWorkScopes,
                    buildRuns.Last(run => !run.Success), implementer, cancellationToken);
                mechanicalReports.Add("WORK_8_REPAIR: " +
                    MilestoneDefinitionContract.Limit(repairReport, 1200));
                var afterRepair = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    workingDirectory, cancellationToken);
                foreach (var path in MilestoneMechanicalExecutor.DiffChangeStates(
                    beforeRepair, afterRepair))
                    if (MilestoneMechanicalExecutor.IsPathWithinScopes(path, plannedWorkScopes))
                        milestoneChangedPaths.Add(path);
                buildRuns = await MilestoneBuildRunner.RunAsync(
                    workingDirectory, jobId, executableDefinitions, cancellationToken);
                foreach (var build in buildRuns)
                    mechanicalReports.Add("REBUILD: " +
                        MilestoneDefinitionContract.FormatMechanicalResult(build));
                buildFailed = buildRuns.Any(run => !run.Success);
            }

            // Preserve original WORK/BUILD failures instead of hiding them after QA.
            if (blockedWorkIds.Length == 0 && buildFailed)
                outcome = "BUILD_FAILED";

            if (milestone.QaReserved)
            {
                qaReport = await ExecuteMilestoneQaAsync(
                    jobId, workingDirectory, milestone,
                    workReports, resourceReports, mechanicalReports,
                    implementer, qa, 1, cancellationToken);
                var qaStatus = MilestoneDefinitionContract.ReadQaStatus(qaReport);
                if (outcome == "COMPLETED" &&
                    !string.Equals(qaStatus, "passed", StringComparison.OrdinalIgnoreCase))
                    outcome = string.Equals(qaStatus, "issue", StringComparison.OrdinalIgnoreCase)
                        ? "QA_ISSUE" : "QA_BLOCKED";
            }

            // HIGH is always a milestone review, not solely a QA issue fixer.
            var beforeHigh = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            highReport = await ExecuteMilestoneHighAsync(
                jobId, workingDirectory, milestone, workReports, resourceReports,
                mechanicalReports, qaReport, implementer, high, 1, cancellationToken);
            var afterHigh = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            foreach (var path in MilestoneMechanicalExecutor.DiffChangeStates(
                beforeHigh, afterHigh))
                milestoneChangedPaths.Add(path);

            if (outcome == "COMPLETED" &&
                string.Equals(MilestoneDefinitionContract.ReadHighStatus(highReport),
                    "blocked", StringComparison.OrdinalIgnoreCase))
                outcome = "HIGH_BLOCKED";

            CaptureResourceStateWithoutWaiting();

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
                if (!initialChangedPaths.Contains(dirtyPath) &&
                    (MilestoneMechanicalExecutor.IsPathWithinScopes(
                         dirtyPath,
                         plannedWorkScopes) ||
                     milestoneChangedPaths.Contains(dirtyPath)))
                {
                    milestoneChangedPaths.Add(dirtyPath);
                }
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
                    "Worker 작업",
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
                qaReport,
                highReport,
                hqFinalState: "READY");

            return new(
                false,
                MilestoneDefinitionContract.BuildHqReport(
                    milestone,
                    outcome,
                    workReports,
                    resourceReports,
                    mechanicalReports,
                    qaReport,
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
                    qaReport,
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

    private static void SaveMilestoneExecutionGraph(
        string workingDirectory,
        string jobId,
        MilestoneDefinition milestone,
        string state,
        IReadOnlyDictionary<string, string>? workReports = null,
        IReadOnlyDictionary<string, string>? resourceReports = null,
        string? qaReport = null,
        string? highReport = null,
        string hqFinalState = "PLANNED")
    {
        workReports ??= new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        resourceReports ??= new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        var nodes = new List<MilestoneGraphNodeSnapshot>
        {
            new("HQ-DESIGN", "HQ", "COMPLETED")
        };

        foreach (var work in milestone.WorkItems.Values
                     .OrderBy(item => int.TryParse(item.Id, out var number) ? number : int.MaxValue))
        {
            nodes.Add(new(
                "WORK-" + work.Id,
                "WORK",
                workReports.ContainsKey(work.Id) ? "COMPLETED" : "PLANNED",
                work.Id));
        }

        foreach (var resource in milestone.Resources.Values)
        {
            var resourceState =
                resourceReports.TryGetValue(resource.Id, out var resourceReport)
                    ? resourceReport.StartsWith(
                        "RESOURCE_STATUS: PENDING",
                        StringComparison.Ordinal)
                        ? "RUNNING"
                        : "COMPLETED"
                    : "PLANNED";
            nodes.Add(new(
                "RESOURCE-" + resource.Id,
                "RESOURCE",
                resourceState,
                resource.Id));
        }

        nodes.Add(new(
            "QA",
            "QA",
            !milestone.QaReserved
                ? "SKIPPED"
                : !string.IsNullOrWhiteSpace(qaReport)
                    ? "COMPLETED"
                    : "PLANNED"));
        nodes.Add(new(
            "HIGH",
            "HIGH",
            string.IsNullOrWhiteSpace(highReport)
                ? "SKIPPED"
                : "COMPLETED"));
        nodes.Add(new(
            "GIT-FINALIZE",
            "WORKER",
            state is "FINALIZED" or "FINALIZE_FAILED"
                ? "COMPLETED"
                : "PLANNED"));
        nodes.Add(new(
            "HQ-FINAL",
            "HQ",
            hqFinalState));

        var edges = new List<MilestoneGraphEdgeSnapshot>();
        var workNodes = milestone.WorkItems.Values
            .Select(work => "WORK-" + work.Id)
            .ToArray();

        foreach (var workNode in workNodes)
        {
            edges.Add(new("HQ-DESIGN", workNode, "DISPATCH"));
            edges.Add(new(
                workNode,
                milestone.QaReserved ? "QA" : "GIT-FINALIZE",
                milestone.QaReserved ? "RESULT_TO_QA" : "RESULT_TO_GIT"));
        }

        foreach (var resource in milestone.Resources.Values)
        {
            edges.Add(new(
                "HQ-DESIGN",
                "RESOURCE-" + resource.Id,
                "BACKGROUND_RESOURCE_REQUEST"));
        }

        if (milestone.QaReserved)
        {
            edges.Add(new("QA", "HIGH", "QA_ISSUE_ONLY"));
            edges.Add(new("QA", "GIT-FINALIZE", "QA_TERMINAL_TO_GIT"));
            edges.Add(new("HIGH", "GIT-FINALIZE", "HIGH_TERMINAL_TO_GIT"));
        }

        edges.Add(new("GIT-FINALIZE", "HQ-FINAL", "REPORT_TO_HQ"));

        ProjectWorkspacePersistence.SaveMilestoneGraph(
            workingDirectory,
            new MilestoneExecutionGraphSnapshot(
                jobId,
                milestone.Id,
                state,
                milestone.QaReserved,
                milestone.Resources.Count > 0,
                nodes,
                edges,
                DateTimeOffset.UtcNow));
    }

    private async Task<WorkExecutionReport> ExecuteMilestoneWorkAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        string workId,
        WorkerAiRoleSettings implementer,
        CancellationToken cancellationToken)
    {
        if (!milestone.WorkItems.TryGetValue(workId, out var work))
        {
            return new(
                workId,
                RoleTextProtocol.BuildResult(
                    "blocked",
                    "계획되지 않은 WORK_ITEM_ID입니다."));
        }

        RunOnUi(() =>
        {
            TaskDirection.Text = "작업";
            TaskTitle.Text = $"GENERAL WORK #{work.Id}";
            ResultTitle.Text = "WORK";
            SetFlowState(
                codexActive: false,
                workerActive: true,
                webActive: false,
                explicitStage: TaskStage.Implementer);
        });

        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            work.Id,
            MilestoneDefinitionContract.BuildWorkContext(work),
            work.WritePaths,
            workingDirectory,
            readOnly: work.ReadOnly);

        var statelessRole = implementer with
        {
            ThreadSessionId = null,
            ThreadProjectPath = null
        };

        async Task<AiRoleRunResult> RunAsync(string currentPrompt) =>
            await RunCoordinatorRoleAsync(
                jobId,
                "WORK",
                currentPrompt,
                statelessRole,
                workingDirectory,
                null,
                null,
                cancellationToken,
                work.ReadOnly
                    ? CodexSandboxMode.ReadOnly
                    : CodexSandboxMode.WorkspaceWrite,
                historyWorkItemId: work.Id,
                historyReferenceId: milestone.Id + ":" + work.Id);

        var result = await RunAsync(prompt);
        if (result.ExitCode == 0 &&
            !RoleTextProtocol.ParseWork(result.FinalMessage).IsValid)
        {
            _formatRecoveryJobs[jobId] = 1;
            result = await RunAsync(BuildRoleTextRetryPrompt(
                prompt,
                result.FinalMessage,
                RoleContractLoader.LoadWorkFooter()));
        }

        var report = MilestoneDefinitionContract.NormalizeWorkReport(
            result.ExitCode,
            result.FinalMessage,
            result.StandardError);
        var reportStatus =
            MilestoneDefinitionContract.ReadWorkStatus(report);

        AddRoleResponseHistory(
            WorkerRoleState.Work,
            "작업 응답",
            report,
            result.Usage,
            result.Files,
            status: string.Equals(
                reportStatus,
                "blocked",
                StringComparison.OrdinalIgnoreCase)
                    ? "BLOCKED"
                    : "RECEIVED",
            providerWireId: implementer.Provider,
            fullMessage: report,
            workItemId: work.Id,
            referenceId: milestone.Id + ":" + work.Id);

        return new(work.Id, report);
    }

    private async Task<ResourceExecutionReport> ExecuteMilestoneResourceBackgroundAsync(
        string workingDirectory,
        MilestoneDefinition milestone,
        string resourceId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteMilestoneResourceAsync(
                workingDirectory,
                milestone,
                resourceId,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            var report =
                "RESOURCE_STATUS: BLOCKED" +
                Environment.NewLine +
                "RESOURCE_CANCELED";
            AddRoleResponseHistory(
                WorkerRoleState.Resource,
                "리소스 독립 실행 종료",
                report,
                status: "BLOCKED",
                workItemId: "0",
                referenceId: milestone.Id + ":RESOURCE:" + resourceId);
            return new(
                resourceId,
                report,
                Array.Empty<string>());
        }
        catch (Exception exception)
        {
            var report =
                "RESOURCE_STATUS: BLOCKED" +
                Environment.NewLine +
                "RESOURCE_BACKGROUND_EXECUTION_FAILED" +
                Environment.NewLine +
                exception.Message;
            AddRoleResponseHistory(
                WorkerRoleState.Resource,
                "리소스 독립 실행 실패",
                report,
                status: "BLOCKED",
                workItemId: "0",
                referenceId: milestone.Id + ":RESOURCE:" + resourceId);
            return new(
                resourceId,
                report,
                Array.Empty<string>());
        }
    }

    private async Task<ResourceExecutionReport> ExecuteMilestoneResourceAsync(
        string workingDirectory,
        MilestoneDefinition milestone,
        string resourceId,
        CancellationToken cancellationToken)
    {
        if (!milestone.Resources.TryGetValue(resourceId, out var resource))
        {
            var report =
                "RESOURCE_STATUS: BLOCKED" +
                Environment.NewLine +
                "계획되지 않은 RESOURCE_ID입니다.";
            AddRoleResponseHistory(
                WorkerRoleState.Resource,
                "리소스 독립 실행 종료",
                report,
                status: "BLOCKED",
                workItemId: "0",
                referenceId: milestone.Id + ":RESOURCE:" + resourceId);
            return new(
                resourceId,
                report,
                Array.Empty<string>());
        }

        AddTaskMessage(
            "RESOURCE DETAIL",
            $"RESOURCE #{resource.Id} 생성 시작",
            status: "RUNNING",
            referenceId: resource.Id,
            includeHistory: false,
            workItemId: "0");

        var registry = new MechanicalWorkRegistry();
        await using var queue = new ResourceSidecarQueue(
            _bridgeServer,
            workingDirectory,
            cancellationToken,
            registry);

        queue.StateChanged += OnResourceSidecarStateChanged;
        queue.TransportEvent += OnResourceSidecarTransportEvent;

        queue.Enqueue(
            resource.Type,
            resource.Body,
            resource.Id,
            workingDirectory);
        await queue.WaitForIdleAsync(cancellationToken);

        if (!queue.TryDequeueCompletion(out var completion))
        {
            var missingReport =
                "RESOURCE_STATUS: BLOCKED" +
                Environment.NewLine +
                "RESOURCE_RESULT_MISSING";
            AddRoleResponseHistory(
                WorkerRoleState.Resource,
                "리소스 반영 실패",
                missingReport,
                status: "BLOCKED",
                workItemId: "0",
                referenceId: milestone.Id + ":RESOURCE:" + resource.Id);
            return new(resource.Id, missingReport, Array.Empty<string>());
        }

        if (!completion.Success)
        {
            var failedReport =
                "RESOURCE_STATUS: BLOCKED" +
                Environment.NewLine +
                (completion.ErrorCode ?? "RESOURCE_FAILED") +
                Environment.NewLine +
                completion.Message;
            AddRoleResponseHistory(
                WorkerRoleState.Resource,
                "리소스 반영 실패",
                failedReport,
                status: "BLOCKED",
                workItemId: "0",
                referenceId: milestone.Id + ":RESOURCE:" + resource.Id);
            return new(resource.Id, failedReport, Array.Empty<string>());
        }

        var moveResult = MoveResourceResults(
            workingDirectory,
            resource.TargetPath,
            completion.SavedPaths);
        if (moveResult.ChangedPaths.Count > 0)
            _resourcePendingGit.Register(workingDirectory, moveResult.ChangedPaths);
        AddRoleResponseHistory(
            WorkerRoleState.Resource,
            "리소스 반영",
            moveResult.Report,
            status: moveResult.Report.StartsWith(
                "RESOURCE_STATUS: COMPLETED",
                StringComparison.Ordinal)
                    ? "COMPLETED"
                    : "BLOCKED",
            workItemId: "0",
            referenceId: milestone.Id + ":RESOURCE:" + resource.Id);
        return new(
            resource.Id,
            moveResult.Report,
            moveResult.ChangedPaths);
    }

    private static (string Report, IReadOnlyList<string> ChangedPaths)
        MoveResourceResults(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> sources)
    {
        if (sources.Count == 0)
        {
            return (
                "RESOURCE_STATUS: BLOCKED" +
                Environment.NewLine +
                "RESOURCE_FILE_MISSING",
                Array.Empty<string>());
        }

        var root = Path.GetFullPath(workingDirectory);
        var destination = Path.GetFullPath(
            Path.Combine(root, targetPath));

        if (!MilestoneDefinitionContract.IsPathInsideRoot(root, destination))
        {
            return (
                "RESOURCE_STATUS: BLOCKED" +
                Environment.NewLine +
                "TARGET_PATH_OUTSIDE_PROJECT_ROOT",
                Array.Empty<string>());
        }

        var targetLooksLikeFile = Path.HasExtension(destination);
        if (targetLooksLikeFile && sources.Count != 1)
        {
            return (
                "RESOURCE_STATUS: BLOCKED" +
                Environment.NewLine +
                "RESOURCE_TARGET_REQUIRES_SINGLE_FILE",
                Array.Empty<string>());
        }

        var moved = new List<string>();

        if (targetLooksLikeFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(sources[0], destination, overwrite: true);
            moved.Add(Path.GetRelativePath(root, destination));
        }
        else
        {
            Directory.CreateDirectory(destination);
            foreach (var source in sources)
            {
                var target = Path.Combine(
                    destination,
                    Path.GetFileName(source));
                File.Move(source, target, overwrite: true);
                moved.Add(Path.GetRelativePath(root, target));
            }
        }

        return (
            "RESOURCE_STATUS: COMPLETED" +
            Environment.NewLine +
            "최종 경로로 move 완료:" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                moved.Select(path => "- " + path)),
            moved);
    }

    private async Task<string> ExecuteMilestoneQaAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        IReadOnlyDictionary<string, string> workReports,
        IReadOnlyDictionary<string, string> resourceReports,
        IReadOnlyList<string> mechanicalReports,
        WorkerAiRoleSettings implementer,
        WorkerAiRoleSettings qa,
        int validationRound,
        CancellationToken cancellationToken)
    {
        _ = resourceReports;
        _ = mechanicalReports;
        _ = implementer;

        RunOnUi(() =>
        {
            TaskDirection.Text = "QA";
            TaskTitle.Text = $"QA 동작 조사 · {validationRound}";
            ResultTitle.Text = "QA";
            SetFlowState(
                codexActive: true,
                workerActive: false,
                webActive: false,
                explicitStage: TaskStage.Qa);
        });

        var prompt = RoleContractLoader.BuildQaPrompt(
            MilestoneDefinitionContract.BuildQaContext(
                milestone,
                workReports,
                mechanicalReports));

        async Task<AiRoleRunResult> RunAsync(string currentPrompt) =>
            await RunCoordinatorRoleAsync(
                jobId,
                "QA",
                currentPrompt,
                qa,
                workingDirectory,
                null,
                null,
                cancellationToken,
                CodexSandboxMode.WorkspaceWrite,
                historyWorkItemId: "QA",
                historyReferenceId: milestone.Id + ":QA");

        var result = await RunAsync(prompt);
        if (result.ExitCode == 0 &&
            !RoleTextProtocol.ParseQa(result.FinalMessage).IsValid)
        {
            _formatRecoveryJobs[jobId] = 1;
            result = await RunAsync(BuildRoleTextRetryPrompt(
                prompt,
                result.FinalMessage,
                RoleContractLoader.LoadQaFooter()));
        }

        var report = MilestoneDefinitionContract.NormalizeQaReport(
            result.ExitCode,
            result.FinalMessage,
            result.StandardError);
        var qaStatus = MilestoneDefinitionContract.ReadQaStatus(report);

        AddRoleResponseHistory(
            WorkerRoleState.Qa,
            "QA 조사 결과",
            report,
            result.Usage,
            result.Files,
            status: string.Equals(
                qaStatus,
                "blocked",
                StringComparison.OrdinalIgnoreCase)
                    ? "BLOCKED"
                    : "RECEIVED",
            providerWireId: qa.Provider,
            fullMessage: report,
            workItemId: "QA",
            referenceId: milestone.Id + ":QA");

        return report;
    }

    private async Task<string> ExecuteMilestoneHighAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        IReadOnlyDictionary<string, string> workReports,
        IReadOnlyDictionary<string, string> resourceReports,
        IReadOnlyList<string> mechanicalReports,
        string qaReport,
        WorkerAiRoleSettings implementer,
        WorkerAiRoleSettings high,
        int validationRound,
        CancellationToken cancellationToken)
    {
        _ = resourceReports;
        _ = mechanicalReports;
        _ = implementer;

        RunOnUi(() =>
        {
            TaskDirection.Text = "검토";
            TaskTitle.Text = $"HIGH 마일스톤 검토 · {validationRound}";
            ResultTitle.Text = "HIGH";
            SetFlowState(
                codexActive: true,
                workerActive: false,
                webActive: false,
                explicitStage: TaskStage.HighLevel);
        });

        var prompt = RoleContractLoader.BuildHighPrompt(
            MilestoneDefinitionContract.BuildHighContext(
                milestone,
                workReports,
                qaReport,
                mechanicalReports));

        var sandbox = milestone.ReadOnlyNoFileChanges
            ? CodexSandboxMode.ReadOnly
            : CodexSandboxMode.WorkspaceWrite;

        async Task<AiRoleRunResult> RunAsync(string currentPrompt) =>
            await RunCoordinatorRoleAsync(
                jobId,
                "HIGH",
                currentPrompt,
                high,
                workingDirectory,
                null,
                null,
                cancellationToken,
                sandbox,
                historyWorkItemId: "HIGH",
                historyReferenceId: milestone.Id + ":HIGH");

        var result = await RunAsync(prompt);
        if (result.ExitCode == 0 &&
            !RoleTextProtocol.ParseHigh(result.FinalMessage).IsValid)
        {
            _formatRecoveryJobs[jobId] = 1;
            result = await RunAsync(BuildRoleTextRetryPrompt(
                prompt,
                result.FinalMessage,
                RoleContractLoader.LoadHighFooter()));
        }

        var report = MilestoneDefinitionContract.NormalizeHighReport(
            result.ExitCode,
            result.FinalMessage,
            result.StandardError);
        var highStatus = MilestoneDefinitionContract.ReadHighStatus(report);

        AddRoleResponseHistory(
            WorkerRoleState.High,
            "검토 결과",
            report,
            result.Usage,
            result.Files,
            status: string.Equals(
                highStatus,
                "blocked",
                StringComparison.OrdinalIgnoreCase)
                    ? "BLOCKED"
                    : "RECEIVED",
            providerWireId: high.Provider,
            fullMessage: report,
            workItemId: "HIGH",
            referenceId: milestone.Id + ":HIGH");

        return report;
    }

    private async Task<string> ExecuteBuildRepairAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        IReadOnlyList<string> writePaths,
        MilestoneMechanicalResult failure,
        WorkerAiRoleSettings implementer,
        CancellationToken cancellationToken)
    {
        if (writePaths.Count == 0)
            return "blocked: NO_WRITABLE_SCOPE";
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            "8", "@@GOAL" + Environment.NewLine +
            "Worker 빌드 오류에 필요한 최소 소스 수정만 수행하라." +
            Environment.NewLine + "@@INSTRUCTIONS" + Environment.NewLine +
            "원래 작업 범위 외 변경 금지. Git 명령 실행 금지. " +
            "오류: " + MilestoneDefinitionContract.Limit(failure.Summary, 1600),
            writePaths, workingDirectory);
        var stateless = implementer with
        {
            ThreadSessionId = null,
            ThreadProjectPath = null
        };
        var result = await RunCoordinatorRoleAsync(
            jobId, "WORK", prompt, stateless, workingDirectory,
            null, null, cancellationToken, CodexSandboxMode.WorkspaceWrite,
            historyWorkItemId: "8",
            historyReferenceId: milestone.Id + ":BUILD:REPAIR-1");
        var report = MilestoneDefinitionContract.NormalizeWorkReport(
            result.ExitCode, result.FinalMessage, result.StandardError);
        AddRoleResponseHistory(WorkerRoleState.Work,
            "임시 빌드 복구 #8", report, result.Usage, result.Files,
            status: MilestoneDefinitionContract.ReadWorkStatus(report) == "blocked"
                ? "BLOCKED" : "RECEIVED",
            providerWireId: implementer.Provider, fullMessage: report,
            workItemId: "8",
            referenceId: milestone.Id + ":BUILD:REPAIR-1");
        return report;
    }

    private async Task<string> ExecuteGitHighAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        WorkerAiRoleSettings high,
        string stage,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        var prompt = RoleContractLoader.BuildHighPrompt(
            "REASON: GIT_FAILED" + Environment.NewLine +
            "STAGE: " + stage + Environment.NewLine +
            "Git 자체의 commit/push/reset 등 변경 명령은 Worker 전용이다." +
            Environment.NewLine +
            "안전하게 수정 가능한 소스/설정만 수정하고 결과를 보고하라." +
            Environment.NewLine +
            "@@ERROR" + Environment.NewLine +
            MilestoneDefinitionContract.Limit(diagnostic, 2400));
        var result = await RunCoordinatorRoleAsync(
            jobId, "HIGH", prompt, high, workingDirectory,
            null, null, cancellationToken, CodexSandboxMode.WorkspaceWrite,
            historyWorkItemId: "HIGH-GIT",
            historyReferenceId: milestone.Id + ":HIGH:GIT:" + stage);
        var report = MilestoneDefinitionContract.NormalizeHighReport(
            result.ExitCode, result.FinalMessage, result.StandardError);
        AddRoleResponseHistory(WorkerRoleState.High, "Git 오류 검토",
            report, result.Usage, result.Files,
            status: MilestoneDefinitionContract.ReadHighStatus(report) == "blocked"
                ? "BLOCKED" : "RECEIVED",
            providerWireId: high.Provider, fullMessage: report,
            workItemId: "HIGH-GIT",
            referenceId: milestone.Id + ":HIGH:GIT:" + stage);
        return report;
    }

    private static string BuildRoleTextRetryPrompt(
        string originalPrompt,
        string? previousResponse,
        string currentContract) =>
        originalPrompt +
        Environment.NewLine +
        Environment.NewLine +
        "이전 역할 응답의 형식을 복구하기 위한 전체 응답 재요청이다." +
        Environment.NewLine +
        "이전 응답은 참고 데이터로 사용하고 현재 역할 계약 전문을 출력 기준으로 사용한다." +
        Environment.NewLine +
        "완결된 전체 RESULT 응답을 처음부터 한 번 다시 출력한다." +
        Environment.NewLine +
        Environment.NewLine +
        "PREVIOUS_RESPONSE_AS_DATA:" +
        Environment.NewLine +
        (previousResponse ?? string.Empty) +
        Environment.NewLine +
        Environment.NewLine +
        currentContract;

    private static string BuildMilestoneHqPrompt(
        string inboundType,
        string body,
        string workingDirectory,
        string? configuredRepositoryUrl,
        string? remoteMainSha) =>
        "역할: HQ" +
        Environment.NewLine +
        $"입력 유형: {inboundType}" +
        Environment.NewLine +
        $"실제 프로젝트 루트: {workingDirectory}" +
        Environment.NewLine +
        $"강제 원격 저장소: {configuredRepositoryUrl ?? "없음"}" +
        Environment.NewLine +
        "원격 작업 기준: origin/main" +
        Environment.NewLine +
        $"현재 origin/main SHA: {remoteMainSha ?? "확인 실패"}" +
        Environment.NewLine +
        "Worker가 이 호출 직전에 origin/main을 fetch했다. 원격 저장소를 직접 참조하여 설계한다. 로컬 Git 명령을 사용할 수 있는 transport에서는 git log, git ls-tree, git show, git diff 등 읽기 전용 명령으로 origin/main의 실제 구조와 이력을 조사하고 현재 로컬 상태와 비교한다. Web transport에서는 위 강제 원격 저장소 URL의 main을 직접 열어 구조와 이력을 조사한다. 필요하면 git ls-remote origin main으로 원격 기준을 재확인한다." +
        Environment.NewLine +
        "사용자가 지정한 실제 프로젝트 루트 하나를 공통 작업공간으로 사용하고 GENERAL WORK별 WRITE_PATH를 명확히 지정한다." +
        Environment.NewLine +
        "같은 milestone의 GENERAL WORK 사이에는 실행 순서나 결과 dependency를 만들지 않는다. WORKITEM 분할 판단은 HQ routing contract의 우선순위를 따른다." +
        Environment.NewLine +
        "입력 본문:" +
        Environment.NewLine +
        body +
        Environment.NewLine +
        Environment.NewLine +
        RoleContractLoader.LoadHqFooter();

    private static string BuildMilestoneFollowupInput(
        CoordinatorContinuationState continuation,
        string followup) =>
        "이전 HQ 상태:" +
        Environment.NewLine +
        continuation.LastHqMessage +
        Environment.NewLine +
        Environment.NewLine +
        "사용자가 직접 개입을 마치고 전달한 재개 입력:" +
        Environment.NewLine +
        followup;

    private void SaveMilestoneContinuation(
        string status,
        string lastHqMessage,
        string jobId,
        string workingDirectory,
        WorkerAiRoleSettings coordinator,
        WorkerAiRoleSettings implementer,
        string? coordinatorSession,
        WorkerAiRoleSettings highLevel)
    {
        _continuationState = new CoordinatorContinuationState(
            jobId,
            workingDirectory,
            coordinator,
            implementer,
            coordinatorSession,
            null,
            status,
            lastHqMessage ?? string.Empty,
            highLevel);

        if (TaskContinuationContract.IsResumableStatus(status))
            ProjectWorkspacePersistence.SaveContinuation(_continuationState);
        else
            ProjectWorkspacePersistence.ClearContinuation(workingDirectory);

        SetFollowupComposerVisible(true);
    }
}
