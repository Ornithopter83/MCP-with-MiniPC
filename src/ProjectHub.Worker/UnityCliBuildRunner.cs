using System.Diagnostics;
using System.IO;
using System.Text;

namespace ProjectHub.Worker;

/// <summary>
/// Uses the Unity CLI attached to an already running editor. Does not start
/// another batch-mode editor against the user's live Unity project.
/// </summary>
internal static class UnityCliBuildRunner
{
    public static bool IsUnityProject(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return false;
        return Directory.Exists(Path.Combine(root, "Assets")) &&
               File.Exists(Path.Combine(root, "Packages", "manifest.json")) &&
               File.Exists(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"));
    }

    internal static string[] RecompileArguments(string root) =>
        new[] { "recompile", "--project-path", Path.GetFullPath(root), "--format", "json" };

    public static async Task<MilestoneMechanicalResult> RunAsync(
        string root, string jobId, CancellationToken cancellationToken)
    {
        var projectRoot = Path.GetFullPath(root);
        using var editorLease = await UnityEditorOperationGate.EnterAsync(
            projectRoot, cancellationToken).ConfigureAwait(false);
        var args = RecompileArguments(projectRoot);
        var command = "unity " + string.Join(" ", args);
        var folder = Path.Combine(projectRoot, "temp", "ProjectHub", jobId, "build");
        Directory.CreateDirectory(folder);
        var logFile = Path.Combine(folder,
            "unity-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + ".log");
        var output = new StringBuilder();
        var exitCode = -1;
        var timeoutReached = false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "unity",
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            using var job = new WorkerChildProcessJob("UnityWorker CLI BUILD");
            using var child = job.Start(psi, cancellationToken);
            var process = child.Process;
            var stdout = child.StandardOutput!.ReadToEndAsync(cancellationToken);
            var stderr = child.StandardError!.ReadToEndAsync(cancellationToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                exitCode = process.ExitCode;
                output.Append(await stdout.ConfigureAwait(false));
                output.AppendLine(await stderr.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timeoutReached = true;
                job.Dispose();
                output.AppendLine("UNITY_CLI_TIMEOUT_10_MIN");
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            output.AppendLine("UNITY_CLI_LAUNCH_FAILED: " + error.GetType().Name + ": " + error.Message);
        }
        await File.WriteAllTextAsync(logFile, output.ToString(),
            cancellationToken).ConfigureAwait(false);

        var summary = exitCode switch
        {
            0 => "BUILD_PASSED: Unity CLI recompile completed",
            6 => "BUILD_FAILED: UNITY_COMPILATION_ERRORS",
            7 => "UNITY_CLI_ENVIRONMENT: EDITOR_UNAVAILABLE",
            _ => "UNITY_CLI_ENVIRONMENT: " + (timeoutReached ? "TIMEOUT" : "RECOMPILE_FAILED")
        };
        if (exitCode != 0)
            summary += ": " + MilestoneDefinitionContract.Limit(output.ToString(), 1800);
        return new MilestoneMechanicalResult("BUILD", exitCode == 0,
            exitCode, command, logFile, summary);
    }

    // Only a confirmed compiler error (Unity CLI exit 6) can enter the
    // existing bounded #8 code-fix path. Missing CLI, auth, editor, timeout
    // and unknown exits remain environment/tool failures.
    public static bool IsEnvironmentFailure(MilestoneMechanicalResult report)
        => !report.Success &&
           report.Command.StartsWith("unity recompile", StringComparison.OrdinalIgnoreCase) &&
           report.ExitCode != 6;
}
