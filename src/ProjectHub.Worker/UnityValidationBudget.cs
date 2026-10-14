namespace ProjectHub.Worker;

/// <summary>
/// The budget counts whole integration rounds, not debug commands or polling.
/// </summary>
internal static class UnityValidationBudget
{
    internal const int BasicRoundCount = 2;
    internal const int MaximumRoundCount = 3;

    internal static bool CanStart(int round, bool previousRoundChangedIntegrationEvidence) =>
        round >= 1 && round <= BasicRoundCount ||
        round == MaximumRoundCount && previousRoundChangedIntegrationEvidence;

    internal static string Guidance =>
        "R1 existing BUILD/TEST evidence and critical scenes; " +
        "R2 changed-area regression plus Player smoke; " +
        "R3 at most once when R2 evidence invalidated. " +
        "Local debugging is separate; failures/untested work remain visible.";
}
