using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// The accepted-command gate only inspects an existing Editor/Pipeline.
/// It never opens, restarts, refocuses or waits for a new GUI process.
/// </summary>
internal static class UnityEditorCommandLifecycle
{
    internal static bool ProjectEditorLockHeld(string root)
    {
        foreach (var relative in new[] { "Temp/UnityLockfile", "Library/UnityLockfile" })
        {
            var file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) continue;
            try
            {
                using var stream = new FileStream(file, FileMode.Open,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }
        return false;
    }

    internal static Task<UnityToolchainPreflight.Result> EnsureForAcceptedCommandAsync(
        string root, CancellationToken cancellationToken) =>
        EnsureForAcceptedCommandAsync(root, cancellationToken,
            UnityToolchainPreflight.RunAsync, ProjectEditorLockHeld);

    internal static async Task<UnityToolchainPreflight.Result> EnsureForAcceptedCommandAsync(
        string root, CancellationToken cancellationToken,
        Func<string, CancellationToken, Task<UnityToolchainPreflight.Result>> probe,
        Func<string, bool> editorLockHeld)
    {
        var project = UnityToolchainPreflight.ValidateProject(root);
        if (!project.Ready) return project;
        var first = await probe(root, cancellationToken).ConfigureAwait(false);
        if (first.Ready || first.Code != "UNITY_EDITOR_UNAVAILABLE") return first;
        if (editorLockHeld(root))
            return new(false, "UNITY_EDITOR_RUNNING_PIPELINE_UNAVAILABLE",
                "Existing Editor locked this project; Pipeline is unavailable.");
        return new(false, "UNITY_EDITOR_UNAVAILABLE_BACKGROUND_ONLY",
            "Existing Editor is unavailable. No GUI launch or automatic recovery.");
    }
}
