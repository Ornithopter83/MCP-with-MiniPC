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

    internal static MilestoneMechanicalResult EditorUnavailableResult(string reason) =>
        new("BUILD", false, -2, "unity editor preflight", "",
            "BUILD_UNVERIFIED: " + reason + " (no Unity recompile invoked)");

    public static async Task<MilestoneMechanicalResult> RunAsync(
        string root, string jobId, CancellationToken cancellationToken,
        bool editorConnectivityVerified = false,
        bool editorLaneHeld = false)
    {
        var projectRoot = Path.GetFullPath(root);
        // A caller that just probed this Editor does not need another CLI
        // catalog request before recompile. Command failure still remains
        // UNVERIFIED and does not trigger compiler-error repair.
        if (!editorConnectivityVerified)
        {
            // Direct Editor WORK already owns the scheduler's gate.
            var probe = await (editorLaneHeld
                ? UnityToolchainPreflight.RunUncoordinatedAsync(
                    projectRoot, cancellationToken)
                : UnityToolchainPreflight.RunAsync(
                    projectRoot, cancellationToken)).ConfigureAwait(false);
            if (!probe.Ready)
                return EditorUnavailableResult(probe.Code + ": " +
                    MilestoneDefinitionContract.Limit(probe.Detail, 900));
        }
        using var editorLease = editorLaneHeld ? null :
            await UnityEditorOperationGate.EnterAsync(
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

        var pipelineDisconnected =
            output.ToString().Contains("Pipeline 인스턴스를 찾을 수 없습니다",
                StringComparison.Ordinal) ||
            output.ToString().Contains("No Pipeline instance found",
                StringComparison.OrdinalIgnoreCase) ||
            output.ToString().Contains("STATUS_NO_INSTANCES",
                StringComparison.OrdinalIgnoreCase);
        var summary = exitCode switch
        {
            0 => "BUILD_PASSED: Unity CLI recompile completed",
            _ when pipelineDisconnected => "BUILD_UNVERIFIED: UNITY_EDITOR_UNAVAILABLE",
            6 => "BUILD_FAILED: UNITY_COMPILATION_ERRORS",
            7 => "BUILD_UNVERIFIED: UNITY_CLI_ENVIRONMENT",
            _ => "BUILD_UNVERIFIED: UNITY_CLI_ENVIRONMENT: " +
                (timeoutReached ? "TIMEOUT" : "RECOMPILE_FAILED")
        };
        if (exitCode != 0)
            summary += ": " + MilestoneDefinitionContract.Limit(output.ToString(), 1800);
        return new MilestoneMechanicalResult("BUILD", exitCode == 0,
            exitCode, command, logFile, summary);
    }

    // Classify genuine compilation errors separately from tool/environment
    // failures. Worker never dispatches an automatic repair WORK.
    public static bool IsEnvironmentFailure(MilestoneMechanicalResult report)
        => !report.Success &&
           (report.Summary.Contains("BUILD_UNVERIFIED:",
                StringComparison.OrdinalIgnoreCase) ||
            (report.Command.StartsWith("unity recompile",
                StringComparison.OrdinalIgnoreCase) &&
             report.ExitCode != 6));
}
