using System.IO;

namespace ProjectHub.Worker;

public static class WorkspaceLaunchGate
{
    public static string? Validate(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return "WORKSPACE_NOT_SELECTED";

        return Directory.Exists(workingDirectory.Trim())
            ? null
            : "WORKSPACE_NOT_FOUND";
    }
}
