namespace ProjectHub.Worker;

/// <summary>
/// DESIGN may be invoked by HIGH even when HQ did not pre-reserve a DESIGN
/// stage. The live role always takes precedence over initial reservation.
/// </summary>
internal static class UnityDesignCardActivationPolicy
{
    internal static bool IsDisabled(
        bool coordinatorFirst, bool? initiallyReserved, bool designCurrentlyRunning) =>
        coordinatorFirst && initiallyReserved == false && !designCurrentlyRunning;

    // The retired RESOURCE sidecar must not keep the DESIGN visual stage
    // highlighted after the DESIGN turn ends in the current task group.
    internal static bool ShouldHighlight(
        bool designTurnRunning, bool legacyResourceSidecarOutstanding) =>
        designTurnRunning;
}
