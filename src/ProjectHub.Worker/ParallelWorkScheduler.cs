namespace ProjectHub.Worker;

public sealed record WorkItemDependencyResult(
    string WorkItemId,
    string? ResultRef,
    string? ResultSummary);

public sealed record WorkItemExecutionRequest(
    WorkItemSnapshot Item,
    int Slot,
    IReadOnlyList<WorkItemDependencyResult> Dependencies,
    string InboundType,
    string InboundBody);

public enum WorkItemExecutionOutcome
{
    Completed,
    Failed,
    Blocked
}

public sealed record WorkItemExecutionResult(
    WorkItemExecutionOutcome Outcome,
    string? ResultRef = null,
    string? ResultSummary = null,
    string? FailureCode = null,
    string? BlockCode = null,
    string? Branch = null,
    string? WorktreePath = null,
    string? SessionId = null)
{
    public static WorkItemExecutionResult Completed(
        string? resultRef = null,
        string? resultSummary = null,
        string? branch = null,
        string? worktreePath = null,
        string? sessionId = null)
        => new(WorkItemExecutionOutcome.Completed, resultRef, resultSummary, null, null, branch, worktreePath, sessionId);

    public static WorkItemExecutionResult Failed(
        string failureCode,
        string? resultSummary = null,
        string? branch = null,
        string? worktreePath = null,
        string? sessionId = null)
        => new(WorkItemExecutionOutcome.Failed, null, resultSummary, failureCode, null, branch, worktreePath, sessionId);

    public static WorkItemExecutionResult Blocked(
        string blockCode,
        string? resultSummary = null,
        string? resultRef = null,
        string? branch = null,
        string? worktreePath = null,
        string? sessionId = null)
        => new(WorkItemExecutionOutcome.Blocked, resultRef, resultSummary, null, blockCode, branch, worktreePath, sessionId);
}

public interface IWorkItemExecutor
{
    Task<WorkItemExecutionResult> ExecuteAsync(
        WorkItemExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record RunningWorkItemSnapshot(
    string WorkItemId,
    int Slot);

public sealed record ParallelWorkSchedulerSnapshot(
    WorkGraphSnapshot Graph,
    IReadOnlyList<RunningWorkItemSnapshot> Running)
{
    public int RunningCount => Running.Count;
    public int ReadyCount => Graph.Items.Count(item => item.State == WorkItemState.Ready);
    public int BlockedCount => Graph.Items.Count(item => item.State == WorkItemState.Blocked);
    public int CompletedCount => Graph.Items.Count(item => item.State == WorkItemState.Completed);
    public int FailedCount => Graph.Items.Count(item => item.State == WorkItemState.Failed);
}

public sealed class ParallelWorkScheduler : IAsyncDisposable
{
    private readonly WorkGraph _graph;
    private readonly IWorkItemExecutor _executor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts;
    private readonly Dictionary<string, RunningWork> _running = new(StringComparer.Ordinal);
    private TaskCompletionSource<bool> _quiescent = CompletedSignal();
    private bool _started;
    private bool _launchPaused;
    private bool _disposed;

    public ParallelWorkScheduler(
        WorkGraph graph,
        IWorkItemExecutor executor,
        CancellationToken cancellationToken = default)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public event Action<ParallelWorkSchedulerSnapshot>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ParallelWorkSchedulerSnapshot snapshot;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _started = true;
            if (_lifetimeCts.IsCancellationRequested)
                CancelRemainingLocked("SCHEDULER_LIFETIME_CANCELED");
            else
                LaunchReadyLocked();
            UpdateQuiescenceLocked();
            snapshot = CreateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }

        StateChanged?.Invoke(snapshot);
    }

    public async Task<WorkGraphPatchResult> ApplyPatchAsync(
        WorkGraphPatch patch,
        CancellationToken cancellationToken = default)
    {
        ParallelWorkSchedulerSnapshot snapshot;
        WorkGraphPatchResult result;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            result = _graph.ApplyPatch(patch);
            if (result.Success)
            {
                CancelGraphCanceledRunningItemsLocked();
                if (_lifetimeCts.IsCancellationRequested)
                    CancelRemainingLocked("SCHEDULER_LIFETIME_CANCELED");
                else if (_started)
                    LaunchReadyLocked();
            }

            UpdateQuiescenceLocked();
            snapshot = CreateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }

