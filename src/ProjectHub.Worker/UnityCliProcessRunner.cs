using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Single Unity CLI child-process transport for connected Editor operations.
/// Callers retain ownership of the Editor gate and all result verification.
/// An uncertain timeout is reported, never replayed automatically.
/// </summary>
internal static class UnityCliProcessRunner
{
    internal static ProcessStartInfo CreateStartInfo(string root, IReadOnlyList<string> args)
    {
        var info = new ProcessStartInfo
        {
            FileName = "unity",
            WorkingDirectory = Path.GetFullPath(root),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        return info;
    }

    internal static async Task<UnityAssetCliResult> RunAsync(
        string root, IReadOnlyList<string> args, TimeSpan timeout,
        string owner, string timeoutDiagnostic, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        UnityAssetCliResult result;
        try
        {
            var info = CreateStartInfo(root, args);
            using var job = new WorkerChildProcessJob(owner);
            using var child = job.Start(info, cancellationToken);
            var stdout = child.StandardOutput!.ReadToEndAsync(cancellationToken);
            var stderr = child.StandardError!.ReadToEndAsync(cancellationToken);
            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);
            try
            {
                await child.Process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                result = new(child.Process.ExitCode,
                    await stdout.ConfigureAwait(false),
                    await stderr.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                job.Dispose();
                result = new(-1, "", timeoutDiagnostic);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            result = new(-1, "", "UNITY_CLI_START_ERROR: " +
                ex.GetType().Name + ": " + ex.Message);
        }
        return result with
        {
            EvidencePath = PersistEvidence(root, args, started, result)
        };
    }

    // Private, bounded original output records. Never stored in the Unity
    // repository or injected into HQ messages. No arbitrary expression/args
    // are persisted: only the CLI verb and subcommand are metadata.
    private static string? PersistEvidence(string root, IReadOnlyList<string> args,
        DateTimeOffset started, UnityAssetCliResult result)
    {
        try
        {
            var local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) local = Path.GetTempPath();
            var rootHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..16];
            var directory = Path.Combine(local, "ProjectHub", "UnityWorker",
                "unity-cli-evidence", rootHash);
            Directory.CreateDirectory(directory);
            var filename = started.ToString("yyyyMMddTHHmmssfff") +
                "-" + Guid.NewGuid().ToString("N") + ".json";
            var filepath = Path.Combine(directory, filename);
            const int limit = 2_000_000;
            var payload = new
            {
                startedUtc = started,
                finishedUtc = DateTimeOffset.UtcNow,
                verb = args.Count > 0 ? args[0] : "",
                subcommand = args.Count > 1 && !args[1].StartsWith("--",
                    StringComparison.Ordinal) ? args[1] : "",
                result.ExitCode,
                stdout = result.Output[..Math.Min(limit, result.Output.Length)],
                stderr = result.Error[..Math.Min(limit, result.Error.Length)],
                stdoutTruncated = result.Output.Length > limit,
                stderrTruncated = result.Error.Length > limit
            };
            File.WriteAllText(filepath, JsonSerializer.Serialize(payload),
                ProjectHubJson.Utf8NoBom);
            return filepath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            return null; // Logging failure must never mask the CLI result.
        }
    }
}
