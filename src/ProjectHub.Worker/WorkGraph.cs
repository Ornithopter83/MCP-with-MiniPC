using System.Collections.ObjectModel;

namespace ProjectHub.Worker;

public enum WorkItemKind
{
    Normal,
    Integration
}

public enum WorkItemState
{
    Planned,
    Ready,
    Running,
    Completed,
    Failed,
    Blocked,
    Canceled
}

public sealed record WorkItemSpec(
    string Id,
    string Goal,
    IReadOnlyList<string>? Dependencies = null,
    WorkItemKind Kind = WorkItemKind.Normal,
    string? BaseRef = null);

public sealed record WorkItemSnapshot(
    string Id,
    string Goal,
    IReadOnlyList<string> Dependencies,
    WorkItemKind Kind,
    WorkItemState State,
    long CreatedOrder,
    string? BaseRef,
    string? Branch,
    string? WorktreePath,
    string? SessionId,
    string? ResultRef,
    string? ResultSummary,
    string? FailureCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc);

public sealed record WorkGraphSnapshot(
    string JobId,
    long Revision,
    int MaxConcurrentWork,
    IReadOnlyList<WorkItemSnapshot> Items);

public sealed class WorkGraph
{
    public const int MinimumConcurrency = 1;
    public const int MaximumConcurrency = 8;

    private readonly Dictionary<string, WorkItemEntry> _items = new(StringComparer.Ordinal);
    private long _nextCreatedOrder;

    public WorkGraph(string jobId, int maxConcurrentWork = 1)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID가 비어 있습니다.", nameof(jobId));
        ValidateConcurrency(maxConcurrentWork);

