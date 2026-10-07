using System.IO;

namespace ProjectHub.Worker;

internal sealed class ResourceTaskLifecycle
{
    private sealed class TaskState
    {
        public TaskState(
            string taskId,
            string workingDirectory)
        {
            TaskId = taskId;
            WorkingDirectory = Path.GetFullPath(workingDirectory);
        }

        public string TaskId { get; }
        public string WorkingDirectory { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public SemaphoreSlim SerialGate { get; } = new(1, 1);
        public int ActiveCount { get; set; }
        public TaskCompletionSource<bool> Idle { get; set; } =
            CompletedIdle();
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, TaskState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<T> RunAsync<T>(
        string taskId,
        string workingDirectory,
        Func<CancellationToken, Task<T>> operation)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException(
                "RESOURCE task ID가 비어 있습니다.",
                nameof(taskId));
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new ArgumentException(
                "RESOURCE 작업 폴더가 비어 있습니다.",
                nameof(workingDirectory));
        ArgumentNullException.ThrowIfNull(operation);

        TaskState state;
        lock (_gate)
        {
            if (!_states.TryGetValue(taskId, out var existing))
            {
                state = new TaskState(taskId, workingDirectory);
                _states.Add(taskId, state);
            }
            else
            {
                state = existing;
                if (!PathsEqual(
                        state.WorkingDirectory,
                        workingDirectory))
                {
                    throw new InvalidOperationException(
                        "RESOURCE_TASK_WORKSPACE_MISMATCH");
                }
            }

            if (state.Cancellation.IsCancellationRequested)
                throw new OperationCanceledException(
                    "RESOURCE_TASK_ALREADY_CANCELED");

            if (state.ActiveCount == 0)
            {
                state.Idle = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            state.ActiveCount++;
        }

        return RunTrackedAsync(state, operation);
    }

    public async Task CancelAllAsync()
    {
        TaskState[] states;
        lock (_gate)
            states = _states.Values.ToArray();

        foreach (var state in states)
        {
            try
            {
                state.Cancellation.Cancel();
            }
            catch
            {
                // 새 작업 정리는 각 RESOURCE가 terminal이 되는 것을 기다리는 것이 목적이다.
            }
        }

        await Task.WhenAll(
            states.Select(state => state.Idle.Task));

        lock (_gate)
        {
            foreach (var state in states)
            {
                if (_states.TryGetValue(
                        state.TaskId,
                        out var current) &&
                    ReferenceEquals(current, state))
                {
                    _states.Remove(state.TaskId);
                }

                state.Cancellation.Dispose();
                state.SerialGate.Dispose();
            }
        }
    }

    private async Task<T> RunTrackedAsync<T>(
        TaskState state,
        Func<CancellationToken, Task<T>> operation)
    {
        var entered = false;
        try
        {
            await state.SerialGate.WaitAsync(
                state.Cancellation.Token);
            entered = true;

            return await operation(
                state.Cancellation.Token);
        }
        finally
        {
            if (entered)
                state.SerialGate.Release();

            lock (_gate)
            {
                state.ActiveCount =
                    Math.Max(0, state.ActiveCount - 1);
                if (state.ActiveCount == 0)
                    state.Idle.TrySetResult(true);
            }
        }
    }

    private static bool PathsEqual(
        string left,
        string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            Path.GetFullPath(left)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
            comparison);
    }

    private static TaskCompletionSource<bool> CompletedIdle()
    {
        var source = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.TrySetResult(true);
        return source;
    }
}
