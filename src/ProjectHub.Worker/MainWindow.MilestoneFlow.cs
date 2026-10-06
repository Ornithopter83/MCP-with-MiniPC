using System.Text;
using System.Text.RegularExpressions;
using System.IO;

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
                    normalizedRoot,
                    includeFullContract:
                        !continuing && milestoneIndex == 1);

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
                    var repairResponse =
                        await ExecuteHqJsonRepairWorkAsync(
                            jobId,
                            normalizedRoot,
                            hqMessage,
                            milestoneError,
                            implementer,
                            cts.Token);
                    var repairError = string.Empty;

                    if (!string.IsNullOrWhiteSpace(repairResponse) &&
                        HqJsonRepairContract.TryWrapWorkJson(
                            repairResponse,
                            out var repairedHqMessage,
                            out repairError))
                    {
                        var repairedParse =
                            ActionBlockContract.ParseHq(repairedHqMessage);
                        if (MilestoneDefinitionContract.TryBuild(
                                repairedHqMessage,
                                repairedParse,
                                out milestone,
                                out var repairedMilestoneError))
                        {
                            hqMessage = repairedHqMessage;
                            hqParse = repairedParse;
                            AddDataFlowHistory(
                                WorkerRoleState.Work,
                                "Worker 작업",
                                "HQ JSON 복구 성공\n기존 HQ 파서 재검증 완료",
                                status: "REPAIRED",
                                persistenceSource: "WORKER ACTION");
                        }
                        else
                        {
                            repairError =
                                "HQ_JSON_REPAIR_REPARSE_FAILED" +
                                Environment.NewLine +
                                repairedMilestoneError;
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(repairError))
                    {
                        repairError = "HQ_JSON_REPAIR_WORK_FAILED";
                    }

                    if (milestone is null)
                    {
                        hqInbound =
                            "HQ ACTION/JSON 계약 오류입니다. " +
                            "복구 전용 WORK가 JSON 문법 교정을 시도했지만 기존 HQ 파서를 통과하지 못했습니다. " +
                            "[ACTION=WORK] 바로 다음의 단일 JSON 객체에 실행 가능한 milestone 전체를 다시 작성하세요." +
                            Environment.NewLine +
                            milestoneError +
                            Environment.NewLine +
                            "JSON_REPAIR_RESULT:" +
                            Environment.NewLine +
                            repairError +
                            Environment.NewLine +
                            "이전 응답:" +
                            Environment.NewLine +
                            hqMessage;
                        continuing = false;
                        continue;
                    }
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
                        $"HQ 응답 파싱 완료\nMILESTONE: {milestone.Id}\nWORK: {milestone.WorkItems.Count}건\nRESOURCE: {milestone.Resources.Count}건\nQA: {(milestone.QaReserved ? "예약" : "미예약")}",
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

                MilestoneManagerResult managerReport;
                try
                {
                    managerReport = await RunSingleMilestoneAsync(
                        jobId,
                        normalizedRoot,
                        milestone!,
                        implementer,
                        manager,
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
                        managerFinalState: "FAILED",
                        hqFinalState: "BLOCKED");

                    managerReport = new(
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
                            currentLocalChanges));

                    AddTaskMessage(
                        "MILESTONE ERROR",
                        failureMessage,
                        status: "BLOCKED");
                }

                if (managerReport.PauseRequired)
                {
                    SaveMilestoneExecutionGraph(
                        normalizedRoot,
                        jobId,
                        milestone!,
                        "PAUSED",
                        managerFinalState: "PAUSED",
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
        AddDataFlowHistory(
            WorkerRoleState.Unknown,
            "Worker 작업",
            $"마일스톤 사전 점검\nID: {milestone.Id}\nBRANCH: {milestone.TargetBranch}\nPOLICY: {(milestone.ReadOnlyNoFileChanges ? "READ_ONLY_NO_FILE_CHANGES" : "DEFAULT")}",
            status: "PROCESSING",
            persistenceSource: "WORKER ACTION");

        var gitPreflight = await MilestoneMechanicalExecutor.CheckGitReadyAsync(
            workingDirectory,
            milestone.TargetBranch,
            milestone.InitializeGitIfMissing,
            cancellationToken);
        if (!gitPreflight.Success)
            return new(true, gitPreflight.Summary);

        var milestoneChangedPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        // 현재 ProjectHub 작업이 지정된 쓰기 영역보다 우선한다.
        // 기존 dirty 변경은 시작을 차단하지 않으며, 지정되지 않은 경로는
        // milestoneChangedPaths에 들어오지 않으므로 stage 대상이 아니다.
        WorkerPaths.EnsureProjectHubLocalExclude(workingDirectory);

        var workReports = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var resourceReports = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var mechanicalReports = new List<string>();
        var qaReport = string.Empty;
        var highReport = string.Empty;
        var gitResult = MilestoneGitResult.NotStarted(milestone.TargetBranch);
        string? managerSession = null;

        SaveMilestoneExecutionGraph(
            workingDirectory,
            jobId,
            milestone,
            "RUNNING",
            workReports,
            resourceReports,
            qaReport,
            highReport,
            managerDispatchState: "RUNNING");

        async Task<MilestoneManagerResult> BuildFailureReportAsync(
            string failureDetail)
        {
            var stoppedRun = await MilestoneManagedRunRegistry.StopAsync(
                jobId,
                CancellationToken.None);
            if (stoppedRun is not null)
            {
                mechanicalReports.Add(
                    MilestoneDefinitionContract.FormatMechanicalResult(
                        stoppedRun));
            }

            var currentLocalChanges =
                await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                    workingDirectory,
                    CancellationToken.None);

            return new(
                false,
                MilestoneDefinitionContract.BuildHqReport(
                    milestone,
                    "MILESTONE_EXECUTION_ERROR" +
                    Environment.NewLine +
                    failureDetail,
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

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            RunOnUi(() =>
            {
                TaskDirection.Text = "통합/분배";
                TaskTitle.Text = $"중간관리자 일괄 분배 · {milestone.Id}";
                ResultTitle.Text = "MANAGER";
                SetFlowState(
                    codexActive: true,
                    workerActive: false,
                    webActive: false,
                    explicitStage: TaskStage.Manager);
            });

            var dispatchInput = MilestoneDefinitionContract.BuildManagerInput(
                milestone,
                workReports,
                resourceReports,
                mechanicalReports,
                qaReport,
                highReport,
                gitResult,
                "MILESTONE_START" +
                Environment.NewLine +
                "이번 응답에서 계획된 모든 WORK/RESOURCE를 일괄 분배하세요. " +
                "이후 개별 WORK 결과를 받을 때마다 MANAGER를 다시 호출하지 않습니다.");

            var dispatchPrompt = RoleContractLoader.BuildManagerPrompt(
                dispatchInput +
                Environment.NewLine +
                Environment.NewLine +
                BuildManagerMechanicalSupplement(workingDirectory),
                includeFullContract: true);

            var dispatchResult = await RunCoordinatorRoleAsync(
                jobId,
                "MANAGER",
                dispatchPrompt,
                manager,
                workingDirectory,
                managerSession,
                null,
                cancellationToken,
                CodexSandboxMode.ReadOnly);

            managerSession =
                CodexCliRunner.NormalizeSessionId(dispatchResult.SessionId) ??
                managerSession;

            if (dispatchResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "MANAGER_DISPATCH_FAILED: " +
                    dispatchResult.StandardError);
            }

            var dispatchMessage =
                dispatchResult.FinalMessage?.Trim() ?? string.Empty;
            AddRoleResponseHistory(
                WorkerRoleState.Manager,
                "통합 분배",
                dispatchMessage,
                dispatchResult.Usage,
                dispatchResult.Files,
                status: "RECEIVED",
                providerWireId: manager.Provider,
                fullMessage: dispatchMessage);

            var parsed = ActionBlockContract.ParseManager(dispatchMessage);
            var dispatchNotes = new List<string>();

            if (parsed.Errors.Count > 0)
            {
                dispatchNotes.Add(
                    "MANAGER_ACTION_PARSE_ERRORS:" +
                    Environment.NewLine +
                    string.Join(
                        Environment.NewLine,
                        parsed.Errors.Select(error => "- " + error)));
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

            var resourceActions = parsed.ValidActions
                .Where(action => string.Equals(
                    action.Name,
                    "RUN_RESOURCE",
                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(
                    action => action.GetSingle("RESOURCE_ID") ?? "0",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            foreach (var action in resourceActions)
            {
                var resourceId = action.GetSingle("RESOURCE_ID") ?? "0";
                if (!milestone.Resources.TryGetValue(
                        resourceId,
                        out var resourceDefinition))
                {
                    dispatchNotes.Add(
                        "UNPLANNED_RESOURCE_IGNORED: " + resourceId);
                    continue;
                }

                var dispatchBody =
                    action.RawText +
                    Environment.NewLine +
                    Environment.NewLine +
                    resourceDefinition.RawText;
                AddDataFlowHistory(
                    WorkerRoleState.Resource,
                    "Worker 분배",
                    dispatchBody,
                    status: "DISPATCHED",
                    workItemId: "0",
                    persistenceSource: "WORKER DISPATCH");

                var resourceResult = await ExecuteMilestoneResourceAsync(
                    workingDirectory,
                    milestone,
                    action,
                    cancellationToken);
                resourceReports[resourceResult.Id] = resourceResult.Report;
                foreach (var changedPath in resourceResult.ChangedPaths)
                    milestoneChangedPaths.Add(changedPath);
            }

            foreach (var resource in milestone.Resources.Values)
            {
                if (resourceReports.ContainsKey(resource.Id))
                    continue;

                resourceReports[resource.Id] =
                    "RESOURCE_STATUS: BLOCKED" +
                    Environment.NewLine +
                    "MANAGER_DID_NOT_DISPATCH";
            }

            var runnableWork = parsed.ValidActions
                .Where(action =>
                {
                    if (!string.Equals(
                            action.Name,
                            "RUN_WORK",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    var workId = action.GetSingle("WORK_ITEM_ID");
                    return workId is not null &&
                           milestone.WorkItems.ContainsKey(workId);
                })
                .GroupBy(
                    action => action.GetSingle("WORK_ITEM_ID")!,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            foreach (var action in runnableWork)
            {
                var workId = action.GetSingle("WORK_ITEM_ID")!;
                var definition = milestone.WorkItems[workId];
                AddDataFlowHistory(
                    WorkerRoleState.Work,
                    "Worker 분배",
                    action.RawText +
                    Environment.NewLine +
                    Environment.NewLine +
                    definition.RawText,
                    status: "DISPATCHED",
                    workItemId: workId,
                    persistenceSource: "WORKER DISPATCH");
            }

            if (runnableWork.Length > 0)
            {
                var runnableScopes = runnableWork
                    .Select(action =>
                        milestone.WorkItems[
                            action.GetSingle("WORK_ITEM_ID")!].WritePaths)
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
                    dispatchNotes.Add(
                        "WORK_CONCURRENCY_REDUCED: 동일/상위·하위 WRITE_PATH가 겹쳐 순차 실행합니다.");
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

                // 모든 WORKITEM을 분배한 뒤 마지막 항목이 terminal 될 때까지 기다린다.
                var completed = await Task.WhenAll(workTasks);

                RunOnUi(() =>
                    ImplementerWorkGaugeText.Text =
                        FormatActiveWorkItemGauge(0));

                foreach (var item in completed)
                    workReports[item.Id] = item.Report;

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
                    dispatchNotes.Add(
                        "WORK_BATCH_CHANGED_PATHS:" +
                        Environment.NewLine +
                        string.Join(
                            Environment.NewLine,
                            observedWorkChanges.Select(path => "- " + path)));
                }
            }

            foreach (var work in milestone.WorkItems.Values)
            {
                if (workReports.ContainsKey(work.Id))
                    continue;

                workReports[work.Id] =
                    "[GOTO : MANAGER]" +
                    Environment.NewLine +
                    "WORK_ITEM_STATUS: BLOCKED" +
                    Environment.NewLine +
                    "MANAGER_DID_NOT_DISPATCH";
            }

            foreach (var action in parsed.ValidActions.Where(action =>
                         string.Equals(
                             action.Name,
                             "MECHANICAL",
                             StringComparison.OrdinalIgnoreCase)))
            {
                RunOnUi(() =>
                {
                    TaskDirection.Text = "통합/분배";
                    TaskTitle.Text = "중간관리자 기계 실행";
                    SetFlowState(
                        codexActive: false,
                        workerActive: true,
                        webActive: false,
                        explicitStage: TaskStage.Manager);
                });

                var operation = action.GetSingle("OPERATION") ?? "UNKNOWN";
                AddDataFlowHistory(
                    WorkerRoleState.Unknown,
                    "Worker 작업",
                    action.RawText,
                    status: "EXECUTING",
                    persistenceSource: "WORKER ACTION");

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

                mechanicalReports.Add(
                    MilestoneDefinitionContract.FormatMechanicalResult(result));
            }

            SaveMilestoneExecutionGraph(
                workingDirectory,
                jobId,
                milestone,
                "WORK_COMPLETED",
                workReports,
                resourceReports,
                qaReport,
                highReport,
                managerDispatchState: "COMPLETED");

            AddDataFlowHistory(
                WorkerRoleState.Unknown,
                "Worker 작업",
                $"작업 묶음 종료\nWORK: {workReports.Count}/{milestone.WorkItems.Count} terminal\nRESOURCE: {resourceReports.Count}/{milestone.Resources.Count} terminal\n다음 단계: {(milestone.QaReserved ? "QA" : "HIGH")}",
                status: "COMPLETED",
                persistenceSource: "WORKER ACTION");

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
                    1,
                    cancellationToken);
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
                1,
                cancellationToken);

            foreach (var changedPath in
                     MilestoneDefinitionContract.ExtractHighChangedPaths(
                         highReport))
            {
                milestoneChangedPaths.Add(changedPath);
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

            SaveMilestoneExecutionGraph(
                workingDirectory,
                jobId,
                milestone,
                "VALIDATED",
                workReports,
                resourceReports,
                qaReport,
                highReport,
                managerDispatchState: "COMPLETED");

            AddDataFlowHistory(
                WorkerRoleState.Unknown,
                "Worker 작업",
                "GIT_FINALIZE 자동 실행",
                status: "EXECUTING",
                persistenceSource: "WORKER ACTION");

            gitResult = await MilestoneMechanicalExecutor.FinalizeGitAsync(
                workingDirectory,
                milestone,
                milestoneChangedPaths,
                cancellationToken);

            AddDataFlowHistory(
                WorkerRoleState.Unknown,
                "Worker 작업",
                "GIT_FINALIZE 결과" +
                Environment.NewLine +
                gitResult.Summary,
                status: gitResult.Success ? "COMPLETED" : "FAILED",
                persistenceSource: "WORKER ACTION");

            SaveMilestoneExecutionGraph(
                workingDirectory,
                jobId,
                milestone,
                gitResult.Success ? "FINALIZED" : "FINALIZE_FAILED",
                workReports,
                resourceReports,
                qaReport,
                highReport,
                managerDispatchState: "COMPLETED");

            RunOnUi(() =>
            {
                TaskDirection.Text = "통합/보고";
                TaskTitle.Text = $"중간관리자 최종 보고 · {milestone.Id}";
                ResultTitle.Text = "MANAGER";
                SetFlowState(
                    codexActive: true,
                    workerActive: false,
                    webActive: false,
                    explicitStage: TaskStage.Manager);
            });

            var finalEvent = new StringBuilder();
            finalEvent.AppendLine("FINAL_REPORT_REQUIRED");
            finalEvent.AppendLine(
                "모든 WORK/RESOURCE가 terminal 상태이고 QA/HIGH와 Git finalize까지 끝났습니다.");
            finalEvent.AppendLine(
                "추가 WORK, RESOURCE, MECHANICAL, 재검증을 요청하지 말고 지시사항 대비 실제 결과를 무조건 HQ에 보고하세요.");
            if (dispatchNotes.Count > 0)
            {
                finalEvent.AppendLine("DISPATCH_NOTES:");
                foreach (var note in dispatchNotes)
                    finalEvent.AppendLine(note);
            }

            var finalInput = MilestoneDefinitionContract.BuildManagerInput(
                milestone,
                workReports,
                resourceReports,
                mechanicalReports,
                qaReport,
                highReport,
                gitResult,
                finalEvent.ToString());

            var finalPrompt = RoleContractLoader.BuildManagerPrompt(
                finalInput,
                includeFullContract: false);

            var finalResult = await RunCoordinatorRoleAsync(
                jobId,
                "MANAGER",
                finalPrompt,
                manager,
                workingDirectory,
                managerSession,
                null,
                cancellationToken,
                CodexSandboxMode.ReadOnly);

            var managerMessage = finalResult.ExitCode == 0
                ? finalResult.FinalMessage?.Trim() ?? string.Empty
                : "MANAGER_FINAL_EXECUTION_FAILED" +
                  Environment.NewLine +
                  finalResult.StandardError;

            if (finalResult.ExitCode == 0 &&
                !IsManagerFinalReport(managerMessage))
            {
                managerMessage =
                    "MANAGER_FINAL_REPORT_CONTRACT_INVALID" +
                    Environment.NewLine +
                    "원본 응답:" +
                    Environment.NewLine +
                    managerMessage;
            }

            AddRoleResponseHistory(
                WorkerRoleState.Manager,
                "통합 최종 보고",
                managerMessage,
                finalResult.Usage,
                finalResult.Files,
                status: finalResult.ExitCode == 0 ? "RECEIVED" : "FAILED",
                providerWireId: manager.Provider,
                fullMessage: managerMessage);

            var currentLocalChanges =
                await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                    workingDirectory,
                    cancellationToken);

            SaveMilestoneExecutionGraph(
                workingDirectory,
                jobId,
                milestone,
                "REPORT_READY",
                workReports,
                resourceReports,
                qaReport,
                highReport,
                managerDispatchState: "COMPLETED",
                managerFinalState: "COMPLETED",
                hqFinalState: "READY");

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
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await BuildFailureReportAsync(
                exception.GetType().Name +
                ": " +
                exception.Message);
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
        string managerDispatchState = "PLANNED",
        string managerFinalState = "PLANNED",
        string hqFinalState = "PLANNED")
    {
        workReports ??= new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        resourceReports ??= new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        var nodes = new List<MilestoneGraphNodeSnapshot>
        {
            new("HQ-DESIGN", "HQ", "COMPLETED"),
            new("MANAGER-DISPATCH", "MANAGER", managerDispatchState)
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
            nodes.Add(new(
                "RESOURCE-" + resource.Id,
                "RESOURCE",
                resourceReports.ContainsKey(resource.Id) ? "COMPLETED" : "PLANNED",
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
            !string.IsNullOrWhiteSpace(highReport)
                ? "COMPLETED"
                : "PLANNED"));
        nodes.Add(new(
            "MANAGER-FINAL",
            "MANAGER",
            managerFinalState));
        nodes.Add(new(
            "HQ-FINAL",
            "HQ",
            hqFinalState));

        var edges = new List<MilestoneGraphEdgeSnapshot>
        {
            new("HQ-DESIGN", "MANAGER-DISPATCH", "DESIGN_TO_EXECUTION")
        };

        var executionNodes = milestone.WorkItems.Values
            .Select(work => "WORK-" + work.Id)
            .Concat(milestone.Resources.Values.Select(resource => "RESOURCE-" + resource.Id))
            .ToArray();

        if (executionNodes.Length == 0)
        {
            edges.Add(new(
                "MANAGER-DISPATCH",
                milestone.QaReserved ? "QA" : "HIGH",
                "VALIDATION"));
        }
        else
        {
            foreach (var executionNode in executionNodes)
            {
                edges.Add(new(
                    "MANAGER-DISPATCH",
                    executionNode,
                    "DISPATCH"));
                edges.Add(new(
                    executionNode,
                    milestone.QaReserved ? "QA" : "HIGH",
                    "RESULT_TO_VALIDATION"));
            }
        }

        if (milestone.QaReserved)
            edges.Add(new("QA", "HIGH", "QA_TO_REVIEW"));
        edges.Add(new("HIGH", "MANAGER-FINAL", "REVIEW_TO_INTEGRATION"));
        edges.Add(new("MANAGER-FINAL", "HQ-FINAL", "REPORT_TO_HQ"));

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

    private async Task<string?> ExecuteHqJsonRepairWorkAsync(
        string jobId,
        string workingDirectory,
        string hqMessage,
        string parserError,
        WorkerAiRoleSettings implementer,
        CancellationToken cancellationToken)
    {
        RunOnUi(() =>
        {
            TaskDirection.Text = "작업";
            TaskTitle.Text = "HQ JSON 복구";
            ResultTitle.Text = "WORK";
            SetFlowState(
                codexActive: true,
                workerActive: false,
                webActive: false,
                explicitStage: TaskStage.Implementer);
        });

        var prompt = HqJsonRepairContract.BuildPrompt(
            hqMessage,
            parserError);
        AddDataFlowHistory(
            WorkerRoleState.Work,
            "Worker 분배",
            prompt,
            status: "DISPATCHED",
            workItemId: "HQ-JSON-REPAIR",
            persistenceSource: "WORKER DISPATCH");

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
            CodexSandboxMode.ReadOnly,
            historyWorkItemId: "HQ-JSON-REPAIR",
            historyReferenceId: "HQ-JSON-REPAIR");

        var response = result.FinalMessage?.Trim() ?? string.Empty;
        AddRoleResponseHistory(
            WorkerRoleState.Work,
            "HQ JSON 복구 응답",
            response,
            result.Usage,
            result.Files,
            status: result.ExitCode == 0 ? "RECEIVED" : "FAILED",
            providerWireId: implementer.Provider,
            fullMessage: response,
            workItemId: "HQ-JSON-REPAIR",
            referenceId: "HQ-JSON-REPAIR");

        return result.ExitCode == 0 && response.Length > 0
            ? response
            : null;
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
            work.ReadOnly
                ? CodexSandboxMode.ReadOnly
                : CodexSandboxMode.WorkspaceWrite,
            historyWorkItemId: work.Id,
            historyReferenceId: work.Id);

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
            milestone.ReadOnlyNoFileChanges
                ? CodexSandboxMode.ReadOnly
                : CodexSandboxMode.DangerFullAccess);

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
        string workingDirectory,
        bool includeFullContract) =>
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
        (includeFullContract
            ? RoleContractLoader.LoadHqFooter()
            : RoleContractLoader.BuildContractReference(
                RoleContractLoader.HqContractPath));

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
