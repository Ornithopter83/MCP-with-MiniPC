namespace ProjectHub.Worker;

/// <summary>
/// Only actual unowned writes / unsafe scope or checkpoint identity may
/// require immediate user intervention. Ordinary environment and task
/// failures return evidence to HQ to choose a new plan or PAUSE.
/// </summary>
internal static class MilestoneHqDecisionPolicy
{
    internal static bool IsHardStop(string? detail) =>
        !string.IsNullOrWhiteSpace(detail) &&
        (detail.Contains("ASSET_MANAGER_SCOPE_VIOLATION", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("ASSET_MANAGER_CHECKPOINT_SCOPE_OR_MODEL_CHANGED", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("ASSET_MANAGER_ID_INVALID", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("ASSET_TARGET_OUTSIDE_ASSETS", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("ASSET_REPARSE_POINT", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("ASSET_MANAGER_USER_FILE_CONFLICT", StringComparison.OrdinalIgnoreCase));
}