        JobId = jobId.Trim();
        MaxConcurrentWork = maxConcurrentWork;
    }

    public string JobId { get; }

    public long Revision { get; private set; }

    public int MaxConcurrentWork { get; private set; }

    public int Count => _items.Count;

    public IReadOnlyList<WorkItemSnapshot> Items
        => _items.Values
            .OrderBy(item => item.CreatedOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(ToSnapshot)
            .ToArray();

    public WorkGraphSnapshot Snapshot()
        => new(JobId, Revision, MaxConcurrentWork, Items);

    public WorkItemSnapshot? Find(string id)
        => _items.TryGetValue(id, out var item) ? ToSnapshot(item) : null;

    public IReadOnlyList<WorkItemSnapshot> GetReadyItems()
        => _items.Values
            .Where(item => item.State == WorkItemState.Ready)
            .OrderBy(item => item.CreatedOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(ToSnapshot)
            .ToArray();

    public WorkGraphPatchResult ApplyPatch(WorkGraphPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        if (patch.ExpectedRevision != Revision)
            return WorkGraphPatchResult.Fail("WORK_GRAPH_REVISION_MISMATCH", Revision);

        if (patch.Operations is null || patch.Operations.Count == 0)
            return WorkGraphPatchResult.Fail("WORK_GRAPH_PATCH_EMPTY", Revision);

        var staged = _items.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal);
        var stagedNextCreatedOrder = _nextCreatedOrder;
        var stagedConcurrency = MaxConcurrentWork;

        foreach (var operation in patch.Operations)
        {
            var error = ApplyOperation(staged, ref stagedNextCreatedOrder, ref stagedConcurrency, operation);
            if (error is not null)
                return WorkGraphPatchResult.Fail(error, Revision);
        }

        var validationError = ValidateGraph(staged);
        if (validationError is not null)
            return WorkGraphPatchResult.Fail(validationError, Revision);

        _items.Clear();
        foreach (var pair in staged)
            _items[pair.Key] = pair.Value;

        _nextCreatedOrder = stagedNextCreatedOrder;
        MaxConcurrentWork = stagedConcurrency;
        Revision++;
        RecalculateStates();

        return WorkGraphPatchResult.Ok(Revision);
    }

    public bool TryMarkRunning(string id, string? branch = null, string? worktreePath = null, string? sessionId = null)
    {
        if (!_items.TryGetValue(id, out var item) || item.State != WorkItemState.Ready)
            return false;

        item.State = WorkItemState.Running;
        item.Branch = NullIfWhiteSpace(branch);
        item.WorktreePath = NullIfWhiteSpace(worktreePath);
        item.SessionId = NullIfWhiteSpace(sessionId);
        item.StartedAtUtc = DateTimeOffset.UtcNow;
        item.FinishedAtUtc = null;
        return true;
    }

    public bool TryUpdateExecutionContext(string id, string? branch = null, string? worktreePath = null, string? sessionId = null)
    {
        if (!_items.TryGetValue(id, out var item) || item.State != WorkItemState.Running)
            return false;

        if (!string.IsNullOrWhiteSpace(branch))
            item.Branch = branch.Trim();
        if (!string.IsNullOrWhiteSpace(worktreePath))
            item.WorktreePath = worktreePath.Trim();
        if (!string.IsNullOrWhiteSpace(sessionId))
            item.SessionId = sessionId.Trim();
        return true;
    }

    public bool TryMarkCompleted(string id, string? resultRef = null, string? resultSummary = null)
    {
        if (!_items.TryGetValue(id, out var item) || item.State != WorkItemState.Running)
            return false;

        item.State = WorkItemState.Completed;
        item.ResultRef = NullIfWhiteSpace(resultRef);
        item.ResultSummary = NullIfWhiteSpace(resultSummary);
        item.FailureCode = null;
        item.FinishedAtUtc = DateTimeOffset.UtcNow;
        RecalculateStates();
        return true;
    }

    public bool TryMarkFailed(string id, string failureCode, string? resultSummary = null)
    {
        if (!_items.TryGetValue(id, out var item) || item.State != WorkItemState.Running)
            return false;
        if (string.IsNullOrWhiteSpace(failureCode))
            throw new ArgumentException("실패 코드는 비어 있을 수 없습니다.", nameof(failureCode));

        item.State = WorkItemState.Failed;
        item.FailureCode = failureCode.Trim();
        item.ResultSummary = NullIfWhiteSpace(resultSummary);
        item.FinishedAtUtc = DateTimeOffset.UtcNow;
        RecalculateStates();
        return true;
    }

    private static string? ApplyOperation(
        Dictionary<string, WorkItemEntry> items,
        ref long nextCreatedOrder,
        ref int maxConcurrentWork,
        WorkGraphPatchOperation operation)
    {
        if (operation is null)
            return "WORK_GRAPH_OPERATION_INVALID";

        var id = operation.WorkItemId?.Trim() ?? string.Empty;

        switch (operation.Type)
        {
            case WorkGraphPatchOperationType.Add:
            {
                if (operation.Item is null)
                    return "WORK_GRAPH_ADD_ITEM_MISSING";
                if (!IsSafeId(operation.Item.Id))
                    return "WORK_GRAPH_ITEM_ID_INVALID";
                if (items.ContainsKey(operation.Item.Id))
                    return "WORK_GRAPH_ITEM_DUPLICATE";
                if (string.IsNullOrWhiteSpace(operation.Item.Goal))
                    return "WORK_GRAPH_GOAL_MISSING";

                var dependencies = NormalizeDependencies(operation.Item.Dependencies);
                items[operation.Item.Id] = new WorkItemEntry
                {
                    Id = operation.Item.Id,
                    Goal = operation.Item.Goal.Trim(),
                    Dependencies = dependencies,
                    Kind = operation.Item.Kind,
                    State = WorkItemState.Planned,
                    CreatedOrder = nextCreatedOrder++,
                    BaseRef = NullIfWhiteSpace(operation.Item.BaseRef),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                return null;
            }

            case WorkGraphPatchOperationType.Cancel:
            {
                if (!items.TryGetValue(id, out var item))
                    return "WORK_GRAPH_ITEM_NOT_FOUND";
                if (item.State == WorkItemState.Completed)
                    return "WORK_GRAPH_COMPLETED_ITEM_IMMUTABLE";
                item.State = WorkItemState.Canceled;
                item.FinishedAtUtc ??= DateTimeOffset.UtcNow;
                return null;
            }

            case WorkGraphPatchOperationType.SetDependencies:
            {
                if (!items.TryGetValue(id, out var item))
                    return "WORK_GRAPH_ITEM_NOT_FOUND";
                if (!CanEditDefinition(item.State))
                    return "WORK_GRAPH_RUNNING_OR_TERMINAL_ITEM_IMMUTABLE";
                item.Dependencies = NormalizeDependencies(operation.Dependencies);
                return null;
            }

            case WorkGraphPatchOperationType.SetGoal:
            {
                if (!items.TryGetValue(id, out var item))
                    return "WORK_GRAPH_ITEM_NOT_FOUND";
                if (!CanEditDefinition(item.State))
                    return "WORK_GRAPH_RUNNING_OR_TERMINAL_ITEM_IMMUTABLE";
                if (string.IsNullOrWhiteSpace(operation.Value))
                    return "WORK_GRAPH_GOAL_MISSING";
                item.Goal = operation.Value.Trim();
                return null;
            }

            case WorkGraphPatchOperationType.SetBaseRef:
            {
                if (!items.TryGetValue(id, out var item))
                    return "WORK_GRAPH_ITEM_NOT_FOUND";
                if (!CanEditDefinition(item.State))
                    return "WORK_GRAPH_RUNNING_OR_TERMINAL_ITEM_IMMUTABLE";
                item.BaseRef = NullIfWhiteSpace(operation.Value);
                return null;
            }

            case WorkGraphPatchOperationType.SetMaxConcurrency:
            {
                if (operation.IntegerValue is null ||
                    operation.IntegerValue < MinimumConcurrency ||
                    operation.IntegerValue > MaximumConcurrency)
                    return "WORK_GRAPH_CONCURRENCY_INVALID";
                maxConcurrentWork = operation.IntegerValue.Value;
                return null;
            }

            default:
                return "WORK_GRAPH_OPERATION_UNSUPPORTED";
        }
    }

    private static string? ValidateGraph(Dictionary<string, WorkItemEntry> items)
    {
        foreach (var item in items.Values)
        {
            foreach (var dependency in item.Dependencies)
            {
                if (string.Equals(item.Id, dependency, StringComparison.Ordinal))
                    return "WORK_GRAPH_SELF_DEPENDENCY";
                if (!items.ContainsKey(dependency))
                    return "WORK_GRAPH_DEPENDENCY_NOT_FOUND";
            }
        }

        var marks = new Dictionary<string, VisitMark>(StringComparer.Ordinal);
        foreach (var id in items.Keys)
        {
            if (HasCycle(id, items, marks))
                return "WORK_GRAPH_CYCLE_DETECTED";
        }

        return null;
    }

    private static bool HasCycle(
        string id,
        IReadOnlyDictionary<string, WorkItemEntry> items,
        IDictionary<string, VisitMark> marks)
    {
        if (marks.TryGetValue(id, out var mark))
            return mark == VisitMark.Visiting;

        marks[id] = VisitMark.Visiting;
        foreach (var dependency in items[id].Dependencies)
        {
            if (HasCycle(dependency, items, marks))
                return true;
        }

        marks[id] = VisitMark.Visited;
        return false;
    }

    private void RecalculateStates()
    {
        foreach (var item in _items.Values.OrderBy(item => item.CreatedOrder))
        {
            if (item.State is WorkItemState.Running
                or WorkItemState.Completed
                or WorkItemState.Failed
                or WorkItemState.Canceled)
                continue;

            item.State = item.Dependencies.All(dependency =>
                    _items.TryGetValue(dependency, out var dependencyItem) &&
                    dependencyItem.State == WorkItemState.Completed)
                ? WorkItemState.Ready
                : WorkItemState.Blocked;
        }
    }

    private static bool CanEditDefinition(WorkItemState state)
        => state is WorkItemState.Planned or WorkItemState.Ready or WorkItemState.Blocked;

    private static List<string> NormalizeDependencies(IReadOnlyList<string>? dependencies)
        => (dependencies ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static bool IsSafeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96)
            return false;

        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
                return false;
        }

        return true;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static WorkItemSnapshot ToSnapshot(WorkItemEntry item)
        => new(
            item.Id,
            item.Goal,
            new ReadOnlyCollection<string>(item.Dependencies.ToArray()),
            item.Kind,
            item.State,
            item.CreatedOrder,
            item.BaseRef,
            item.Branch,
            item.WorktreePath,
            item.SessionId,
            item.ResultRef,
            item.ResultSummary,
            item.FailureCode,
            item.CreatedAtUtc,
            item.StartedAtUtc,
            item.FinishedAtUtc);

    private static void ValidateConcurrency(int value)
    {
        if (value is < MinimumConcurrency or > MaximumConcurrency)
            throw new ArgumentOutOfRangeException(nameof(value), $"동시 WORK 수는 {MinimumConcurrency}~{MaximumConcurrency} 범위여야 합니다.");
    }

    private enum VisitMark
    {
        Visiting,
        Visited
    }

    private sealed class WorkItemEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Goal { get; set; } = string.Empty;
        public List<string> Dependencies { get; set; } = new();
        public WorkItemKind Kind { get; set; }
        public WorkItemState State { get; set; }
        public long CreatedOrder { get; set; }
        public string? BaseRef { get; set; }
        public string? Branch { get; set; }
        public string? WorktreePath { get; set; }
        public string? SessionId { get; set; }
        public string? ResultRef { get; set; }
        public string? ResultSummary { get; set; }
        public string? FailureCode { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset? StartedAtUtc { get; set; }
        public DateTimeOffset? FinishedAtUtc { get; set; }

        public WorkItemEntry Clone()
            => new()
            {
                Id = Id,
                Goal = Goal,
                Dependencies = Dependencies.ToList(),
                Kind = Kind,
                State = State,
                CreatedOrder = CreatedOrder,
                BaseRef = BaseRef,
                Branch = Branch,
                WorktreePath = WorktreePath,
                SessionId = SessionId,
                ResultRef = ResultRef,
                ResultSummary = ResultSummary,
                FailureCode = FailureCode,
                CreatedAtUtc = CreatedAtUtc,
                StartedAtUtc = StartedAtUtc,
                FinishedAtUtc = FinishedAtUtc
            };
    }
}
