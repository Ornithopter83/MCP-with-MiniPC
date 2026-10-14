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
}
