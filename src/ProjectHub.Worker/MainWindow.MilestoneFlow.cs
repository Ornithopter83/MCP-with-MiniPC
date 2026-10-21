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

    private sealed record SupervisorExecutionResult(
        string Report, DelegationMetrics Delegations);

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
        var high = NormalizeRoleSessionForWorkspace(
            _targetSettings.EffectiveHighLevel,
            normalizedRoot);

        _activeWorkingDirectory = normalizedRoot;
        _activeProjectJobId = jobId;
        var interventionQueue = new HqUserInterventionQueue(normalizedRoot, jobId);
        _hqInterventions = interventionQueue;
        RunOnUi(() => SetDashboardBodyMode(DashboardBodyMode.TaskHistory));
        using var cts = new CancellationTokenSource();
        _activeTaskCts = cts;
        UpdateTaskConfigurationLockState();
        _activeCoordinatorFirst = true;
        SetFollowupComposerVisible(true);
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
                RunOnUi(() => _historyEvents.Clear());
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
                // Restore undelivered user input after a process interruption.
                foreach (var entry in interventionQueue.Pending())
                    if (!_historyEvents.Any(card =>
                        card.EventType == "USER_INTERVENTION" &&
                        card.ReferenceId == entry.Id))
                        AddHqInterventionHistory(entry, delivered: false);
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

            void PersistRecovery(string evidence)
            {
                var snapshot = new CoordinatorContinuationState(
                    jobId, normalizedRoot, coordinator, implementer,
                    hqSession, null, "INTERRUPTED", evidence, high);
                if (!ProjectWorkspacePersistence.SaveContinuation(snapshot))
                    throw new IOException("TASK_RECOVERY_CHECKPOINT_WRITE_FAILED");
            }
            PersistRecovery(hqInbound);

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

                // Snapshot immediately before this HQ request. Messages entered
                // while HQ is responding stay queued for the next invocation.
                var sentInterventions = interventionQueue.Pending();
                var hqHistoryReferenceId = WorkerHistoryCardPolicy.NewInvocationReference(
                    "HQ", "MILESTONE_" + milestoneIndex);
                var hqPrompt = BuildMilestoneHqPrompt(
                    inboundType,
                    HqUserInterventionQueue.AppendToHqBody(
                        hqInbound, sentInterventions),
                    normalizedRoot,
                    configuredRepositoryUrl,
                    remoteReference.CommitSha,
                    includeFullContract: IsWebTransport(coordinator.Transport) ||
                        milestoneIndex == 1 || string.IsNullOrWhiteSpace(hqSession));

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
                        milestoneIndex == 1 ? webAttachments : null,
                    historyReferenceId: hqHistoryReferenceId);

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
                    fullMessage: hqMessage,
                    referenceId: hqHistoryReferenceId);

                var hqText = HqTextProtocol.Parse(
                    hqMessage,
                    completionValidatedByTransport:
                        IsWebTransport(coordinator.Transport));
                var unmodifiedHqResponse = hqMessage;
                if (!hqText.IsValid &&
                    HqResponseRecoveryContract.TryRepairClosingTags(
                        hqMessage, out var localRepair) &&
                    HqResponseRecoveryContract.PreservesPaths(hqMessage, localRepair))
                {
                    var locallyParsed = HqTextProtocol.Parse(
                        localRepair,
                        completionValidatedByTransport:
                            IsWebTransport(coordinator.Transport));
                    if (locallyParsed.IsValid)
                    {
                        hqMessage = localRepair;
                        hqText = locallyParsed;
                        AddDataFlowHistory(
                            WorkerRoleState.Hq, "Worker 작업",
                            "HQ 태그 닫기 국소 복구: WORK 경로 보존",
                            status: "RECOVERED",
                            persistenceSource: "WORKER ACTION");
                    }
                }

                if (!hqText.IsValid)
                {
                    _formatRecoveryJobs[jobId] = 1;

                    var recoveryError =
                        string.Join(", ", hqText.Errors);
                    var recoveryBody =
                        HqResponseRecoveryContract.BuildBody(
                            hqMessage,
                            recoveryError);
                    // The first response was invalid: do not acknowledge it.
                    // Re-send its messages, plus any new ones, with the
                    // existing format-recovery request (no extra HQ call).
                    sentInterventions = interventionQueue.Pending();
                    var recoveryPrompt = BuildMilestoneHqPrompt(
                        "HQ_RESPONSE_RECOVERY",
                        HqUserInterventionQueue.AppendToHqBody(
                            recoveryBody, sentInterventions),
                        normalizedRoot,
                        configuredRepositoryUrl,
                        remoteReference.CommitSha,
                        includeFullContract: IsWebTransport(coordinator.Transport) ||
                            string.IsNullOrWhiteSpace(hqSession));

                    AddDataFlowHistory(
                        WorkerRoleState.Hq,
                        "Worker 작업",
                        "HQ 전체 응답 복구 1회 실행" +
                        Environment.NewLine +
                        recoveryError,
                        status: "RECOVERY",
                        persistenceSource: "WORKER ACTION");

                    var hqRecoveryReferenceId = WorkerHistoryCardPolicy.NewInvocationReference(
                        "HQ", "RESPONSE_RECOVERY_" + milestoneIndex);
                    var recoveryResult = await RunHqRoleAsync(
                        jobId,
                        "HQ_RESPONSE_RECOVERY",
                        recoveryPrompt,
                        coordinator,
                        normalizedRoot,
                        hqSession,
                        cts.Token,
                        sessionStarted: session =>
                            hqSession = CodexCliRunner.NormalizeSessionId(session),
                        historyReferenceId: hqRecoveryReferenceId);

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
                        fullMessage: hqMessage,
                        referenceId: hqRecoveryReferenceId);

                    hqText = HqTextProtocol.Parse(
                        hqMessage,
                        completionValidatedByTransport:
                            IsWebTransport(coordinator.Transport));
                    if (!hqText.IsValid ||
                        !HqResponseRecoveryContract.PreservesPaths(
                            unmodifiedHqResponse, hqMessage))
                    {
                        throw new InvalidOperationException(
                            "HQ_RESPONSE_RECOVERY_FAILED: " +
                            (!hqText.IsValid
                                ? string.Join(", ", hqText.Errors)
                                : "ORIGINAL_PATHS_CHANGED"));
                    }
                }

                var hqParse = hqText.Parse;
                MilestoneDefinition? milestone = null;
                var milestoneError = string.Empty;
                var hqAction = hqParse.ValidActions.Single();
                AddDataFlowHistory(
                    WorkerRoleState.Hq,
                    "HQ 응답 수신",
                    "HQ ACTION: " + hqAction.Name + Environment.NewLine +
                    WorkerHistoryCardFormatter.Preview(hqMessage),
                    status: "RECEIVED",
                    persistenceSource: "WORKER ACTION");

                // The transport returned a protocol-valid HQ decision.
                // Commit acknowledgments before proceeding; an interrupted
                // request must leave messages pending instead of losing them.
                interventionQueue.Acknowledge(sentInterventions);
                foreach (var delivered in sentInterventions)
                {
                    AddTaskMessage(
                        "USER INTERVENTION DELIVERED",
                        "HQ 요청에 포함되어 유효한 응답을 받았습니다. ID: " + delivered.Id,
                        status: "DELIVERED",
                        referenceId: delivered.Id,
                        includeHistory: false);
                    AddHqInterventionHistory(delivered, delivered: true);
                }

                if (string.Equals(
                        hqAction.Name,
                        "PAUSE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (interventionQueue.Pending().Count > 0)
                    {
                        hqInbound = "PAUSE_DEFERRED_USER_INTERVENTION: " +
                            "HQ 응답 생성 중 추가 사용자 메시지가 도착했다. " +
                            "다음 호출에서 원문을 확인한 후 PAUSE 여부를 다시 판단하라.";
                        PersistRecovery(hqInbound);
                        continue;
                    }
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
                    if (interventionQueue.Pending().Count > 0)
                    {
                        hqInbound = "END_DEFERRED_USER_INTERVENTION: " +
                            "HQ 응답 생성 중 추가 사용자 메시지가 도착했다. " +
                            "다음 호출에서 원문을 확인한 후 완료 여부를 다시 판단하라.";
                        PersistRecovery(hqInbound);
                        continue;
                    }
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
                // If the process stops mid-WORK, HQ gets this plan plus the
                // persistent per-WORKITEM journal when recovery is initiated.
                PersistRecovery(hqMessage);

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
                    _currentMilestoneResourceReserved =
                        milestone.Resources.Count > 0;
                    if (_currentMilestoneResourceReserved != true)
                        _resourceSidecarStatus = "ChatGPT Web";
                    UpdatePipelineVisuals();
                    AddDataFlowHistory(
                        WorkerRoleState.Unknown,
                        "Worker 작업",
                        $"HQ 응답 파싱 완료\nMILESTONE: {milestone.Id}\nSuperVisor: #1\nRESOURCE: {milestone.Resources.Count}건",
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
                PersistRecovery(hqInbound);
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
            _hqInterventions = null;
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

        // RESOURCE is an independent sidecar: never gate SV, HIGH or Git
        // on generation, failure, artifact validation or transport completion.
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
        var highReport = string.Empty;
        var gitResult = MilestoneGitResult.NotStarted(milestone.TargetBranch);
        var outcome = "COMPLETED";
        var supervisorDelegations = DelegationMetrics.Empty;
        var highDelegations = DelegationMetrics.Empty;

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
            if (MilestoneFinalizationPolicy.ShouldWriteInitialPlan(
                milestone.ReadOnlyNoFileChanges, milestone.PlanDocument))
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

            // SV is HQ's sole implementation owner. Its internal WORK children
            // are transient MCP invocations, never persistent HQ work-items.
            var beforeSv = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            var supervisor = await ExecuteMilestoneSupervisorAsync(
                jobId, workingDirectory, milestone, cancellationToken);
            supervisorDelegations = supervisor.Delegations;
            workReports["1"] = supervisor.Report;
            var afterSv = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            var svChangedPaths = MilestoneMechanicalExecutor.DiffChangeStates(
                beforeSv, afterSv).ToArray();
            foreach (var path in svChangedPaths)
                milestoneChangedPaths.Add(path);
            var executableDefinitions = new[]
            {
                new MilestoneWorkDefinition("1", svChangedPaths,
                    milestone.ReadOnlyNoFileChanges, false, "", "")
            };

            var blockedWorkIds = workReports
                .Where(pair =>
                    pair.Key == "1" &&
                    string.Equals(
                        MilestoneDefinitionContract.ReadWorkStatus(pair.Value),
                        "blocked",
                        StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray();
            var pendingWorkIds = workReports
                .Where(pair =>
                    pair.Key == "1" &&
                    string.Equals(
                        MilestoneDefinitionContract.ReadWorkStatus(pair.Value),
                        "in_progress",
                        StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray();
            if (blockedWorkIds.Length > 0)
            {
                outcome =
                    "SV_BLOCKED:" +
                    string.Join(",", blockedWorkIds.OrderBy(
                        id => int.TryParse(id, out var number)
                            ? number
                            : int.MaxValue));
            }

            else if (pendingWorkIds.Length > 0)
            {
                outcome = "SV_IN_PROGRESS:" + string.Join(",", pendingWorkIds);
            }

            CaptureResourceState();

            var plannedWorkScopes = executableDefinitions
                .Where(work => !work.ReadOnly)
                .SelectMany(work => work.WritePaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Worker owns mechanical BUILD. Repairs are assigned to SV by HQ.
            var buildRuns = await MilestoneBuildRunner.RunAsync(
                workingDirectory, jobId, executableDefinitions, cancellationToken);
            foreach (var build in buildRuns)
                mechanicalReports.Add(
                    MilestoneDefinitionContract.FormatMechanicalResult(build));
            var buildFailed = buildRuns.Any(run => !run.Success);
            // BUILD failures return to HQ for a new SV-led repair milestone.

            // Preserve the original implementation/build outcome.
            if (blockedWorkIds.Length == 0 && buildFailed)
                outcome = "BUILD_FAILED";


            // HIGH only reviews; the next SV milestone handles modifications.
            if (pendingWorkIds.Length == 0)
            {
            var beforeHigh = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            highReport = await ExecuteMilestoneHighAsync(
                jobId, workingDirectory, milestone, workReports, resourceReports,
                mechanicalReports, implementer, high, 1, cancellationToken,
                supervisorDelegations,
                metrics => highDelegations = metrics);
            var afterHigh = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            foreach (var path in MilestoneMechanicalExecutor.DiffChangeStates(
                beforeHigh, afterHigh))
                milestoneChangedPaths.Add(path);

            if (outcome == "COMPLETED" &&
                string.Equals(MilestoneDefinitionContract.ReadHighStatus(highReport),
                    "blocked", StringComparison.OrdinalIgnoreCase))
                outcome = "HIGH_BLOCKED";

            }

            CaptureResourceState();

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
            else if (!MilestoneFinalizationPolicy.ShouldPublish(
                blockedWorkIds.Length, pendingWorkIds.Length,
                buildFailed, outcome))
            {
                // Never publish an unchanged old HEAD and label the Git phase
                // successful after the implementation was blocked. Keep any
                // partial changes locally for the next explicit HQ decision.
                gitResult = MilestoneGitResult.NotStarted(milestone.TargetBranch);
                AddDataFlowHistory(
                    WorkerRoleState.Unknown,
                    "Git 최종화",
                    "GIT_FINALIZE_SKIPPED: 구현·검증 미완료, 기존 HEAD 게시 안 함",
                    status: "BLOCKED",
                    persistenceSource: "WORKER ACTION");
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

            // A successful implementation is not a completed milestone when
            // the required Git commit/push failed. Preserve earlier failure
            // causes rather than hiding them behind the Git failure.
            outcome = MilestoneFinalizationPolicy.ResolveGitOutcome(
                outcome, milestone.ReadOnlyNoFileChanges, gitResult.Success);

            var currentLocalChanges =
                await MilestoneMechanicalExecutor.SnapshotChangedPathsAsync(
                    workingDirectory,
                    cancellationToken);

            var graphGitState = milestone.ReadOnlyNoFileChanges
                ? "SKIPPED_READ_ONLY"
                : !MilestoneFinalizationPolicy.ShouldPublish(
                    blockedWorkIds.Length, pendingWorkIds.Length, buildFailed, outcome)
                    ? "SKIPPED_BLOCKED"
                    : gitResult.Success ? "FINALIZED" : "FINALIZE_FAILED";
            SaveMilestoneExecutionGraph(
                workingDirectory,
                jobId,
                milestone,
                graphGitState,
                workReports,
                resourceReports,
                highReport,
                hqFinalState: "READY",
                supervisorDelegations: supervisorDelegations,
                highDelegations: highDelegations);

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
                    jobId,
                    supervisorDelegations,
                    highDelegations));
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
                    jobId,
                    supervisorDelegations,
                    highDelegations));
        }
    }

    private static void SaveMilestoneExecutionGraph(
        string workingDirectory,
        string jobId,
        MilestoneDefinition milestone,
        string state,
        IReadOnlyDictionary<string, string>? workReports = null,
        IReadOnlyDictionary<string, string>? resourceReports = null,
        string? highReport = null,
        string hqFinalState = "PLANNED",
        DelegationMetrics? supervisorDelegations = null,
        DelegationMetrics? highDelegations = null)
    {
        workReports ??= new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        resourceReports ??= new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        var nodes = new List<MilestoneGraphNodeSnapshot>
        {
            new("HQ-DESIGN", "HQ", "COMPLETED"),
            new("SV-1", "SV",
                workReports.TryGetValue("1", out var svReport)
                    ? MilestoneGraphStatePolicy.RoleState(
                        MilestoneDefinitionContract.ReadWorkStatus(svReport))
                    : "PLANNED"),
            // Parent completion alone does not prove child execution.
            new("TEMP-WORK", "WORK",
                supervisorDelegations is null ? "PLANNED" :
                supervisorDelegations.Successful > 0 ? "COMPLETED" :
                supervisorDelegations.Attempts > 0 ? "BLOCKED" : "PLANNED")
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
                    ? resourceReport.StartsWith("RESOURCE_STATUS: COMPLETED",
                        StringComparison.Ordinal) ? "COMPLETED" :
                      resourceReport.StartsWith("RESOURCE_STATUS: BLOCKED",
                        StringComparison.Ordinal) ? "BLOCKED" : "RUNNING"
                    : "PLANNED";
            nodes.Add(new(
                "RESOURCE-" + resource.Id,
                "RESOURCE",
                resourceState,
                resource.Id));
        }

        nodes.Add(new(
            "HIGH",
            "HIGH",
            string.IsNullOrWhiteSpace(highReport)
                ? "SKIPPED"
                : MilestoneGraphStatePolicy.RoleState(
                    MilestoneDefinitionContract.ReadHighStatus(highReport))));
        nodes.Add(new("HIGH-TEMP-WORK", "WORK",
            highDelegations is null ? "PLANNED" :
            highDelegations.Successful > 0 ? "COMPLETED" :
            highDelegations.Attempts > 0 ? "BLOCKED" : "PLANNED"));
        nodes.Add(new(
            "GIT-FINALIZE",
            "WORKER",
            MilestoneGraphStatePolicy.GitState(state)));
        nodes.Add(new(
            "HQ-FINAL",
            "HQ",
            hqFinalState));

        var edges = new List<MilestoneGraphEdgeSnapshot>
        {
            new("HQ-DESIGN", "SV-1", "DISPATCH"),
            new("SV-1", "TEMP-WORK", "DELEGATE"),
            new("TEMP-WORK", "SV-1", "RESULT"),
            new("SV-1", "HIGH", "INTEGRATE_AND_REVIEW"),
            new("HIGH", "HIGH-TEMP-WORK", "VERIFY_DELEGATE"),
            new("HIGH-TEMP-WORK", "HIGH", "VERIFY_RESULT"),
            new("HIGH", "GIT-FINALIZE", "REVIEW_TO_GIT"),
            new("GIT-FINALIZE", "HQ-FINAL", "REPORT_TO_HQ")
        };
        foreach (var resource in milestone.Resources.Values)
            edges.Add(new("HQ-DESIGN", "RESOURCE-" + resource.Id, "BACKGROUND_RESOURCE_REQUEST"));

        ProjectWorkspacePersistence.SaveMilestoneGraph(
            workingDirectory,
            new MilestoneExecutionGraphSnapshot(
                jobId,
                milestone.Id,
                state,
                milestone.Resources.Count > 0,
                nodes,
                edges,
                DateTimeOffset.UtcNow));
    }

    private async Task<SupervisorExecutionResult> ExecuteMilestoneSupervisorAsync(
        string jobId, string workingDirectory, MilestoneDefinition milestone,
        CancellationToken cancellationToken)
    {
        RunOnUi(() =>
        {
            TaskDirection.Text = "구현";
            TaskTitle.Text = "SuperVisor #1 · " + milestone.Id;
            ResultTitle.Text = "SuperVisor";
            SetFlowState(codexActive: true, workerActive: true, webActive: false,
                explicitStage: TaskStage.Manager);
        });
        var settings = _targetSettings.EffectiveManager with
        {
            ThreadSessionId = null,
            ThreadProjectPath = null
        };
        var token = _bridgeServer?.RegisterMcpSession(
            "SV", jobId, workingDirectory, cancellationToken,
            forbidWrites: milestone.ReadOnlyNoFileChanges) ??
            throw new InvalidOperationException("SV_MCP_BRIDGE_NOT_RUNNING");
        var reference = WorkerHistoryCardPolicy.NewInvocationReference(milestone.Id, "SV-1");
        try
        {
            if (settings.ProviderKind != AiServiceProvider.OpenAI)
                throw new InvalidOperationException("SV_WORK_DELEGATION_REQUIRES_CODEX_CLI");

            // UnityWorker model: execute each delegate_work MCP tool call now;
            // parent SuperVisor remains alive and receives the real child result.
            var result = await RunCoordinatorRoleAsync(
                jobId, "SV",
                RoleContractLoader.BuildSvPrompt(
                    milestone.SvInstructions +
                    Environment.NewLine +
                    "작업을 먼저 분석하고 실제 실행·조사를 delegate_work에 배정하라. " +
                    "각 위임 도구 응답은 WORK 실행의 최종 결과이며 이를 직접 읽고 " +
                    "필요 시 다음 독립 과업을 위임한 뒤 실제 증거만 통합 보고하라.",
                    workingDirectory),
                settings, workingDirectory, null, null, cancellationToken,
                SpecialistDelegationPolicy.DelegationSandbox,
                historyWorkItemId: "1", historyReferenceId: reference,
                mcpToken: token);
            var metrics = _bridgeServer.GetDelegationMetrics(token);
            RunOnUi(() => AddRoleProgressHistory(
                WorkerRoleState.Manager,
                "WORK 위임: 시도 " + metrics.Attempts +
                "회 · 완료 " + metrics.Successful +
                "회 · 실행 실패 " + metrics.RecoverableFailures +
                "회 · 경로 거부 " + metrics.Rejected +
                "회 · 위반 " + metrics.UnsafeFailures + "회",
                settings.Provider, referenceId: reference, workItemId: "1"));

            if (SpecialistDelegationPolicy.MustBlockForUnsafeDelegation(metrics))
            {
                var parsed = RoleTextProtocol.ParseWork(result.FinalMessage);
                var original = parsed.IsValid ? parsed.Summary :
                    MilestoneDefinitionContract.Limit(result.FinalMessage, 1800);
                result = result with
                {
                    ExitCode = 1,
                    FinalMessage = RoleTextProtocol.BuildResult(
                        "blocked", SpecialistDelegationPolicy.UnsafeFailureCode,
                        issues: new[] { original }.Concat(
                            metrics.Evidence.Where(entry => entry.Status is
                                "UNSAFE" or "CANCELED").Select(entry => entry.Detail)).ToArray())
                };
            }
            else
            {
                result = SpecialistDelegationPolicy.Enforce(
                    result, mandatory: true,
                    successfulDelegations: metrics.Successful);
            }

            var report = MilestoneDefinitionContract.NormalizeWorkReport(
                result.ExitCode, result.FinalMessage, result.StandardError);
            var normalizedStatus = MilestoneDefinitionContract.ReadWorkStatus(report);
            RunOnUi(() => AddRoleResponseHistory(WorkerRoleState.Manager,
                "SuperVisor 통합 보고", report, result.Usage, result.Files,
                status: string.Equals(normalizedStatus, "blocked",
                    StringComparison.OrdinalIgnoreCase) ? "BLOCKED" : "RECEIVED",
                providerWireId: settings.Provider, fullMessage: report,
                workItemId: "1", referenceId: reference));
            return new SupervisorExecutionResult(report, metrics);
        }
        finally
        {
            _bridgeServer?.UnregisterMcpSession(token);
        }
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

        // A new ID is unique for this user task; only an explicit CONTINUE
        // can reuse an unfinished ID. Persist ownership before invoking Luna.
        if (!WorkExecutionJournal.TryBegin(
                workingDirectory, jobId, milestone.Id, work, implementer,
                out var checkpoint, out var startError))
        {
            return new(work.Id, RoleTextProtocol.BuildResult(
                "blocked", startError));
        }

        var sessionId = checkpoint!.SessionId;
        var historyInvocationReference = WorkerHistoryCardPolicy.NewInvocationReference(
            milestone.Id, work.Id);
        var isolatedRole = implementer with
        {
            ThreadSessionId = null,
            ThreadProjectPath = null
        };

        async Task<AiRoleRunResult> RunAsync(string currentPrompt)
        {
            if (!WorkExecutionJournal.Record(
                    workingDirectory, jobId, work.Id, "RUNNING",
                    sessionId, invocationStarted: true))
                throw new IOException("WORK_CHECKPOINT_WRITE_FAILED");

            var run = await RunCoordinatorRoleAsync(
                jobId,
                "WORK",
                currentPrompt,
                isolatedRole,
                workingDirectory,
                sessionId,
                null,
                cancellationToken,
                work.ReadOnly
                    ? CodexSandboxMode.ReadOnly
                    : CodexSandboxMode.WorkspaceWrite,
                sessionStarted: id =>
                {
                    sessionId = CodexCliRunner.NormalizeSessionId(id) ?? sessionId;
                    WorkExecutionJournal.Record(
                        workingDirectory, jobId, work.Id, "RUNNING", sessionId);
                },
                historyWorkItemId: work.Id,
                historyReferenceId: historyInvocationReference);

            sessionId = CodexCliRunner.NormalizeSessionId(run.SessionId) ?? sessionId;
            return run;
        }

        AiRoleRunResult result;
        try
        {
            var currentPrompt = prompt;
            var continuationTurns = 0;
            var reResolutionPerformed = false;
            for (;;)
            {
                var beforeTurn = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    workingDirectory, cancellationToken);
                result = await RunAsync(currentPrompt);
                var parsed = RoleTextProtocol.ParseWork(result.FinalMessage);
                if (result.ExitCode == 0 && !parsed.IsValid && sessionId is not null)
                {
                    // Reformat only; never re-execute the original implementation.
                    _formatRecoveryJobs[jobId] = 1;
                    result = await RunAsync(
                        "직전 WORKITEM의 작업·명령·파일 수정은 다시 실행하지 마라. " +
                        "직전 실행 결과를 그대로 유지하고 형식만 [ACTION=RESULT], " +
                        "@@REPORT의 <STATUS>, <SUMMARY>, <CHANGED_PATH>, <ISSUES>, " +
                        "[RESPONSE=OK] 계약에 맞추어 재출력하라.\n\n" +
                        RoleContractLoader.LoadWorkFooter());
                    parsed = RoleTextProtocol.ParseWork(result.FinalMessage);
                }

                var state = result.ExitCode != 0 || !parsed.IsValid
                    ? "BLOCKED"
                    : parsed.Status switch
                    {
                        "completed" => "COMPLETED",
                        "in_progress" => "IN_PROGRESS",
                        _ => "BLOCKED"
                    };

                // Existing progress-based CONTINUE remains bounded to two
                // continuations and is separate from the one-time self-review.
                // The review is only considered when the current WORK attempt
                // would otherwise finish; it never interrupts active progress.
                var progress = false;
                if (state == "IN_PROGRESS" && continuationTurns < 2 &&
                    !reResolutionPerformed)
                {
                    var afterTurn =
                        await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                            workingDirectory, cancellationToken);
                    progress = MilestoneMechanicalExecutor.DiffChangeStates(
                        beforeTurn, afterTurn).Any(path =>
                            MilestoneMechanicalExecutor.IsPathWithinScopes(
                                path, work.WritePaths));
                }
                if (state == "IN_PROGRESS" && progress)
                {
                    if (!WorkExecutionJournal.Record(
                            workingDirectory, jobId, work.Id, "IN_PROGRESS",
                            sessionId, result.FinalMessage))
                        throw new IOException("WORK_CHECKPOINT_WRITE_FAILED");

                    continuationTurns++;
                    currentPrompt =
                        "동일 WORKITEM #" + work.Id + "의 같은 세션에서 이어서 작업하라. " +
                        "새 WORKITEM이 아니며 쓰기 범위는 바꾸지 않는다. " +
                        "남은 구현과 검증을 진행하고 완료하면 completed, " +
                        "아직 해결 가능한 잔여 작업이 있으면 in_progress를 보고하라. " +
                        "같은 실패를 그대로 반복하지 마라.";
                    continue;
                }

                // A provisional final report with an actual unresolved item
                // earns exactly one self-review. Claim it durably BEFORE the
                // invocation; the ID must never become terminal and then be
                // reassigned. No retry on CLI errors or invalid protocols.
                var firstReport = MilestoneDefinitionContract.NormalizeWorkReport(
                    result.ExitCode, result.FinalMessage, result.StandardError);
                if (!reResolutionPerformed && sessionId is not null &&
                    WorkReResolutionPolicy.IsEligible(result.ExitCode, parsed) &&
                    WorkExecutionJournal.TryClaimReResolution(
                        workingDirectory, jobId, work.Id, sessionId, firstReport))
                {
                    reResolutionPerformed = true;
                    AddDataFlowHistory(
                        WorkerRoleState.Work,
                        "작업 재해결",
                        $"WORK_ITEM_ID: {work.Id} · 미해결 항목 재검토 1/1",
                        status: "PROCESSING",
                        workItemId: work.Id,
                        referenceId: historyInvocationReference,
                        persistenceSource: "WORKER ACTION");
                    currentPrompt = WorkReResolutionPolicy.BuildPrompt(
                        prompt, firstReport, work.Id);
                    continue;
                }

                if (!WorkExecutionJournal.Record(
                        workingDirectory, jobId, work.Id, state,
                        sessionId, result.FinalMessage))
                    throw new IOException("WORK_CHECKPOINT_WRITE_FAILED");
                break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WorkExecutionJournal.Record(workingDirectory, jobId, work.Id,
                "CANCELED", sessionId);
            throw;
        }
        catch
        {
            WorkExecutionJournal.Record(workingDirectory, jobId, work.Id,
                "IN_PROGRESS", sessionId);
            throw;
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
            referenceId: historyInvocationReference);

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

        RunOnUi(() => AddRoleProgressHistory(
            WorkerRoleState.Resource,
            $"RESOURCE #{resource.Id} 생성 시작 · 독립 실행 대기",
            referenceId: milestone.Id + ":RESOURCE:" + resource.Id,
            workItemId: "0"));

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
            ResourceImagePromptBuilder.Build(resource),
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

        if (!ResourceArtifactValidator.TrySelect(
                resource, completion.SavedPaths, out var selectedPath,
                out var validationDetail))
        {
            var validationReport =
                "RESOURCE_STATUS: BLOCKED" + Environment.NewLine +
                validationDetail;
            AddRoleResponseHistory(
                WorkerRoleState.Resource, "리소스 규격 불합격",
                validationReport, status: "BLOCKED",
                workItemId: "0",
                referenceId: milestone.Id + ":RESOURCE:" + resource.Id);
            return new(resource.Id, validationReport, Array.Empty<string>());
        }

        var moveResult = MoveResourceResults(
            workingDirectory,
            resource.TargetPath,
            new[] { selectedPath });
        if (moveResult.ChangedPaths.Count > 0)
            _resourcePendingGit.Register(workingDirectory, moveResult.ChangedPaths);
        var completedReport = moveResult.Report + Environment.NewLine +
            validationDetail;
        AddRoleResponseHistory(
            WorkerRoleState.Resource,
            "리소스 반영",
            completedReport,
            status: moveResult.Report.StartsWith(
                "RESOURCE_STATUS: COMPLETED",
                StringComparison.Ordinal)
                    ? "COMPLETED"
                    : "BLOCKED",
            workItemId: "0",
            referenceId: milestone.Id + ":RESOURCE:" + resource.Id);
        return new(
            resource.Id,
            completedReport,
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

    private async Task<string> ExecuteMilestoneHighAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        IReadOnlyDictionary<string, string> workReports,
        IReadOnlyDictionary<string, string> resourceReports,
        IReadOnlyList<string> mechanicalReports,
        WorkerAiRoleSettings implementer,
        WorkerAiRoleSettings high,
        int validationRound,
        CancellationToken cancellationToken,
        DelegationMetrics? supervisorDelegations = null,
        Action<DelegationMetrics>? recordDelegations = null)
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

        var historyInvocationReference = WorkerHistoryCardPolicy.NewInvocationReference(
            milestone.Id, "HIGH");
        var prompt = RoleContractLoader.BuildHighPrompt(
            MilestoneDefinitionContract.BuildHighContext(
                milestone,
                workReports,
                mechanicalReports,
                supervisorDelegations));

        var result = await RunHighDelegatedReviewAsync(
            jobId, workingDirectory, high, prompt,
            historyInvocationReference, cancellationToken,
            recordDelegations);

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
            referenceId: historyInvocationReference);

        return report;
    }

    private async Task<AiRoleRunResult> RunHighDelegatedReviewAsync(
        string jobId, string workingDirectory, WorkerAiRoleSettings high,
        string prompt, string historyReferenceId, CancellationToken cancellationToken,
        Action<DelegationMetrics>? recordDelegations = null)
    {
        if (high.ProviderKind != AiServiceProvider.OpenAI)
            throw new InvalidOperationException(
                "HIGH_WORK_DELEGATION_REQUIRES_CODEX_CLI");

        var token = _bridgeServer?.RegisterMcpSession(
            "HIGH", jobId, workingDirectory, cancellationToken) ??
            throw new InvalidOperationException("HIGH_MCP_BRIDGE_NOT_RUNNING");
        try
        {
            // As in UnityWorker, HIGH awaits each MCP WORK invocation and
            // receives its real result inside this SAME parent provider turn.
            var result = await RunCoordinatorRoleAsync(
                jobId, "HIGH",
                RoleContractLoader.BuildHighPrompt(
                    RoleContractLoader.BuildHistoryPrompt(prompt) +
                    Environment.NewLine +
                    "실제 파일 검토·테스트는 delegate_work(read_only=true)로 먼저 요청하라. " +
                    "MCP 도구 호출이 WORK 결과까지 반환하므로 직접 검증 내용을 확인하고 " +
                    "미검증 항목을 구분해 최종 판정하라. 직접 명령·파일 수정은 금지한다."),
                high, workingDirectory, null, null, cancellationToken,
                CodexSandboxMode.ReadOnly,
                historyWorkItemId: "HIGH",
                historyReferenceId: historyReferenceId, mcpToken: token);
            var metrics = _bridgeServer.GetDelegationMetrics(token);
            recordDelegations?.Invoke(metrics);
            RunOnUi(() => AddRoleProgressHistory(
                WorkerRoleState.High,
                "WORK 검증 위임: 시도 " + metrics.Attempts +
                "회 · 완료 " + metrics.Successful +
                "회 · 실패 " + (metrics.RecoverableFailures +
                    metrics.UnsafeFailures + metrics.Rejected) + "회",
                high.Provider, referenceId: historyReferenceId,
                workItemId: "HIGH"));

            if (metrics.UnsafeFailures > 0)
            {
                var parsed = RoleTextProtocol.ParseHigh(result.FinalMessage);
                result = result with
                {
                    ExitCode = 1,
                    FinalMessage = RoleTextProtocol.BuildResult(
                        "blocked", "HIGH_WORK_UNSAFE",
                        issues: metrics.Evidence.Where(item =>
                            item.Status is "UNSAFE" or "CANCELED")
                            .Select(item => item.Detail)
                            .Prepend(parsed.IsValid ? parsed.Summary :
                                MilestoneDefinitionContract.Limit(result.FinalMessage, 1000))
                            .ToArray())
                };
            }
            else if (metrics.Successful == 0 && result.ExitCode == 0 &&
                     string.Equals(
                         RoleTextProtocol.ParseHigh(result.FinalMessage).Status,
                         "completed", StringComparison.OrdinalIgnoreCase))
            {
                result = result with
                {
                    ExitCode = 1,
                    FinalMessage = RoleTextProtocol.BuildResult(
                        "blocked", "HIGH_WORK_DELEGATION_REQUIRED"),
                    StandardError = result.StandardError + Environment.NewLine +
                        "HIGH_WORK_DELEGATION_REQUIRED"
                };
            }
            return result;
        }
        finally
        {
            _bridgeServer?.UnregisterMcpSession(token);
        }
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
            "WORK #8 전용 복구: 실제 코드 빌드 오류만 이 호출에서 한 번 최소 수정하라." +
            Environment.NewLine + "@@INSTRUCTIONS" + Environment.NewLine +
            "원래 작업 범위 외 변경 금지. Git 명령 실행 금지. " +
            "환경 EPERM 등 실행 환경 오류를 소스 수정으로 우회하지 마라. " +
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
            "소스·설정을 직접 수정하지 말고 오류 원인과 필요한 수정 사항만 보고하라." +
            Environment.NewLine +
            "@@ERROR" + Environment.NewLine +
            MilestoneDefinitionContract.Limit(diagnostic, 2400));
        var result = await RunHighDelegatedReviewAsync(
            jobId, workingDirectory, high, prompt,
            milestone.Id + ":HIGH:GIT:" + stage, cancellationToken);
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
        string? remoteMainSha,
        bool includeFullContract = true) =>
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
        "사용자가 지정한 프로젝트 루트가 유일한 실행 작업공간이다. HQ는 마일스톤의 구현 목표를 @@SV 섹션에만 지정한다. SV가 실제 파일·범위·WORK 병렬 위임을 자율적으로 계획한다." +
        Environment.NewLine +
        "SV는 매 마일스톤 새 세션으로 실행되며 각 임시 WORK도 새 세션이다. HIGH는 결과를 읽기 전용으로 검토한다." +
        Environment.NewLine +
        "입력 본문:" +
        Environment.NewLine +
        body +
        Environment.NewLine +
        Environment.NewLine +
        RoleContractLoader.HqContractForTurn(includeFullContract);

    private static string BuildMilestoneFollowupInput;
        CoordinatorContinuationState continuation,
        string followup)
    {
        return "이전 HQ 상태:" + Environment.NewLine +
            continuation.LastHqMessage + Environment.NewLine +
            "마일스톤별 SV 및 모든 임시 WORK는 신규 세션이다. 파일과 명시적 보고를 통해 다음 @@SV를 설계하라." +
            Environment.NewLine + "재개 입력:" + Environment.NewLine + followup;
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
