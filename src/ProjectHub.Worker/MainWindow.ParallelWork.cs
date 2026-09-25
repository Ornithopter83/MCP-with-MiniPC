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

            ProjectWorkspacePersistence.SaveWorkGraph(workingDirectory, graph.Snapshot());

            var baseRef =
                _gitTarget?.HeadSha ??
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
                observationGate: observationGate);

            async Task<string> RunParallelHqAsync(
                string prompt,
                CancellationToken cancellationToken)
            {
                RunOnUi(() =>
                {
                    TaskDirection.Text = "설계·관제 AI";
                    TaskTitle.Text = "병렬 WorkGraph 관제";
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
                        "HQ_PARALLEL",
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
                            "HQ_PARALLEL",
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
                            ? "HQ_PARALLEL_PROCESS_EXIT"
                            : result.StandardError);
                }

                lastHqMessage = result.FinalMessage;
                RunOnUi(() =>
                {
                    AddRoleResponseHistory(
                        WorkerRoleState.Hq,
                        "병렬 관제",
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

            executor.Progress += progress => RunOnUi(() =>
            {
                _lastActivityAt = DateTimeOffset.UtcNow;
                AddRoleProgressHistory(
                    WorkerRoleState.Work,
                    $"[{progress.WorkItemId}] {progress.Message}",
                    implementer.Provider);
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
                    ImplementerStageModelText.Text =
                        $"{implementerModel} · {snapshot.RunningCount}/{snapshot.Graph.MaxConcurrentWork}";
                    TaskDirection.Text = "작업 AI";
                    TaskTitle.Text =
                        $"병렬 WORK · {snapshot.RunningCount}/{snapshot.Graph.MaxConcurrentWork} 실행 중 · " +
                        $"READY {snapshot.ReadyCount} · BLOCKED {snapshot.BlockedCount}";
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
                    "PARALLEL RESOURCE",
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
                        "PARALLEL JUDGE",
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
            var inboundBody = continuing
                ? TaskContinuationContract.BuildHqFollowupInput(
                    continuation!.Status,
                    continuation.LastHqMessage,
                    request,
                    ProjectWorkspacePersistence.HandoffPath(workingDirectory),
                    ProjectWorkspacePersistence.EventLogPath(workingDirectory, jobId)) +
                  Environment.NewLine +
                  $"병렬 WorkGraph snapshot: {ProjectWorkspacePersistence.WorkGraphPath(workingDirectory, jobId)}"
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
                    $"병렬 WorkGraph 관제가 기계적 오류로 종료되었습니다.{Environment.NewLine}" +
                    $"errorCode={result.ErrorCode ?? "PARALLEL_WORK_FAILED"}{Environment.NewLine}" +
                    result.HqBody;
                AddTaskMessage(
                    "TASK ERROR",
                    errorBody,
                    status: result.ErrorCode ?? "PARALLEL_WORK_FAILED");
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

            var finalStatus = hadMechanicalErrors
                ? "DONE_WITH_ERROR"
                : "DONE";
            ResultTitle.Text = hadMechanicalErrors
                ? "DONE · 오류 기록 있음"
                : "DONE";
            ResultBody.Text = result.HqBody;
            TaskTitle.Text = "병렬 WORK와 기계적 대기 작업을 모두 확인했습니다.";
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
                    "사용자가 병렬 실행 구간을 중단했습니다. WorkGraph와 확보된 세션 정보를 보존합니다.",
                    status: "CANCELED");
                ResultTitle.Text = "CANCELED";
                ResultBody.Text =
                    "현재 병렬 실행 구간을 중단했습니다. 작업 추가로 같은 WorkGraph에서 이어갈 수 있습니다.";
                TaskTitle.Text = "병렬 작업이 중단되었습니다. 후속 작업 입력 대기";
                SetFollowupComposerVisible(true);
            }
            else
            {
                AddTaskMessage(
                    "TASK CANCELED",
                    "병렬 Coordinator 작업이 취소되었습니다.");
                ResultTitle.Text = "CANCELED";
                TaskTitle.Text = "병렬 작업이 취소되었습니다.";
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
            TaskTitle.Text = "병렬 WORK 실행 오류";
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
            RunOnUi(UpdateDashboardSummary);
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
