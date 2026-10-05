using System.Text;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private sealed record MilestoneManagerResult(
        bool PauseRequired,
        string Body);

    private sealed record WorkExecutionReport(
        string Id,
        string Report);

    private sealed record ResourceExecutionReport(
        string Id,
        string Report,
        IReadOnlyList<string> ChangedPaths);

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
        var manager = NormalizeRoleSessionForWorkspace(
            _targetSettings.EffectiveManager,
            normalizedRoot);
        var qa = NormalizeRoleSessionForWorkspace(
            _targetSettings.EffectiveQa,
            normalizedRoot);
        var high = NormalizeRoleSessionForWorkspace(
            _targetSettings.EffectiveHighLevel,
            normalizedRoot);

        _activeWorkingDirectory = normalizedRoot;
        _activeProjectJobId = jobId;
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
            var hqInbound = continuing
                ? BuildMilestoneFollowupInput(continuation!, request)
                : request;

            for (var milestoneIndex = 1;
                 milestoneIndex <= 64;
                 milestoneIndex++)
            {
                cts.Token.ThrowIfCancellationRequested();

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
                var hqPrompt = BuildMilestoneHqPrompt(
                    inboundType,
                    hqInbound,
                    normalizedRoot);

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
                    providerWireId: coordinator.Provider,
                    fullMessage: hqMessage);

                var hqParse = ActionBlockContract.ParseHq(hqMessage);
                var pause = hqParse.ValidActions.FirstOrDefault(action =>
                    string.Equals(
                        action.Name,
                        "PAUSE",
                        StringComparison.OrdinalIgnoreCase));

                if (pause is not null)
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
                        ResultBody.Text = pause.Body;
                        TaskTitle.Text = "사용자 직접 개입 필요";
                        AddTaskMessage(
                            "TASK PAUSED",
                            pause.Body,
                            status: "PAUSED",
                            includeHistory: false);
                        SetFlowState(false, false, false);
                        SetFollowupComposerVisible(true);
                        SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
                    });
                    return;
                }

                var end = hqParse.ValidActions.FirstOrDefault(action =>
                    string.Equals(
                        action.Name,
                        "END",
                        StringComparison.OrdinalIgnoreCase));

                if (end is not null)
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
                        ResultBody.Text = end.Body;
                        TaskTitle.Text = "프로젝트 작업 완료";
                        AddTaskMessage(
                            "TASK RESULT",
                            end.Body,
                            status: "COMPLETED");
                        SetFlowState(false, false, false);
                        SetFollowupComposerVisible(true);
                        SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
                    });
                    return;
                }

                if (!MilestoneDefinitionContract.TryBuild(
                        hqMessage,
                        hqParse,
                        out var milestone,
                        out var milestoneError))
                {
                    hqInbound =
                        "HQ ACTION 계약 오류입니다. 정상 파싱된 ACTION은 폐기하지 않았지만 " +
                        "실행 가능한 MILESTONE이 필요합니다." +
                        Environment.NewLine +
                        milestoneError +
                        Environment.NewLine +
                        "이전 응답:" +
                        Environment.NewLine +
                        hqMessage;
                    continuing = false;
                    continue;
                }

                var initialChangedPaths =
                    await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                        normalizedRoot,
                        cts.Token);

                var managerReport = await RunSingleMilestoneAsync(
                    jobId,
                    normalizedRoot,
                    milestone!,
                    implementer,
                    manager,
                    qa,
                    high,
                    initialChangedPaths,
                    cts.Token);

                if (managerReport.PauseRequired)
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
                        ResultBody.Text = managerReport.Body;
                        TaskTitle.Text = "사용자 직접 개입 필요";
                        AddTaskMessage(
                            "TASK PAUSED",
                            managerReport.Body,
                            status: "PAUSED",
                            includeHistory: false);
                        SetFlowState(false, false, false);
                        SetFollowupComposerVisible(true);
                        SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
                    });
                    return;
                }

                hqInbound = managerReport.Body;
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

    private async Task<MilestoneManagerResult> RunSingleMilestoneAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        WorkerAiRoleSettings implementer,
        WorkerAiRoleSettings manager,
        WorkerAiRoleSettings qa,
        WorkerAiRoleSettings high,
        IReadOnlySet<string> initialChangedPaths,
        CancellationToken cancellationToken)
    {
        var gitPreflight = await MilestoneMechanicalExecutor.CheckGitReadyAsync(
            workingDirectory,
            milestone.TargetBranch,
            cancellationToken);
        if (!gitPreflight.Success)
            return new(true, gitPreflight.Summary);

        var milestoneWriteScopes = milestone.WorkItems.Values
            .SelectMany(work => work.WritePaths)
            .Concat(milestone.Resources.Values.Select(resource => resource.TargetPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var initialWriteConflicts = initialChangedPaths
            .Where(path =>
                MilestoneMechanicalExecutor.IsPathWithinScopes(
                    path,
                    milestoneWriteScopes))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (initialWriteConflicts.Length > 0)
        {
            return new(
                true,
                "현재 마일스톤 쓰기 영역에 기존 로컬 변경이 있어 안전하게 시작할 수 없습니다." +
                Environment.NewLine +
                "사용자가 직접 정리한 뒤 재개하세요." +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    initialWriteConflicts.Select(path => "- " + path)));
        }

        var workReports = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var resourceReports = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var mechanicalReports = new List<string>();
        var milestoneChangedPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var qaReport = string.Empty;
        var highReport = string.Empty;
        var validationRound = 0;
        var validationCompleted = false;
        var gitResult = MilestoneGitResult.NotStarted(milestone.TargetBranch);
        var gitFinalizeAttempted = false;
        string? managerSession = null;

        var managerInput = MilestoneDefinitionContract.BuildManagerInput(
            milestone,
            workReports,
            resourceReports,
            mechanicalReports,
            qaReport,
            highReport,
            gitResult,
            "MILESTONE_START");

        for (var managerRound = 1;
             managerRound <= 48;
             managerRound++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RunOnUi(() =>
            {
                TaskDirection.Text = "통합";
                TaskTitle.Text = $"중간관리자 · {milestone.Id} · {managerRound}";
                ResultTitle.Text = "MANAGER";
                SetFlowState(
                    codexActive: true,
                    workerActive: false,
                    webActive: false,
                    explicitStage: TaskStage.Manager);
            });

            var managerPrompt = RoleContractLoader.BuildManagerPrompt(
                managerInput +
                Environment.NewLine +
                Environment.NewLine +
                BuildManagerMechanicalSupplement(workingDirectory));

            var managerResult = await RunCoordinatorRoleAsync(
                jobId,
                "MANAGER",
                managerPrompt,
                manager,
                workingDirectory,
                managerSession,
                null,
                cancellationToken,
                CodexSandboxMode.ReadOnly);

            managerSession =
                CodexCliRunner.NormalizeSessionId(managerResult.SessionId) ??
                managerSession;

            if (managerResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "MANAGER_EXECUTION_FAILED: " +
                    managerResult.StandardError);
            }

            var managerMessage =
                managerResult.FinalMessage?.Trim() ?? string.Empty;
            AddRoleResponseHistory(
                WorkerRoleState.Manager,
                "통합 판단",
                managerMessage,
                managerResult.Usage,
                managerResult.Files,
                status: "RECEIVED",
                providerWireId: manager.Provider,
                fullMessage: managerMessage);

            if (IsManagerFinalReport(managerMessage))
            {
                if (!validationCompleted)
                {
                    managerInput =
                        "HIGH 검토까지 완료된 validation이 없습니다. " +
                        "READY_FOR_VALIDATION을 수행한 뒤 마무리하세요." +
                        Environment.NewLine +
                        MilestoneDefinitionContract.BuildManagerInput(
                            milestone,
                            workReports,
                            resourceReports,
                            mechanicalReports,
                            qaReport,
                            highReport,
                            gitResult,
                            "VALIDATION_REQUIRED");
                    continue;
                }

                if (!gitFinalizeAttempted)
                {
                    managerInput =
                        "GIT_FINALIZE가 아직 수행되지 않았습니다. " +
                        "마일스톤 성패와 관계없이 GIT_FINALIZE를 한 번 이상 수행하고 실제 commit/push 결과를 포함해 HQ 최종 보고를 작성하세요." +
                        Environment.NewLine +
                        MilestoneDefinitionContract.BuildManagerInput(
                            milestone,
                            workReports,
                            resourceReports,
                            mechanicalReports,
                            qaReport,
                            highReport,
                            gitResult,
                            "GIT_FINALIZE_REQUIRED");
                    continue;
                }

                var currentLocalChanges =
                    await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                        workingDirectory,
                        cancellationToken);
                return new(
                    false,
                    MilestoneDefinitionContract.BuildHqReport(
                        milestone,
                        managerMessage,
                        workReports,
                        resourceReports,
                        mechanicalReports,
                        qaReport,
                        highReport,
                        gitResult,
                        initialChangedPaths,
                        milestoneChangedPaths,
                        currentLocalChanges));
            }

            var parsed = ActionBlockContract.ParseManager(managerMessage);
            var feedback = new List<string>();

            if (parsed.Errors.Count > 0)
            {
                feedback.Add(
                    "MANAGER_ACTION_PARSE_ERRORS:" +
                    Environment.NewLine +
                    string.Join(
                        Environment.NewLine,
                        parsed.Errors.Select(error => "- " + error)));
            }

            var runnableWork = parsed.ValidActions
                .Where(action => string.Equals(
                    action.Name,
                    "RUN_WORK",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (runnableWork.Length > 0)
            {
                var runnableScopes = runnableWork
                    .Select(action =>
                    {
                        var id = action.GetSingle("WORK_ITEM_ID");
                        return id is not null &&
                               milestone.WorkItems.TryGetValue(id, out var definition)
                            ? definition.WritePaths
                            : (IReadOnlyList<string>)Array.Empty<string>();
                    })
                    .ToArray();
                var allRunnableScopes = runnableScopes
                    .SelectMany(scopes => scopes)
                    .ToArray();
                var overlappingScopes =
                    MilestoneMechanicalExecutor.HasOverlappingScopes(
                        runnableScopes);
                var maxConcurrency = overlappingScopes
                    ? 1
                    : Math.Clamp(
                        _targetSettings.EffectiveMaxConcurrentWork,
                        WorkerTargetConfiguration.MinimumConcurrentWork,
                        WorkerTargetConfiguration.MaximumConcurrentWork);
                if (overlappingScopes)
                {
                    feedback.Add(
                        "WORK_CONCURRENCY_REDUCED: 동일/상위·하위 WRITE_PATH가 겹치는 RUN_WORK 요청이 있어 동시에 실행하지 않고 순차 실행합니다.");
                }

                var workBatchBefore =
                    await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        workingDirectory,
                        cancellationToken);
                using var gate = new SemaphoreSlim(maxConcurrency);
                var workTasks = runnableWork.Select(async action =>
                {
                    await gate.WaitAsync(cancellationToken);
                    try
                    {
                        return await ExecuteMilestoneWorkAsync(
                            jobId,
                            workingDirectory,
                            milestone,
                            action,
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
                            Math.Min(runnableWork.Length, maxConcurrency)));

                var completed = await Task.WhenAll(workTasks);

                RunOnUi(() =>
                    ImplementerWorkGaugeText.Text =
                        FormatActiveWorkItemGauge(0));

                foreach (var item in completed)
                {
                    workReports[item.Id] = item.Report;
                    feedback.Add(
                        $"WORK {item.Id}:" +
                        Environment.NewLine +
                        item.Report);
                }

                var workBatchAfter =
                    await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        workingDirectory,
                        cancellationToken);
                var observedWorkChanges =
                    MilestoneMechanicalExecutor.DiffChangeStates(
                        workBatchBefore,
                        workBatchAfter);
                foreach (var changedPath in observedWorkChanges)
                {
                    if (MilestoneMechanicalExecutor.IsPathWithinScopes(
                            changedPath,
                            allRunnableScopes))
                    {
                        milestoneChangedPaths.Add(changedPath);
                    }
                }

                if (observedWorkChanges.Count > 0)
                {
                    feedback.Add(
                        "WORK_BATCH_CHANGED_PATHS:" +
                        Environment.NewLine +
                        string.Join(
                            Environment.NewLine,
                            observedWorkChanges.Select(path => "- " + path)));
                }

                validationCompleted = false;
                gitFinalizeAttempted = false;
            }

            foreach (var action in parsed.ValidActions.Where(action =>
                         string.Equals(
                             action.Name,
                             "RUN_RESOURCE",
                             StringComparison.OrdinalIgnoreCase)))
            {
                var resourceResult = await ExecuteMilestoneResourceAsync(
                    workingDirectory,
                    milestone,
                    action,
                    cancellationToken);
                resourceReports[resourceResult.Id] = resourceResult.Report;
                foreach (var changedPath in resourceResult.ChangedPaths)
                    milestoneChangedPaths.Add(changedPath);
                feedback.Add(
                    $"RESOURCE {resourceResult.Id}:" +
                    Environment.NewLine +
                    resourceResult.Report);
                validationCompleted = false;
                gitFinalizeAttempted = false;
            }

            foreach (var action in parsed.ValidActions.Where(action =>
                         string.Equals(
                             action.Name,
                             "MECHANICAL",
                             StringComparison.OrdinalIgnoreCase)))
            {
                RunOnUi(() =>
                {
                    TaskDirection.Text = "통합";
                    TaskTitle.Text = "중간관리자 기계 실행";
                    SetFlowState(
                        codexActive: false,
                        workerActive: true,
                        webActive: false,
                        explicitStage: TaskStage.Manager);
                });

                var operation = action.GetSingle("OPERATION") ?? "UNKNOWN";
                var result = string.Equals(
                        operation,
                        "RUN",
                        StringComparison.OrdinalIgnoreCase)
                    ? await MilestoneManagedRunRegistry.StartAsync(
                        jobId,
                        workingDirectory,
                        action.Body,
                        cancellationToken)
                    : await MilestoneMechanicalExecutor.ExecuteAsync(
                        jobId,
                        workingDirectory,
                        operation,
                        action.Body,
                        cancellationToken);
                var report =
                    MilestoneDefinitionContract.FormatMechanicalResult(result);
                mechanicalReports.Add(report);
                feedback.Add(report);
                validationCompleted = false;
                gitFinalizeAttempted = false;
            }

            var pause = parsed.ValidActions.FirstOrDefault(action =>
                string.Equals(
                    action.Name,
                    "PAUSE",
                    StringComparison.OrdinalIgnoreCase));
            if (pause is not null)
            {
                await MilestoneManagedRunRegistry.StopAsync(
                    jobId,
                    CancellationToken.None);
                return new(true, pause.Body);
            }

            if (parsed.ValidActions.Any(action =>
                    string.Equals(
                        action.Name,
                        "READY_FOR_VALIDATION",
                        StringComparison.OrdinalIgnoreCase)))
            {
                var missingWork = milestone.WorkItems.Keys
                    .Where(id =>
                        !workReports.TryGetValue(id, out var report) ||
                        !MilestoneDefinitionContract.IsTerminalWorkReport(report))
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var missingResources = milestone.Resources.Keys
                    .Where(id => !resourceReports.ContainsKey(id))
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (missingWork.Length > 0 || missingResources.Length > 0)
                {
                    var missing = new List<string>();
                    if (missingWork.Length > 0)
                        missing.Add("미종료 WORK: " + string.Join(", ", missingWork));
                    if (missingResources.Length > 0)
                        missing.Add("미종료 RESOURCE: " + string.Join(", ", missingResources));

                    feedback.Add(
                        "READY_FOR_VALIDATION_BLOCKED:" +
                        Environment.NewLine +
                        string.Join(Environment.NewLine, missing));
                }
                else
                {
                    validationRound++;

                    if (milestone.QaReserved)
                {
                    qaReport = await ExecuteMilestoneQaAsync(
                        jobId,
                        workingDirectory,
                        milestone,
                        workReports,
                        resourceReports,
                        mechanicalReports,
                        qa,
                        validationRound,
                        cancellationToken);
                    feedback.Add(
                        "QA_REPORT:" +
                        Environment.NewLine +
                        qaReport);
                }
                else
                {
                    qaReport = string.Empty;
                    feedback.Add(
                        "QA_REPORT: HQ가 QA=NO로 예약하지 않아 실행하지 않음");
                }

                highReport = await ExecuteMilestoneHighAsync(
                    jobId,
                    workingDirectory,
                    milestone,
                    workReports,
                    resourceReports,
                    mechanicalReports,
                    qaReport,
                    high,
                    validationRound,
                    cancellationToken);
                var highChangedPaths =
                    MilestoneDefinitionContract.ExtractReportPaths(
                        highReport,
                        "CHANGED_PATH");
                foreach (var changedPath in highChangedPaths)
                {
                    if (MilestoneDefinitionContract.IsSafeRelativePath(changedPath))
                        milestoneChangedPaths.Add(changedPath);
                }
                if (highChangedPaths.Count > 0)
                {
                    feedback.Add(
                        "HIGH_CHANGED_PATHS:" +
                        Environment.NewLine +
                        string.Join(
                            Environment.NewLine,
                            highChangedPaths.Select(path => "- " + path)));
                }
                    feedback.Add(
                        "HIGH_REPORT:" +
                        Environment.NewLine +
                        highReport);

                    var stoppedRun =
                        await MilestoneManagedRunRegistry.StopAsync(
                            jobId,
                            CancellationToken.None);
                    if (stoppedRun is not null)
                    {
                        var stoppedRunReport =
                            MilestoneDefinitionContract.FormatMechanicalResult(
                                stoppedRun);
                        mechanicalReports.Add(stoppedRunReport);
                        feedback.Add(stoppedRunReport);
                    }

                    validationCompleted = true;
                    gitFinalizeAttempted = false;
                }
            }

            if (parsed.ValidActions.Any(action =>
                    string.Equals(
                        action.Name,
                        "GIT_FINALIZE",
                        StringComparison.OrdinalIgnoreCase)))
            {
                if (!validationCompleted)
                {
                    feedback.Add(
                        "GIT_FINALIZE_BLOCKED: READY_FOR_VALIDATION → HIGH 검토가 먼저 필요합니다.");
                }
                else
                {
                    gitResult =
                        await MilestoneMechanicalExecutor.FinalizeGitAsync(
                            workingDirectory,
                            milestone,
                            milestoneChangedPaths,
                            cancellationToken);
                    feedback.Add(
                        "GIT_FINALIZE_RESULT:" +
                        Environment.NewLine +
                        gitResult.Summary);

                    if (gitResult.PauseRequired)
                        return new(true, gitResult.Summary);

                    gitFinalizeAttempted = true;
                }
            }

            if (feedback.Count == 0)
            {
                feedback.Add(
                    "실행 가능한 정상 MANAGER ACTION이 없습니다. " +
                    "독립 ACTION 블록 계약에 맞춰 다시 지시하세요.");
            }

            managerInput = MilestoneDefinitionContract.BuildManagerInput(
                milestone,
                workReports,
                resourceReports,
                mechanicalReports,
                qaReport,
                highReport,
                gitResult,
                string.Join(
                    Environment.NewLine + Environment.NewLine,
                    feedback));
        }

        throw new InvalidOperationException("MANAGER_ROUND_LIMIT_EXCEEDED");
    }

    private async Task<WorkExecutionReport> ExecuteMilestoneWorkAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        ActionBlock action,
        WorkerAiRoleSettings implementer,
        CancellationToken cancellationToken)
    {
        var workId = action.GetSingle("WORK_ITEM_ID") ?? string.Empty;
        if (!milestone.WorkItems.TryGetValue(workId, out var work))
        {
            return new(
                workId,
                "WORK_ITEM_STATUS: BLOCKED" +
                Environment.NewLine +
                "계획되지 않은 WORK_ITEM_ID입니다.");
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

        var workTempRoot = Path.Combine(
            workingDirectory,
            "temp",
            "ProjectHub",
            jobId,
            "work-" + work.Id);
        Directory.CreateDirectory(workTempRoot);

        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            work.Id,
            work.Body,
            work.WritePaths,
            workingDirectory,
            resourceStagingRoot: Path.Combine(
                workingDirectory,
                "temp",
                "Resource"),
            workTempRoot: workTempRoot);

        var statelessRole = implementer with
        {
            ThreadSessionId = null,
            ThreadProjectPath = null
        };

        var result = await RunCoordinatorRoleAsync(
            jobId,
            "WORK",
            prompt,
            statelessRole,
            workingDirectory,
            null,
            null,
            cancellationToken,
            CodexSandboxMode.WorkspaceWrite);

        var forbiddenExecution = result.CommandExecutions
            .FirstOrDefault(execution =>
                BuildExecutionPolicy.IsGeneralWorkForbiddenCommand(
                    execution.Command));
        var report = forbiddenExecution is null
            ? MilestoneDefinitionContract.NormalizeWorkReport(
                result.ExitCode,
                result.FinalMessage,
                result.StandardError)
            : "[GOTO : MANAGER]" +
              Environment.NewLine +
              "WORK_ITEM_STATUS: BLOCKED" +
              Environment.NewLine +
              BuildExecutionPolicy.GeneralWorkCommandForbiddenError +
              Environment.NewLine +
              "command=" +
              forbiddenExecution.Command;

        AddRoleResponseHistory(
            WorkerRoleState.Work,
            "작업 응답",
            report,
            result.Usage,
            result.Files,
            status: result.ExitCode == 0 ? "RECEIVED" : "FAILED",
            providerWireId: implementer.Provider,
            fullMessage: report,
            workItemId: work.Id,
            referenceId: work.Id);

        return new(work.Id, report);
    }

    private async Task<ResourceExecutionReport> ExecuteMilestoneResourceAsync(
        string workingDirectory,
        MilestoneDefinition milestone,
        ActionBlock action,
        CancellationToken cancellationToken)
    {
        var resourceId = action.GetSingle("RESOURCE_ID") ?? string.Empty;
        if (!milestone.Resources.TryGetValue(resourceId, out var resource))
        {
            return new(
                resourceId,
                "RESOURCE_STATUS: BLOCKED" +
                Environment.NewLine +
                "계획되지 않은 RESOURCE_ID입니다.",
                Array.Empty<string>());
        }

        RunOnUi(() =>
        {
            TaskDirection.Text = "작업";
            TaskTitle.Text = "RESOURCE 생성";
            ResultTitle.Text = "RESOURCE";
            SetFlowState(
                codexActive: false,
                workerActive: true,
                webActive: true,
                explicitStage: TaskStage.Resource);
        });

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
                status: "BLOCKED");
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
                status: "BLOCKED");
            return new(resource.Id, failedReport, Array.Empty<string>());
        }

        var moveResult = MoveResourceResults(
            workingDirectory,
            resource.TargetPath,
            completion.SavedPaths);
        AddRoleResponseHistory(
            WorkerRoleState.Resource,
            "리소스 반영",
            moveResult.Report,
            status: moveResult.Report.StartsWith(
                "RESOURCE_STATUS: COMPLETED",
                StringComparison.Ordinal)
                    ? "COMPLETED"
                    : "BLOCKED");
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
        WorkerAiRoleSettings qa,
        int validationRound,
        CancellationToken cancellationToken)
    {
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

        var body = MilestoneDefinitionContract.BuildValidationContext(
            milestone,
            workReports,
            resourceReports,
            mechanicalReports,
            qaReport: null);

        var result = await RunCoordinatorRoleAsync(
            jobId,
            "QA",
            RoleContractLoader.BuildQaPrompt(body),
            qa,
            workingDirectory,
            null,
            null,
            cancellationToken,
            CodexSandboxMode.ReadOnly);

        var report = MilestoneDefinitionContract.NormalizeQaReport(
            result.ExitCode,
            result.FinalMessage,
            result.StandardError);

        AddRoleResponseHistory(
            WorkerRoleState.Qa,
            "QA 조사 결과",
            report,
            result.Usage,
            result.Files,
            status: result.ExitCode == 0 ? "RECEIVED" : "BLOCKED",
            providerWireId: qa.Provider,
            fullMessage: report);

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
        WorkerAiRoleSettings high,
        int validationRound,
        CancellationToken cancellationToken)
    {
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

        var body = MilestoneDefinitionContract.BuildValidationContext(
            milestone,
            workReports,
            resourceReports,
            mechanicalReports,
            qaReport);

        var result = await RunCoordinatorRoleAsync(
            jobId,
            "HIGH",
            RoleContractLoader.BuildHighPrompt(body),
            high,
            workingDirectory,
            null,
            null,
            cancellationToken,
            CodexSandboxMode.DangerFullAccess);

        var report = MilestoneDefinitionContract.NormalizeHighReport(
            result.ExitCode,
            result.FinalMessage,
            result.StandardError);

        AddRoleResponseHistory(
            WorkerRoleState.High,
            "검토 결과",
            report,
            result.Usage,
            result.Files,
            status: result.ExitCode == 0 ? "RECEIVED" : "INCOMPLETE",
            providerWireId: high.Provider,
            fullMessage: report);

        return report;
    }

    private static string BuildMilestoneHqPrompt(
        string inboundType,
        string body,
        string workingDirectory) =>
        "역할: HQ" +
        Environment.NewLine +
        $"입력 유형: {inboundType}" +
        Environment.NewLine +
        $"실제 프로젝트 루트: {workingDirectory}" +
        Environment.NewLine +
        "사용자가 지정한 실제 프로젝트 루트 하나를 공통 작업공간으로 사용하고 GENERAL WORK별 WRITE_PATH를 명확히 지정한다." +
        Environment.NewLine +
        "동시에 실행할 GENERAL WORK의 WRITE_PATH가 겹치지 않게 설계한다." +
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

    private static string BuildManagerMechanicalSupplement(
        string workingDirectory) =>
        "Worker 기계 실행 보충 계약:" +
        Environment.NewLine +
        "- MECHANICAL ACTION의 BODY에는 실행할 단일 Windows 명령을 COMMAND: ... 한 줄로 반드시 포함한다." +
        Environment.NewLine +
        "- BUILD/PUBLISH 명령은 최신 결과를 프로젝트 루트의 bin에 만들도록 작성한다." +
        Environment.NewLine +
        "- 프로젝트 루트: " + workingDirectory +
        Environment.NewLine +
        "- RUN은 보고 전에 종료 가능한 foreground 실행만 요청한다." +
        Environment.NewLine +
        "- 명령 자체가 설계 변경을 만들지 않게 한다.";

    private static bool IsManagerFinalReport(string message)
    {
        var first = (message ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?.Trim();

        return first is not null &&
               Regex.IsMatch(
                   first,
                   @"^\[GOTO\s*:\s*HQ\]$",
                   RegexOptions.IgnoreCase |
                   RegexOptions.CultureInvariant);
    }

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
