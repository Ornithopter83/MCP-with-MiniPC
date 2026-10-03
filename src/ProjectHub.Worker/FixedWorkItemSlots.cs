namespace ProjectHub.Worker;

public static class FixedWorkItemSlots
{
    public const string Resource = "0";
    public const string BuildPublish = "9";

    public static bool IsReusable(string? workItemId)
        => workItemId is Resource or BuildPublish;

    public static bool AllowsTargetWorkspaceWrite(string? workItemId)
        => false;

    public static string BuildExecutionKey(string workItemId, long createdOrder)
    {
        if (string.IsNullOrWhiteSpace(workItemId))
            throw new ArgumentException("WorkItem ID가 비어 있습니다.", nameof(workItemId));

        return IsReusable(workItemId)
            ? $"{workItemId}-run-{createdOrder}"
            : workItemId;
    }
}
