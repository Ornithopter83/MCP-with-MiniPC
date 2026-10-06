using System.Text.Json;
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
                    remoteReference.CommitSha,
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
                MilestoneDefinition? milestone = null;
                var milestoneError = hqParse.HasErrors
                    ? string.Join(", ", hqParse.Errors)
                    : string.Empty;

                ActionBlock? hqAction =
                    !hqParse.HasErrors &&
                    hqParse.ValidActions.Count == 1
                        ? hqParse.ValidActions[0]
                        : null;

                if (hqAction is not null &&
                    string.Equals(
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
                            status: "PAUSED",
                            includeHistory: false);
                        SetFlowState(false, false, false);
                        SetFollowupComposerVisible(true);
                        SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
                    });
                    return;
                }

                if (hqAction is not null &&
                    string.Equals(
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

                if (hqAction is not null &&
                    string.Equals(
                        hqAction.Name,
                        "WORK",
                        StringComparison.OrdinalIgnoreCase))
                {
                    MilestoneDefinitionContract.TryBuild(
                        hqMessage,
                        hqParse,
                        out milestone,
                        out milestoneError);
                }

                if (milestone is null)
                {
                    var elementScan =
                        RoleElementRecoveryContract.Scan(
                            "HQ",
                            hqMessage,
                            expectedAction: "WORK",
                            contractError: milestoneError);
                    var recoveryTargets =
                        elementScan.RecoveryTargets.ToHashSet(
                            StringComparer.OrdinalIgnoreCase);

                    if (string.Equals(
                            elementScan.ActionName,
                            "PAUSE",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            elementScan.ActionName,
                            "END",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        var repairedControl =
                            await ExecuteRoleJsonRepairWorkAsync(
                                jobId,
                                normalizedRoot,
                                "HQ",
                                hqMessage,
                                milestoneError,
                                elementScan.ActionName,
                                implementer,
                                cts.Token);

                        if (!string.IsNullOrWhiteSpace(repairedControl))
                        {
                            var controlParse =
                                ActionBlockContract.ParseHq(repairedControl);
                            if (!controlParse.HasErrors &&
                                controlParse.ValidActions.Count == 1)
                            {
                                hqMessage = repairedControl;
                                hqParse = controlParse;
                                hqAction = controlParse.ValidActions[0];

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
                                    ProjectWorkspacePersistence.ClearContinuation(
                                        normalizedRoot);
                                    return;
                                }
                            }
                        }
                    }
                    else
                    {
                        IReadOnlyDictionary<string, string> recoveredByWork =
                            new Dictionary<string, string>(
                                StringComparer.OrdinalIgnoreCase);

                        if (recoveryTargets.Count > 0)
                        {
                            var recoveryResponse =
                                await ExecuteRoleElementRecoveryWorkAsync(
                                    jobId,
                                    normalizedRoot,
                                    "HQ",
                                    "WORK",
                                    hqMessage,
                                    milestoneError,
                                    recoveryTargets,
                                    elementScan.Recovered.Keys.ToArray(),
                                    implementer,
                                    cts.Token);

                            if (!string.IsNullOrWhiteSpace(recoveryResponse))
                            {
                                recoveredByWork =
                                    RoleElementRecoveryContract
                                        .ReadRecoveredElements(
                                            recoveryResponse,
                                            recoveryTargets);

                                foreach (var recoveredName in recoveredByWork.Keys)
                                    recoveryTargets.Remove(recoveredName);
                            }
                        }

                        if (RoleElementRecoveryContract.TryBuildHqWorkResponse(
                                elementScan,
                                recoveredByWork,
                                out var mergedHqMessage,
                                out var remainingElements))
                        {
                            var mergedParse =
                                ActionBlockContract.ParseHq(mergedHqMessage);
                            if (!mergedParse.HasErrors &&
                                MilestoneDefinitionContract.TryBuild(
                                    mergedHqMessage,
                                    mergedParse,
                                    out milestone,
                                    out milestoneError))
                            {
                                hqMessage = mergedHqMessage;
                                hqParse = mergedParse;
                                hqAction = mergedParse.ValidActions[0];
                                AddDataFlowHistory(
                                    WorkerRoleState.Work,
                                    "Worker 작업",
                                    "HQ element 복구 성공" +
                                    Environment.NewLine +
                                    $"기계 확보: {elementScan.Recovered.Count}" +
                                    Environment.NewLine +
                                    $"WORK 복구: {recoveredByWork.Count}",
                                    status: "REPAIRED",
                                    persistenceSource: "WORKER ACTION");
                            }
                            else
                            {
                                foreach (var inferred in
                                         RoleElementRecoveryContract.Scan(
                                             "HQ",
                                             mergedHqMessage,
                                             expectedAction: "WORK",
                                             contractError: milestoneError)
                                             .RecoveryTargets)
                                {
                                    recoveryTargets.Add(inferred);
                                }
                            }
                        }
                        else
                        {
                            foreach (var remaining in remainingElements)
                                recoveryTargets.Add(remaining);
                        }
                    }

                    if (milestone is null)
                    {
                        var retryPrompt =
                            RoleElementRecoveryContract.BuildHqFullRetryPrompt(
                                recoveryTargets,
                                milestoneError);

                        AddDataFlowHistory(
                            WorkerRoleState.Hq,
                            "Worker 재요청",
                            "HQ 전체 오더 재요청" +
                            Environment.NewLine +
                            "미확보 element:" +
                            Environment.NewLine +
                            (recoveryTargets.Count == 0
                                ? "- strict validation 실패"
                                : string.Join(
                                    Environment.NewLine,
                                    recoveryTargets.Select(
                                        name => "- " + name))),
                            status: "RETRY",
                            persistenceSource: "WORKER ACTION");

                        var retryResult = await RunHqRoleAsync(
                            jobId,
                            "HQ_MILESTONE_RECOVERY",
                            retryPrompt,
                            coordinator,
                            normalizedRoot,
                            hqSession,
                            cts.Token,
                            sessionStarted: session =>
                                hqSession =
                                    CodexCliRunner.NormalizeSessionId(session));

                        hqSession =
                            CodexCliRunner.NormalizeSessionId(
                                retryResult.SessionId) ?? hqSession;

                        if (retryResult.ExitCode != 0)
                        {
                            throw new InvalidOperationException(
                                "HQ_FULL_ORDER_RETRY_FAILED: " +
                                retryResult.StandardError);
                        }

                        hqMessage =
                            retryResult.FinalMessage?.Trim() ?? string.Empty;
                        AddRoleResponseHistory(
                            WorkerRoleState.Hq,
                            "마일스톤 설계 전체 재요청",
                            hqMessage,
                            retryResult.Usage,
                            retryResult.Files,
                            status: "RECEIVED",
                            providerWireId: coordinator.Provider,
                            fullMessage: hqMessage);

                        hqParse = ActionBlockContract.ParseHq(hqMessage);
                        if (hqParse.HasErrors ||
                            hqParse.ValidActions.Count != 1 ||
                            !string.Equals(
                                hqParse.ValidActions[0].Name,
                                "WORK",
                                StringComparison.OrdinalIgnoreCase) ||
                            !MilestoneDefinitionContract.TryBuild(
                                hqMessage,
                                hqParse,
                                out milestone,
                                out milestoneError))
                        {
                            throw new InvalidOperationException(
                                "HQ_MILESTONE_CONTRACT_INVALID_AFTER_FULL_RETRY: " +
                                milestoneError +
                                Environment.NewLine +
                                string.Join(", ", hqParse.Errors));
                        }

                        hqAction = hqParse.ValidActions[0];
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
            _targetSettings.ManualRepositoryUrl?.Trim(),
            cancellationToken);
        if (!gitPreflight.Success)
            return new(true, gitPreflight.Summary);

        var milestoneChangedPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
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
                "계획된 모든 GENERAL WORK를 DISPATCH JSON 하나로 일괄 분배하세요. RESOURCE는 Worker가 독립 대기열에서 처리하므로 DISPATCH에 포함하지 마세요.");

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

            var dispatchEnvelope =
                await EnsureRoleJsonResponseAsync(
                    jobId,
                    workingDirectory,
                    "MANAGER",
                    dispatchResult.FinalMessage?.Trim() ?? string.Empty,
                    new[] { "DISPATCH", "PAUSE" },
                    expectedGoto: null,
                    implementer,
                    cancellationToken);

            var dispatchMessage = dispatchEnvelope.Message;
            AddRoleResponseHistory(
                WorkerRoleState.Manager,
                "통합 분배",
                dispatchMessage,
                dispatchResult.Usage,
                dispatchResult.Files,
                status: dispatchEnvelope.Parse.HasErrors
                    ? "FAILED"
                    : "RECEIVED",
                providerWireId: manager.Provider,
                fullMessage: dispatchMessage);

            if (dispatchEnvelope.Parse.HasErrors ||
                dispatchEnvelope.Parse.ValidActions.Count != 1 ||
                !new[] { "DISPATCH", "PAUSE" }.Contains(
                    dispatchEnvelope.Parse.ValidActions[0].Name,
                    StringComparer.OrdinalIgnoreCase))
            {
                return await BuildFailureReportAsync(
                    "MANAGER_DISPATCH_CONTRACT_INVALID: " +
                    string.Join(", ", dispatchEnvelope.Parse.Errors));
            }

            var dispatchAction =
                dispatchEnvelope.Parse.ValidActions[0];

            if (string.Equals(
                    dispatchAction.Name,
                    "PAUSE",
                    StringComparison.OrdinalIgnoreCase))
            {
                await MilestoneManagedRunRegistry.StopAsync(
                    jobId,
                    CancellationToken.None);
                return new(true, dispatchAction.Body);
            }

            var requestedWorkIds =
                ActionBlockContract.GetIdArray(
                    dispatchAction,
                    "workItemIds")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var dispatchNotes = new List<string>();

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
                        workItemId: "0",
                        persistenceSource: "WORKER RESOURCE QUEUE");

                    return (
                        Id: resource.Id,
                        Task: ExecuteMilestoneResourceBackgroundAsync(
                            workingDirectory,
                            milestone,
                            resource.Id,
                            cancellationToken));
                })
                .ToArray();

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

            var runnableWorkIds = requestedWorkIds
                .Where(milestone.WorkItems.ContainsKey)
                .ToArray();

            foreach (var workId in requestedWorkIds
                         .Where(id => !milestone.WorkItems.ContainsKey(id)))
            {
                dispatchNotes.Add(
                    "UNPLANNED_WORK_IGNORED: " + workId);
            }

            foreach (var workId in runnableWorkIds)
            {
                var definition = milestone.WorkItems[workId];
                AddDataFlowHistory(
                    WorkerRoleState.Work,
                    "Worker 분배",
                    $"WORK_ITEM_ID: {workId}" +
                    Environment.NewLine +
                    $"ORDER: {definition.Order}",
                    status: "DISPATCHED",
                    workItemId: workId,
                    persistenceSource: "WORKER DISPATCH");
            }

            if (runnableWorkIds.Length > 0)
            {
                var orderedWaves = runnableWorkIds
                    .Select(id => milestone.WorkItems[id])
                    .GroupBy(work => work.Order)
                    .OrderBy(group => group.Key)
                    .ToArray();

                foreach (var wave in orderedWaves)
                {
                    var waveWorkIds = wave
                        .Select(work => work.Id)
                        .ToArray();
                    var runnableScopes = wave
                        .Select(work => work.WritePaths)
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

                    AddDataFlowHistory(
                        WorkerRoleState.Unknown,
                        "Worker 작업",
                        $"WORK ORDER {wave.Key} 시작 · {waveWorkIds.Length}건",
                        status: "PROCESSING",
                        persistenceSource: "WORKER ACTION");

                    if (overlappingScopes)
                    {
                        dispatchNotes.Add(
                            $"WORK_ORDER_{wave.Key}_CONCURRENCY_REDUCED: 동일/상위·하위 WRITE_PATH가 겹쳐 순차 실행합니다.");
                    }

                    var workBatchBefore =
                        await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                            workingDirectory,
                            cancellationToken);
                    using var gate = new SemaphoreSlim(maxConcurrency);
                    var workTasks = waveWorkIds.Select(async workId =>
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
                                Math.Min(waveWorkIds.Length, maxConcurrency)));

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
                            $"WORK_ORDER_{wave.Key}_CHANGED_PATHS:" +
                            Environment.NewLine +
                            string.Join(
                                Environment.NewLine,
                                observedWorkChanges.Select(path => "- " + path)));
                    }

                    AddDataFlowHistory(
                        WorkerRoleState.Unknown,
                        "Worker 작업",
                        $"WORK ORDER {wave.Key} 종료 · {completed.Length}/{waveWorkIds.Length} terminal",
                        status: "COMPLETED",
                        persistenceSource: "WORKER ACTION");
                }
            }

            foreach (var work in milestone.WorkItems.Values)
            {
                if (!workReports.ContainsKey(work.Id))
                {
                    workReports[work.Id] =
                        MilestoneDefinitionContract.BuildRoleResult(
                            null,
                            "blocked",
                            "MANAGER_DID_NOT_DISPATCH");
                }
            }

            var plannedWorkScopes = milestone.WorkItems.Values
                .SelectMany(work => work.WritePaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var currentWorkDirtyPaths =
                await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                    workingDirectory,
                    cancellationToken);
            var workBarrierPaths = currentWorkDirtyPaths
                .Where(path =>
                    MilestoneMechanicalExecutor.IsPathWithinScopes(
                        path,
                        plannedWorkScopes))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var observedPath in milestoneChangedPaths)
            {
                if (MilestoneMechanicalExecutor.IsPathWithinScopes(
                        observedPath,
                        plannedWorkScopes))
                {
                    workBarrierPaths.Add(observedPath);
                }
            }

            AddDataFlowHistory(
                WorkerRoleState.Unknown,
                "Worker 작업",
                "GENERAL WORK Git barrier · 일괄 commit/force push 시작",
                status: "EXECUTING",
                persistenceSource: "WORKER ACTION");

            var workGitBarrier =
                await MilestoneMechanicalExecutor.ForceCommitPushAsync(
                    workingDirectory,
                    $"ProjectHub milestone {milestone.Id} work batch",
                    workBarrierPaths,
                    _targetSettings.ManualRepositoryUrl?.Trim(),
                    cancellationToken);

            mechanicalReports.Add(
                "WORK_GIT_BARRIER" +
                Environment.NewLine +
                workGitBarrier.Summary);

            AddDataFlowHistory(
                WorkerRoleState.Unknown,
                "Worker 작업",
                workGitBarrier.Summary,
                status: workGitBarrier.Success ? "COMPLETED" : "WARNING",
                persistenceSource: "WORKER ACTION");

            if (!workGitBarrier.Success)
            {
                dispatchNotes.Add(
                    "WORK_GIT_BARRIER_FAILED_CONTINUED: HIGH 이후 final force push에서 다시 시도합니다.");
            }

            CaptureResourceStateWithoutWaiting();

            using (var dispatchDocument =
                   JsonDocument.Parse(dispatchAction.JsonPayload!))
            {
                var mechanical =
                    dispatchDocument.RootElement.GetProperty("mechanical");
                foreach (var request in mechanical.EnumerateArray())
                {
                    var operation =
                        request.GetProperty("operation")
                            .GetString()?.Trim() ?? "UNKNOWN";
                    var command =
                        request.GetProperty("command")
                            .GetString()?.Trim() ?? string.Empty;

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

                    AddDataFlowHistory(
                        WorkerRoleState.Unknown,
                        "Worker 작업",
                        $"OPERATION: {operation}" +
                        Environment.NewLine +
                        $"COMMAND: {command}",
                        status: "EXECUTING",
                        persistenceSource: "WORKER ACTION");

                    var body = "COMMAND: " + command;
                    var result = string.Equals(
                            operation,
                            "RUN",
                            StringComparison.OrdinalIgnoreCase)
                        ? await MilestoneManagedRunRegistry.StartAsync(
                            jobId,
                            workingDirectory,
                            body,
                            cancellationToken)
                        : await MilestoneMechanicalExecutor.ExecuteAsync(
                            jobId,
                            workingDirectory,
                            operation,
                            body,
                            cancellationToken);

                    mechanicalReports.Add(
                        MilestoneDefinitionContract.FormatMechanicalResult(
                            result));
                }
            }

            CaptureResourceStateWithoutWaiting();

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
                $"GENERAL WORK 묶음 종료\nWORK: {workReports.Count}/{milestone.WorkItems.Count} terminal\nRESOURCE: 독립 sidecar · 완료 대기 없음\n다음 단계: {(milestone.QaReserved ? "QA" : "HIGH")}",
                status: "COMPLETED",
                persistenceSource: "WORKER ACTION");

            CaptureResourceStateWithoutWaiting();

            if (milestone.QaReserved)
            {
                qaReport = await ExecuteMilestoneQaAsync(
                    jobId,
                    workingDirectory,
                    milestone,
                    workReports,
                    resourceReports,
                    mechanicalReports,
                    implementer,
                    qa,
                    1,
                    cancellationToken);
            }

            CaptureResourceStateWithoutWaiting();

            var highBefore =
                await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    workingDirectory,
                    cancellationToken);

            highReport = await ExecuteMilestoneHighAsync(
                jobId,
                workingDirectory,
                milestone,
                workReports,
                resourceReports,
                mechanicalReports,
                qaReport,
                implementer,
                high,
                1,
                cancellationToken);

            var highAfter =
                await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    workingDirectory,
                    cancellationToken);
            foreach (var changedPath in
                     MilestoneMechanicalExecutor.DiffChangeStates(
                         highBefore,
                         highAfter))
            {
                milestoneChangedPaths.Add(changedPath);
            }

            CaptureResourceStateWithoutWaiting();

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

            var finalDirtyPaths =
                await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                    workingDirectory,
                    cancellationToken);

            foreach (var dirtyPath in finalDirtyPaths)
            {
                if (!initialChangedPaths.Contains(dirtyPath) &&
                    MilestoneMechanicalExecutor.IsPathWithinScopes(
                        dirtyPath,
                        plannedWorkScopes))
                {
                    milestoneChangedPaths.Add(dirtyPath);
                }
            }

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
                _targetSettings.ManualRepositoryUrl?.Trim(),
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
                "추가 작업이나 재검증 없이 지시사항 대비 실제 결과를 REPORT JSON으로 HQ에 보고하세요.");
            if (dispatchNotes.Count > 0)
            {
                finalEvent.AppendLine("DISPATCH_NOTES:");
                foreach (var note in dispatchNotes)
                    finalEvent.AppendLine(note);
            }

            CaptureResourceStateWithoutWaiting();

            var finalInput = MilestoneDefinitionContract.BuildManagerInput(
                milestone,
                workReports,
                resourceReports,
                mechanicalReports,
                qaReport,
                highReport,
                gitResult,
                finalEvent.ToString());

            var finalResult = await RunCoordinatorRoleAsync(
                jobId,
                "MANAGER",
                RoleContractLoader.BuildManagerPrompt(
                    finalInput,
                    includeFullContract: false),
                manager,
                workingDirectory,
                managerSession,
                null,
                cancellationToken,
                CodexSandboxMode.ReadOnly);

            string managerMessage;
            if (finalResult.ExitCode != 0)
            {
                managerMessage =
                    BuildManagerFallbackReport(
                        "MANAGER_FINAL_EXECUTION_FAILED",
                        new[] { finalResult.StandardError });
            }
            else
            {
                var finalEnvelope =
                    await EnsureRoleJsonResponseAsync(
                        jobId,
                        workingDirectory,
                        "MANAGER",
                        finalResult.FinalMessage?.Trim() ?? string.Empty,
                        new[] { "REPORT" },
                        expectedGoto: "HQ",
                        implementer,
                        cancellationToken);

                managerMessage =
                    !finalEnvelope.Parse.HasErrors &&
                    finalEnvelope.Parse.ValidActions.Count == 1 &&
                    string.Equals(
                        finalEnvelope.Parse.ValidActions[0].Name,
                        "REPORT",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        finalEnvelope.Parse.ValidActions[0].GotoTarget,
                        "HQ",
                        StringComparison.OrdinalIgnoreCase)
                        ? finalEnvelope.Message
                        : BuildManagerFallbackReport(
                            "MANAGER_FINAL_REPORT_CONTRACT_INVALID",
                            finalEnvelope.Parse.Errors);
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

        var resourceNodes = milestone.Resources.Values
            .Select(resource => "RESOURCE-" + resource.Id)
            .ToArray();
        var validationNode = milestone.QaReserved ? "QA" : "HIGH";
        var workWaves = milestone.WorkItems.Values
            .GroupBy(work => work.Order)
            .OrderBy(group => group.Key)
            .Select(group => group
                .Select(work => "WORK-" + work.Id)
                .ToArray())
            .ToArray();

        if (workWaves.Length == 0)
        {
            edges.Add(new(
                "MANAGER-DISPATCH",
                validationNode,
                "WORK_DISPATCH_COMPLETE"));
        }
        else
        {
            foreach (var workNode in workWaves[0])
            {
                edges.Add(new(
                    "MANAGER-DISPATCH",
                    workNode,
                    "DISPATCH_ORDER"));
            }

            for (var index = 1; index < workWaves.Length; index++)
            {
                foreach (var previousNode in workWaves[index - 1])
                {
                    foreach (var currentNode in workWaves[index])
                    {
                        edges.Add(new(
                            previousNode,
                            currentNode,
                            "ORDER_BARRIER"));
                    }
                }
            }

            foreach (var workNode in workWaves[^1])
            {
                edges.Add(new(
                    workNode,
                    validationNode,
                    "RESULT_TO_VALIDATION"));
            }
        }

        foreach (var resourceNode in resourceNodes)
        {
            edges.Add(new(
                "HQ-DESIGN",
                resourceNode,
                "BACKGROUND_RESOURCE_REQUEST"));
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

    private async Task<(string Message, ActionBlockParseResult Parse, bool RepairUsed)>
        EnsureRoleJsonResponseAsync(
            string jobId,
            string workingDirectory,
            string role,
            string originalMessage,
            IReadOnlyCollection<string> expectedActions,
            string? expectedGoto,
            WorkerAiRoleSettings implementer,
            CancellationToken cancellationToken)
    {
        var parsed = ActionBlockContract.ParseRole(
            role,
            originalMessage);

        bool IsExpected(ActionBlockParseResult candidate)
        {
            if (candidate.HasErrors ||
                candidate.ValidActions.Count != 1)
            {
                return false;
            }

            var action = candidate.ValidActions[0];
            if (expectedActions.Count > 0 &&
                !expectedActions.Contains(
                    action.Name,
                    StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(expectedGoto) ||
                   string.Equals(
                       action.GotoTarget,
                       expectedGoto,
                       StringComparison.OrdinalIgnoreCase);
        }

        if (IsExpected(parsed))
            return (originalMessage, parsed, false);

        var protocolError = new List<string>(parsed.Errors);
        if (!parsed.HasErrors &&
            parsed.ValidActions.Count == 1 &&
            expectedActions.Count > 0 &&
            !expectedActions.Contains(
                parsed.ValidActions[0].Name,
                StringComparer.OrdinalIgnoreCase))
        {
            protocolError.Add(
                "EXPECTED_ACTION=" +
                string.Join("/", expectedActions));
        }

        if (!string.IsNullOrWhiteSpace(expectedGoto) &&
            parsed.ValidActions.Count == 1 &&
            !string.Equals(
                parsed.ValidActions[0].GotoTarget,
                expectedGoto,
                StringComparison.OrdinalIgnoreCase))
        {
            protocolError.Add(
                "EXPECTED_GOTO=" + expectedGoto);
        }

        var protocolErrorText = string.Join(
            Environment.NewLine,
            protocolError);
        var expectedAction =
            expectedActions.Count == 1
                ? expectedActions.First()
                : null;
        var elementScan =
            RoleElementRecoveryContract.Scan(
                role,
                originalMessage,
                expectedAction,
                protocolErrorText);

        if (!string.Equals(
                role,
                "HQ",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(elementScan.ActionName) &&
            RoleElementRecoveryContract.GetDefinitions(
                role,
                elementScan.ActionName).Count > 0)
        {
            var recoveryTargets =
                elementScan.RecoveryTargets.ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
            IReadOnlyDictionary<string, string> recoveredByWork =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            if (recoveryTargets.Count > 0)
            {
                var elementRecovery =
                    await ExecuteRoleElementRecoveryWorkAsync(
                        jobId,
                        workingDirectory,
                        role,
                        elementScan.ActionName,
                        originalMessage,
                        protocolErrorText,
                        recoveryTargets,
                        elementScan.Recovered.Keys.ToArray(),
                        implementer,
                        cancellationToken);

                if (!string.IsNullOrWhiteSpace(elementRecovery))
                {
                    recoveredByWork =
                        RoleElementRecoveryContract.ReadRecoveredElements(
                            elementRecovery,
                            recoveryTargets);
                    foreach (var recoveredName in recoveredByWork.Keys)
                        recoveryTargets.Remove(recoveredName);
                }
            }

            if (RoleElementRecoveryContract.TryBuildRoleResponse(
                    elementScan,
                    recoveredByWork,
                    expectedGoto,
                    out var mergedResponse,
                    out var remainingElements))
            {
                var mergedParse = ActionBlockContract.ParseRole(
                    role,
                    mergedResponse);
                if (IsExpected(mergedParse))
                {
                    AddDataFlowHistory(
                        WorkerRoleState.Work,
                        "Worker 작업",
                        role + " element 복구 성공" +
                        Environment.NewLine +
                        $"기계 확보: {elementScan.Recovered.Count}" +
                        Environment.NewLine +
                        $"WORK 복구: {recoveredByWork.Count}",
                        status: "REPAIRED",
                        persistenceSource: "WORKER ACTION");
                    return (mergedResponse, mergedParse, true);
                }
            }
            else if (remainingElements.Count > 0)
            {
                protocolError.Add(
                    "ELEMENT_RECOVERY_REMAINING=" +
                    string.Join(",", remainingElements));
                protocolErrorText = string.Join(
                    Environment.NewLine,
                    protocolError);
            }
        }

        var repaired = await ExecuteRoleJsonRepairWorkAsync(
            jobId,
            workingDirectory,
            role,
            originalMessage,
            protocolErrorText,
            expectedAction,
            implementer,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(repaired))
            return (originalMessage, parsed, true);

        var reparsed = ActionBlockContract.ParseRole(
            role,
            repaired);

        if (IsExpected(reparsed))
        {
            AddDataFlowHistory(
                WorkerRoleState.Work,
                "Worker 작업",
                role + " 응답 JSON 복구 성공",
                status: "REPAIRED",
                persistenceSource: "WORKER ACTION");
            return (repaired, reparsed, true);
        }

        return (repaired, reparsed, true);
    }

    private async Task<string?> ExecuteRoleJsonRepairWorkAsync(
        string jobId,
        string workingDirectory,
        string role,
        string originalMessage,
        string parserError,
        string? expectedAction,
        WorkerAiRoleSettings implementer,
        CancellationToken cancellationToken)
    {
        RunOnUi(() =>
        {
            TaskDirection.Text = "작업";
            TaskTitle.Text = role + " JSON 복구";
            ResultTitle.Text = "WORK";
            SetFlowState(
                codexActive: true,
                workerActive: false,
                webActive: false,
                explicitStage: TaskStage.Implementer);
        });

        var prompt = RoleJsonRepairContract.BuildPrompt(
            role,
            originalMessage,
            parserError,
            expectedAction);
        var repairId =
            "JSON-REPAIR-" + role.Trim().ToUpperInvariant();

        AddDataFlowHistory(
            WorkerRoleState.Work,
            "Worker 분배",
            prompt,
            status: "DISPATCHED",
            workItemId: repairId,
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
            historyWorkItemId: repairId,
            historyReferenceId: repairId);

        var response = result.FinalMessage?.Trim() ?? string.Empty;
        AddRoleResponseHistory(
            WorkerRoleState.Work,
            role + " JSON 복구 응답",
            response,
            result.Usage,
            result.Files,
            status: result.ExitCode == 0 ? "RECEIVED" : "FAILED",
            providerWireId: implementer.Provider,
            fullMessage: response,
            workItemId: repairId,
            referenceId: repairId);

        return result.ExitCode == 0 && response.Length > 0
            ? response
            : null;
    }

    private async Task<string?> ExecuteRoleElementRecoveryWorkAsync(
        string jobId,
        string workingDirectory,
        string role,
        string actionName,
        string originalMessage,
        string parserError,
        IReadOnlyCollection<string> recoveryTargets,
        IReadOnlyCollection<string> recoveredElements,
        WorkerAiRoleSettings implementer,
        CancellationToken cancellationToken)
    {
        RunOnUi(() =>
        {
            TaskDirection.Text = "작업";
            TaskTitle.Text = role + " element 복구";
            ResultTitle.Text = "WORK";
            SetFlowState(
                codexActive: true,
                workerActive: false,
                webActive: false,
                explicitStage: TaskStage.Implementer);
        });

        var prompt =
            RoleElementRecoveryContract.BuildElementRecoveryPrompt(
                role,
                actionName,
                originalMessage,
                parserError,
                recoveryTargets,
                recoveredElements);
        var repairId =
            "ELEMENT-REPAIR-" + role.Trim().ToUpperInvariant();

        AddDataFlowHistory(
            WorkerRoleState.Work,
            "Worker 분배",
            prompt,
            status: "DISPATCHED",
            workItemId: repairId,
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
            historyWorkItemId: repairId,
            historyReferenceId: repairId);

        var response = result.FinalMessage?.Trim() ?? string.Empty;
        AddRoleResponseHistory(
            WorkerRoleState.Work,
            role + " element 복구 응답",
            response,
            result.Usage,
            result.Files,
            status: result.ExitCode == 0 ? "RECEIVED" : "FAILED",
            providerWireId: implementer.Provider,
            fullMessage: response,
            workItemId: repairId,
            referenceId: repairId);

        return result.ExitCode == 0 && response.Length > 0
            ? response
            : null;
    }

    private static string BuildManagerFallbackReport(
        string summary,
        IEnumerable<string?> issues)
    {
        var detail = issues
            .Where(issue => !string.IsNullOrWhiteSpace(issue))
            .Select(issue => issue!.Trim())
            .ToArray();
        var content = detail.Length == 0
            ? summary
            : summary +
              Environment.NewLine +
              string.Join(Environment.NewLine, detail);

        var payload = new Dictionary<string, object?>
        {
            ["status"] = "blocked",
            ["content"] = content
        };

        return "[GOTO : HQ]" +
               Environment.NewLine +
               "[ACTION=REPORT]" +
               Environment.NewLine +
               JsonSerializer.Serialize(
                   payload,
                   new JsonSerializerOptions
                   {
                       WriteIndented = true
                   });
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
                MilestoneDefinitionContract.BuildRoleResult(
                    null,
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

        string report;
        if (forbiddenExecution is not null)
        {
            report = MilestoneDefinitionContract.BuildRoleResult(
                null,
                "blocked",
                BuildExecutionPolicy.GeneralWorkCommandForbiddenError,
                issues: new[]
                {
                    "command=" + forbiddenExecution.Command
                });
        }
        else if (result.ExitCode != 0)
        {
            report = MilestoneDefinitionContract.NormalizeWorkReport(
                result.ExitCode,
                result.FinalMessage,
                result.StandardError);
        }
        else
        {
            var envelope = await EnsureRoleJsonResponseAsync(
                jobId,
                workingDirectory,
                "WORK",
                result.FinalMessage?.Trim() ?? string.Empty,
                new[] { "RESULT" },
                expectedGoto: null,
                implementer,
                cancellationToken);

            report =
                !envelope.Parse.HasErrors &&
                envelope.Parse.ValidActions.Count == 1
                    ? MilestoneDefinitionContract.NormalizeWorkReport(
                        0,
                        envelope.Message,
                        null)
                    : MilestoneDefinitionContract.BuildRoleResult(
                        null,
                        "blocked",
                        "WORK_REPORT_CONTRACT_INVALID",
                        issues: envelope.Parse.Errors);
        }

        var reportParse = ActionBlockContract.ParseWork(report);
        var reportStatus =
            !reportParse.HasErrors &&
            reportParse.ValidActions.Count == 1
                ? ActionBlockContract.GetJsonString(
                    reportParse.ValidActions[0],
                    "status")
                : null;

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
            referenceId: work.Id);

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
                status: "BLOCKED");
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
                status: "BLOCKED");
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
                status: "BLOCKED");
            return new(
                resourceId,
                report,
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
        WorkerAiRoleSettings implementer,
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

        var body = MilestoneDefinitionContract.BuildQaContext(
            milestone,
            workReports,
            resourceReports,
            mechanicalReports);

        var result = await RunCoordinatorRoleAsync(
            jobId,
            "QA",
            RoleContractLoader.BuildQaPrompt(body),
            qa,
            workingDirectory,
            null,
            null,
            cancellationToken,
            CodexSandboxMode.WorkspaceWrite);

        string report;
        if (result.ExitCode != 0)
        {
            report = MilestoneDefinitionContract.NormalizeQaReport(
                result.ExitCode,
                result.FinalMessage,
                result.StandardError);
        }
        else
        {
            var envelope = await EnsureRoleJsonResponseAsync(
                jobId,
                workingDirectory,
                "QA",
                result.FinalMessage?.Trim() ?? string.Empty,
                new[] { "RESULT" },
                expectedGoto: null,
                implementer,
                cancellationToken);

            report =
                !envelope.Parse.HasErrors &&
                envelope.Parse.ValidActions.Count == 1
                    ? MilestoneDefinitionContract.NormalizeQaReport(
                        0,
                        envelope.Message,
                        null)
                    : MilestoneDefinitionContract.BuildRoleResult(
                        "HIGH",
                        "blocked",
                        "QA_REPORT_CONTRACT_INVALID",
                        issues: envelope.Parse.Errors);
        }

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
        WorkerAiRoleSettings implementer,
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

        var body = MilestoneDefinitionContract.BuildHighContext(
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

        string report;
        if (result.ExitCode != 0)
        {
            report = MilestoneDefinitionContract.NormalizeHighReport(
                result.ExitCode,
                result.FinalMessage,
                result.StandardError);
        }
        else
        {
            var envelope = await EnsureRoleJsonResponseAsync(
                jobId,
                workingDirectory,
                "HIGH",
                result.FinalMessage?.Trim() ?? string.Empty,
                new[] { "RESULT" },
                expectedGoto: null,
                implementer,
                cancellationToken);

            report =
                !envelope.Parse.HasErrors &&
                envelope.Parse.ValidActions.Count == 1
                    ? MilestoneDefinitionContract.NormalizeHighReport(
                        0,
                        envelope.Message,
                        null)
                    : MilestoneDefinitionContract.BuildRoleResult(
                        "MANAGER",
                        "incomplete",
                        "HIGH_REPORT_CONTRACT_INVALID",
                        issues: envelope.Parse.Errors);
        }

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
        string? configuredRepositoryUrl,
        string? remoteMainSha,
        bool includeFullContract) =>
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
        "사용자가 지정한 실제 프로젝트 루트 하나를 공통 작업공간으로 사용하고 GENERAL WORK별 WRITE_PATH와 order를 명확히 지정한다." +
        Environment.NewLine +
        "선행 의존성이 없는 WORK는 같은 order로 두고, 선행 결과가 필요한 WORK는 더 큰 order를 지정한다. 같은 order의 WRITE_PATH는 겹치지 않게 설계한다." +
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
        "- DISPATCH JSON의 mechanical 배열 항목은 operation과 command를 가진다." +
        Environment.NewLine +
        "- operation은 BUILD, RUN, PUBLISH 중 하나다." +
        Environment.NewLine +
        "- BUILD/PUBLISH 명령은 최신 결과를 프로젝트 루트의 bin에 만들도록 작성한다." +
        Environment.NewLine +
        "- 프로젝트 루트: " + workingDirectory +
        Environment.NewLine +
        "- RUN은 보고 전에 종료 가능한 foreground 실행만 요청한다.";

    private static bool IsManagerFinalReport(string message)
    {
        var parsed = ActionBlockContract.ParseManager(message);
        if (parsed.HasErrors || parsed.ValidActions.Count != 1)
            return false;

        var action = parsed.ValidActions[0];
        return string.Equals(
                   action.Name,
                   "REPORT",
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   action.GotoTarget,
                   "HQ",
                   StringComparison.OrdinalIgnoreCase);
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
