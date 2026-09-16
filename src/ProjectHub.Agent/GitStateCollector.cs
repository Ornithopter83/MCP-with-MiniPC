using System.ComponentModel;
using System.Diagnostics;
using ProjectHub.Core;

namespace ProjectHub.Agent;

public sealed class GitStateCollector
{
    public async Task<ProjectState> CollectAsync(
        string projectId,
        string workstationId,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("Project ID is required.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(workstationId)) throw new ArgumentException("Workstation ID is required.", nameof(workstationId));
        if (string.IsNullOrWhiteSpace(localPath)) throw new ArgumentException("Local path is required.", nameof(localPath));

        var branch = await RunGitAsync(localPath, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken);
        var headSha = await RunGitAsync(localPath, ["rev-parse", "HEAD"], cancellationToken);
        var status = await RunGitAsync(localPath, ["status", "--porcelain=v1", "--untracked-files=all"], cancellationToken);
        var summary = ParseStatus(status);

        return new ProjectState(
            projectId.Trim(),
            workstationId.Trim(),
            branch,
            headSha,
            summary.IsDirty,
            summary.ChangedCount,
            summary.UntrackedCount,
            summary.DeletedCount,
            null,
            null,
            DateTimeOffset.UtcNow);
    }

    public static GitStatusSummary ParseStatus(string output)
    {
        var changed = 0;
        var untracked = 0;
        var deleted = 0;

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("?? ", StringComparison.Ordinal))
            {
                untracked++;
                continue;
            }

            if (line.Length < 2) continue;
            changed++;
            if (line[0] == 'D' || line[1] == 'D') deleted++;
        }

        return new GitStatusSummary(changed > 0 || untracked > 0, changed, untracked, deleted);
    }

    private static async Task<string> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new GitStateException("Unable to start git.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();

            if (process.ExitCode != 0)
            {
                throw new GitStateException(
                    $"Git command failed ({string.Join(' ', arguments)}): {error}");
            }

            return output;
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new GitStateException($"Project path was not found: {workingDirectory}", exception);
        }
        catch (Win32Exception exception)
        {
            throw new GitStateException("Git executable could not be started.", exception);
        }
    }
}

public sealed record GitStatusSummary(
    bool IsDirty,
    int ChangedCount,
    int UntrackedCount,
    int DeletedCount);

public sealed class GitStateException(string message, Exception? innerException = null)
    : Exception(message, innerException);
