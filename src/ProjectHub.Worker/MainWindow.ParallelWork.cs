namespace ProjectHub.Worker;

public partial class MainWindow
{
    private async Task RunParallelCoordinatorFirstJobAsync(
        string request,
        CodexThreadOption? selectedThread,
        string workingDirectory,
        WorkerAiRoleSettings coordinator,
        WorkerAiRoleSettings implementer,
        CoordinatorContinuationState? continuation = null)
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
        ResetDashboardTaskInput();
        RunButton.Content = "■   취소";
        _userCanceledTask = false;
        _jobTimedOut = false;
        _lastActivityAt = DateTimeOffset.UtcNow;

        if (!continuing)
        {
            StartTaskTranscript(selectedThread, request, string.Empty);
            AddTaskMessage(
                "TASK REQUEST",
                request,
                sizeBytes: System.Text.Encoding.UTF8.GetByteCount(request),
                itemCount: 1);
        }
        else
        {
            StartCommandTranscript();
            AddTaskMessage(
                "USER FOLLOWUP",
                request,
                sizeBytes: System.Text.Encoding.UTF8.GetByteCount(request),
                itemCount: 1,
                includeHistory: false);
        }

        var coordinatorSession =
            continuation?.CoordinatorSessionId ??
            CodexCliRunner.NormalizeSessionId(coordinator.ThreadSessionId);
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
        ParallelJudgeWorkItemRouter? judgeRouter = null;
        CodexWorkItemExecutor? executor = null;
        WorkGraph? graph = null;

        try
        {
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

            var currentGitTarget = WorkerTargetConfiguration.ResolveGit(
                workingDirectory,
                _targetSettings);
            var gitPreflight = ParallelWorkGitPreflight.Validate(currentGitTarget);
            if (!gitPreflight.Success)
                throw new InvalidOperationException(
                    gitPreflight.ErrorCode + ": " + gitPreflight.Message);

            IReadOnlyList<string> recoveredPreparationItems = Array.Empty<string>();
            if (continuing)
                recoveredPreparationItems = graph.RecoverPreparationFailuresForContinuation();

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

            executor = new CodexWorkItemExecutor(
                jobId,
                workingDirectory,
                implementer,
                runner,
                judgeAvailable: _targetSettings.EffectiveJudge.Enabled,
                observationGate: observationGate,
                expectedPrimaryBranch: currentGitTarget.Branch);

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
                        });
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
                            }));
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

            supervisor = new ParallelWorkSupervisor(
                graph,
                executor,
                baseRef,
                RunParallelHqAsync,
                cts.Token);

            resourceRouter = new ParallelResourceWorkItemRouter(
                supervisor,
                resourceQueue,
                cts.Token);

            if (_targetSettings.EffectiveJudge.Enabled)
            {
                judgeRouter = new ParallelJudgeWorkItemRouter(
                    supervisor,
                    new JevParallelJudgeTransport(
                        _jevJudgeRunner,
                        _targetSettings.EffectiveJudge),
                    jobId,
                    cts.Token);
            }

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
                            referenceId: call.WorkItemId));
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
                    referenceId: progress.WorkItemId);
            });

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
                    var implementerModel = AiProviderCatalog.FormatModel(
                        implementer.Provider,
                        implementer.Model);
                    ImplementerStageModelText.Text = implementerModel;
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

            if (judgeRouter is not null)
            {
                judgeRouter.RoutingEvent += routing => RunOnUi(() =>
                {
                    _lastActivityAt = DateTimeOffset.UtcNow;
                    if (routing.Telemetry is not null)
                        RecordJevTransportTelemetry(jobId, routing.Telemetry, "JUDGE_PARALLEL");

                    AddTaskMessage(
                        "JUDGE",
                        $"workItemId={routing.WorkItemId} · stage={routing.Stage}" +
                        Environment.NewLine +
                        routing.Message,
                        status: routing.ErrorCode ?? routing.Stage,
                        includeHistory: false);

                    if (string.Equals(routing.Stage, "SENDING", StringComparison.Ordinal))
                    {
                        SetFlowState(
                            codexActive: false,
                            workerActive: true,
                            webActive: false,
                            explicitStage: TaskStage.Judge);
                    }
                });
            }

            var inboundType = continuing ? "USER_FOLLOWUP" : "USER_REQUEST";
            var restoredGraphSummary = continuing
                ? ParallelWorkSupervisor.FormatMechanicalGraphEvent(
                    recoveredPreparationItems.Count == 0
                        ? new[] { "저장된 WorkGraph를 복구했습니다." }
                        : new[]
                        {
                            "저장된 WorkGraph를 복구했습니다.",
                            "현재 Git 사전 검사를 통과해 WORK 시작 전 준비 실패 항목을 재활성화했습니다: " +
                            string.Join(",", recoveredPreparationItems)
                        },
                    new ParallelWorkSchedulerSnapshot(
                        graph.Snapshot(),
                        Array.Empty<RunningWorkItemSnapshot>()))
                : null;
            var inboundBody = continuing
                ? TaskContinuationContract.BuildHqFollowupInput(
                    continuation!.Status,
                    continuation.LastHqMessage,
                    request,
                    ProjectWorkspacePersistence.HandoffPath(workingDirectory),
                    ProjectWorkspacePersistence.EventLogPath(workingDirectory, jobId),
                    restoredGraphSummary) +
                  Environment.NewLine +
                  $"WorkGraph snapshot 파일: {ProjectWorkspacePersistence.WorkGraphPath(workingDirectory, jobId)}"
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
                TaskTitle.Text = "병렬 WORK 관제 오류";
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
                        $"기계적 대기 작업 완료 대기 · {pendingMechanical}건";
                    SetFlowState(false, false, false);
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
            var finalStatus = finalHasErrors
                ? "DONE_WITH_ERROR"
                : "DONE";
            ResultTitle.Text = finalHasErrors
                ? "DONE · 오류 기록 있음"
                : "DONE";
            ResultBody.Text = result.HqBody;
            TaskTitle.Text = "WORK와 기계적 대기 작업을 모두 확인했습니다.";
            AddTaskMessage(
                "TASK RESULT",
                result.HqBody,
                status: finalStatus,
                includeHistory: false);
            SaveParallelContinuation(
                finalStatus,
                result.HqBody,
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
            if (judgeRouter is not null)
            {
                try { await judgeRouter.DisposeAsync(); }
                catch (OperationCanceledException) { }
            }

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

            observationQueue.TransportEvent -= OnObservationSidecarEvent;
            resourceQueue.StateChanged -= OnResourceSidecarStateChanged;
            resourceQueue.CompletionAvailable -= OnResourceSidecarCompletion;
            resourceQueue.TransportEvent -= OnResourceSidecarTransportEvent;
            _resourceSidecarActive = false;
            _resourceSidecarQueued = 0;
            _resourceSidecarStatus = "ChatGPT Web";
            RunOnUi(() =>
            {
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
        CancellationToken cancellationToken)
    {
        try
        {
            await supervisor.UpdateRunningContextAsync(
                workItemId,
                branch,
                worktreePath,
                sessionId,
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
        ProjectWorkspacePersistence.SaveContinuation(_continuationState);
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
