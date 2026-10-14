using System.IO;

namespace ProjectHub.Worker;

internal static class UnityUserPausePolicy
{
    internal static bool IsPauseRequest(string? message)
    {
        var value = (message ?? string.Empty).Trim();
        if (value.Length == 0 ||
            value.Contains("중단하지", StringComparison.Ordinal)) return false;
        return value.Contains("[ACTION=PAUSE]", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("임시 저장하고 중단", StringComparison.Ordinal) ||
               value.Contains("저장 후 중단", StringComparison.Ordinal) ||
               value.Contains("저장하고 중단", StringComparison.Ordinal) ||
               value.Contains("pause now", StringComparison.OrdinalIgnoreCase);
    }

    internal static void PersistRequested(string root, string jobId, string milestoneId)
    {
        var folder = Path.Combine(root, "temp", "ProjectHub", jobId);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "unity-pause-requested.txt"),
            "PAUSE_REQUESTED\nMILESTONE=" + milestoneId + "\nTIME=" +
            DateTimeOffset.UtcNow.ToString("O"));
    }
}