        StateChanged?.Invoke(snapshot);
        return result;
    }

    public async Task<bool> ResumeBlockedAsync(
        string workItemId,
        string inputType,
        string body,
        CancellationToken cancellationToken = default)
    {
        ParallelWorkSchedulerSnapshot snapshot;
        bool released;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            released = _graph.TryReleaseBlocked(workItemId, inputType, body);
            if (released && _started && !_lifetimeCts.IsCancellationRequested)
                LaunchReadyLocked();
            UpdateQuiescenceLocked();
            snapshot = CreateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }

        if (released)
            StateChanged?.Invoke(snapshot);
        return released;
    }

    public async Task<bool> UpdateRunningContextAsync(
        string workItemId,
        string? branch = null,
        string? worktreePath = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        ParallelWorkSchedulerSnapshot snapshot;
        bool updated;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            updated = _graph.TryUpdateExecutionContext(
                workItemId,
                branch,
                worktreePath,
                sessionId);
            snapshot = CreateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }

        if (updated)
            StateChanged?.Invoke(snapshot);
        return updated;
    }

    public async Task<ParallelWorkSchedulerSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return CreateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WaitForQuiescenceAsync(CancellationToken cancellationToken = default)
    {
        Task waitTask;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            waitTask = _quiescent.Task;
        }
        finally
        {
            _gate.Release();
        }

        await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PauseLaunchingAsync(CancellationToken cancellationToken = default)
    {
        ParallelWorkSchedulerSnapshot snapshot;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _launchPaused = true;
            UpdateQuiescenceLocked();
            snapshot = CreateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }

        StateChanged?.Invoke(snapshot);
    }

    public async Task CancelAllAsync(CancellationToken cancellationToken = default)
    {
        ParallelWorkSchedulerSnapshot snapshot;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            CancelRemainingLocked("SCHEDULER_CANCELED");

            foreach (var running in _running.Values)
                running.Cancellation.Cancel();

            UpdateQuiescenceLocked();
            snapshot = CreateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }

        StateChanged?.Invoke(snapshot);
    }

    private void LaunchReadyLocked()
    {
        while (_started &&
               !_launchPaused &&
               !_lifetimeCts.IsCancellationRequested &&
               _running.Count < _graph.MaxConcurrentWork)
        {
            var next = _graph.GetReadyItems().FirstOrDefault();
            if (next is null)
                break;

            var slot = FindAvailableSlotLocked();
            if (slot is null)
                break;

            var inboundType = string.IsNullOrWhiteSpace(next.ResumeInputType)
                ? (string.IsNullOrWhiteSpace(next.SessionId) ? "WORK_ITEM" : "WORK_ITEM_RESUME")
                : next.ResumeInputType!;
            var inboundBody = string.IsNullOrWhiteSpace(next.ResumeBody)
                ? next.Goal
                : next.ResumeBody!;

            if (!_graph.TryMarkRunning(next.Id))
                continue;

            var runningSnapshot = _graph.Find(next.Id)
                ?? throw new InvalidOperationException("RUNNING으로 전환한 WorkItem을 찾을 수 없습니다.");
            var dependencyResults = runningSnapshot.Dependencies
                .Select(id => _graph.Find(id))
                .Where(item => item is not null)
                .Select(item => new WorkItemDependencyResult(item!.Id, item.ResultRef, item.ResultSummary))
                .ToArray();
            var itemCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            var running = new RunningWork(next.Id, slot.Value, itemCancellation);
            _running.Add(next.Id, running);
            running.Task = ExecuteOneAsync(runningSnapshot, dependencyResults, inboundType, inboundBody, slot.Value, itemCancellation.Token);
        }
    }

    private int? FindAvailableSlotLocked()
    {
        var occupied = _running.Values.Select(value => value.Slot).ToHashSet();
        for (var slot = 1; slot <= _graph.MaxConcurrentWork; slot++)
        {
            if (!occupied.Contains(slot))
                return slot;
        }

        return null;
    }

    private async Task ExecuteOneAsync(
        WorkItemSnapshot item,
        IReadOnlyList<WorkItemDependencyResult> dependencies,
        string inboundType,
        string inboundBody,
        int slot,
        CancellationToken cancellationToken)
    {
        WorkItemExecutionResult? result = null;
        Exception? exception = null;
        var canceled = false;

        try
        {
            result = await _executor.ExecuteAsync(
                new WorkItemExecutionRequest(item, slot, dependencies, inboundType, inboundBody),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        ParallelWorkSchedulerSnapshot snapshot;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!_running.Remove(item.Id, out var running))
                return;

            running.Cancellation.Dispose();

            var current = _graph.Find(item.Id);
            if (current?.State == WorkItemState.Canceled || canceled)
            {
                if (current?.State != WorkItemState.Canceled)
                    _graph.TryMarkCanceled(item.Id, "WORK_EXECUTION_CANCELED");
            }
            else if (exception is not null)
            {
                _graph.TryMarkFailed(
                    item.Id,
                    "WORK_EXECUTOR_EXCEPTION",
                    $"{exception.GetType().Name}: {exception.Message}");
            }
            else if (result is null)
            {
                _graph.TryMarkFailed(item.Id, "WORK_EXECUTOR_NO_RESULT");
            }
            else
            {
                _graph.TryUpdateExecutionContext(item.Id, result.Branch, result.WorktreePath, result.SessionId);

                switch (result.Outcome)
                {
                    case WorkItemExecutionOutcome.Completed:
                        _graph.TryMarkCompleted(item.Id, result.ResultRef, result.ResultSummary);
                        break;
                    case WorkItemExecutionOutcome.Blocked:
                        _graph.TryMarkBlocked(
                            item.Id,
                            string.IsNullOrWhiteSpace(result.BlockCode)
                                ? "WORK_EXECUTOR_BLOCKED"
                                : result.BlockCode,
                            result.ResultSummary,
                            result.ResultRef);
                        break;
                    default:
                        _graph.TryMarkFailed(
                            item.Id,
                            string.IsNullOrWhiteSpace(result.FailureCode)
                                ? "WORK_EXECUTOR_FAILED"
                                : result.FailureCode,
                            result.ResultSummary);
                        break;
                }
            }

            LaunchReadyLocked();
            UpdateQuiescenceLocked();
            snapshot = CreateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }

        StateChanged?.Invoke(snapshot);
    }

    private void CancelGraphCanceledRunningItemsLocked()
    {
        foreach (var running in _running.Values)
        {
            if (_graph.Find(running.WorkItemId)?.State == WorkItemState.Canceled)
                running.Cancellation.Cancel();
        }
    }

    private void CancelRemainingLocked(string reason)
    {
        foreach (var item in _graph.Items)
        {
            if (item.State is WorkItemState.Planned or WorkItemState.Ready or WorkItemState.Running or WorkItemState.Blocked)
                _graph.TryMarkCanceled(item.Id, reason);
        }
    }

    private void UpdateQuiescenceLocked()
    {
        var hasRunnableOrRunning =
            _running.Count > 0 ||
            (_started && !_launchPaused && _graph.GetReadyItems().Count > 0);

        if (hasRunnableOrRunning)
        {
            if (_quiescent.Task.IsCompleted)
                _quiescent = NewSignal();
        }
        else
        {
            _quiescent.TrySetResult(true);
        }
    }

    private ParallelWorkSchedulerSnapshot CreateSnapshotLocked()
        => new(
            _graph.Snapshot(),
            _running.Values
                .OrderBy(value => value.Slot)
                .Select(value => new RunningWorkItemSnapshot(value.WorkItemId, value.Slot))
                .ToArray());

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        ParallelWorkSchedulerSnapshot? snapshot = null;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            _lifetimeCts.Cancel();
            CancelRemainingLocked("SCHEDULER_DISPOSED");

            foreach (var running in _running.Values)
                running.Cancellation.Cancel();

            snapshot = CreateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }

        if (snapshot is not null)
            StateChanged?.Invoke(snapshot);

        Task[] runningTasks;
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            runningTasks = _running.Values
                .Select(value => value.Task)
                .Where(task => task is not null)
                .Cast<Task>()
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }

        if (runningTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(runningTasks).ConfigureAwait(false);
            }
            catch
            {
                // ExecuteOneAsync가 executor 예외를 상태로 변환하므로 Dispose에서는 전파하지 않는다.
            }
        }

        _lifetimeCts.Dispose();
        _gate.Dispose();
    }

    private static TaskCompletionSource<bool> CompletedSignal()
    {
        var signal = NewSignal();
        signal.TrySetResult(true);
        return signal;
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RunningWork
    {
        public RunningWork(string workItemId, int slot, CancellationTokenSource cancellation)
        {
            WorkItemId = workItemId;
            Slot = slot;
            Cancellation = cancellation;
        }

        public string WorkItemId { get; }
        public int Slot { get; }
        public CancellationTokenSource Cancellation { get; }
        public Task? Task { get; set; }
    }
}
