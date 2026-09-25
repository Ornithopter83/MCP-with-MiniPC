namespace ProjectHub.Worker;

public enum MechanicalWorkCompletionMode
{
    FinalizeOnly,
    WorkResultRequired
}

public sealed record MechanicalWorkRegistration(
    string Id,
    string Kind,
    MechanicalWorkCompletionMode CompletionMode,
    DateTimeOffset RegisteredAt,
    string? Description = null,
    string? OwnerId = null);

public sealed record MechanicalWorkCompletion(
    string Id,
    string Kind,
    MechanicalWorkCompletionMode CompletionMode,
    bool Success,
    string Message,
    string? ErrorCode,
    IReadOnlyList<string> ResultPaths,
    DateTimeOffset RegisteredAt,
    DateTimeOffset FinishedAt,
    int? ExitCode = null,
    string? OwnerId = null);

public sealed record MechanicalWorkRegistryState(
    int OutstandingCount,
    int WorkResultRequiredCount);

/// <summary>
/// Worker-owned registry for asynchronous mechanical work.
/// It tracks lifecycle and completion delivery only; it never interprets whether a result is semantically good.
/// </summary>
public sealed class MechanicalWorkRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MechanicalWorkRegistration> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<MechanicalWorkCompletion>> _completions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskCompletionSource<bool>> _requiredOwnerIdle = new(StringComparer.Ordinal);
    private TaskCompletionSource<bool> _allIdle = CompletedSource();
    private TaskCompletionSource<bool> _requiredIdle = CompletedSource();

    public event Action<MechanicalWorkRegistryState>? StateChanged;
    public event Action<MechanicalWorkCompletion>? CompletionAvailable;

    public int OutstandingCount
    {
        get { lock (_gate) return _active.Count; }
    }

    public int WorkResultRequiredCount
    {
        get
        {
            lock (_gate)
                return _active.Values.Count(item => item.CompletionMode == MechanicalWorkCompletionMode.WorkResultRequired);
        }
    }

    public IReadOnlyList<MechanicalWorkRegistration> Active
    {
        get
        {
            lock (_gate)
                return _active.Values.OrderBy(item => item.RegisteredAt).ToArray();
        }
    }

    public MechanicalWorkRegistration Register(
        string id,
        string kind,
        MechanicalWorkCompletionMode completionMode,
        string? description = null,
        string? ownerId = null)
    {
        id = NormalizeRequired(id, "MECHANICAL_WORK_ID_MISSING");
        kind = NormalizeRequired(kind, "MECHANICAL_WORK_KIND_MISSING").ToUpperInvariant();
        ownerId = NormalizeOptional(ownerId);

        MechanicalWorkRegistration registration;
        MechanicalWorkRegistryState state;
        lock (_gate)
        {
            if (_active.ContainsKey(id))
                throw new InvalidOperationException("MECHANICAL_WORK_DUPLICATE_ID");

            if (_active.Count == 0)
                _allIdle = NewPendingSource();
            if (completionMode == MechanicalWorkCompletionMode.WorkResultRequired &&
                _active.Values.All(item => item.CompletionMode != MechanicalWorkCompletionMode.WorkResultRequired))
                _requiredIdle = NewPendingSource();

            if (completionMode == MechanicalWorkCompletionMode.WorkResultRequired &&
                ownerId is not null &&
                _active.Values.All(item =>
                    item.CompletionMode != MechanicalWorkCompletionMode.WorkResultRequired ||
                    !string.Equals(item.OwnerId, ownerId, StringComparison.Ordinal)))
                _requiredOwnerIdle[ownerId] = NewPendingSource();

            registration = new MechanicalWorkRegistration(
                id,
                kind,
                completionMode,
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                ownerId);
            _active.Add(id, registration);
            state = SnapshotLocked();
        }

        StateChanged?.Invoke(state);
        return registration;
    }

    public MechanicalWorkCompletion? Complete(
        string id,
        bool success,
        string message,
        string? errorCode = null,
        IReadOnlyList<string>? resultPaths = null,
        int? exitCode = null)
    {
        MechanicalWorkCompletion? completion;
        TaskCompletionSource<bool>? releaseAll = null;
        TaskCompletionSource<bool>? releaseRequired = null;
        TaskCompletionSource<bool>? releaseOwnerRequired = null;
        MechanicalWorkRegistryState state;

        lock (_gate)
        {
            if (!_active.Remove(id, out var registration))
                return null;

            completion = new MechanicalWorkCompletion(
                registration.Id,
                registration.Kind,
                registration.CompletionMode,
                success,
                message ?? string.Empty,
                string.IsNullOrWhiteSpace(errorCode) ? null : errorCode,
                resultPaths ?? Array.Empty<string>(),
                registration.RegisteredAt,
                DateTimeOffset.UtcNow,
                exitCode,
                registration.OwnerId);

            if (!_completions.TryGetValue(registration.Kind, out var queue))
            {
                queue = new Queue<MechanicalWorkCompletion>();
                _completions.Add(registration.Kind, queue);
            }
            queue.Enqueue(completion);

            if (_active.Count == 0)
                releaseAll = _allIdle;
            if (_active.Values.All(item => item.CompletionMode != MechanicalWorkCompletionMode.WorkResultRequired))
                releaseRequired = _requiredIdle;

            if (registration.CompletionMode == MechanicalWorkCompletionMode.WorkResultRequired &&
                registration.OwnerId is not null &&
                _active.Values.All(item =>
                    item.CompletionMode != MechanicalWorkCompletionMode.WorkResultRequired ||
                    !string.Equals(item.OwnerId, registration.OwnerId, StringComparison.Ordinal)) &&
                _requiredOwnerIdle.TryGetValue(registration.OwnerId, out var ownerIdle))
                releaseOwnerRequired = ownerIdle;

            state = SnapshotLocked();
        }

        releaseAll?.TrySetResult(true);
        releaseRequired?.TrySetResult(true);
        releaseOwnerRequired?.TrySetResult(true);
        CompletionAvailable?.Invoke(completion);
        StateChanged?.Invoke(state);
        return completion;
    }

    public IReadOnlyList<MechanicalWorkCompletion> DrainCompletions(
        string? kind = null,
        MechanicalWorkCompletionMode? completionMode = null,
        string? ownerId = null)
    {
        lock (_gate)
        {
            var result = new List<MechanicalWorkCompletion>();
            var keys = string.IsNullOrWhiteSpace(kind)
                ? _completions.Keys.ToArray()
                : new[] { kind.Trim().ToUpperInvariant() };

            foreach (var key in keys)
            {
                if (!_completions.TryGetValue(key, out var queue))
                    continue;

                var retained = new Queue<MechanicalWorkCompletion>();
                while (queue.Count > 0)
                {
                    var item = queue.Dequeue();
                    var modeMatches = completionMode is null || item.CompletionMode == completionMode.Value;
                    var ownerMatches = string.IsNullOrWhiteSpace(ownerId) ||
                        string.Equals(item.OwnerId, ownerId.Trim(), StringComparison.Ordinal);
                    if (modeMatches && ownerMatches)
                        result.Add(item);
                    else
                        retained.Enqueue(item);
                }

                if (retained.Count == 0)
                    _completions.Remove(key);
                else
                    _completions[key] = retained;
            }

            return result.OrderBy(item => item.FinishedAt).ToArray();
        }
    }

    public Task WaitForAllAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (_gate) task = _allIdle.Task;
        return task.WaitAsync(cancellationToken);
    }

    public Task WaitForWorkResultRequiredAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (_gate) task = _requiredIdle.Task;
        return task.WaitAsync(cancellationToken);
    }

    public int WorkResultRequiredCountForOwner(string ownerId)
    {
        ownerId = NormalizeRequired(ownerId, "MECHANICAL_WORK_OWNER_ID_MISSING");
        lock (_gate)
            return _active.Values.Count(item =>
                item.CompletionMode == MechanicalWorkCompletionMode.WorkResultRequired &&
                string.Equals(item.OwnerId, ownerId, StringComparison.Ordinal));
    }

    public Task WaitForWorkResultRequiredAsync(string ownerId, CancellationToken cancellationToken)
    {
        ownerId = NormalizeRequired(ownerId, "MECHANICAL_WORK_OWNER_ID_MISSING");
        Task task;
        lock (_gate)
        {
            if (_active.Values.All(item =>
                item.CompletionMode != MechanicalWorkCompletionMode.WorkResultRequired ||
                !string.Equals(item.OwnerId, ownerId, StringComparison.Ordinal)))
                return Task.CompletedTask;

            if (!_requiredOwnerIdle.TryGetValue(ownerId, out var source) || source.Task.IsCompleted)
            {
                source = NewPendingSource();
                _requiredOwnerIdle[ownerId] = source;
            }
            task = source.Task;
        }
        return task.WaitAsync(cancellationToken);
    }

    private MechanicalWorkRegistryState SnapshotLocked()
        => new(
            _active.Count,
            _active.Values.Count(item => item.CompletionMode == MechanicalWorkCompletionMode.WorkResultRequired));

    private static string NormalizeRequired(string? value, string error)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(error);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TaskCompletionSource<bool> NewPendingSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<bool> CompletedSource()
    {
        var source = NewPendingSource();
        source.TrySetResult(true);
        return source;
    }
}
