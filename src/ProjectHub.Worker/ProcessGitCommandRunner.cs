using System.Diagnostics;
using System.IO;

namespace ProjectHub.Worker;

public sealed record GitCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool Canceled = false);

public interface IGitCommandRunner
{
    Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessGitCommandRunner : IGitCommandRunner
{
    public Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        RunAsync(workingDirectory, arguments, timeout, cancellationToken, null);

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return new GitCommandResult(-1, string.Empty, "GIT_WORKING_DIRECTORY_MISSING");
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        try
        {
            using var processJob = new WorkerChildProcessJob("Git command");
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (environmentOverrides is not null)
            {
                foreach (var pair in environmentOverrides)
                    startInfo.Environment[pair.Key] = pair.Value;
            }

            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("safe.directory=" + workingDirectory);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var launched = processJob.Start(startInfo, cancellationToken);
            var process = launched.Process;
            var stdoutTask = launched.StandardOutput!.ReadToEndAsync();
            var stderrTask = launched.StandardError!.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Job close is the cancellation boundary for the full Git descendant tree.
                processJob.Dispose();
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                try
                {
                    using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
                }
                catch
                {
                }

                var (stdout, _) = await ReadBoundedAsync(stdoutTask).ConfigureAwait(false);
                var (stderr, _) = await ReadBoundedAsync(stderrTask).ConfigureAwait(false);
                return new GitCommandResult(
                    -1,
                    stdout,
                    stderr,
                    TimedOut: timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested,
                    Canceled: cancellationToken.IsCancellationRequested);
            }

            var exitCode = process.ExitCode;
            processJob.Dispose();
            var (output, outputComplete) =
                await ReadBoundedAsync(stdoutTask).ConfigureAwait(false);
            var (error, errorComplete) =
                await ReadBoundedAsync(stderrTask).ConfigureAwait(false);
            if (!outputComplete || !errorComplete)
            {
                return new GitCommandResult(
                    -1,
                    output,
                    error + " GIT_OUTPUT_DRAIN_INCOMPLETE");
            }

            return new GitCommandResult(exitCode, output, error);
        }
        catch (Exception ex)
        {
            return new GitCommandResult(-1, string.Empty, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<(string Output, bool Complete)> ReadBoundedAsync(
        Task<string> readTask)
    {
        var finished = await Task.WhenAny(
            readTask,
            Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        if (finished != readTask)
            return (string.Empty, false);

        try
        {
            return ((await readTask.ConfigureAwait(false)).Trim(), true);
        }
        catch
        {
            return (string.Empty, false);
        }
    }
}
