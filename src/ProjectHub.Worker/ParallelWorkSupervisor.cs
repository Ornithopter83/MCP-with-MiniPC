using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace ProjectHub.Worker;

public enum ParallelWorkSupervisorExit
{
    Ended,
    Paused,
    Failed
}

public sealed record ParallelHqTurn(
    WorkerAction Action,
    string Body,
    WorkGraphPatch? Patch,
    string RawMessage);

public sealed record ParallelHqEnvelope(
    WorkerAction Action,
    string Body,
    string RawMessage);

public sealed record ParallelWorkSupervisorResult(
    ParallelWorkSupervisorExit Exit,
    string HqBody,
    string? ErrorCode,
    WorkGraphSnapshot Graph);

public sealed record ParallelEndFinalizationResult(
    bool Success,
    string? ErrorCode = null,
    string? Body = null);

public sealed record ParallelWorkExternalBlock(
    string WorkItemId,
    string BlockCode,
    string Body,
    string? ResultRef,
    string? Branch,
    string? WorktreePath,
    string? SessionId,
    string? Goal = null);

public static class ParallelHqTurnContract
{
    public static bool TryParseEnvelope(
        string? rawMessage,
        out ParallelHqEnvelope? envelope,
        out string? error)
    {
        envelope = null;
        error = null;

        var route = WorkerGotoContract.Parse(WorkerRoleState.Hq, rawMessage);
        if (route.Error is not null)
        {
            error = "PARALLEL_HQ_" + route.Error;
            return false;
        }

        if (route.Action is null)
        {
            error = "PARALLEL_HQ_ACTION_MISSING";
            return false;
        }

        if (route.Action == WorkerAction.Continue &&
            route.Target != WorkerRoleState.Work)
        {
            error = "PARALLEL_HQ_WORK_TARGET_REQUIRED";
            return false;
        }

        envelope = new(
            route.Action.Value,
            route.Body,
            rawMessage ?? string.Empty);
        return true;
    }

    public static bool TryParse(
        string? rawMessage,
        out ParallelHqTurn? turn,
        out string? error)
    {
        turn = null;
        if (!TryParseEnvelope(rawMessage, out var envelope, out error))
            return false;

        if (envelope!.Action == WorkerAction.Continue)
        {
            var structuredResult =
                WorkerStructuredPayloadHelper.ProcessDeterministically<WorkGraphPatch>(
                    envelope.Body,
                    WorkGraphTransportContract.TryParse);
            if (!structuredResult.Success || structuredResult.Value is null)
            {
                error = structuredResult.FinalErrorCode ??
                        structuredResult.InitialErrorCode ??
                        "WORK_GRAPH_PATCH_INVALID";
                return false;
            }

            turn = new(
                envelope.Action,
                envelope.Body,
                structuredResult.Value,
                envelope.RawMessage);
            return true;
        }

        turn = new(
            envelope.Action,
            envelope.Body,
            null,
            envelope.RawMessage);
        return true;
    }
}

public sealed class ParallelWorkSupervisor : IParallelExternalBlockHost, IAsyncDisposable
{
    private const int MaximumConsecutivePatchRejections = 3;
    private const int MaximumRecoverableHqTransportRetries = 2;

    private readonly WorkGraph _graph;
    private readonly ParallelWorkScheduler _scheduler;
    private readonly Func<string, CancellationToken, Task<string>> _runHqAsync;
    private readonly Func<string, CancellationToken, Task<StructuredPayloadResult<WorkGraphPatch>>> _processWorkGraphPayloadAsync;
    private readonly string _initialBaseRef;
    private readonly bool _includeContractOnFirstHqTurn;
    private readonly bool _enableCompletionReview;
    private readonly Func<WorkGraphSnapshot, CancellationToken, Task<ParallelEndFinalizationResult>>? _finalizeEndAsync;
    private readonly Channel<ParallelWorkSchedulerSnapshot> _stateChanges =
        Channel.CreateUnbounded<ParallelWorkSchedulerSnapshot>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly HashSet<string> _reportedSignals = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _pendingExternalBlocks = new(StringComparer.Ordinal);
    private string? _lastQuiescentSignature;
    private WorkGraphSnapshot _hqKnownSnapshot;
    private bool _schedulerStarted;
    private bool _disposed;

