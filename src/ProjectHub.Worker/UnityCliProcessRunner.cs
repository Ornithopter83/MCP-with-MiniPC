using System.Diagnostics;
using System.IO;

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
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            job.Dispose();
            return new(-1, string.Empty, timeoutDiagnostic);
        }
        return new(child.Process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }
}
