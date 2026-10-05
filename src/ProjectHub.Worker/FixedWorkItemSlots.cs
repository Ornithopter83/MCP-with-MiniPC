namespace ProjectHub.Worker;

public static class FixedWorkItemSlots
{
    public const string Resource = "0";
    public const string ResourceMake = Resource;
    public const string ResourceProcessing = "1";
    public const string FileManager = "8";
    public const string BuildPublish = "9";
    public const string ReservedNumberWarning =
        "예약된 작업 번호이므로 다른 작업 번호를 사용해주세요";

    public static bool IsReusable(string? workItemId)
        => workItemId is ResourceMake or ResourceProcessing or FileManager or BuildPublish;

    public static bool IsReservedNumber(string? workItemId)
        => TryParseNumericId(workItemId, out var number) && number is >= 0 and <= 9;

    public static bool IsUnassignedReservedNumber(string? workItemId)
        => TryParseNumericId(workItemId, out var number) && number is >= 2 and <= 7;

    public static bool IsGeneralWorkNumber(string? workItemId)
        => TryParseNumericId(workItemId, out var number) && number >= 10;

    public static string? GetMechanicalSlotLabel(string? workItemId)
        => workItemId switch
        {
            ResourceMake => "RESOURCE_MAKE",
            ResourceProcessing => "RESOURCE_PROCESSING",
            FileManager => "FILE_MANAGER",
            BuildPublish => "BUILD_PUBLISH",
            _ when IsUnassignedReservedNumber(workItemId) => "RESERVED",
            _ when IsGeneralWorkNumber(workItemId) => "GENERAL",
            _ => null
        };

    public static bool AllowsTargetWorkspaceWrite(string? workItemId)
        => string.Equals(workItemId, FileManager, StringComparison.Ordinal);

    public static bool AllowsBuildExecution(string? workItemId)
        => string.Equals(workItemId, BuildPublish, StringComparison.Ordinal);

    public static string BuildExecutionKey(string workItemId, long createdOrder)
    {
        if (string.IsNullOrWhiteSpace(workItemId))
            throw new ArgumentException("WorkItem ID가 비어 있습니다.", nameof(workItemId));

        return IsReusable(workItemId)
            ? $"{workItemId}-run-{createdOrder}"
            : workItemId;
    }

    private static bool TryParseNumericId(string? workItemId, out int number)
        => int.TryParse(
            workItemId?.Trim(),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out number);
}