    public ParallelWorkSupervisor(
        WorkGraph graph,
        IWorkItemExecutor executor,
        string baseRef,
        Func<string, CancellationToken, Task<string>> runHqAsync,
        CancellationToken cancellationToken = default,
        bool includeContractOnFirstHqTurn = true,
        Func<string, CancellationToken, Task<StructuredPayloadResult<WorkGraphPatch>>>? processWorkGraphPayloadAsync = null,
        bool enableCompletionReview = false,
        Func<WorkGraphSnapshot, CancellationToken, Task<ParallelEndFinalizationResult>>? finalizeEndAsync = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        if (string.IsNullOrWhiteSpace(baseRef))
            throw new ArgumentException("병렬 WorkGraph 기준 ref가 비어 있습니다.", nameof(baseRef));
        _initialBaseRef = baseRef.Trim();
        _runHqAsync = runHqAsync ?? throw new ArgumentNullException(nameof(runHqAsync));
        _processWorkGraphPayloadAsync = processWorkGraphPayloadAsync ??
            new Func<string, CancellationToken, Task<StructuredPayloadResult<WorkGraphPatch>>>(
                (payload, _) => Task.FromResult(
                    WorkerStructuredPayloadHelper.ProcessDeterministically<WorkGraphPatch>(
                        payload,
                        WorkGraphTransportContract.TryParse)));
        _includeContractOnFirstHqTurn = includeContractOnFirstHqTurn;
        _enableCompletionReview = enableCompletionReview;
        _finalizeEndAsync = finalizeEndAsync;
        _hqKnownSnapshot = graph.Snapshot();
        _scheduler = new ParallelWorkScheduler(graph, executor, cancellationToken);
        _scheduler.StateChanged += OnSchedulerStateChanged;
    }

    public event Action<ParallelWorkSchedulerSnapshot>? StateChanged;
    public event Action<ParallelWorkExternalBlock>? ExternalBlockAvailable;

