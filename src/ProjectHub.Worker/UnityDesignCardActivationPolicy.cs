namespace ProjectHub.Worker;

/// <summary>
/// DESIGN is scheduled by HQ. Its live state takes precedence over the
/// reservation displayed in the UI; HIGH never redispatches DESIGN.
/// </summary>
internal static class UnityDesignCardActivationPolicy
{
    internal static bool IsDisabled(
        bool coordinatorFirst, bool? initiallyReserved, bool designCurrentlyRunning) =>
        coordinatorFirst && initiallyReserved == false && !designCurrentlyRunning;

    // Historical RESOURCE callbacks never activate the DESIGN card.
    internal static bool ShouldHighlight(bool designTurnRunning) =>
        designTurnRunning;
}
