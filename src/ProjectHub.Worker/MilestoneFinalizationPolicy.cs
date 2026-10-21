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

    internal static string ResolveGitGraphState(
        bool readOnlyNoFileChanges, int blockedWorkCount, int pendingWorkCount,
        bool buildFailed, string outcome, bool gitSucceeded)
    {
        if (readOnlyNoFileChanges)
            return "SKIPPED_READ_ONLY";
        if (string.Equals(outcome, "GIT_FINALIZE_FAILED", StringComparison.Ordinal))
            return "FINALIZE_FAILED";
        if (!ShouldPublish(blockedWorkCount, pendingWorkCount, buildFailed, outcome))
            return "SKIPPED_BLOCKED";
        return gitSucceeded ? "FINALIZED" : "FINALIZE_FAILED";
    }

    internal static bool ShouldPublish(
        int blockedWorkCount, int pendingWorkCount,
        bool buildFailed, string outcome)
        => blockedWorkCount == 0 && pendingWorkCount == 0 &&
           !buildFailed && string.Equals(outcome, "COMPLETED",
               StringComparison.Ordinal);
}
