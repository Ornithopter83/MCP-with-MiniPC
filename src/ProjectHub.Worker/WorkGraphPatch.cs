namespace ProjectHub.Worker;

public enum WorkGraphPatchOperationType
{
    Add,
    Cancel,
    SetDependencies,
    SetGoal,
    SetBaseRef,
    SetMaxConcurrency
}

public sealed record WorkGraphPatch(
    long ExpectedRevision,
    IReadOnlyList<WorkGraphPatchOperation> Operations);

public sealed record WorkGraphPatchOperation(
    WorkGraphPatchOperationType Type,
    string WorkItemId,
    WorkItemSpec? Item = null,
    IReadOnlyList<string>? Dependencies = null,
    string? Value = null,
    int? IntegerValue = null)
{
    public static WorkGraphPatchOperation Add(WorkItemSpec item)
        => new(WorkGraphPatchOperationType.Add, item.Id, Item: item);

    public static WorkGraphPatchOperation Cancel(string workItemId)
        => new(WorkGraphPatchOperationType.Cancel, workItemId);

    public static WorkGraphPatchOperation SetDependencies(string workItemId, params string[] dependencies)
        => new(WorkGraphPatchOperationType.SetDependencies, workItemId, Dependencies: dependencies);

    public static WorkGraphPatchOperation SetGoal(string workItemId, string goal)
        => new(WorkGraphPatchOperationType.SetGoal, workItemId, Value: goal);

    public static WorkGraphPatchOperation SetBaseRef(string workItemId, string? baseRef)
        => new(WorkGraphPatchOperationType.SetBaseRef, workItemId, Value: baseRef);

    public static WorkGraphPatchOperation SetMaxConcurrency(int value)
        => new(WorkGraphPatchOperationType.SetMaxConcurrency, string.Empty, IntegerValue: value);
}

public sealed record WorkGraphPatchResult(bool Success, string? ErrorCode, long Revision)
{
    public static WorkGraphPatchResult Ok(long revision)
        => new(true, null, revision);

    public static WorkGraphPatchResult Fail(string errorCode, long revision)
        => new(false, errorCode, revision);
}
