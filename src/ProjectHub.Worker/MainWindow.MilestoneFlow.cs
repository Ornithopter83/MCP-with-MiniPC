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
        var high = NormalizeRoleSessionForWorkspace(
            _targetSettings.EffectiveHighLevel,
            normalizedRoot);
        var designRole = NormalizeRoleSessionForWorkspace(
            _targetSettings.EffectiveDesign,
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
        // Timeout/error handlers execute outside the main try body. Keep the
        // latest HQ identity and completed HQ message in the outer scope.
        // New user task: always create a new CLI HQ session.
        // PAUSE/INTERRUPTED recovery keeps the previous task's session.
        var hqSession = continuing
            ? CodexCliRunner.NormalizeSessionId(continuation!.CoordinatorSessionId)
            : null;
        var lastHqMessage = continuation?.LastHqMessage ?? string.Empty;

        // Foreground window protection spans preflight, DESIGN, WORK, CLI
        // recompilation and HIGH; a Codex command hook cannot intercept an
        // already-running Editor promoting its own native window.
        using var unityFocusGuard = UnityBackgroundFocusGuard.Start(normalizedRoot,
            new System.Windows.Interop.WindowInteropHelper(this).Handle);
        UnityEditorRecoverySupervisor? editorRecoverySupervisor = null;
        if (!unityFocusGuard.Available)
            AddTaskMessage("UNITY BACKGROUND GUARD",
                unityFocusGuard.Diagnostic, status: "UNVERIFIED");

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
                // Restore undelivered user input after a process interruption.
                foreach (var entry in interventionQueue.Pending())
                    if (!_historyEvents.Any(card =>
                        card.EventType == "USER_INTERVENTION" &&
                        card.ReferenceId == entry.Id))
                        AddHqInterventionHistory(entry, delivered: false);
            }

            // This path runs only after a real user command was submitted.
            // The '＋ 새 작업' navigation button does not call this method.
            // Prepare destructive NEW-task Git changes before opening an Editor,
            // so Unity never holds an outdated scene in memory during checkout.
            if (!continuing)
            {
                var newTaskGit = await MilestoneMechanicalExecutor.CheckGitReadyAsync(
                    normalizedRoot, "main", initializeIfMissing: true,
                    _targetSettings.ManualRepositoryUrl?.Trim(), cts.Token,
                    overwriteNewTaskConflicts: true);
                AddTaskMessage("NEW TASK GIT PREFLIGHT", newTaskGit.Summary,
                    status: newTaskGit.Success ? "COMPLETED" : "BLOCKED");
                if (!newTaskGit.Success)
                    throw new InvalidOperationException(
                        "NEW_TASK_GIT_PREFLIGHT_BLOCKED: " + newTaskGit.Summary);
            }

            // New commands and resumed/added user commands are the only
            // events allowed to open Unity. Ordinary milestone iteration never
            // starts another Editor. An already running project's lock is honored.
            AddTaskMessage("UNITY EDITOR COMMAND GATE",
                "명령이 접수되어 지정 프로젝트의 Editor / Pipeline 연결을 확인합니다.",
                status: "PROCESSING");
            var unityPreflight =
                await UnityEditorCommandLifecycle.EnsureForAcceptedCommandAsync(
                    normalizedRoot, cts.Token);
            AddTaskMessage(
                "UNITY TOOLCHAIN / EDITOR PREFLIGHT",
                unityPreflight.Code + ": " + unityPreflight.Detail,
                status: unityPreflight.Ready ? "COMPLETED" :
                    unityPreflight.Code == "UNITY_PROJECT_NOT_FOUND"
                        ? "BLOCKED" : "UNVERIFIED");
            // Editor connectivity is an execution capability, not a reason to
            // prevent HQ from planning code-only work or choosing PAUSE.
            // Keep the failure visible to HQ; never treat it as Import proof.
            if (unityPreflight.Code == "UNITY_PROJECT_NOT_FOUND")
                throw new InvalidOperationException(
                    "UNITY_PROJECT_NOT_FOUND: " + unityPreflight.Detail);

            // The first HQ preflight is only a snapshot. Later Editor recovery
            // must be visible to milestone execution and Native verification.
            var editorConnection = new UnityEditorConnectionState(
                normalizedRoot, unityPreflight,
                UnityToolchainPreflight.RunUncoordinatedAsync);

            // Live only while this user job is running. It probes without
            // waiting for AI roles' long-held Editor lane and never persists
            // into the dashboard idle state.
            if (UnityToolchainPreflight.ValidateProject(normalizedRoot).Ready)
                editorRecoverySupervisor = UnityEditorRecoverySupervisor.StartForJob(
                    normalizedRoot, cts.Token, (detail, status) =>
                        AddTaskMessage("UNITY EDITOR RECOVERY", detail,
                            status: status, includeHistory: false),
                    editorConnection.Observe);

            var unityCatalogPath = UnityToolchainPreflight.SaveCatalogForJob(
                normalizedRoot, jobId, unityPreflight);
            if (unityCatalogPath is not null)
                AddTaskMessage("UNITY CLI COMMAND CATALOG",
                    "실제 Editor에서 조회한 명령·매개변수 목록 보존: " +
                    unityCatalogPath, status: "OBSERVED");

            // Re-probe immediately before an Editor-dependent operation.
            // The background recovery watcher also updates this shared state,
            // but process existence/localhost listeners never count as ready.
            async Task<bool> RecheckUnityEditorAsync(CancellationToken token)
            {
                var previous = editorConnection.Current;
                var refreshed = await editorConnection.RecheckAsync(token);
                if (previous.Ready != refreshed.Ready ||
                    previous.Code != refreshed.Code)
                    AddTaskMessage("UNITY EDITOR RECHECK",
                        refreshed.Code + ": " + refreshed.Detail,
                        status: refreshed.Ready ? "OBSERVED" : "UNVERIFIED",
                        includeHistory: false);

                // Invalidate an older catalog on failure; publish only
                // command records from the current live CLI response.
                var updatedCatalog = UnityToolchainPreflight.SaveCatalogForJob(
                    normalizedRoot, jobId, refreshed);
                if (updatedCatalog is not null && !previous.Ready)
                    AddTaskMessage("UNITY CLI COMMAND CATALOG",
                        "Editor 재연결 후 실시간 명령 목록: " + updatedCatalog,
                        status: "OBSERVED", includeHistory: false);
                return refreshed.Ready;
            }

            var editorRecoveryNotice = unityPreflight.Ready ? string.Empty :
                "UNITY_EDITOR_UNAVAILABLE: " + unityPreflight.Code + ": " +
                unityPreflight.Detail + Environment.NewLine +
                "Unity Editor 작업은 확인 전 실행하지 말고, 가능한 코드/진단 대안을 " +
                "계획하라. 해결 수단이 없으면 HQ가 [ACTION=PAUSE]를 판단하라." +
                Environment.NewLine;

            stagedAttachments = UserAttachmentTransport.StageForWorkerRuntime(
                attachments,
                normalizedRoot,
                jobId + "-hq");
            var webAttachments = attachments?
                .Select(UserAttachmentTransport.CreateBridgeAttachment)
                .ToList() ?? new List<BridgeAttachment>();

            var configuredRepositoryUrl =
                _targetSettings.ManualRepositoryUrl?.Trim();
            var priorHandoff = !continuing && !IsWebTransport(coordinator.Transport)
                ? ProjectHandoffJournal.ReadForNewTask(normalizedRoot) : null;
            var handoffContext = string.IsNullOrWhiteSpace(priorHandoff)
                ? ""
                : "이전 프로젝트 인계문 (참고 자료, 이전 세션 자동 재개 금지):" +
                  Environment.NewLine + priorHandoff +
                  Environment.NewLine +
                  "이전 상태를 사실로 가정하지 말고 최신 사용자 지시와 Git·실제 파일을 우선하라." +
                  Environment.NewLine + Environment.NewLine;
            var webHq = IsWebTransport(coordinator.Transport);
            var hqInbound = editorRecoveryNotice +
                (continuing
                    ? BuildMilestoneFollowupInput(continuation!, request, webHq)
                    : webHq ? request : handoffContext +
                        "새 사용자 지시:" + Environment.NewLine + request);
            ProjectHandoffJournal.RecordUserInstruction(
                normalizedRoot, jobId,
                continuing ? "FOLLOWUP-" + DateTimeOffset.UtcNow.ToString("O") : "NEW-TASK",
                request);
            var lastRemoteError = string.Empty;

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
                    // Avoid duplicating the same diagnostic on every HQ turn.
                    var error = remoteReference.Summary;
                    if (!string.Equals(lastRemoteError, error, StringComparison.Ordinal))
                    {
                        hqInbound = "HQ_REMOTE_REFERENCE_FAILED: " +
                            error + Environment.NewLine +
                            "원격 참조를 확인할 수 없다. 검증하지 않은 원격 상태를 " +
                            "가정하지 말고 로컬 읽기 전용 진단 또는 복구를 선택하라. " +
                            "대안이 없으면 HQ가 PAUSE를 선택하라." +
                            Environment.NewLine + hqInbound;
                        lastRemoteError = error;
                    }
                }
                else
                    lastRemoteError = string.Empty;

                AddDataFlowHistory(
                    WorkerRoleState.Unknown,
                    "Worker 작업",
                    "HQ 원격 참조 갱신" +
                    Environment.NewLine +
                    remoteReference.Summary,
                    status: remoteReference.Success ? "COMPLETED" : "FAILED",
                    persistenceSource: "WORKER ACTION");

                // The next dispatched AI claims user messages atomically.
                // HQ has no priority over DESIGN, WORK or HIGH.
                var hqHistoryReferenceId = WorkerHistoryCardPolicy.NewInvocationReference(
                    "HQ", "MILESTONE_" + milestoneIndex);
                var hqPrompt = BuildMilestoneHqPrompt(
                    inboundType,
                    hqInbound,
                    normalizedRoot,
                    configuredRepositoryUrl,
                    remoteReference.CommitSha,
                    IsWebTransport(coordinator.Transport));

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
                if (!string.IsNullOrWhiteSpace(hqMessage))
                    lastHqMessage = hqMessage;
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
                    // A failed AI response releases its batch; grammar
                    // recovery will claim it again, without HQ-only routing.
                    var recoveryPrompt = BuildMilestoneHqPrompt(
                        "HQ_RESPONSE_RECOVERY",
                        recoveryBody,
                        normalizedRoot,
                        configuredRepositoryUrl,
                        remoteReference.CommitSha,
                    IsWebTransport(coordinator.Transport));

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
                    WorkerHistoryCardFormatter.BuildHqReceiptBody(
                        hqAction.Name, hqMessage),
                    status: "RECEIVED",
                    persistenceSource: "WORKER ACTION");

                // Delivery is acknowledged by the actual recipient role.

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
                    ProjectHandoffJournal.RecordHqControl(
                        normalizedRoot, jobId, "PAUSED", hqMessage);
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
                    // HQ cannot declare the goal complete while a WORKITEM
                    // still owns an unfinished execution checkpoint.
                    var unfinished = WorkExecutionJournal.ReadAll(normalizedRoot, jobId)
                        .Where(item => item.State is "RUNNING" or "IN_PROGRESS")
                        .Select(item => item.WorkItemId)
                        .ToArray();
                    if (unfinished.Length > 0)
                    {
                        hqInbound = "END_REJECTED: WORK_IN_PROGRESS=" +
                            string.Join(",", unfinished) + Environment.NewLine +
                            "미완료 WORKITEM의 기존 ID를 @@WORK=<ID> 및 " +
                            "<MODE>CONTINUE</>로 이어서 수행하거나, 명시적으로 " +
                            "사용자 조작이 필요한 경우에만 PAUSE를 선택하라.";
                        AddDataFlowHistory(
                            WorkerRoleState.Hq,
                            "Worker 작업",
                            hqInbound,
                            status: "BLOCKED",
                            persistenceSource: "WORKER ACTION");
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
                    ProjectHandoffJournal.RecordHqControl(
                        normalizedRoot, jobId, "DONE", hqMessage);
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
                // HQ owns the plan. Worker mirrors its accepted contract output.
                ProjectHandoffJournal.RecordHqPlan(
                    normalizedRoot, jobId, milestone.Id,
                    milestone.PlanDocument, hqMessage);
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
                        milestone.Design is not null;
                    if (_currentMilestoneResourceReserved != true)
                        _resourceSidecarStatus = "로컬 Design";
                    UpdatePipelineVisuals();
                    AddDataFlowHistory(
                        WorkerRoleState.Unknown,
                        "Worker 작업",
                        $"HQ 응답 파싱 완료\nMILESTONE: {milestone.Id}\nWORK: {milestone.WorkItems.Count}건\nRESOURCE: {milestone.Resources.Count}건",
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
                        designRole,
                        RecheckUnityEditorAsync,
                        unityFocusGuard,
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

                ProjectHandoffJournal.RecordMilestoneReport(
                    normalizedRoot, jobId, milestone!.Id, milestoneResult.Body);

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
                    ProjectHandoffJournal.RecordHqControl(
                        normalizedRoot, jobId, "PAUSED", hqMessage);
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
            {
                ProjectHandoffJournal.RecordHqControl(
                    normalizedRoot, jobId, "CANCELED", lastHqMessage);
                CompleteFullCancellationUi();
            }
            else if (_jobTimedOut)
            {
                // Idle timeout is NOT a user cancel or a terminal WORK state.
                // Preserve the job/WORK checkpoint and keep the WPF app alive.
                var detail = "WORKER_IDLE_TIMEOUT: Codex/Web가 종료되지 않아 " +
                    "실행을 중단했습니다. 기존 파일과 미완료 WORK 체크포인트를 " +
                    "보존했습니다. 재시작 시 HQ는 완료 증거를 먼저 검사하고 " +
                    "미완료 WORK만 이어서 진행해야 합니다.";
                SaveMilestoneContinuation(
                    "INTERRUPTED", lastHqMessage, jobId, normalizedRoot,
                    coordinator, implementer, hqSession, high);
                ProjectHandoffJournal.RecordHqControl(
                    normalizedRoot, jobId, "INTERRUPTED", lastHqMessage);
                AddTaskMessage("TASK INTERRUPTED", detail, status: "RECOVERABLE");
                ResultTitle.Text = "INTERRUPTED · 이어하기 가능";
                ResultBody.Text = detail;
                TaskTitle.Text = "30분 무응답 · 작업 상태 보존";
                SetFlowState(false, false, false);
                SetFollowupComposerVisible(true);
                SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
                ExportTaskTranscript();
            }
            else
            {
                // Other lifecycle cancellation must not escape an async void
                // UI event handler and crash the Worker process.
                var detail = "WORKER_EXECUTION_INTERRUPTED: 작업이 외부에서 취소됨";
                SaveMilestoneContinuation(
                    "INTERRUPTED", lastHqMessage, jobId, normalizedRoot,
                    coordinator, implementer, hqSession, high);
                ProjectHandoffJournal.RecordHqControl(
                    normalizedRoot, jobId, "INTERRUPTED", lastHqMessage);
                AddTaskMessage("TASK INTERRUPTED", detail, status: "RECOVERABLE");
                ResultTitle.Text = "INTERRUPTED";
                ResultBody.Text = detail;
                SetFlowState(false, false, false);
                SetFollowupComposerVisible(true);
                SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
                ExportTaskTranscript();
            }
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
            ProjectHandoffJournal.RecordHqControl(
                normalizedRoot, jobId, "DONE_WITH_ERROR", detail);
            SetFollowupComposerVisible(true);
            SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
        }
        finally
        {
            // Shut down monitoring before transitioning this job to idle;
            // no later timer tick may start an Editor on an idle dashboard.
            if (editorRecoverySupervisor is not null)
                await editorRecoverySupervisor.DisposeAsync();
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

            // End protection and restore only windows parked by this job.
            var focusGuardWasAvailable = unityFocusGuard.Available;
            unityFocusGuard.Dispose();
            AddTaskMessage("UNITY BACKGROUND GUARD",
                unityFocusGuard.Diagnostic,
                status: focusGuardWasAvailable &&
                        unityFocusGuard.RestoreFailures == 0
                    ? "OBSERVED" : "UNVERIFIED",
                includeHistory: false);
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
    /// The foreground exception is restricted to this one Native Game View
    /// capture. A user switching away from Worker prevents automatic activation
    /// and a user switching away during capture prevents forced focus recovery.
    /// Native screenshots are not marked visually approved without inspection.
    /// </summary>
    private async Task<UnityBackgroundVisualEvidence> CaptureBestAvailableVisualAsync(
        UnityBackgroundVisualPlan plan, UnityBackgroundFocusGuard guard,
        CancellationToken cancellationToken)
    {
        var lease = guard.BeginVisualVerification();
        UnityBackgroundVisualEvidence native;
        try
        {
            native = lease.Active
                ? await UnityBackgroundVisualTools.CaptureNativeAsync(
                    plan, cancellationToken)
                : new UnityBackgroundVisualEvidence(false,
                    "NATIVE_CAPTURE_SKIPPED: " + lease.Diagnostic, null);
        }
        finally
        {
            // Restores Worker even on cancellation/timeout or a failed capture.
            lease.Dispose();
            AddTaskMessage("UNITY NATIVE FOCUS",
                lease.Diagnostic, status: lease.Diagnostic ==
                    "FOCUS_LEASE_WORKER_RESTORED" ? "OBSERVED" : "UNVERIFIED",
                includeHistory: false);
        }

        AddTaskMessage("UNITY NATIVE GAME VIEW",
            native.Detail, status: native.Captured ? "OBSERVED" : "UNVERIFIED",
            includeHistory: false);
        if (native.Captured)
            return native;

        // The existing offscreen path is only a supplementary approximation.
        // In particular, a camera-free Screen Space Overlay UI cannot rely on it.
        var offscreen = await UnityBackgroundVisualTools.CaptureAsync(
            plan, cancellationToken);
        return offscreen with
        {
            Detail = "NATIVE=" + native.Detail + "; OFFSCREEN=" + offscreen.Detail
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
            var initialHigh = await ExecuteMilestoneHighAsync(
                jobId, workingDirectory, milestone, workReports, resourceReports,
                mechanicalReports, implementer, high, 1, cancellationToken);
            highReport = initialHigh.Report;
            // Keep the actual HIGH CLI session for same-milestone DESIGN handoffs.
            // If the provider has no resumable session, the previous report is
            // explicitly supplied as evidence on the next invocation.
            var highSessionId = initialHigh.SessionId;
            var highChangedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var afterHigh = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                workingDirectory, cancellationToken);
            foreach (var path in MilestoneMechanicalExecutor.DiffChangeStates(
                beforeHigh, afterHigh))
            {
                milestoneChangedPaths.Add(path);
                highChangedPaths.Add(path);
            }

            // HIGH remains nonterminal until an evidence-bound DESIGN handoff
            // returns and the original HIGH role reviews the actual changes.
            var designHandoffs = 0;
            var designRevisionBuildFailed = false;
            while (MilestoneDefinitionContract.ReadHighStatus(highReport) == "design_handoff")
            {
                if (designHandoffs >= HighDesignHandoffContract.MaxReviewHandoffs)
                {
                    // The latest HIGH request contains the unresolved
                    // defect, its target and evidence. HQ must see these
                    // details rather than a generic budget failure.
                    var unfinishedDesignRequest = highReport;
                    highReport = RoleTextProtocol.BuildResult(
                        "blocked", "HIGH_DESIGN_HANDOFF_BUDGET_EXHAUSTED",
                        issues: new[] {
                            "DESIGN 재수정 2회 소진. 세 번째 호출은 하지 않고 HQ 판단에 인계.",
                            MilestoneDefinitionContract.Limit(
                                unfinishedDesignRequest, 3600)
                        });
                    break;
                }
                if (!HighDesignHandoffContract.TryRead(
                        highReport, out var request, out var handoffError) ||
                    request is null)
                {
                    highReport = RoleTextProtocol.BuildResult(
                        "blocked", "HIGH_DESIGN_HANDOFF_INVALID",
                        issues: new[] { handoffError });
                    break;
                }

                designHandoffs++;
                var invocation = designHandoffs;
                var verifiedEvidence = HighDesignHandoffContract.ExistingEvidencePaths(
                    workingDirectory, request.EvidencePaths);
                var missingEvidence = request.EvidencePaths.Except(verifiedEvidence,
                    StringComparer.OrdinalIgnoreCase).ToArray();
                if (missingEvidence.Length > 0 || request.Warnings.Count > 0 ||
                    verifiedEvidence.Count == 0)
                    mechanicalReports.Add("HIGH_DESIGN_HANDOFF_EVIDENCE_UNVERIFIED: " +
                        string.Join("; ", missingEvidence.Concat(request.Warnings)) +
                        (verifiedEvidence.Count == 0 ? "; no readable capture" : ""));
                var evidenceDescription = verifiedEvidence.Count > 0
                    ? string.Join("\n", verifiedEvidence)
                    : "UNVERIFIED: no valid captured image; inspect scene directly";
                mechanicalReports.Add("HIGH_DESIGN_HANDOFF: " + invocation +
                    " GOAL: " + request.Goal +
                    " EVIDENCE: " + string.Join("; ", verifiedEvidence) +
                    " TARGET: " + request.TargetPath);
                var handoffDesign = milestone.Design is { } existingDesign
                    ? existingDesign with
                    {
                        Instructions = request.Goal + "\nExpected: " + request.ExpectedResult +
                            "\nReason: " + request.Reason +
                            "\nEvidence: " + evidenceDescription,
                        WritePaths = new[] { request.TargetPath }
                    }
                    : new MilestoneDesignDefinition(
                        "docs/projecthub/visual-handoff-" +
                        new string(milestone.Id.Where(char.IsLetterOrDigit).ToArray()) + ".md",
                        request.Goal + "\nExpected: " + request.ExpectedResult +
                            "\nReason: " + request.Reason +
                            "\nEvidence: " + evidenceDescription)
                    { WritePaths = new[] { request.TargetPath } };

                // HIGH has yielded the Editor lane. Mark only DESIGN active
                // before dispatch, even if HQ did not pre-reserve DESIGN #0.
                RunOnUi(() =>
                {
                    TaskDirection.Text = "Design";
                    TaskTitle.Text = $"HIGH 인계 · #0 DESIGN 수정 {invocation}";
                    ResultTitle.Text = "#0 DESIGN";
                    SetFlowState(codexActive: true, workerActive: true,
                        webActive: false, explicitStage: TaskStage.Resource);
                });
                // Exclusive Editor lane while HIGH yields mutation ownership.
                var beforeReviewDesign =
                    await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        workingDirectory, cancellationToken);
                var reviewReference = milestone.Id + ":DESIGN:0:REVISION:" + invocation;
                AddDataFlowHistory(WorkerRoleState.Design, "#0 DESIGN 인계",
                    mechanicalReports.Last(), status: "DISPATCHED",
                    workItemId: "0", referenceId: reviewReference,
                    persistenceSource: "HIGH DESIGN HANDOFF");
                string designResponse;
                string reviewVisualEvidence = "OFFSCREEN_CAPTURE_UNVERIFIED";
                UnityBackgroundVisualPlan? reviewVisualPlan = null;
                try
                {
                    reviewVisualPlan = UnityBackgroundVisualTools.Prepare(
                        workingDirectory, jobId, milestone.Id + "-review-" + invocation);
                }
                catch (Exception visualToolError) when (visualToolError is IOException or
                    UnauthorizedAccessException or ArgumentException)
                {
                    reviewVisualEvidence += ": " + visualToolError.Message;
                }
                try
                {
                    using var reviewEditorLease = await UnityEditorOperationGate.EnterAsync(
                        workingDirectory, cancellationToken);
                    var roleRun = await RunCoordinatorRoleAsync(
                        jobId, "DESIGN",
                        RoleContractLoader.BuildDesignPrompt(handoffDesign, workingDirectory) +
                        (reviewVisualPlan is null ? string.Empty :
                            Environment.NewLine + reviewVisualPlan.AgentGuidance),
                        designRole with { ThreadSessionId = null, ThreadProjectPath = null },
                        workingDirectory, null, null, cancellationToken,
                        CodexSandboxMode.WorkspaceWrite,
                        historyWorkItemId: "0", historyReferenceId: reviewReference);
                    var parsedDesign = RoleTextProtocol.ParseWork(roleRun.FinalMessage);
                    designResponse = roleRun.ExitCode == 0 && parsedDesign.IsValid
                        ? roleRun.FinalMessage
                        : RoleTextProtocol.BuildResult("blocked",
                            "DESIGN_REVISION_INVALID_OR_PROCESS_FAILED",
                            issues: new[] { roleRun.StandardError ?? string.Empty });
                    if (reviewVisualPlan is not null)
                    {
                        if (await recheckUnityEditorAsync(cancellationToken))
                        {
                            var evidence = await CaptureBestAvailableVisualAsync(
                                reviewVisualPlan, unityFocusGuard, cancellationToken);
                            reviewVisualEvidence = evidence.Detail;
                        }
                        else
                            reviewVisualEvidence =
                                "NATIVE_CAPTURE_UNVERIFIED: latest Editor CLI probe not ready";
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    designResponse = RoleTextProtocol.BuildResult(
                        "blocked", "DESIGN_REVISION_ERROR: " + exception.Message);
                }

                var afterReviewDesign =
                    await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        workingDirectory, cancellationToken);
                var revisionChanges = MilestoneMechanicalExecutor.DiffChangeStates(
                    beforeReviewDesign, afterReviewDesign).ToArray();
                foreach (var changed in revisionChanges)
                    milestoneChangedPaths.Add(changed);
                var designParsed = RoleTextProtocol.ParseWork(designResponse);
                // HIGH owns the visual judgement. A C# change can implement
                // real UI appearance without matching a path-name heuristic.
                // Only a true no-op contradicts DESIGN's completed report.
                if (revisionChanges.Length == 0 && designParsed.Status == "completed")
                    designResponse = RoleTextProtocol.BuildResult("blocked",
                        "DESIGN_REPORTED_COMPLETE_WITHOUT_CHANGES",
                        issues: new[] { "No changed file evidence; HIGH cannot credit a no-op." });
                var revisionReport = "DESIGN #0: INVOCATION=" + invocation +
                    " STATUS=" + RoleTextProtocol.ParseWork(designResponse).Status +
                    "\nCHANGED_PATHS=" + string.Join(", ", revisionChanges) +
                    "\nVISUAL_E2E=UNVERIFIED_UNLESS_INSPECTED\n" +
                    "INTERNAL_VISUAL_CAPTURE=" + reviewVisualEvidence + "\n" +
                    MilestoneDefinitionContract.Limit(designResponse, 4000);
                mechanicalReports.Add(revisionReport);
                AddRoleResponseHistory(WorkerRoleState.Design, "#0 DESIGN 반환",
                    revisionReport,
                    status: RoleTextProtocol.ParseWork(designResponse).Status == "completed"
                        ? "COMPLETED" : "BLOCKED",
                    workItemId: "0", referenceId: reviewReference,
                    providerWireId: designRole.Provider);

                // DESIGN has returned; release the yellow card and return
                // ownership to HIGH for integration decisions. The actual
                // HIGH invocation below refreshes this stage again.
                RunOnUi(() =>
                {
                    TaskDirection.Text = "검토";
                    TaskTitle.Text = $"HIGH · DESIGN 결과 확인 {invocation}";
                    ResultTitle.Text = "HIGH";
                    SetFlowState(codexActive: true, workerActive: false,
                        webActive: false, explicitStage: TaskStage.HighLevel);
                });

                if (!HighDesignContinuationPolicy.ShouldResumeHigh(
                        designResponse, revisionChanges))
                {
                    // A failed/no-op DESIGN cannot be credited as a repair.
                    // Report to HQ instead of spending another HIGH invocation.
                    highReport = RoleTextProtocol.BuildResult("blocked",
                        "DESIGN_REVISION_FAILED_WITHOUT_CHANGES",
                        issues: new[] { MilestoneDefinitionContract.Limit(revisionReport, 1200) });
                    break;
                }

                // Recompile only when DESIGN touched integration/build inputs.
                // Graphics-only revisions reuse earlier BUILD evidence.
                if (HighDesignContinuationPolicy.RequiresBuild(revisionChanges))
                {
                    var reviewBuilds = await MilestoneBuildRunner.RunAsync(
                        workingDirectory, jobId, executableDefinitions, cancellationToken,
                await recheckUnityEditorAsync(cancellationToken));
                    foreach (var build in reviewBuilds)
                        mechanicalReports.Add("DESIGN_REVISION_BUILD: " +
                            MilestoneDefinitionContract.FormatMechanicalResult(build));
                    designRevisionBuildFailed = reviewBuilds.Any(build => !build.Success) &&
                        !MilestoneBuildRunner.IsEnvironmentFailure(reviewBuilds);
                    if (reviewBuilds.Any(build => !build.Success) &&
                        !designRevisionBuildFailed)
                        mechanicalReports.Add(
                            "DESIGN_REVISION_BUILD_UNVERIFIED: Editor/tool unavailable; " +
                            "HIGH may continue source review without claiming build success.");
                }

                if (!UnityValidationBudget.CanStart(
                        1 + invocation,
                        HighDesignContinuationPolicy.HasNewIntegrationEvidence(revisionChanges)))
                {
                    highReport = RoleTextProtocol.BuildResult("blocked",
                        "HIGH_VALIDATION_R3_NOT_JUSTIFIED",
                        issues: new[] { "The third integration review requires new evidence." });
                    break;
                }

                // Resume the real HIGH session when available. The previous
                // decision and DESIGN evidence are included even without one.
                var previousHighReport = highReport;
                var beforeResume = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    workingDirectory, cancellationToken);
                var resumedHigh = await ExecuteMilestoneHighAsync(
                    jobId, workingDirectory, milestone, workReports, resourceReports,
                    mechanicalReports, implementer, high,
                    1 + invocation, cancellationToken,
                    highSessionId, previousHighReport);
                highReport = resumedHigh.Report;
                highSessionId = resumedHigh.SessionId ?? highSessionId;
                var afterResume = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    workingDirectory, cancellationToken);
                foreach (var changed in MilestoneMechanicalExecutor.DiffChangeStates(
                    beforeResume, afterResume))
                {
                    milestoneChangedPaths.Add(changed);
                    highChangedPaths.Add(changed);
                }
            }

            if (outcome == "COMPLETED" &&
                !string.Equals(MilestoneDefinitionContract.ReadHighStatus(highReport),
                    "completed", StringComparison.OrdinalIgnoreCase))
                outcome = "HIGH_BLOCKED_OR_UNVERIFIED";

            CaptureResourceState();

            // HIGH may edit compilation/scene inputs after earlier evidence.
            // Validate its changes once before finalization; never treat a
            // failed recompile as a passed milestone.
            if (HighDesignContinuationPolicy.RequiresBuild(highChangedPaths))
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
                        highReport = HighDesignContinuationPolicy.InvalidateCompletion(
                            highReport, "HIGH_FINAL_BUILD_FAILED");
                        outcome = "HIGH_FINAL_BUILD_FAILED";
                    }
                }
                else
                    designRevisionBuildFailed = false;
            }
            if (designRevisionBuildFailed)
            {
                mechanicalReports.Add("HIGH_BEFORE_FAILED_DESIGN_BUILD: " +
                    MilestoneDefinitionContract.Limit(highReport, 1200));
                highReport = HighDesignContinuationPolicy.InvalidateCompletion(
                    highReport, "DESIGN_REVISION_BUILD_FAILED");
                if (outcome == "COMPLETED")
                    outcome = "DESIGN_REVISION_BUILD_FAILED";
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

    private static void SaveMilestoneExecutionGraph(
        string workingDirectory,
        string jobId,
        MilestoneDefinition milestone,
        string state,
        IReadOnlyDictionary<string, string>? workReports = null,
        IReadOnlyDictionary<string, string>? resourceReports = null,
        string? highReport = null,
        string hqFinalState = "PLANNED",
        bool assetManagerFailed = false)
    {
        workReports ??= new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        resourceReports ??= new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        var nodes = new List<MilestoneGraphNodeSnapshot>
        {
            new("HQ-DESIGN", "HQ", "COMPLETED")
        };
        if (milestone.Design is not null)
            nodes.Add(new("DESIGN-0", "DESIGN",
                state is "FINALIZED" or "FINALIZE_FAILED" ? "COMPLETED" : "RUNNING", "0"));
        if (milestone.AssetManager is not null)
            nodes.Add(new("ASSET-MANAGER-1", "ASSET_MANAGER",
                state == "ASSET_MANAGER_BLOCKED" || assetManagerFailed ? "BLOCKED" :
                state is "FINALIZED" or "FINALIZE_FAILED" ? "COMPLETED" : "RUNNING", "1"));

        foreach (var work in milestone.WorkItems.Values
                     .OrderBy(item => int.TryParse(item.Id, out var number) ? number : int.MaxValue))
        {
            nodes.Add(new(
                "WORK-" + work.Id,
                "WORK",
                workReports.TryGetValue(work.Id, out var workReport)
                    ? MilestoneDefinitionContract.ReadWorkStatus(workReport) switch
                    {
                        "completed" => "COMPLETED",
                        "in_progress" => "IN_PROGRESS",
                        _ => "BLOCKED"
                    }
                    : "PLANNED",
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
                ? "PLANNED"
                : MilestoneDefinitionContract.ReadHighStatus(highReport) switch
                {
                    "completed" => "COMPLETED",
                    "design_handoff" => "IN_PROGRESS",
                    _ => "BLOCKED"
                }));
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

        if (milestone.AssetManager is not null)
            edges.Add(new("HQ-DESIGN", "ASSET-MANAGER-1", "ASSET_MANAGER_DISPATCH"));

        if (milestone.Design is not null)
            edges.Add(new("HQ-DESIGN", "DESIGN-0", "DESIGN_REQUEST"));
        foreach (var workNode in workNodes)
        {
            if (milestone.Design is not null)
                edges.Add(new("DESIGN-0", workNode, "DESIGN_GUIDANCE"));
            edges.Add(new(milestone.AssetManager is null ? "HQ-DESIGN" :
                "ASSET-MANAGER-1", workNode,
                milestone.AssetManager is null ? "DISPATCH" : "ASSET_MANAGER_TO_WORK"));
            edges.Add(new(
                workNode,
                "HIGH",
                "RESULT_TO_HIGH"));
        }

        if (milestone.AssetManager is not null && workNodes.Length == 0)
            edges.Add(new("ASSET-MANAGER-1", "HIGH", "ASSET_MANAGER_TO_HIGH"));

        foreach (var resource in milestone.Resources.Values)
        {
            edges.Add(new(
                "HQ-DESIGN",
                "RESOURCE-" + resource.Id,
                "BACKGROUND_RESOURCE_REQUEST"));
        }

        if (milestone.Design is not null && workNodes.Length == 0)
            edges.Add(new("DESIGN-0", "HIGH", "DESIGN_TO_HIGH"));
        edges.Add(new("HIGH", "GIT-FINALIZE", "HIGH_TERMINAL_TO_GIT"));

        edges.Add(new("GIT-FINALIZE", "HQ-FINAL", "REPORT_TO_HQ"));

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
            TaskTitle.Text = $"Unity WORK #{work.Id}";
            ResultTitle.Text = "WORK";
            SetFlowState(
                codexActive: false,
                workerActive: true,
                webActive: false,
                explicitStage: TaskStage.Implementer);
        });

        // Ownership is journaled before dispatch. CONTINUE retains the
        // actual session and prior report rather than re-investigating work
        // already completed in an earlier milestone.
        if (!WorkExecutionJournal.TryBegin(
                workingDirectory, jobId, milestone.Id, work, implementer,
                out var checkpoint, out var startError))
        {
            return new(work.Id, RoleTextProtocol.BuildResult(
                "blocked", startError));
        }

        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            work.Id,
            MilestoneDefinitionContract.BuildWorkContext(work),
            work.WritePaths,
            workingDirectory,
            readOnly: work.ReadOnly);
        if (work.ExecutionMode == "CONTINUE" &&
            !string.IsNullOrWhiteSpace(checkpoint!.LastReport))
        {
            prompt += Environment.NewLine + Environment.NewLine +
                "이전 WORK 체크포인트:" + Environment.NewLine +
                "기존 세션의 실제 작업 결과다. 완료한 구현·명령·파일 조사를 재실행하지 말고 " +
                "보고된 잔여 항목만 이어서 처리하라." + Environment.NewLine +
                MilestoneDefinitionContract.Limit(checkpoint.LastReport, 4000);
        }
        var sceneWritePath = work.ReadOnly ? null : work.WritePaths
            .FirstOrDefault(path => UnitySceneCliWorkflow.TryNormalizeScenePath(
                path, out _));
        // Source/Editor lane restrictions and Unity tool use are already in
        // the WORK contract. Keep the existing scene-open operation without
        // appending another generic instruction block to every WORK prompt.
        if (UnityWorkSchedulingPolicy.RequiresEditorLane(work) &&
            sceneWritePath is not null)
        {
            // The scheduler holds the project-wide Editor lease here.
            var sceneFile = Path.Combine(workingDirectory,
                sceneWritePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(sceneFile))
            {
                var opened = await UnitySceneCliWorkflow.OpenAsync(
                    workingDirectory, sceneWritePath, cancellationToken);
                prompt += Environment.NewLine +
                    "Unity 씬 열기 증거: " + opened.Detail +
                    (opened.Success ? "" :
                        " · 자동 재호출하지 말고 Editor 상태를 조회하여 필요한 경우 " +
                        "등록된 open_scene 명령으로 대체하라.");
            }
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
                        "직전 실행 결과를 그대로 유지하고 형식만 " +
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

                if (state == "COMPLETED" && sceneWritePath is not null)
                {
                    // Save only once after the role has finished, while this
                    // Editor WORK still owns the shared gate. A timeout may
                    // already have saved the scene: never replay it blindly.
                    var saved = await UnitySceneCliWorkflow.SaveAsync(
                        workingDirectory, sceneWritePath, cancellationToken);
                    if (!saved.Success)
                    {
                        state = "IN_PROGRESS";
                        result = result with
                        {
                            FinalMessage = RoleTextProtocol.BuildResult(
                                "in_progress", parsed.Summary,
                                parsed.ChangedPaths,
                                parsed.Issues.Concat(new[] {
                                    "UNITY_SCENE_SAVE_UNVERIFIED: " + saved.Detail +
                                    " · HQ 확인 전 자동 재시도 금지"
                                }).ToArray())
                        };
                    }
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
                _userCanceledTask ? "CANCELED" : "IN_PROGRESS", sessionId);
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

    private async Task<(string Report, string? SessionId)> ExecuteMilestoneHighAsync(
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
        string? sessionId = null,
        string? previousHighReport = null)
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
            HighDesignContinuationPolicy.BuildReviewContext(
                MilestoneDefinitionContract.BuildHighContext(
                    milestone, workReports, mechanicalReports),
                previousHighReport));

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
                sessionId,
                null,
                cancellationToken,
                sandbox,
                historyWorkItemId: "HIGH",
                historyReferenceId: historyInvocationReference);

        // Preserve HIGH findings without a second model invocation merely
        // to repair formatting.
        using var highEditorLease = await UnityEditorOperationGate.EnterAsync(
            workingDirectory, cancellationToken);
        var result = await RunAsync(prompt);

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

        return (report, result.SessionId ?? sessionId);
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
        var ownerId = MilestoneBuildRepairOwnership.Resolve(
            milestone.WorkItems, failure.Summary);
        if (ownerId is null)
            return "blocked: BUILD_REPAIR_WORK_OWNER_AMBIGUOUS";
        var ownerPaths = milestone.WorkItems[ownerId].WritePaths;
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            ownerId, "복구 목표:" + Environment.NewLine +
            "원래 WORK #" + ownerId +
            "의 실제 컴파일 오류만 이번 복구 호출에서 한 번 최소 수정하라." +
            Environment.NewLine + "복구 지시:" + Environment.NewLine +
            "원래 작업 범위 외 변경 금지. Git 명령 실행 금지. " +
            "환경 EPERM 등 실행 환경 오류를 소스 수정으로 우회하지 마라. " +
            "오류: " + MilestoneDefinitionContract.Limit(failure.Summary, 1600),
            ownerPaths, workingDirectory);
        var stateless = implementer with
        {
            ThreadSessionId = null,
            ThreadProjectPath = null
        };
        var result = await RunCoordinatorRoleAsync(
            jobId, "WORK", prompt, stateless, workingDirectory,
            null, null, cancellationToken, CodexSandboxMode.WorkspaceWrite,
            historyWorkItemId: ownerId,
            historyReferenceId: milestone.Id + ":BUILD:REPAIR-1");
        var report = MilestoneDefinitionContract.NormalizeWorkReport(
            result.ExitCode, result.FinalMessage, result.StandardError);
        AddRoleResponseHistory(WorkerRoleState.Work,
            "빌드 복구 #" + ownerId, report, result.Usage, result.Files,
            status: MilestoneDefinitionContract.ReadWorkStatus(report) == "blocked"
                ? "BLOCKED" : "RECEIVED",
            providerWireId: implementer.Provider, fullMessage: report,
            workItemId: ownerId,
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
            "오류 정보:" + Environment.NewLine +
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
        string? remoteMainSha,
        bool webTransport) =>
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
        HqTransportPolicy.BuildRepositoryGuidance(
            webTransport, remoteMainSha is not null, inboundType) +
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

    private static string BuildMilestoneCliNewSessionInput(
        string workingDirectory, string request)
    {
        try
        {
            // Read-only, workspace-scoped handoff. This file is deliberately
            // NOT injected into WEB and does not alter the browser thread.
            var path = ProjectWorkspacePersistence.HandoffPath(workingDirectory);
            if (!File.Exists(path))
                return request;
            var handoff = File.ReadAllText(path, Encoding.UTF8);
            return HqTransportHandoffPolicy.ComposeNewCliSession(request, handoff);
        }
        catch (IOException)
        {
            return request; // Failure to read a helpful note must not block HQ.
        }
        catch (UnauthorizedAccessException)
        {
            return request;
        }
    }

    private static string BuildMilestoneFollowupInput(
        CoordinatorContinuationState continuation,
        string followup,
        bool webTransport = false)
    {
        // WEB is bound to a conversation, not a newly created CLI role session.
        // The user followup is sufficient; current repo and milestone evidence
        // continue to be provided separately in the ordinary HQ request.
        if (webTransport)
            return HqTransportHandoffPolicy.ComposeWebFollowup(followup);
        var checkpoints = WorkExecutionJournal.ReadAll(
            continuation.WorkingDirectory, continuation.JobId);
        var journal = checkpoints.Count == 0
            ? "없음"
            : string.Join(Environment.NewLine, checkpoints.Select(item =>
                "#" + item.WorkItemId + " state=" + item.State +
                " paths=" + string.Join(",", item.WritePaths) +
                " origin=" + item.OriginMilestoneId +
                " attempts=" + item.Attempts +
                " session=" + (item.SessionId is null ? "missing" : "saved")));
        return "이전 HQ 상태:" + Environment.NewLine +
            continuation.LastHqMessage + Environment.NewLine +
            Environment.NewLine + "WORKITEM_CHECKPOINTS:" + Environment.NewLine +
            journal + Environment.NewLine +
            "RUNNING/IN_PROGRESS WORKITEM은 기존 ID, 동일 PATH, MODE=CONTINUE로 잇고 " +
            "COMPLETED/BLOCKED/CANCELED ID를 재배정하지 마라. 새 업무는 미사용 ID로 생성한다." +
            Environment.NewLine + Environment.NewLine +
            "재개 입력:" + Environment.NewLine + followup;
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
