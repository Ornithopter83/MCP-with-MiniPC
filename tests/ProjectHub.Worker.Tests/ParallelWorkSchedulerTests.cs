using System.Collections.Concurrent;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkSchedulerTests
{
    [Fact]
    public async Task MaxFour_StartsFourAndFifthWaitsUntilSlotIsReleased()
    {
        var graph = CreateGraph(4, "W1", "W2", "W3", "W4", "W5");
        var executor = new ControlledExecutor();

        await using var scheduler = new ParallelWorkScheduler(graph, executor);
        await scheduler.StartAsync();

        await Task.WhenAll(
            executor.WhenStarted("W1"),
            executor.WhenStarted("W2"),
            executor.WhenStarted("W3"),
            executor.WhenStarted("W4"));

        Assert.False(executor.IsStarted("W5"));
        var first = await scheduler.GetSnapshotAsync();
        Assert.Equal(4, first.RunningCount);
        Assert.Equal(1, first.ReadyCount);

        executor.Complete("W1");
        await executor.WhenStarted("W5");

        Assert.Equal(4, (await scheduler.GetSnapshotAsync()).RunningCount);

        executor.Complete("W2");
        executor.Complete("W3");
        executor.Complete("W4");
        executor.Complete("W5");
        await scheduler.WaitForQuiescenceAsync();

        var final = await scheduler.GetSnapshotAsync();
        Assert.Equal(0, final.RunningCount);
        Assert.Equal(5, final.CompletedCount);
        Assert.Equal(4, executor.MaxObservedConcurrency);
    }

    [Fact]
    public async Task FailedWorkDoesNotPreventIndependentReadyWorkFromCompleting()
    {
        var graph = new WorkGraph("job", 2);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "실패 작업")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("B", "A 의존", new[] { "A" })),
            WorkGraphPatchOperation.Add(new WorkItemSpec("C", "독립 작업"))
        })).Success);

        var executor = new ControlledExecutor();
        executor.Fail("A", "EXPECTED_FAILURE");

        await using var scheduler = new ParallelWorkScheduler(graph, executor);
        await scheduler.StartAsync();

        await Task.WhenAll(executor.WhenStarted("A"), executor.WhenStarted("C"));
        executor.Complete("A");
        executor.Complete("C");
        await scheduler.WaitForQuiescenceAsync();

        var snapshot = await scheduler.GetSnapshotAsync();
        Assert.Equal(WorkItemState.Failed, snapshot.Graph.Items.Single(item => item.Id == "A").State);
        Assert.Equal(WorkItemState.Blocked, snapshot.Graph.Items.Single(item => item.Id == "B").State);
        Assert.Equal(WorkItemState.Completed, snapshot.Graph.Items.Single(item => item.Id == "C").State);
        Assert.False(executor.IsStarted("B"));
    }

    [Fact]
    public async Task IncreasingConcurrencyStartsAdditionalReadyItemsWithoutRestart()
    {
        var graph = CreateGraph(1, "W1", "W2", "W3");
        var executor = new ControlledExecutor();

        await using var scheduler = new ParallelWorkScheduler(graph, executor);
        await scheduler.StartAsync();
        await executor.WhenStarted("W1");

        Assert.False(executor.IsStarted("W2"));
        Assert.False(executor.IsStarted("W3"));

        var patch = await scheduler.ApplyPatchAsync(new WorkGraphPatch(
            graph.Revision,
            new[] { WorkGraphPatchOperation.SetMaxConcurrency(3) }));

        Assert.True(patch.Success);
        await Task.WhenAll(executor.WhenStarted("W2"), executor.WhenStarted("W3"));
        Assert.Equal(3, (await scheduler.GetSnapshotAsync()).RunningCount);

        executor.Complete("W1");
        executor.Complete("W2");
        executor.Complete("W3");
        await scheduler.WaitForQuiescenceAsync();
    }

    [Fact]
    public async Task CancelPatchCancelsOnlyTheTargetRunningItem()
    {
        var graph = CreateGraph(2, "W1", "W2");
        var executor = new ControlledExecutor();

        await using var scheduler = new ParallelWorkScheduler(graph, executor);
        await scheduler.StartAsync();
        await Task.WhenAll(executor.WhenStarted("W1"), executor.WhenStarted("W2"));

        var patch = await scheduler.ApplyPatchAsync(new WorkGraphPatch(
            graph.Revision,
            new[] { WorkGraphPatchOperation.Cancel("W1") }));

        Assert.True(patch.Success);
        await executor.WhenCanceled("W1");

        executor.Complete("W2");
        await scheduler.WaitForQuiescenceAsync();

        var snapshot = await scheduler.GetSnapshotAsync();
        Assert.Equal(WorkItemState.Canceled, snapshot.Graph.Items.Single(item => item.Id == "W1").State);
        Assert.Equal(WorkItemState.Completed, snapshot.Graph.Items.Single(item => item.Id == "W2").State);
    }

    [Fact]
    public async Task LifetimeCancellationCancelsRunningAndQueuedWork()
    {
        using var lifetime = new CancellationTokenSource();
        var graph = CreateGraph(1, "W1", "W2");
        var executor = new ControlledExecutor();

        await using var scheduler = new ParallelWorkScheduler(graph, executor, lifetime.Token);
        await scheduler.StartAsync();
        await executor.WhenStarted("W1");

        lifetime.Cancel();
        await executor.WhenCanceled("W1");
        await scheduler.WaitForQuiescenceAsync();

        var snapshot = await scheduler.GetSnapshotAsync();
        Assert.Equal(WorkItemState.Canceled, snapshot.Graph.Items.Single(item => item.Id == "W1").State);
        Assert.Equal(WorkItemState.Canceled, snapshot.Graph.Items.Single(item => item.Id == "W2").State);
        Assert.False(executor.IsStarted("W2"));
    }

    [Fact]
    public async Task SchedulerUsesStableCreationOrderWhenOnlyOneSlotExists()
    {
        var graph = CreateGraph(1, "B", "A", "C");
        var executor = new ControlledExecutor();

        await using var scheduler = new ParallelWorkScheduler(graph, executor);
        await scheduler.StartAsync();

        await executor.WhenStarted("B");
        Assert.Equal(new[] { "B" }, executor.StartOrder);

        executor.Complete("B");
        await executor.WhenStarted("A");
        executor.Complete("A");
        await executor.WhenStarted("C");
        executor.Complete("C");

        await scheduler.WaitForQuiescenceAsync();
        Assert.Equal(new[] { "B", "A", "C" }, executor.StartOrder);
    }

    private static WorkGraph CreateGraph(int maxConcurrency, params string[] ids)
    {
        var graph = new WorkGraph("job", maxConcurrency);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(
            0,
            ids.Select(id => WorkGraphPatchOperation.Add(new WorkItemSpec(id, id))).ToArray())).Success);
        return graph;
    }

    private sealed class ControlledExecutor : IWorkItemExecutor
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _started = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _release = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _canceled = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.Ordinal);
        private readonly object _orderGate = new();
        private readonly List<string> _startOrder = new();
        private int _active;
        private int _maxObserved;

        public IReadOnlyList<string> StartOrder
        {
            get
            {
                lock (_orderGate)
                    return _startOrder.ToArray();
            }
        }

        public int MaxObservedConcurrency => Volatile.Read(ref _maxObserved);

        public bool IsStarted(string id)
            => _started.TryGetValue(id, out var signal) && signal.Task.IsCompleted;

        public Task WhenStarted(string id)
            => StartedSignal(id).Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task WhenCanceled(string id)
            => CanceledSignal(id).Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Complete(string id)
            => ReleaseSignal(id).TrySetResult(true);

        public void Fail(string id, string failureCode)
            => _failures[id] = failureCode;

        public async Task<WorkItemExecutionResult> ExecuteAsync(
            WorkItemExecutionRequest request,
            CancellationToken cancellationToken)
        {
            lock (_orderGate)
                _startOrder.Add(request.Item.Id);

            var active = Interlocked.Increment(ref _active);
            UpdateMax(active);
            StartedSignal(request.Item.Id).TrySetResult(true);

            try
            {
                await ReleaseSignal(request.Item.Id).Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CanceledSignal(request.Item.Id).TrySetResult(true);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }

            return _failures.TryGetValue(request.Item.Id, out var failure)
                ? WorkItemExecutionResult.Failed(failure)
                : WorkItemExecutionResult.Completed("ref-" + request.Item.Id);
        }

        private void UpdateMax(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxObserved);
                if (active <= current)
                    return;
                if (Interlocked.CompareExchange(ref _maxObserved, active, current) == current)
                    return;
            }
        }

        private TaskCompletionSource<bool> StartedSignal(string id)
            => _started.GetOrAdd(id, _ => NewSignal());

        private TaskCompletionSource<bool> ReleaseSignal(string id)
            => _release.GetOrAdd(id, _ => NewSignal());

        private TaskCompletionSource<bool> CanceledSignal(string id)
            => _canceled.GetOrAdd(id, _ => NewSignal());

        private static TaskCompletionSource<bool> NewSignal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
