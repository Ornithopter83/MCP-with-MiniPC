using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Worker-owned build phase. Only known build manifests are executed; a WORK
/// cannot supply shell commands or mutate Git through this phase.
/// </summary>
internal static class MilestoneBuildRunner
{
    private sealed record Target(string Directory, string Executable, string[] Arguments);

    public static async Task<IReadOnlyList<MilestoneMechanicalResult>> RunAsync(
        string root, string jobId, IEnumerable<MilestoneWorkDefinition> workItems,
        CancellationToken cancellationToken)
    {
        var targets = DiscoverTargets(root, workItems).ToArray();
        if (targets.Length == 0)
            return new[] { new MilestoneMechanicalResult("BUILD", true, 0, "",
                "", "BUILD_SKIPPED: 빌드 매니페스트 없음") };

        var results = new List<MilestoneMechanicalResult>();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunTargetAsync(root, jobId, target, cancellationToken)
                .ConfigureAwait(false);
            results.Add(result);
            if (!result.Success)
                break;
        }
        return results;
    }

    private static IEnumerable<Target> DiscoverTargets(
        string root, IEnumerable<MilestoneWorkDefinition> items)
    {
        var fullRoot = Path.GetFullPath(root);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(item => !item.ReadOnly))
        foreach (var path in item.WritePaths)
        {
            var candidate = Path.GetFullPath(Path.Combine(
                fullRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!MilestoneDefinitionContract.IsPathInsideRoot(fullRoot, candidate))
                continue;

            var current = Directory.Exists(candidate) ? candidate :
                Path.GetDirectoryName(candidate);
            while (!string.IsNullOrWhiteSpace(current) &&
                   MilestoneDefinitionContract.IsPathInsideRoot(fullRoot, current))
            {
                var package = Path.Combine(current, "package.json");
                if (File.Exists(package))
                {
                    var hasBuild = false;
                    try
                    {
                        using var json = JsonDocument.Parse(File.ReadAllText(package));
                        hasBuild = json.RootElement.TryGetProperty("scripts", out var scripts) &&
                            scripts.ValueKind == JsonValueKind.Object &&
                            scripts.TryGetProperty("build", out var build) &&
                            build.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(build.GetString());
                    }
                    catch (JsonException)
                    {
                        // Never fabricate commands from malformed manifests.
                    }
                    if (hasBuild && seen.Add(current))
                        yield return new Target(current,
                            OperatingSystem.IsWindows() ? "npm.cmd" : "npm",
                            new[] { "run", "build" });
                    break;
                }

                var projects = Directory.Exists(current)
                    ? Directory.EnumerateFiles(current, "*.csproj",
                        SearchOption.TopDirectoryOnly).Take(2).ToArray()
                    : Array.Empty<string>();
                if (projects.Length == 1)
                {
                    if (seen.Add(projects[0]))
                        yield return new Target(current, "dotnet",
                            new[] { "build", projects[0], "--nologo" });
                    break;
                }

                if (string.Equals(current, fullRoot,
                    StringComparison.OrdinalIgnoreCase))
                    break;
                current = Path.GetDirectoryName(current);
            }
        }
    }

    private static async Task<MilestoneMechanicalResult> RunTargetAsync(
        string root, string jobId, Target target,
        CancellationToken cancellationToken)
    {
        var display = target.Executable + " " +
            string.Join(" ", target.Arguments);
        var directory = Path.Combine(root, "temp", "ProjectHub", jobId, "build");
        Directory.CreateDirectory(directory);
        var logFile = Path.Combine(directory,
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + ".log");
        var output = new StringBuilder();
        var exit = -1;
        var success = false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = target.Executable,
                WorkingDirectory = target.Directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in target.Arguments)
                psi.ArgumentList.Add(argument);
            using var job = new WorkerChildProcessJob("Milestone Worker BUILD");
            using var child = job.Start(psi, cancellationToken);
            var process = child.Process;
            var stdout = child.StandardOutput!.ReadToEndAsync(cancellationToken);
            var stderr = child.StandardError!.ReadToEndAsync(cancellationToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                exit = process.ExitCode;
                output.Append(await stdout.ConfigureAwait(false));
                output.AppendLine(await stderr.ConfigureAwait(false));
                success = exit == 0;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                job.Dispose();
                output.AppendLine("BUILD_TIMEOUT_3_MIN");
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            output.AppendLine(error.GetType().Name + ": " + error.Message);
        }
        await File.WriteAllTextAsync(logFile, output.ToString(),
            cancellationToken).ConfigureAwait(false);
        return new MilestoneMechanicalResult("BUILD", success, exit,
            display + " (cwd=" + target.Directory + ")",
            logFile, success ? "BUILD_PASSED" :
                "BUILD_FAILED: " + MilestoneDefinitionContract.Limit(
                    output.ToString(), 2000));
    }

    public static bool IsEnvironmentFailure(IEnumerable<MilestoneMechanicalResult> reports) =>
        reports.Any(report => !report.Success &&
            (report.Summary.Contains("EPERM", StringComparison.OrdinalIgnoreCase) ||
             report.Summary.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
             report.Summary.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
             report.Summary.Contains("Win32Exception", StringComparison.OrdinalIgnoreCase) ||
             report.Summary.Contains("BUILD_TIMEOUT", StringComparison.OrdinalIgnoreCase)));
}
