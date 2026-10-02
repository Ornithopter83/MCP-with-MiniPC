namespace ProjectHub.Worker;

public partial class MainWindow
{
    private async Task RunParallelCoordinatorFirstJobAsync(
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
        var maxConcurrentWork = _targetSettings.EffectiveMaxConcurrentWork;

        _activeWorkingDirectory = workingDirectory;
        _activeProjectJobId = jobId;
        using var cts = new CancellationTokenSource();
        _activeTaskCts = cts;
        _activeCoordinatorFirst = true;
        SetFollowupComposerVisible(false);
        RunButton.Content = "■   취소";
        _userCanceledTask = false;
        _jobTimedOut = false;
        _lastActivityAt = DateTimeOffset.UtcNow;

        if (!continuing)
        {
            _historyEvents.Clear();
            StartTaskTranscript(selectedThread, request, string.Empty);
            AddTaskMessage(
                "TASK REQUEST",
                request,
                sizeBytes: System.Text.Encoding.UTF8.GetByteCount(request),
                itemCount: 1,
                fileCount: attachments?.Count);
        }
        else
        {
            StartCommandTranscript();
            AddTaskMessage(
                "USER FOLLOWUP",
                request,
                sizeBytes: System.Text.Encoding.UTF8.GetByteCount(request),
                itemCount: 1,
                fileCount: attachments?.Count,
                includeHistory: false);
        }

        var coordinatorSession = continuation?.CoordinatorSessionId;
        var lastHqMessage = continuation?.LastHqMessage ?? string.Empty;
        var mechanicalWork = new MechanicalWorkRegistry();
        var resourceQueue = new ResourceSidecarQueue(
            _bridgeServer,
            workingDirectory,
            cts.Token,
            mechanicalWork);
        var observationQueue = new ObservationSidecarQueue(
            workingDirectory,
            jobId,
            mechanicalWork,
            cts.Token);

        resourceQueue.StateChanged += OnResourceSidecarStateChanged;
        resourceQueue.CompletionAvailable += OnResourceSidecarCompletion;
        resourceQueue.TransportEvent += OnResourceSidecarTransportEvent;
        observationQueue.TransportEvent += OnObservationSidecarEvent;

        ParallelWorkSupervisor? supervisor = null;
        ParallelResourceWorkItemRouter? resourceRouter = null;
        CodexWorkItemExecutor? executor = null;
        WorkGraph? graph = null;
        var compactRuntimeOnPause = false;
        var forceRuntimeResetAfterDispose = false;

        try
        {
            if (!continuing)
            {
                var staleRuntimeCleanup = await new GitWorktreeManager()
                    .ResetRepositoryRuntimeAsync(
                        workingDirectory,
                        cts.Token);

                var staleCleanupMessage = staleRuntimeCleanup.Success
                    ? "새 작업 시작 전에 이전 ProjectHub runtime을 초기화했습니다."
                    : "새 작업 시작 전에 이전 ProjectHub runtime을 완전히 초기화하지 못했습니다.";
                if (!string.IsNullOrWhiteSpace(staleRuntimeCleanup.ErrorDetail))
                    staleCleanupMessage += Environment.NewLine + staleRuntimeCleanup.ErrorDetail;

                if (staleRuntimeCleanup.RuntimeDeleted ||
                    staleRuntimeCleanup.RemovedWorktrees.Count > 0 ||
                    !staleRuntimeCleanup.Success)
                {
                    AddTaskMessage(
                        "STALE RUNTIME RESET",
                        staleCleanupMessage +
                        Environment.NewLine +
                        $"runtimeRoot={staleRuntimeCleanup.RuntimeRoot}" +
                        Environment.NewLine +
                        $"removedWorktrees={staleRuntimeCleanup.RemovedWorktrees.Count}",
                        status: staleRuntimeCleanup.Success
                            ? "COMPLETED"
                            : staleRuntimeCleanup.ErrorCode ?? "RUNTIME_RESET_FAILED",
                        includeHistory: false);
                }

                if (!staleRuntimeCleanup.Success)
                {
                    // 이전 runtime 정리는 새 작업의 의미 실행을 막는 선행조건이 아니다.
                    // 잠긴 cache/worktree가 남아 있어도 새 jobId의 작업은 계속 시작한다.
                    ResultTitle.Text = "RUNTIME WARNING";
                    ResultBody.Text = staleCleanupMessage;
                    TaskTitle.Text = "이전 runtime 일부 정리 실패 · HQ 시작 계속";
                    AddTaskMessage(
                        "RUNTIME WARNING",
                        staleCleanupMessage,
                        status: staleRuntimeCleanup.ErrorCode ?? "RUNTIME_RESET_FAILED",
                        includeHistory: false);
                }
            }


            var currentGitTarget = await PrepareParallelGitForLaunchAsync(
                workingDirectory,
                cts.Token);
            if (currentGitTarget is null)
                return;

            if (!continuing)
            {
                SetDashboardBodyMode(DashboardBodyMode.TaskHistory);
                ResetDashboardTaskInput();
            }

            AddTaskMessage(
                "GIT READY",
                "Git 기준점 준비를 완료했습니다. HQ를 시작합니다.",
                status: "COMPLETED",
                includeHistory: false);

            var restored = continuing
                ? ProjectWorkspacePersistence.TryLoadWorkGraph(workingDirectory, jobId)
                : null;
            graph = restored is null
                ? new WorkGraph(jobId, maxConcurrentWork)
                : WorkGraph.Restore(restored, markRunningAsRecoveryBlocked: true);

            if (graph.MaxConcurrentWork != maxConcurrentWork)
            {
                var concurrencyPatch = graph.ApplyPatch(new WorkGraphPatch(
                    graph.Revision,
                    new[] { WorkGraphPatchOperation.SetMaxConcurrency(maxConcurrentWork) }));
                if (!concurrencyPatch.Success)
                    throw new InvalidOperationException(
                        concurrencyPatch.ErrorCode ?? "WORK_GRAPH_CONCURRENCY_UPDATE_FAILED");
            }

            var stagedHqAttachments = StageUserAttachments(
                attachments,
                workingDirectory,
                jobId + "-hq-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var webUserAttachments = BuildUserWebAttachments(attachments);
            var deliverUserAttachmentsToHq = stagedHqAttachments.Count > 0;
            RunOnUi(() => ConsumePendingAttachments(attachments));

            if (continuing)
                graph.RecoverPreparationFailuresForContinuation();

            ProjectWorkspacePersistence.SaveWorkGraph(workingDirectory, graph.Snapshot());

            var baseRef =
                currentGitTarget.HeadSha ??
                graph.Items
                    .Where(item => item.Kind == WorkItemKind.Integration &&
                                   item.State == WorkItemState.Completed)
                    .OrderByDescending(item => item.FinishedAtUtc ?? DateTimeOffset.MinValue)
                    .Select(item => item.ResultRef)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
                graph.Items.Select(item => item.BaseRef)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
                "HEAD";

            var runner = _aiRoleRunners.Resolve(implementer)
                ?? throw new InvalidOperationException(
                    $"PROVIDER_RUNNER_UNAVAILABLE: {implementer.Provider}");
            var observationGate = new WorkItemObservationGate(
                observationQueue,
                mechanicalWork);
            var targetWorkspaceFinalizer = new TargetWorkspaceFinalizer(
                workingDirectory,
                currentGitTarget.Branch);

            executor = new CodexWorkItemExecutor(
                jobId,
                workingDirectory,
                implementer,
                runner,
                judgeAvailable: false,
                observationGate: observationGate,
                expectedPrimaryBranch: currentGitTarget.Branch,
                userAttachments: attachments);

            async Task<string> RunParallelHqAsync(
                string prompt,
                CancellationToken cancellationToken)
            {
                RunOnUi(() =>
                {
                    TaskDirection.Text = "설계·관제 AI";
                    TaskTitle.Text = "WorkGraph 관제";
                    ResultTitle.Text = "HQ";
                    SetFlowState(
                        codexActive: true,
                        workerActive: false,
                        webActive: false,
                        explicitStage: TaskStage.Coordinator);
                });

                var turnInputAttachments = deliverUserAttachmentsToHq
                    ? stagedHqAttachments
                    : Array.Empty<AiInputAttachment>();
                var turnWebAttachments = deliverUserAttachmentsToHq
                    ? webUserAttachments
                    : new List<BridgeAttachment>();
                deliverUserAttachmentsToHq = false;

                AiRoleRunResult result;
                if (Dispatcher.CheckAccess())
                {
                    result = await RunHqRoleAsync(
                        jobId,
                        "HQ_WORK_GRAPH",
                        prompt,
                        coordinator,
                        workingDirectory,
                        coordinatorSession,
                        cancellationToken,
                        started =>
                        {
                            var normalized = CodexCliRunner.NormalizeSessionId(started);
                            if (!string.IsNullOrWhiteSpace(normalized))
                                coordinatorSession = normalized;
                        },
                        turnInputAttachments,
                        turnWebAttachments);
                }
                else
                {
                    var operation = Dispatcher.InvokeAsync(() =>
                        RunHqRoleAsync(
                            jobId,
                            "HQ_WORK_GRAPH",
                            prompt,
                            coordinator,
                            workingDirectory,
                            coordinatorSession,
                            cancellationToken,
                            started =>
                            {
                                var normalized = CodexCliRunner.NormalizeSessionId(started);
                                if (!string.IsNullOrWhiteSpace(normalized))
                                    coordinatorSession = normalized;
                            },
                            turnInputAttachments,
                            turnWebAttachments));
                    result = await (await operation.Task);
                }

                coordinatorSession =
                    CodexCliRunner.NormalizeSessionId(result.SessionId) ??
                    coordinatorSession;

                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(result.StandardError)
                            ? "HQ_WORK_GRAPH_PROCESS_EXIT"
                            : result.StandardError);
                }

                lastHqMessage = result.FinalMessage;
                RunOnUi(() =>
                {
                    AddRoleResponseHistory(
                        WorkerRoleState.Hq,
                        "WorkGraph 관제",
                        result.FinalMessage,
                        usage: result.Usage,
                        files: result.Files,
                        status: "WORK_GRAPH",
                        providerWireId: coordinator.Provider,
                        fullMessage: result.FinalMessage);
                });
                return result.FinalMessage;
            }

            async Task<StructuredPayloadResult<WorkGraphPatch>> ProcessWorkGraphPayloadAsync(
                string payload,
                CancellationToken cancellationToken)
            {
                var structuredResult = await _structuredPayloadHelper.ProcessAsync<WorkGraphPatch>(
                    new StructuredPayloadRequest(
                        ContractType: "WORK_GRAPH_PATCH",
                        RawPayload: payload,
                        RepairRole: implementer,
                        WorkingDirectory: workingDirectory,
                        RepairInstruction:
                            """
                            JSON 객체 하나만 반환한다.
                            최상위에는 expectedRevision과 operations를 유지한다.
                            operations 각 객체의 operation 종류 판별자 필드는 정확히 type이다.
                            type이 없고 operation 값이 ADD, CANCEL, SET_DEPENDENCIES, SET_GOAL, SET_BASE_REF, RELEASE 중 하나이면 그 동일 값을 type으로 옮기고 operation 키만 제거할 수 있다.
                            WorkItem ID, operation 종류 값, dependency, goal, baseRef 등 원문의 의미 값은 추가·삭제·변경하지 않는다.
                            따옴표, 쉼표, 괄호, JSON 타입, 코드펜스와 같은 구조 문제만 복구한다.
                            """,
                        AllowAiRepair: false),
                    WorkGraphTransportContract.TryParse,
                    WorkGraphTransportContract.TryParseJsonPayload,
                    cancellationToken,
                    deterministicRepair: WorkGraphTransportContract.TryRepairOperationTypeAliases);

                var structuredErrorCode =
                    structuredResult.FinalErrorCode ??
                    structuredResult.InitialErrorCode;
                if (!structuredResult.Success)
                {
                    var errorDetail =
                        WorkGraphTransportContract.DescribeError(
                            structuredResult.FinalPayload,
                            structuredErrorCode) ??
                        WorkGraphTransportContract.DescribeError(
                            payload,
                            structuredErrorCode);
                    if (!string.IsNullOrWhiteSpace(errorDetail))
                        structuredResult = structuredResult with { ErrorDetail = errorDetail };
                }

                if (structuredResult.RepairAttempted)
                {
                    RunOnUi(() =>
                    {
                        var detail =
                            $"contract=WORK_GRAPH_PATCH · repaired={structuredResult.Repaired} · initialError={structuredResult.InitialErrorCode ?? "none"} · finalError={structuredResult.FinalErrorCode ?? "none"}";
                        if (!string.IsNullOrWhiteSpace(structuredResult.RepairSummary))
                            detail += Environment.NewLine + "repair=" + structuredResult.RepairSummary;
                        if (!string.IsNullOrWhiteSpace(structuredResult.ErrorDetail))
                            detail += Environment.NewLine + structuredResult.ErrorDetail;

                        AddTaskMessage(
                            "STRUCTURED HELPER",
                            detail,
                            status: structuredResult.Success
                                ? "STRUCTURED_REPAIRED"
                                : "STRUCTURED_REPAIR_FAILED",
                            includeHistory: false);
                    });
                }

                return structuredResult;
            }

            supervisor = new ParallelWorkSupervisor(
                graph,
                executor,
                baseRef,
                RunParallelHqAsync,
                cts.Token,
                includeContractOnFirstHqTurn: string.IsNullOrWhiteSpace(coordinatorSession),
                processWorkGraphPayloadAsync: ProcessWorkGraphPayloadAsync,
                enableCompletionReview: true,
                finalizeEndAsync: async (snapshot, cancellationToken) =>
                {
                    var finalization = await targetWorkspaceFinalizer
                        .FinalizeAsync(snapshot, cancellationToken)
                        .ConfigureAwait(false);

                    ProjectWorkspacePersistence.AppendEvent(
                        workingDirectory,
                        jobId,
                        DateTimeOffset.UtcNow,
                        "TARGET WORKSPACE",
                        finalization.Message +
                        (string.IsNullOrWhiteSpace(finalization.ErrorCode)
                            ? string.Empty
                            : Environment.NewLine + "errorCode=" + finalization.ErrorCode) +
                        (string.IsNullOrWhiteSpace(finalization.LandedWorkItemId)
                            ? string.Empty
                            : Environment.NewLine + "workItemId=" + finalization.LandedWorkItemId) +
                        (string.IsNullOrWhiteSpace(finalization.LandedResultRef)
                            ? string.Empty
                            : Environment.NewLine + "resultRef=" + finalization.LandedResultRef),
                        finalization.Success ? "COMPLETED" : finalization.ErrorCode ?? "WORKSPACE_FINALIZATION_REQUIRED");

                    RunOnUi(() =>
                        AddTaskMessage(
                            "TARGET WORKSPACE",
                            finalization.Message +
                            (string.IsNullOrWhiteSpace(finalization.LandedWorkItemId)
                                ? string.Empty
                                : Environment.NewLine + $"workItemId={finalization.LandedWorkItemId}") +
                            (string.IsNullOrWhiteSpace(finalization.LandedResultRef)
                                ? string.Empty
                                : Environment.NewLine + $"resultRef={finalization.LandedResultRef}"),
                            status: finalization.Success
                                ? "COMPLETED"
                                : finalization.ErrorCode ?? "WORKSPACE_FINALIZATION_REQUIRED",
                            includeHistory: false));

                    return new ParallelEndFinalizationResult(
                        finalization.Success,
                        finalization.ErrorCode,
                        finalization.Message);
                });

            resourceRouter = new ParallelResourceWorkItemRouter(
                supervisor,
                resourceQueue,
                cts.Token);

            executor.CallCompleted += call =>
            {
                var usage = call.Result.Usage;
                UsageTelemetryStore.Append(new ModelCallTelemetry(
                    jobId,
                    null,
                    "WORK",
                    call.Result.Model,
                    call.Result.Reasoning,
                    $"WORK_ITEM:{call.WorkItemId}:{call.InboundType}",
                    usage.UsageKnown ? usage.InputTokens : null,
                    usage.UsageKnown ? usage.CachedInputTokens : null,
                    usage.UsageKnown ? usage.OutputTokens : null,
                    usage.UsageKnown ? usage.ReasoningOutputTokens : null,
                    usage.ProviderTotalTokens,
                    call.PromptBytes,
                    call.PromptBytes,
                    0,
                    null,
                    System.Text.Encoding.UTF8.GetByteCount(call.Result.FinalMessage),
                    call.LatencyMs,
                    call.Result.ExitCode == 0 ? null : "WORK_PROCESS_EXIT",
                    usage.UsageKnown,
                    null,
                    null,
                    DateTimeOffset.UtcNow));

                ProjectWorkspacePersistence.AppendEvent(
                    workingDirectory,
                    jobId,
                    DateTimeOffset.UtcNow,
                    "WORK CALL",
                    $"workItemId={call.WorkItemId} · inboundType={call.InboundType} · exitCode={call.Result.ExitCode}",
                    call.Result.ExitCode == 0 ? "COMPLETED" : "FAILED",
                    workItemId: call.WorkItemId);

                if (!string.IsNullOrWhiteSpace(call.Result.FinalMessage))
                {
                    RunOnUi(() =>
                        AddRoleResponseHistory(
                            WorkerRoleState.Work,
                            "작업 응답",
                            call.Result.FinalMessage,
                            usage: call.Result.Usage,
                            files: call.Result.Files,
                            status: call.Result.ExitCode == 0 ? "RECEIVED" : "FAILED",
                            providerWireId: implementer.Provider,
                            fullMessage: call.Result.FinalMessage,
                            workNumber: call.WorkNumber,
                            referenceId: call.WorkItemId,
                            workItemId: call.WorkItemId));
                }
            };

            executor.Progress += progress => RunOnUi(() =>
            {
                _lastActivityAt = DateTimeOffset.UtcNow;
                AddRoleProgressHistory(
                    WorkerRoleState.Work,
                    progress.Message,
                    implementer.Provider,
                    workNumber: progress.WorkNumber,
                    referenceId: progress.WorkItemId,
                    workItemId: progress.WorkItemId);
            });

            executor.MechanicalProgress += progress =>
            {
                ProjectWorkspacePersistence.AppendEvent(
                    workingDirectory,
                    jobId,
                    DateTimeOffset.UtcNow,
                    "HQ MECHANICAL",
                    $"workItemId={progress.WorkItemId} · stage={progress.Stage} · {progress.Message}",
                    progress.Active ? "RUNNING" : "COMPLETED",
                    workItemId: progress.WorkItemId);

                RunOnUi(() =>
                {
                    _lastActivityAt = DateTimeOffset.UtcNow;
                    TaskDirection.Text = "설계·관제 AI";
                    TaskTitle.Text = progress.Active
                        ? $"HQ Worker {progress.Stage} 실행 중"
                        : $"HQ Worker {progress.Stage} 완료";
                    AddRoleProgressHistory(
                        WorkerRoleState.Hq,
                        progress.Message,
                        coordinator.Provider,
                        workNumber: progress.WorkNumber,
                        referenceId: progress.WorkItemId,
                        workItemId: progress.WorkItemId);
                    SetFlowState(
                        codexActive: progress.Active,
                        workerActive: !progress.Active,
                        webActive: false,
                        explicitStage: progress.Active
                            ? TaskStage.Coordinator
                            : TaskStage.Implementer);
                });
            };

            executor.ContextPrepared += context =>
            {
                var activeSupervisor = supervisor;
                if (activeSupervisor is null)
                    return;
                _ = UpdateParallelRunningContextSafelyAsync(
                    activeSupervisor,
                    context.WorkItemId,
                    context.Branch,
                    context.WorktreePath,
                    null,
                    context.BaseRef,
                    cts.Token);
            };

            executor.SessionStarted += started =>
            {
                var activeSupervisor = supervisor;
                if (activeSupervisor is null)
                    return;
                _ = UpdateParallelRunningContextSafelyAsync(
                    activeSupervisor,
                    started.WorkItemId,
                    null,
                    null,
                    started.SessionId,
                    null,
                    cts.Token);
            };

            supervisor.StateChanged += snapshot =>
            {
                ProjectWorkspacePersistence.SaveWorkGraph(
                    workingDirectory,
                    snapshot.Graph);

                ProjectWorkspacePersistence.AppendEvent(
                    workingDirectory,
                    jobId,
                    DateTimeOffset.UtcNow,
                    "WORK GRAPH",
                    ParallelWorkSupervisor.FormatMechanicalGraphEvent(
                        Array.Empty<string>(),
                        snapshot),
                    "WORK_GRAPH",
                    graphRevision: snapshot.Graph.Revision);

                RunOnUi(() =>
                {
                    _lastActivityAt = DateTimeOffset.UtcNow;
                    ImplementerWorkGaugeText.Text =
                        FormatActiveWorkItemGauge(snapshot.RunningCount);
                    PipelineImplementerCard.ToolTip = null;
                    TaskDirection.Text = "작업 AI";
                    TaskTitle.Text = snapshot.RunningCount > 0
                        ? $"작업 진행 · {snapshot.RunningCount}건 실행 중"
                        : snapshot.ReadyCount > 0
                            ? $"작업 대기 · {snapshot.ReadyCount}건"
                            : "작업 상태 갱신";
                    if (snapshot.RunningCount > 0)
                    {
                        SetFlowState(
                            codexActive: false,
                            workerActive: true,
                            webActive: false,
                            explicitStage: TaskStage.Implementer);
                    }
                });
            };

            resourceRouter.RoutingEvent += routing => RunOnUi(() =>
            {
                _lastActivityAt = DateTimeOffset.UtcNow;
                AddTaskMessage(
                    "RESOURCE",
                    $"workItemId={routing.WorkItemId} · stage={routing.Stage}" +
                    (string.IsNullOrWhiteSpace(routing.RequestId)
                        ? string.Empty
                        : $" · requestId={routing.RequestId}") +
                    Environment.NewLine +
                    routing.Message,
                    status: routing.ErrorCode ?? routing.Stage,
                    includeHistory: false);
            });

            var inboundType = continuing ? "USER_FOLLOWUP" : "USER_REQUEST";
            var inboundBody = continuing
                ? TaskContinuationContract.BuildHqFollowupInput(
                    continuation!.Status,
                    continuation.LastHqMessage,
                    request)
                : request;

            var result = await supervisor.RunAsync(
                inboundType,
                inboundBody,
                cts.Token);

            lastHqMessage = result.HqBody;
            ProjectWorkspacePersistence.SaveWorkGraph(
                workingDirectory,
                result.Graph);

            if (result.Exit == ParallelWorkSupervisorExit.Failed)
            {
                var errorBody =
                    $"WorkGraph 관제가 기계적 오류로 종료되었습니다.{Environment.NewLine}" +
                    $"errorCode={result.ErrorCode ?? "WORK_GRAPH_FAILED"}{Environment.NewLine}" +
                    result.HqBody;
                AddTaskMessage(
                    "TASK ERROR",
                    errorBody,
                    status: result.ErrorCode ?? "WORK_GRAPH_FAILED");
                ResultTitle.Text = "DONE · 오류 기록 있음";
                ResultBody.Text = errorBody;
                TaskTitle.Text = "WorkGraph 관제 오류";
                SaveParallelContinuation(
                    "DONE_WITH_ERROR",
                    result.HqBody,
                    jobId,
                    workingDirectory,
                    coordinator,
                    implementer,
                    coordinatorSession,
                    result.Graph);
                SetFlowState(false, false, false);
                return;
            }

            if (result.Exit == ParallelWorkSupervisorExit.Paused)
            {
                ResultTitle.Text = "PAUSED";
                ResultBody.Text = result.HqBody;
                TaskTitle.Text = "HQ가 사용자 입력을 기다립니다.";
                AddTaskMessage(
                    "TASK PAUSED",
                    result.HqBody,
                    status: "PAUSED",
                    includeHistory: false);
                SaveParallelContinuation(
                    "PAUSED",
                    result.HqBody,
                    jobId,
                    workingDirectory,
                    coordinator,
                    implementer,
                    coordinatorSession,
                    result.Graph);
                compactRuntimeOnPause = true;
                SetFlowState(false, false, false);
                return;
            }

            await observationQueue.ScanNowAsync(cts.Token);
            var pendingMechanical = mechanicalWork.OutstandingCount;
            if (pendingMechanical > 0)
            {
                RunOnUi(() =>
                {
                    ResultTitle.Text = "WAITING";
                    ResultBody.Text = result.HqBody;
                    TaskTitle.Text =
                        $"HQ 기계 작업 완료 대기 · {pendingMechanical}건";
                    SetFlowState(
                        codexActive: true,
                        workerActive: false,
                        webActive: false,
                        explicitStage: TaskStage.Coordinator);
                });

                await WaitForParallelMechanicalWorkAsync(
                    mechanicalWork,
                    cts.Token);
            }

            var hadMechanicalErrors = false;
            while (resourceQueue.TryDequeueCompletion(out var resourceCompletion))
            {
                if (!resourceCompletion.Success)
                    hadMechanicalErrors = true;
            }

            foreach (var completion in mechanicalWork.DrainCompletions())
            {
                if (!completion.Success)
                    hadMechanicalErrors = true;
            }

            var hadGraphErrors = result.Graph.Items.Any(
                item => item.State == WorkItemState.Failed);
            var finalHasErrors = hadMechanicalErrors || hadGraphErrors;

            forceRuntimeResetAfterDispose = true;
            var runtimeCleanup = await new GitWorktreeManager()
                .ResetRepositoryRuntimeAsync(
                    workingDirectory,
                    cts.Token);
            var cleanupMessage = runtimeCleanup.Success
                ? "최종 DONE 확정 전에 ProjectHub runtime을 완전히 초기화했습니다."
                : "ProjectHub runtime 초기화에 실패하여 DONE_WITH_ERROR로 종료합니다.";
            if (!string.IsNullOrWhiteSpace(runtimeCleanup.ErrorDetail))
                cleanupMessage += Environment.NewLine + runtimeCleanup.ErrorDetail;

            ProjectWorkspacePersistence.AppendEvent(
                workingDirectory,
                jobId,
                DateTimeOffset.UtcNow,
                "RUNTIME RESET",
                cleanupMessage +
                Environment.NewLine +
                $"runtimeRoot={runtimeCleanup.RuntimeRoot}" +
                Environment.NewLine +
                $"removedWorktrees={runtimeCleanup.RemovedWorktrees.Count}",
                runtimeCleanup.Success
                    ? "COMPLETED"
                    : runtimeCleanup.ErrorCode ?? "RUNTIME_RESET_FAILED");

            AddTaskMessage(
                "RUNTIME RESET",
                cleanupMessage,
                status: runtimeCleanup.Success
                    ? "COMPLETED"
                    : runtimeCleanup.ErrorCode ?? "RUNTIME_RESET_FAILED",
                includeHistory: false);

            if (!runtimeCleanup.Success)
                finalHasErrors = true;

            var finalStatus = finalHasErrors
                ? "DONE_WITH_ERROR"
                : "DONE";
            var finalResultBody = FinalResultPathNormalizer.NormalizeLandedPaths(
                result.HqBody,
                workingDirectory,
                result.Graph);
            lastHqMessage = finalResultBody;

            ResultTitle.Text = finalHasErrors
                ? "DONE · 오류 기록 있음"
                : "DONE";
            ResultBody.Text = finalResultBody;
            TaskTitle.Text = "WORK와 기계적 대기 작업을 모두 확인했습니다.";
            AddTaskMessage(
                "TASK RESULT",
                finalResultBody,
                status: finalStatus,
                includeHistory: false);
            SaveParallelContinuation(
                finalStatus,
                finalResultBody,
                jobId,
                workingDirectory,
                coordinator,
                implementer,
                coordinatorSession,
                result.Graph);
            SetFlowState(false, false, false);
        }
        catch (OperationCanceledException)
        {
            if (graph is not null)
                ProjectWorkspacePersistence.SaveWorkGraph(
                    workingDirectory,
                    graph.Snapshot());

            if (_userCanceledTask)
            {
                var snapshot = graph?.Snapshot();
                SaveParallelContinuation(
                    "CANCELED",
                    lastHqMessage,
                    jobId,
                    workingDirectory,
                    coordinator,
                    implementer,
                    coordinatorSession,
                    snapshot);
                AddTaskMessage(
                    "TASK CANCELED",
                    "사용자가 실행 구간을 중단했습니다. WorkGraph와 확보된 세션 정보를 보존합니다.",
                    status: "CANCELED");
                ResultTitle.Text = "CANCELED";
                ResultBody.Text =
                    "현재 실행 구간을 중단했습니다. 작업 추가로 같은 WorkGraph에서 이어갈 수 있습니다.";
                TaskTitle.Text = "WorkGraph 작업이 중단되었습니다. 후속 작업 입력 대기";
                SetFollowupComposerVisible(true);
            }
            else
            {
                AddTaskMessage(
                    "TASK CANCELED",
                    "WorkGraph 관제 작업이 취소되었습니다.");
                ResultTitle.Text = "CANCELED";
                TaskTitle.Text = "WorkGraph 작업이 취소되었습니다.";
            }

            SetFlowState(false, false, false);
        }
        catch (Exception exception)
        {
            if (graph is not null)
                ProjectWorkspacePersistence.SaveWorkGraph(
                    workingDirectory,
                    graph.Snapshot());

            var detail = WorkerTranscriptJson.Serialize(new
            {
                error_type = exception.GetType().Name,
                detail = exception.Message
            });
            AddTaskMessage(
                "TASK ERROR",
                detail,
                status: "UNKNOWN");
            ResultTitle.Text = "ERROR";
            ResultBody.Text = detail;
            TaskTitle.Text = "WorkGraph 실행 오류";
            SetFlowState(false, false, false);
        }
        finally
        {
            if (resourceRouter is not null)
            {
                try { await resourceRouter.DisposeAsync(); }
                catch (OperationCanceledException) { }
            }

            if (supervisor is not null)
            {
                try { await supervisor.DisposeAsync(); }
                catch (OperationCanceledException) { }
            }

            try { await observationQueue.DisposeAsync(); }
            catch (OperationCanceledException) { }
            try { await resourceQueue.DisposeAsync(); }
            catch (OperationCanceledException) { }

            if (forceRuntimeResetAfterDispose)
            {
                var finalReset = await new GitWorktreeManager()
                    .ResetRepositoryRuntimeAsync(
                        workingDirectory,
                        CancellationToken.None);
                if (!finalReset.Success)
                {
                    AddTaskMessage(
                        "RUNTIME RESET RETRY",
                        "실행기 dispose 뒤 runtime 초기화를 다시 시도했지만 완료하지 못했습니다." +
                        Environment.NewLine +
                        (finalReset.ErrorDetail ??
                         finalReset.ErrorCode ??
                         "RUNTIME_RESET_FAILED"),
                        status: finalReset.ErrorCode ?? "RUNTIME_RESET_FAILED",
                        includeHistory: false);
                }
            }

            if (compactRuntimeOnPause)
            {
                var pauseCleanup = await new GitWorktreeManager()
                    .CompactRepositoryRuntimeAsync(
                        workingDirectory,
                        CancellationToken.None);
                var pauseCleanupMessage = pauseCleanup.Success
                    ? "PAUSE 전환 뒤 재개에 불필요한 clean worktree와 도구 cache를 정리했습니다."
                    : "PAUSE runtime 압축 중 일부 항목을 정리하지 못했습니다.";
                if (!string.IsNullOrWhiteSpace(pauseCleanup.ErrorDetail))
                    pauseCleanupMessage += Environment.NewLine + pauseCleanup.ErrorDetail;

                ProjectWorkspacePersistence.AppendEvent(
                    workingDirectory,
                    jobId,
                    DateTimeOffset.UtcNow,
                    "PAUSE RUNTIME COMPACT",
                    pauseCleanupMessage +
                    Environment.NewLine +
                    $"runtimeRoot={pauseCleanup.RuntimeRoot}" +
                    Environment.NewLine +
                    $"removedWorktrees={pauseCleanup.RemovedWorktrees.Count}",
                    pauseCleanup.Success
                        ? "COMPLETED"
                        : pauseCleanup.ErrorCode ?? "RUNTIME_COMPACT_FAILED");

                AddTaskMessage(
                    "PAUSE RUNTIME COMPACT",
                    pauseCleanupMessage,
                    status: pauseCleanup.Success
                        ? "COMPLETED"
                        : pauseCleanup.ErrorCode ?? "RUNTIME_COMPACT_FAILED",
                    includeHistory: false);
            }

            observationQueue.TransportEvent -= OnObservationSidecarEvent;
            resourceQueue.StateChanged -= OnResourceSidecarStateChanged;
            resourceQueue.CompletionAvailable -= OnResourceSidecarCompletion;
            resourceQueue.TransportEvent -= OnResourceSidecarTransportEvent;
            _resourceSidecarActive = false;
            _resourceSidecarQueued = 0;
            _resourceSidecarStatus = "ChatGPT Web";
            RunOnUi(() =>
            {
                ImplementerWorkGaugeText.Text = FormatActiveWorkItemGauge(0);
                PipelineImplementerCard.ToolTip = null;
                UpdateDashboardSummary();
            });
            _activeCoordinatorFirst = false;
            _activeTaskCts = null;
            _userCanceledTask = false;
            ExportTaskTranscript();
            SetFlowState(false, false, false);
            ApplyConnectionStatus();
        }
    }

    private async Task UpdateParallelRunningContextSafelyAsync(
        ParallelWorkSupervisor supervisor,
        string workItemId,
        string? branch,
        string? worktreePath,
        string? sessionId,
        string? baseRef,
        CancellationToken cancellationToken)
    {
        try
        {
            await supervisor.UpdateRunningContextAsync(
                workItemId,
                branch,
                worktreePath,
                sessionId,
                baseRef,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SaveParallelContinuation(
        string status,
        string lastHqMessage,
        string jobId,
        string workingDirectory,
        WorkerAiRoleSettings coordinator,
        WorkerAiRoleSettings implementer,
        string? coordinatorSession,
        WorkGraphSnapshot? graph)
    {
        if (graph is not null)
            ProjectWorkspacePersistence.SaveWorkGraph(
                workingDirectory,
                graph);

        _continuationState = new CoordinatorContinuationState(
            jobId,
            workingDirectory,
            coordinator,
            implementer,
            coordinatorSession,
            null,
            status,
            lastHqMessage ?? string.Empty);

        if (TaskContinuationContract.IsResumableStatus(status))
            ProjectWorkspacePersistence.SaveContinuation(_continuationState);
        else
            ProjectWorkspacePersistence.ClearContinuation(workingDirectory);

        SetFollowupComposerVisible(true);
    }

    private async Task WaitForParallelMechanicalWorkAsync(
        MechanicalWorkRegistry registry,
        CancellationToken cancellationToken)
    {
        var waitTask = registry.WaitForAllAsync(cancellationToken);
        while (!waitTask.IsCompleted)
        {
            var heartbeat = Task.Delay(
                TimeSpan.FromMinutes(1),
                cancellationToken);
            var completed = await Task.WhenAny(
                waitTask,
                heartbeat);
            if (completed == waitTask)
                break;
            _lastActivityAt = DateTimeOffset.UtcNow;
        }

        await waitTask;
        _lastActivityAt = DateTimeOffset.UtcNow;
    }
}