    public Task<bool> UpdateRunningContextAsync(
        string workItemId,
        string? branch = null,
        string? worktreePath = null,
        string? sessionId = null,
        string? baseRef = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParallelWorkSupervisor));

        return _scheduler.UpdateRunningContextAsync(
            workItemId,
            branch,
            worktreePath,
            sessionId,
            baseRef,
            cancellationToken);
    }

    public async Task<bool> ResumeExternalWorkItemAsync(
        string workItemId,
        string inputType,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParallelWorkSupervisor));

        var released = await _scheduler.ResumeBlockedAsync(
            workItemId,
            inputType,
            body,
            cancellationToken).ConfigureAwait(false);

        if (released)
            _pendingExternalBlocks.TryRemove(workItemId, out _);
        return released;
    }

    public async Task<ParallelWorkSupervisorResult> RunAsync(
        string initialInboundType,
        string initialBody,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParallelWorkSupervisor));
        if (string.IsNullOrWhiteSpace(initialInboundType))
            throw new ArgumentException("초기 입력 유형이 비어 있습니다.", nameof(initialInboundType));

        var inboundType = initialInboundType.Trim();
        var inboundBody = initialBody ?? string.Empty;
        var firstHqTurn = true;
        var consecutivePatchRejections = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prompt = RoleContractLoader.BuildHqPrompt(
                inboundType,
                inboundBody,
                new WorkGraphPromptContext(
                    _graph.Revision,
                    _graph.MaxConcurrentWork,
                    GetCurrentDefaultBaseRef()),
                includeContract: firstHqTurn && _includeContractOnFirstHqTurn);
            firstHqTurn = false;

            string rawHqMessage;
            var hqTransportRetry = 0;
            while (true)
            {
                try
                {
                    rawHqMessage = await _runHqAsync(prompt, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (
                    IsRecoverableHqTransportFailure(ex) &&
                    hqTransportRetry < MaximumRecoverableHqTransportRetries)
                {
                    hqTransportRetry++;
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(750 * hqTransportRetry),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Failure(
                        "PARALLEL_HQ_EXECUTION_FAILED",
                        ex.GetType().Name + ": " + ex.Message);
                }
            }

            if (!ParallelHqTurnContract.TryParseEnvelope(
                    rawHqMessage,
                    out var envelope,
                    out var turnError))
            {
                consecutivePatchRejections++;
                var rejectionCode = turnError ?? "PARALLEL_HQ_RESPONSE_INVALID";
                var rejectionBody = FormatHqResponseRejected(
                    rejectionCode,
                    _graph.Snapshot(),
                    consecutivePatchRejections);

                if (consecutivePatchRejections >= MaximumConsecutivePatchRejections)
                {
                    return Failure(
                        "WORK_GRAPH_PATCH_RETRY_LIMIT",
                        rejectionBody +
                        Environment.NewLine +
                        $"같은 관제 흐름에서 HQ 제어 응답이 {MaximumConsecutivePatchRejections}회 연속 기계적으로 거부되어 종료합니다.");
                }

                inboundType = "HQ_RESPONSE_CONTRACT_REJECTED";
                inboundBody = rejectionBody;
                continue;
            }

            ParallelHqTurn turn;
            if (envelope!.Action == WorkerAction.Continue)
            {
                if (!WorkGraphTransportContract.TryParse(
                        envelope.Body,
                        out _,
                        out var preliminaryPatchError) &&
                    IsWorkGraphJsonParseError(preliminaryPatchError))
                {
                    consecutivePatchRejections++;
                    var rejectionCode = preliminaryPatchError!;
                    var rejectionBody = FormatJsonPatchRejected(
                        rejectionCode,
                        _graph.Snapshot(),
                        consecutivePatchRejections);

                    if (consecutivePatchRejections >= MaximumConsecutivePatchRejections)
                    {
                        return Failure(
                            "WORK_GRAPH_PATCH_RETRY_LIMIT",
                            rejectionBody +
                            Environment.NewLine +
                            $"같은 관제 흐름에서 WorkGraph patch JSON이 {MaximumConsecutivePatchRejections}회 연속 기계적으로 거부되어 종료합니다.");
                    }

                    inboundType = "WORK_GRAPH_PATCH_SCHEMA_REJECTED";
                    inboundBody = rejectionBody;
                    continue;
                }

                StructuredPayloadResult<WorkGraphPatch> structuredResult;
                try
                {
                    structuredResult = await _processWorkGraphPayloadAsync(
                        envelope.Body,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return Failure(
                        "WORK_GRAPH_STRUCTURED_HELPER_FAILED",
                        exception.GetType().Name + ": " + exception.Message);
                }

                if (!structuredResult.Success || structuredResult.Value is null)
                {
                    consecutivePatchRejections++;
                    var rejectionCode =
                        structuredResult.FinalErrorCode ??
                        structuredResult.InitialErrorCode ??
                        "WORK_GRAPH_PATCH_INVALID";
                    var errorDetail =
                        structuredResult.ErrorDetail ??
                        WorkGraphTransportContract.DescribeError(
                            structuredResult.FinalPayload,
                            rejectionCode) ??
                        WorkGraphTransportContract.DescribeError(
                            envelope.Body,
                            rejectionCode);
                    var rejectionBody = FormatStructuredPatchRejected(
                        rejectionCode,
                        errorDetail,
                        _graph.Snapshot(),
                        consecutivePatchRejections);

                    if (consecutivePatchRejections >= MaximumConsecutivePatchRejections)
                    {
                        return Failure(
                            "WORK_GRAPH_PATCH_RETRY_LIMIT",
                            rejectionBody +
                            Environment.NewLine +
                            $"같은 관제 흐름에서 WorkGraph patch가 {MaximumConsecutivePatchRejections}회 연속 기계적으로 거부되어 종료합니다.");
                    }

                    inboundType = "WORK_GRAPH_PATCH_SCHEMA_REJECTED";
                    inboundBody = rejectionBody;
                    continue;
                }

                var structuredPatch = structuredResult.Value;
                if (structuredPatch.Operations.Count == 0 &&
                    (structuredResult.Repaired || !HasRunnableOrRunningWork(_graph.Snapshot())))
                {
                    consecutivePatchRejections++;
                    var rejectionCode = structuredResult.Repaired
                        ? "WORK_GRAPH_REPAIRED_EMPTY_PATCH"
                        : "WORK_GRAPH_EMPTY_CONTINUE";
                    var errorDetail = structuredResult.Repaired
                        ? "구조 복구 결과 operations가 비어 있습니다. 불완전한 HQ 응답을 no-op patch로 간주하지 않습니다."
                        : "READY 또는 RUNNING WorkItem이 없는 상태에서 operations가 빈 CONTINUE는 진행을 만들 수 없습니다.";
                    var rejectionBody = FormatStructuredPatchRejected(
                        rejectionCode,
                        errorDetail,
                        _graph.Snapshot(),
                        consecutivePatchRejections);

                    if (consecutivePatchRejections >= MaximumConsecutivePatchRejections)
                    {
                        return Failure(
                            "WORK_GRAPH_PATCH_RETRY_LIMIT",
                            rejectionBody +
                            Environment.NewLine +
                            $"같은 관제 흐름에서 WorkGraph patch가 {MaximumConsecutivePatchRejections}회 연속 기계적으로 거부되어 종료합니다.");
                    }

                    inboundType = "WORK_GRAPH_PATCH_SCHEMA_REJECTED";
                    inboundBody = rejectionBody;
                    continue;
                }

                turn = new(
                    envelope.Action,
                    envelope.Body,
                    structuredPatch,
                    envelope.RawMessage);
            }
            else
            {
                turn = new(
                    envelope.Action,
                    envelope.Body,
                    null,
                    envelope.RawMessage);
            }

            if (turn.Action == WorkerAction.End)
            {
                if (_schedulerStarted)
                    await _scheduler.WaitForQuiescenceAsync(cancellationToken).ConfigureAwait(false);

                var endSnapshot = _graph.Snapshot();
                var openItems = endSnapshot.Items
                    .Where(item => item.State is
                        WorkItemState.Planned or
                        WorkItemState.Ready or
                        WorkItemState.Running or
                        WorkItemState.Blocked)
                    .ToArray();

                if (openItems.Length > 0)
                {
                    inboundType = "WORK_GRAPH_END_REJECTED";
                    inboundBody = FormatEndRejected(openItems, endSnapshot);
                    continue;
                }

                if (_finalizeEndAsync is not null)
                {
                    ParallelEndFinalizationResult finalization;
                    try
                    {
                        finalization = await _finalizeEndAsync(
                            endSnapshot,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        return Failure(
                            "WORKSPACE_FINALIZATION_FAILED",
                            exception.GetType().Name + ": " + exception.Message);
                    }

                    if (!finalization.Success)
                    {
                        inboundType = "WORKSPACE_FINALIZATION_REQUIRED";
                        inboundBody = FormatEndFinalizationRejected(finalization, endSnapshot);
                        continue;
                    }
                }

                return new(
                    ParallelWorkSupervisorExit.Ended,
                    turn.Body,
                    null,
                    endSnapshot);
            }

            if (turn.Action == WorkerAction.Pause)
            {
                if (_schedulerStarted)
                {
                    await _scheduler.PauseLaunchingAsync(cancellationToken).ConfigureAwait(false);
                    await _scheduler.WaitForQuiescenceAsync(cancellationToken).ConfigureAwait(false);
                }

                var pauseSnapshot = _graph.Snapshot();
                if (pauseSnapshot.Items.Any(item => item.State == WorkItemState.Running))
                {
                    return Failure(
                        "WORK_GRAPH_PAUSE_RUNNING_REMAINS",
                        turn.Body);
                }

                return new(
                    ParallelWorkSupervisorExit.Paused,
                    turn.Body,
                    null,
                    pauseSnapshot);
            }

            var normalizedPatch = ApplyDefaultBaseRef(
                turn.Patch!,
                GetCurrentDefaultBaseRef());
            var patchResult = await _scheduler.ApplyPatchAsync(
                normalizedPatch,
                cancellationToken).ConfigureAwait(false);

            if (!patchResult.Success)
            {
                consecutivePatchRejections++;
                var rejectionCode = patchResult.ErrorCode ?? "WORK_GRAPH_PATCH_REJECTED";
                var rejectionBody = FormatPatchRejected(
                    rejectionCode,
                    _graph.Snapshot(),
                    consecutivePatchRejections);

                if (consecutivePatchRejections >= MaximumConsecutivePatchRejections)
                {
                    return Failure(
                        "WORK_GRAPH_PATCH_RETRY_LIMIT",
                        rejectionBody +
                        Environment.NewLine +
                        $"같은 관제 흐름에서 WorkGraph patch가 {MaximumConsecutivePatchRejections}회 연속 거부되어 종료합니다.");
                }

                inboundType = "WORK_GRAPH_PATCH_REJECTED";
                inboundBody = rejectionBody;
                continue;
            }

            consecutivePatchRejections = 0;

            if (!_schedulerStarted)
            {
                _schedulerStarted = true;
                await _scheduler.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            var next = await WaitForHqWakeAsync(cancellationToken).ConfigureAwait(false);
            inboundType = next.InboundType;
            inboundBody = next.Body;
        }
    }


    internal static bool IsRecoverableHqTransportFailure(Exception exception)
    {
        var message = exception?.Message ?? string.Empty;
        return message.Contains("WEB_RESPONSE_TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("WEB_RESPONSE_LOST_AFTER_STREAM_END", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("WEB_RESPONSE_KEY_MISSING", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("WEB_RESPONSE_BODY_MISSING_AFTER_STREAM_END", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("response_timeout", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("response_lost_after_stream_end", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("response_key_missing", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("response_body_missing_after_stream_end", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRunnableOrRunningWork(WorkGraphSnapshot snapshot)
        => snapshot.Items.Any(item =>
            item.State is WorkItemState.Ready or WorkItemState.Running);

    private string GetCurrentDefaultBaseRef()
    {
        var latestIntegration = _graph.Items
            .Where(item =>
                item.Kind == WorkItemKind.Integration &&
                item.State == WorkItemState.Completed &&
                !string.IsNullOrWhiteSpace(item.ResultRef))
            .OrderByDescending(item => item.FinishedAtUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.CreatedOrder)
            .FirstOrDefault();

        return latestIntegration?.ResultRef ?? _initialBaseRef;
    }

    internal static WorkGraphPatch ApplyDefaultBaseRef(
        WorkGraphPatch patch,
        string baseRef)
    {
        if (string.IsNullOrWhiteSpace(baseRef))
            throw new ArgumentException("기준 ref가 비어 있습니다.", nameof(baseRef));

        var operations = patch.Operations
            .Select(operation =>
            {
                if (operation.Type != WorkGraphPatchOperationType.Add ||
                    operation.Item is null ||
                    !string.IsNullOrWhiteSpace(operation.Item.BaseRef))
                    return operation;

                return operation with
                {
                    Item = operation.Item with { BaseRef = baseRef.Trim() }
                };
            })
            .ToArray();

        return patch with { Operations = operations };
    }

    private async Task<SupervisorWake> WaitForHqWakeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var snapshot = await _stateChanges.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            while (_stateChanges.Reader.TryRead(out var newer))
                snapshot = newer;

            RefreshExternalBlocks(snapshot);
            var signals = CollectNewHqSignals(snapshot);
            if (signals.UrgentReasons.Count > 0)
            {
                var reasons = signals.UrgentReasons
                    .Concat(signals.CompletionReasons)
                    .ToArray();
                var body = FormatMechanicalGraphDeltaEvent(
                    reasons,
                    _hqKnownSnapshot,
                    snapshot);
                _hqKnownSnapshot = snapshot.Graph;
                return new SupervisorWake(
                    "WORK_GRAPH_EVENT",
                    body);
            }

            if (snapshot.RunningCount == 0 &&
                snapshot.ReadyCount == 0 &&
                _pendingExternalBlocks.IsEmpty)
            {
                var signature = BuildQuiescentSignature(snapshot);
                if (!string.Equals(signature, _lastQuiescentSignature, StringComparison.Ordinal))
                {
                    _lastQuiescentSignature = signature;
                    var reasons = new List<string>
                    {
                        "실행 가능한 WORK와 실행 중 WORK가 없습니다."
                    };
                    reasons.AddRange(signals.CompletionReasons);
                    var body = FormatMechanicalGraphDeltaEvent(
                        reasons,
                        _hqKnownSnapshot,
                        snapshot);
                    _hqKnownSnapshot = snapshot.Graph;
                    return new SupervisorWake(
                        "WORK_GRAPH_QUIESCENT",
                        body);
                }
            }

            if (_enableCompletionReview &&
                signals.CompletionReasons.Count > 0)
            {
                var reasons = new List<string>(
                    signals.CompletionReasons)
                {
                    "HQ가 직전 관제 이후 완료된 WorkItem을 즉시 점검하고 추가·보완·통합·검증 작업 필요 여부를 판단합니다."
                };
                var body = FormatMechanicalGraphDeltaEvent(
                    reasons,
                    _hqKnownSnapshot,
                    snapshot);
                _hqKnownSnapshot = snapshot.Graph;
                return new SupervisorWake(
                    "WORK_GRAPH_PROGRESS_REVIEW",
                    body);
            }
        }
    }

    private HqSignalBatch CollectNewHqSignals(
        ParallelWorkSchedulerSnapshot snapshot)
    {
        var urgentReasons = new List<string>();
        var completionReasons = new List<string>();

        foreach (var item in snapshot.Graph.Items)
        {
            string? signal = null;
            string? reason = null;
            var completion = false;

            if (item.State == WorkItemState.Failed)
            {
                signal = $"{item.Id}|FAILED|{item.FailureCode}|{item.FinishedAtUtc:O}";
                reason = $"WorkItem {item.Id}가 FAILED 상태가 되었습니다.";
            }
            else if (item.State == WorkItemState.Blocked &&
                     !string.IsNullOrWhiteSpace(item.BlockCode))
            {
                signal = $"{item.Id}|BLOCKED|{item.BlockCode}|{item.BlockDetailCode}|{item.FinishedAtUtc:O}";
                if (IsExternalBlockCode(item.BlockCode))
                {
                    if (_reportedSignals.Add(signal))
                    {
                        _pendingExternalBlocks[item.Id] = signal;
                        try
                        {
                            ExternalBlockAvailable?.Invoke(
                                new ParallelWorkExternalBlock(
                                    item.Id,
                                    item.BlockCode,
                                    item.ResultSummary ?? string.Empty,
                                    item.ResultRef,
                                    item.Branch,
                                    item.WorktreePath,
                                    item.SessionId,
                                    item.Goal));
                        }
                        catch
                        {
                        }
                    }
                    continue;
                }

                reason = $"WorkItem {item.Id}가 {item.BlockCode} 상태로 HQ 판단을 기다립니다.";
            }
            else if (_enableCompletionReview &&
                     item.State == WorkItemState.Completed)
            {
                signal = $"{item.Id}|COMPLETED|{item.ResultType}|{item.ResultRef}|{item.FinishedAtUtc:O}";
                reason = $"WorkItem {item.Id}가 COMPLETED 상태가 되었습니다.";
                completion = true;
            }

            if (signal is null || !_reportedSignals.Add(signal))
                continue;

            if (completion)
                completionReasons.Add(reason!);
            else
                urgentReasons.Add(reason!);
        }

        return new HqSignalBatch(
            urgentReasons,
            completionReasons);
    }

    private void RefreshExternalBlocks(ParallelWorkSchedulerSnapshot snapshot)
    {
        foreach (var id in _pendingExternalBlocks.Keys)
        {
            var item = snapshot.Graph.Items.FirstOrDefault(value => value.Id == id);
            if (item is null ||
                item.State != WorkItemState.Blocked ||
                !IsExternalBlockCode(item.BlockCode))
                _pendingExternalBlocks.TryRemove(id, out _);
        }
    }

    private static bool IsExternalBlockCode(string? blockCode)
        => string.Equals(blockCode, "RESOURCE_REQUEST", StringComparison.Ordinal);

    public static string FormatMechanicalGraphDeltaEvent(
        IReadOnlyList<string> reasons,
        WorkGraphSnapshot previous,
        ParallelWorkSchedulerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(snapshot);

        var previousItems = previous.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var changedItems = snapshot.Graph.Items
            .Where(item =>
                !previousItems.TryGetValue(item.Id, out var prior) ||
                HasHqVisibleChange(prior, item))
            .OrderBy(item => item.CreatedOrder)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("WorkGraph 변경");
        builder.AppendLine($"revision={snapshot.Graph.Revision}");
        builder.AppendLine($"running={snapshot.RunningCount} ready={snapshot.ReadyCount} blocked={snapshot.BlockedCount} completed={snapshot.CompletedCount} failed={snapshot.FailedCount}");

        if (reasons.Count > 0)
            builder.AppendLine("event=" + string.Join(" | ", reasons));

        if (changedItems.Length == 0)
        {
            builder.AppendLine("changedItems=없음");
        }
        else
        {
            foreach (var item in changedItems)
                AppendMechanicalItem(builder, item);
        }

        return builder.ToString().TrimEnd();
    }

    private static bool HasHqVisibleChange(
        WorkItemSnapshot previous,
        WorkItemSnapshot current)
        => previous.State != current.State ||
           previous.Kind != current.Kind ||
           !previous.Dependencies.SequenceEqual(current.Dependencies) ||
           !string.Equals(previous.BaseRef, current.BaseRef, StringComparison.Ordinal) ||
           !string.Equals(previous.ResultRef, current.ResultRef, StringComparison.Ordinal) ||
           previous.ResultType != current.ResultType ||
           !string.Equals(previous.ResultSummary, current.ResultSummary, StringComparison.Ordinal) ||
           !string.Equals(previous.FailureCode, current.FailureCode, StringComparison.Ordinal) ||
           !string.Equals(previous.BlockCode, current.BlockCode, StringComparison.Ordinal) ||
           !string.Equals(previous.BlockDetailCode, current.BlockDetailCode, StringComparison.Ordinal);

    private static void AppendMechanicalItem(StringBuilder builder, WorkItemSnapshot item)
    {
        builder.Append("workItemId=").Append(item.Id)
            .Append(" kind=").Append(item.Kind.ToString().ToUpperInvariant())
            .Append(" state=").Append(item.State.ToString().ToUpperInvariant());

        if (item.Dependencies.Count > 0)
            builder.Append(" dependencies=").Append(string.Join(",", item.Dependencies));
        if (!string.IsNullOrWhiteSpace(item.ResultRef))
            builder.Append(" resultRef=").Append(item.ResultRef);
        if (!string.IsNullOrWhiteSpace(item.FailureCode))
            builder.Append(" failureCode=").Append(item.FailureCode);
        if (!string.IsNullOrWhiteSpace(item.BlockCode))
            builder.Append(" blockCode=").Append(item.BlockCode);
        if (!string.IsNullOrWhiteSpace(item.BlockDetailCode))
            builder.Append(" blockDetailCode=").Append(item.BlockDetailCode);
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(item.ResultSummary))
        {
            builder.AppendLine("WORK_REPORT_BEGIN");
            builder.AppendLine(item.ResultSummary.TrimEnd());
            builder.AppendLine("WORK_REPORT_END");
        }
    }

    public static string FormatMechanicalGraphEvent(
        IReadOnlyList<string> reasons,
        ParallelWorkSchedulerSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("병렬 WorkGraph 기계적 상태 이벤트");
        builder.AppendLine($"revision={snapshot.Graph.Revision}");
        builder.AppendLine($"maxConcurrentWork={snapshot.Graph.MaxConcurrentWork}");
        builder.AppendLine($"running={snapshot.RunningCount}");
        builder.AppendLine($"ready={snapshot.ReadyCount}");
        builder.AppendLine($"blocked={snapshot.BlockedCount}");
        builder.AppendLine($"completed={snapshot.CompletedCount}");
        builder.AppendLine($"failed={snapshot.FailedCount}");

        if (reasons.Count > 0)
        {
            builder.AppendLine("eventReasons:");
            foreach (var reason in reasons)
                builder.AppendLine("- " + reason);
        }

        builder.AppendLine("items:");
        foreach (var item in snapshot.Graph.Items.OrderBy(value => value.CreatedOrder))
            AppendMechanicalItem(builder, item);

        builder.AppendLine("위 값은 Worker가 관측한 기계적 상태이며 작업 의미 판단 결과가 아닙니다.");
        return builder.ToString().TrimEnd();
    }

    private static string FormatHqResponseRejected(
        string errorCode,
        WorkGraphSnapshot snapshot,
        int consecutiveRejections)
        => string.Join(
            Environment.NewLine,
            "HQ_RESPONSE_REJECTED",
            $"revision={snapshot.Revision}",
            "errorCode=" + errorCode,
            $"attempt={consecutiveRejections}",
            "직전 의미는 유지하고 ACTION/GOTO 형식만 수정해 다시 응답하세요.");

    private static bool IsWorkGraphJsonParseError(string? errorCode)
        => string.Equals(
               errorCode,
               "WORK_GRAPH_PATCH_JSON_INVALID",
               StringComparison.Ordinal) ||
           string.Equals(
               errorCode,
               "WORK_GRAPH_PATCH_JSON_MISSING",
               StringComparison.Ordinal);

    private static string FormatJsonPatchRejected(
        string errorCode,
        WorkGraphSnapshot snapshot,
        int consecutiveRejections)
        => string.Join(
            Environment.NewLine,
            "WORK_GRAPH_PATCH_REJECTED",
            $"revision={snapshot.Revision}",
            "errorCode=" + errorCode,
            $"attempt={consecutiveRejections}",
            "직전 의미는 유지하고 완전한 WORK_GRAPH_PATCH JSON만 다시 출력하세요.");

    private static string FormatStructuredPatchRejected(
        string errorCode,
        string? errorDetail,
        WorkGraphSnapshot snapshot,
        int consecutiveRejections)
        => string.Join(
            Environment.NewLine,
            "WORK_GRAPH_PATCH_REJECTED",
            $"revision={snapshot.Revision}",
            "errorCode=" + errorCode,
            string.IsNullOrWhiteSpace(errorDetail) ? string.Empty : errorDetail.Trim(),
            $"attempt={consecutiveRejections}",
            "현재 revision에 맞는 patch 형식만 수정해 다시 응답하세요.")
            .Replace(Environment.NewLine + Environment.NewLine, Environment.NewLine);

    private static string FormatPatchRejected(
        string errorCode,
        WorkGraphSnapshot snapshot,
        int consecutiveRejections)
        => string.Join(
            Environment.NewLine,
            "WORK_GRAPH_PATCH_REJECTED",
            $"revision={snapshot.Revision}",
            "errorCode=" + errorCode,
            $"attempt={consecutiveRejections}",
            "현재 WorkGraph 상태에 맞는 patch를 다시 응답하세요.");

    private static string FormatEndFinalizationRejected(
        ParallelEndFinalizationResult finalization,
        WorkGraphSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("HQ의 END 요청 뒤 사용자 작업 폴더 최종 반영이 완료되지 않아 종료를 보류했습니다.");
        builder.AppendLine($"revision={snapshot.Revision}");
        builder.AppendLine("errorCode=" + (finalization.ErrorCode ?? "WORKSPACE_FINALIZATION_REQUIRED"));
        if (!string.IsNullOrWhiteSpace(finalization.Body))
            builder.AppendLine(finalization.Body.Trim());
        builder.Append("WorkGraph는 그대로 유지됩니다. 필요한 경우 INTEGRATION WorkItem을 추가하거나 현재 기계 오류에 맞는 다음 동작을 결정하세요.");
        return builder.ToString().TrimEnd();
    }

    private static string FormatEndRejected(
        IReadOnlyList<WorkItemSnapshot> openItems,
        WorkGraphSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("HQ의 END 요청 시 완료되지 않은 WorkItem이 남아 있어 종료를 보류했습니다.");
        builder.AppendLine($"revision={snapshot.Revision}");
        builder.AppendLine($"openItemCount={openItems.Count}");
        builder.AppendLine("openItems:");
        foreach (var item in openItems.OrderBy(value => value.CreatedOrder))
        {
            builder.Append("- id=").Append(item.Id)
                .Append(" state=").Append(item.State.ToString().ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(item.BlockCode))
                builder.Append(" blockCode=").Append(item.BlockCode);
            if (!string.IsNullOrWhiteSpace(item.BlockDetailCode))
                builder.Append(" blockDetailCode=").Append(item.BlockDetailCode);
            if (!string.IsNullOrWhiteSpace(item.FailureCode))
                builder.Append(" failureCode=").Append(item.FailureCode);
            builder.AppendLine();
        }
        builder.Append("위 항목은 기계적 상태이며 작업 의미에 대한 판정이 아닙니다.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildQuiescentSignature(ParallelWorkSchedulerSnapshot snapshot)
        => string.Join(
            "|",
            new[] { snapshot.Graph.Revision.ToString() }
                .Concat(snapshot.Graph.Items.Select(item =>
                    $"{item.Id}:{item.State}:{item.BlockCode}:{item.BlockDetailCode}:{item.FailureCode}:{item.FinishedAtUtc:O}")));

    private static string SingleLine(string value)
    {
        var normalized = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }

    private void OnSchedulerStateChanged(ParallelWorkSchedulerSnapshot snapshot)
    {
        StateChanged?.Invoke(snapshot);
        _stateChanges.Writer.TryWrite(snapshot);
    }

    private ParallelWorkSupervisorResult Failure(string errorCode, string detail)
        => new(
            ParallelWorkSupervisorExit.Failed,
            detail,
            errorCode,
            _graph.Snapshot());

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _scheduler.StateChanged -= OnSchedulerStateChanged;
        _stateChanges.Writer.TryComplete();
        await _scheduler.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record HqSignalBatch(
        IReadOnlyList<string> UrgentReasons,
        IReadOnlyList<string> CompletionReasons);

    private sealed record SupervisorWake(
        string InboundType,
        string Body);
}
