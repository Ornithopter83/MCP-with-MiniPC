namespace ProjectHub.Worker;

/// <summary>
/// Publishing a milestone is meaningful only after implementation and
/// required checks have finished. This does not interpret the AI's intent.
/// </summary>
internal static class MilestoneFinalizationPolicy
{
    internal static bool ShouldPublish(
        int blockedWorkCount, int pendingWorkCount,
        bool buildFailed, string outcome)
        => blockedWorkCount == 0 && pendingWorkCount == 0 &&
           !buildFailed && string.Equals(outcome, "COMPLETED",
               StringComparison.Ordinal);
}
