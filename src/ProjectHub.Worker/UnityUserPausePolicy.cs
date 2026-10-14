namespace ProjectHub.Worker;

/// <summary>
/// Only explicit user stop/save instructions preempt new milestone stages.
/// Other queued HQ interventions keep their established delivery contract.
/// </summary>
internal static class UnityUserPausePolicy
{
    internal static bool IsPauseRequest(string? message)
    {
        var value = (message ?? string.Empty).Trim();
        if (value.Length == 0) return false;
        if (value.Contains("중단하지", StringComparison.Ordinal) ||
            value.Contains("중단하지 마", StringComparison.Ordinal))
            return false;
        return value.Contains("[ACTION=PAUSE]", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("임시 저장하고 중단", StringComparison.Ordinal) ||
               value.Contains("저장 후 중단", StringComparison.Ordinal) ||
               value.Contains("저장하고 중단", StringComparison.Ordinal) ||
               value.Contains("pause now", StringComparison.OrdinalIgnoreCase);
    }
}
