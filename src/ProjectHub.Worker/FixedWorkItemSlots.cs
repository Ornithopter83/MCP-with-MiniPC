namespace ProjectHub.Worker;

public static class FixedWorkItemSlots
{
    public const string Resource = "0";
    public const string ResourceMake = Resource;
    public const string ResourceProcessing = "1";
    public const string FileManager = "8";
    public const string BuildPublish = "9";

    public static bool IsReusable(string? workItemId)
        => workItemId is ResourceMake or ResourceProcessing or FileManager or BuildPublish;

    public static bool AllowsTargetWorkspaceWrite(string? workItemId)
        => string.Equals(workItemId, FileManager, StringComparison.Ordinal);

    public static string BuildExecutionKey(string workItemId, long createdOrder)
    {
        if (string.IsNullOrWhiteSpace(workItemId))
            throw new ArgumentException("WorkItem ID가 비어 있습니다.", nameof(workItemId));

        return IsReusable(workItemId)
            ? $"{workItemId}-run-{createdOrder}"
            : workItemId;
    }
}
