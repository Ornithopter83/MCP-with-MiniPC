using System.Collections.Concurrent;
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

public sealed record ParallelWorkSupervisorResult(
    ParallelWorkSupervisorExit Exit,
    string HqBody,
    string? ErrorCode,
    WorkGraphSnapshot Graph);

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
    public static bool TryParse(string? rawMessage, out ParallelHqTurn? turn, out string? error)
    {
        turn = null;
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

        if (route.Action == WorkerAction.Continue)
        {
            if (route.Target != WorkerRoleState.Work)
            {
                error = "PARALLEL_HQ_WORK_TARGET_REQUIRED";
                return false;
            }

            if (!WorkGraphTransportContract.TryParse(route.Body, out var patch, out var patchError))
            {
                error = patchError ?? "WORK_GRAPH_PATCH_INVALID";
                return false;
            }

            turn = new ParallelHqTurn(route.Action.Value, route.Body, patch, rawMessage ?? string.Empty);
            return true;
        }

        turn = new ParallelHqTurn(route.Action.Value, route.Body, null, rawMessage ?? string.Empty);
        return true;
    }
}

public sealed class ParallelWorkSupervisor : IParallelExternalBlockHost, IAsyncDisposable
{
    private readonly WorkGraph _graph;
    private readonly ParallelWorkScheduler _scheduler;
    private readonly Func<string, CancellationToken, Task<string>> _runHqAsync;
    private readonly string _baseRef;
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
    private bool _schedulerStarted;
    private bool _disposed;

    public ParallelWorkSupervisor(
        WorkGraph graph,
        IWorkItemExecutor executor,
        string baseRef,
        Func<string, CancellationToken, Task<string>> runHqAsync,
        CancellationToken cancellationToken = default)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        if (string.IsNullOrWhiteSpace(baseRef))
            throw new ArgumentException("병렬 WorkGraph 기준 ref가 비어 있습니다.", nameof(baseRef));
        _baseRef = baseRef.Trim();
        _runHqAsync = runHqAsync ?? throw new ArgumentNullException(nameof(runHqAsync));
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
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ParallelWorkSupervisor));

        return _scheduler.UpdateRunningContextAsync(
            workItemId,
            branch,
            worktreePath,
            sessionId,
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

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prompt = RoleContractLoader.BuildHqPrompt(
                inboundType,
                inboundBody,
                new WorkGraphPromptContext(
                    _graph.Revision,
                    _graph.MaxConcurrentWork,
                    _baseRef));

            string rawHqMessage;
            try
            {
                rawHqMessage = await _runHqAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Failure(
                    "PARALLEL_HQ_EXECUTION_FAILED",
                    ex.GetType().Name + ": " + ex.Message);
            }

            if (!ParallelHqTurnContract.TryParse(rawHqMessage, out var turn, out var turnError))
                return Failure(turnError ?? "PARALLEL_HQ_RESPONSE_INVALID", rawHqMessage);

            if (turn!.Action == WorkerAction.End)
            {
                if (_schedulerStarted)
                    await _scheduler.WaitForQuiescenceAsync(cancellationToken).ConfigureAwait(false);
                return new(
                    ParallelWorkSupervisorExit.Ended,
                    turn.Body,
                    null,
                    _graph.Snapshot());
            }

            if (turn.Action == WorkerAction.Pause)
            {
                return new(
                    ParallelWorkSupervisorExit.Paused,
                    turn.Body,
                    null,
                    _graph.Snapshot());
            }

            var normalizedPatch = ApplyDefaultBaseRef(
                turn.Patch!,
                _baseRef);
            var patchResult = await _scheduler.ApplyPatchAsync(
                normalizedPatch,
                cancellationToken).ConfigureAwait(false);

            if (!patchResult.Success)
                return Failure(patchResult.ErrorCode ?? "WORK_GRAPH_PATCH_REJECTED", turn.Body);

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
            var reasons = CollectNewSemanticSignals(snapshot);
            if (reasons.Count > 0)
            {
                return new SupervisorWake(
                    "WORK_GRAPH_EVENT",
                    FormatMechanicalGraphEvent(reasons, snapshot));
            }

            if (snapshot.RunningCount == 0 &&
                snapshot.ReadyCount == 0 &&
                _pendingExternalBlocks.IsEmpty)
            {
                var signature = BuildQuiescentSignature(snapshot);
                if (!string.Equals(signature, _lastQuiescentSignature, StringComparison.Ordinal))
                {
                    _lastQuiescentSignature = signature;
                    return new SupervisorWake(
                        "WORK_GRAPH_QUIESCENT",
                        FormatMechanicalGraphEvent(
                            new[] { "실행 가능한 WORK와 실행 중 WORK가 없습니다." },
                            snapshot));
                }
            }
        }
    }

    private List<string> CollectNewSemanticSignals(ParallelWorkSchedulerSnapshot snapshot)
    {
        var reasons = new List<string>();

        foreach (var item in snapshot.Graph.Items)
        {
            string? signal = null;
            string? reason = null;

            if (item.State == WorkItemState.Failed)
            {
                signal = $"{item.Id}|FAILED|{item.FailureCode}|{item.FinishedAtUtc:O}";
                reason = $"WorkItem {item.Id}가 FAILED 상태가 되었습니다.";
            }
            else if (item.State == WorkItemState.Blocked && !string.IsNullOrWhiteSpace(item.BlockCode))
            {
                signal = $"{item.Id}|BLOCKED|{item.BlockCode}|{item.FinishedAtUtc:O}";
                if (IsExternalBlockCode(item.BlockCode))
                {
                    if (_reportedSignals.Add(signal))
                    {
                        _pendingExternalBlocks[item.Id] = signal;
                        try
                        {
                            ExternalBlockAvailable?.Invoke(new ParallelWorkExternalBlock(
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

            if (signal is not null && _reportedSignals.Add(signal))
                reasons.Add(reason!);
        }

        return reasons;
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
        => string.Equals(blockCode, "JUDGE_REQUEST", StringComparison.Ordinal) ||
           string.Equals(blockCode, "RESOURCE_REQUEST", StringComparison.Ordinal);

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
        {
            builder.Append("- id=").Append(item.Id)
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
            if (!string.IsNullOrWhiteSpace(item.ResultSummary))
                builder.Append(" report=").Append(SingleLine(item.ResultSummary));
            builder.AppendLine();
        }

        builder.AppendLine("위 값은 Worker가 관측한 기계적 상태이며 작업 의미 판단 결과가 아닙니다.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildQuiescentSignature(ParallelWorkSchedulerSnapshot snapshot)
        => string.Join(
            "|",
            new[] { snapshot.Graph.Revision.ToString() }
                .Concat(snapshot.Graph.Items.Select(item =>
                    $"{item.Id}:{item.State}:{item.BlockCode}:{item.FailureCode}:{item.FinishedAtUtc:O}")));

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

    private sealed record SupervisorWake(
        string InboundType,
        string Body);
}
