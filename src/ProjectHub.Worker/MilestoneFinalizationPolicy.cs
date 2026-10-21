namespace ProjectHub.Worker;

/// <summary>
/// Publishing a milestone is meaningful only after implementation and
/// required checks have finished. This does not interpret the AI's intent.
/// </summary>
internal static class MilestoneFinalizationPolicy
{
    internal static bool ShouldWriteInitialPlan(
        bool readOnlyNoFileChanges, string? planDocument)
        => !readOnlyNoFileChanges && !string.IsNullOrWhiteSpace(planDocument);

    internal static string ResolveGitOutcome(
        string existingOutcome, bool readOnlyNoFileChanges, bool gitSucceeded)
        => !readOnlyNoFileChanges &&
           string.Equals(existingOutcome, "COMPLETED", StringComparison.Ordinal) &&
           !gitSucceeded ? "GIT_FINALIZE_FAILED" : existingOutcome;

    internal static bool ShouldPublish(
        int blockedWorkCount, int pendingWorkCount,
        bool buildFailed, string outcome)
        => blockedWorkCount == 0 && pendingWorkCount == 0 &&
           !buildFailed && string.Equals(outcome, "COMPLETED",
               StringComparison.Ordinal);
}
