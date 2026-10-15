using System.IO;

namespace ProjectHub.Worker;

internal static class UnityUserPausePolicy
{
    internal static void PersistRequested(string root, string jobId, string milestoneId)
    {
        var folder = Path.Combine(root, "temp", "ProjectHub", jobId);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "unity-pause-requested.txt"),
            "PAUSE_REQUESTED\nMILESTONE=" + milestoneId + "\nTIME=" +
            DateTimeOffset.UtcNow.ToString("O"));
    }
}
