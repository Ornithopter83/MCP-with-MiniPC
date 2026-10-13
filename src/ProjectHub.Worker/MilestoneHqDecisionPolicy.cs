namespace ProjectHub.Worker;

/// <summary>
/// Only concurrent duplicate-file writers qualify as scheduling conflicts.
/// Out-of-scope Unity changes, auto-generated settings and checkpoint
/// discrepancies are not Worker-invented HARD_STOP conditions.
/// </summary>
internal static class MilestoneHqDecisionPolicy
{
    internal static bool IsHardStop(string? detail) =>
        !string.IsNullOrWhiteSpace(detail) &&
        (detail.Contains("WORK_WRITE_PATH_OVERLAP", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("ASSET_MANAGER_WORK_PATH_OVERLAP", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("ASSET_MANAGER_RESOURCE_PATH_OVERLAP", StringComparison.OrdinalIgnoreCase));
}
