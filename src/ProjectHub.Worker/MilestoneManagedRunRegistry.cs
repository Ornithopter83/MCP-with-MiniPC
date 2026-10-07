using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.IO;

namespace ProjectHub.Worker;

internal static class MilestoneManagedRunRegistry
{
    private sealed record ManagedRunEntry(
        WorkerChildProcessJob ProcessJob,
        SuspendedJobProcess Launched,
        Task<string> StandardOutput,
        Task<string> StandardError,
        string Command,
        string LogPath,
        int ProcessId);

    private static readonly ConcurrentDictionary<string, ManagedRunEntry> Active =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task<MilestoneMechanicalResult> StartAsync(
        string jobId,
        string workingDirectory,
        string body,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        var command = MilestoneDefinitionContract.ReadBodyDirective(
            body,
            "COMMAND");
        if (string.IsNullOrWhiteSpace(command))
        {
            return new(
                "RUN",
                false,
                -1,
                string.Empty,
                string.Empty,
                "MECHANICAL_STATUS: BLOCKED" +
                Environment.NewLine +
                "COMMAND 지시가 없습니다.");
        }

        var previous = await StopAsync(
            jobId,
            CancellationToken.None).ConfigureAwait(false);

        var logRoot = Path.Combine(
            workingDirectory,
            "temp",
            "ProjectHub",
            jobId,
            "mechanical");
        Directory.CreateDirectory(logRoot);
        var logPath = Path.Combine(
            logRoot,
            "run-" +
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") +
            ".log");

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);

        var processJob = new WorkerChildProcessJob(
            "Worker RUN " + jobId);
        SuspendedJobProcess launched;
        try
        {
            launched = processJob.Start(
                startInfo,
                cancellationToken);
        }
        catch
        {
            processJob.Dispose();
            throw;
        }

        var outputTask = launched.StandardOutput!
            .ReadToEndAsync();
        var errorTask = launched.StandardError!
            .ReadToEndAsync();
        var entry = new ManagedRunEntry(
            processJob,
            launched,
            outputTask,
            errorTask,
            command,
            logPath,
            launched.Process.Id);

        Active[jobId] = entry;

        var exitTask = launched.Process.WaitForExitAsync(
            cancellationToken);
        var startupDelay = Task.Delay(
            TimeSpan.FromMilliseconds(900),
            cancellationToken);
        var completed = await Task.WhenAny(
            exitTask,
            startupDelay).ConfigureAwait(false);

        if (completed == exitTask)
        {
            await exitTask.ConfigureAwait(false);
            Active.TryRemove(jobId, out _);
            return await FinishEntryAsync(
                entry,
                forced: false).ConfigureAwait(false);
        }

        var prefix = previous is null
            ? string.Empty
            : "기존 managed RUN을 종료하고 새 RUN을 시작했습니다." +
              Environment.NewLine;
        return new(
            "RUN",
            true,
            -1,
            command,
            logPath,
            prefix +
            "RUN_STATUS: RUNNING" +
            Environment.NewLine +
            $"pid={entry.ProcessId}" +
            Environment.NewLine +
            "QA/HIGH validation 종료 시 Worker가 이 프로세스 트리를 종료합니다.");
    }

    public static async Task<MilestoneMechanicalResult?> StopAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        if (!Active.TryRemove(jobId, out var entry))
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        return await FinishEntryAsync(
            entry,
            forced: true).ConfigureAwait(false);
    }

    public static bool HasActive(string jobId) =>
        Active.ContainsKey(jobId);

    private static async Task<MilestoneMechanicalResult> FinishEntryAsync(
        ManagedRunEntry entry,
        bool forced)
    {
        if (forced)
            entry.ProcessJob.Dispose();

        if (!entry.Launched.Process.HasExited)
        {
            try
            {
                var wait = entry.Launched.Process.WaitForExitAsync();
                await Task.WhenAny(
                    wait,
                    Task.Delay(TimeSpan.FromSeconds(3)))
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        var stdout = await ReadCompletedAsync(
            entry.StandardOutput).ConfigureAwait(false);
        var stderr = await ReadCompletedAsync(
            entry.StandardError).ConfigureAwait(false);
        var combined = stdout +
            (string.IsNullOrWhiteSpace(stderr)
                ? string.Empty
                : Environment.NewLine + stderr);

        try
        {
            await File.WriteAllTextAsync(
                entry.LogPath,
                combined,
                new UTF8Encoding(false),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        int exitCode;
        try
        {
            exitCode = entry.Launched.Process.HasExited
                ? entry.Launched.Process.ExitCode
                : -1;
        }
        catch
        {
            exitCode = -1;
        }

        if (!forced)
            entry.ProcessJob.Dispose();
        entry.Launched.Dispose();

        var status = forced
            ? "RUN_STATUS: STOPPED_BY_WORKER"
            : exitCode == 0
                ? "RUN_STATUS: COMPLETED"
                : "RUN_STATUS: FAILED";

        return new(
            "RUN",
            forced || exitCode == 0,
            exitCode,
            entry.Command,
            entry.LogPath,
            status +
            Environment.NewLine +
            $"pid={entry.ProcessId}" +
            Environment.NewLine +
            MilestoneDefinitionContract.Limit(
                combined,
                16000));
    }

    private static async Task<string> ReadCompletedAsync(
        Task<string> task)
    {
        if (task.IsCompleted)
        {
            try { return await task.ConfigureAwait(false); }
            catch { return string.Empty; }
        }

        var completed = await Task.WhenAny(
            task,
            Task.Delay(TimeSpan.FromSeconds(1)))
            .ConfigureAwait(false);
        if (completed != task)
            return string.Empty;

        try { return await task.ConfigureAwait(false); }
        catch { return string.Empty; }
    }
}
