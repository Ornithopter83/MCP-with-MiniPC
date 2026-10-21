namespace ProjectHub.Worker;

/// <summary>
/// Milestone graph nodes reflect actual role reports and lifecycle state.
/// An aggregate parent report is not sufficient to mark any child completed.
/// </summary>
internal static class MilestoneGraphStatePolicy
{
    internal static string RoleState(string? reportStatus) =>
        reportStatus?.ToLowerInvariant() switch
        {
            "completed" => "COMPLETED",
            "blocked" => "BLOCKED",
            "in_progress" => "IN_PROGRESS",
            _ => "UNVERIFIED"
        };

    internal static string GitState(string milestoneState) =>
        milestoneState switch
        {
            "FINALIZED" => "COMPLETED",
            "FINALIZE_FAILED" => "FAILED",
            "SKIPPED_READ_ONLY" => "SKIPPED",
            "SKIPPED_BLOCKED" => "BLOCKED",
            _ => "PLANNED"
        };
}
