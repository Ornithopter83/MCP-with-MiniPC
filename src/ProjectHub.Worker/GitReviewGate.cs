using System.Diagnostics;

namespace ProjectHub.Worker;

public sealed record GitReviewCheckpoint(
    string SyncState,
    string? RepositoryUrl,
    string? Branch,
    string? LocalHeadSha,
    string? RemoteHeadSha,
    string PushConfirmation,
    string ServerObservation,
    string? ReviewCommitSha,
    string? PauseReason);

public static class GitReviewGate
{
    public static Task<GitReviewCheckpoint> CheckAsync(string projectPath, WorkerTargetSettings settings, CancellationToken cancellationToken = default) =>
        Task.Run(() => Check(projectPath, settings, cancellationToken), cancellationToken);

    private static GitReviewCheckpoint Check(string projectPath, WorkerTargetSettings settings, CancellationToken cancellationToken)
    {
        var target = WorkerTargetConfiguration.ResolveGit(projectPath, settings);
        const string serverObservation = "UNAVAILABLE: Worker has no projectId/workstationId mapping for the current Server state API";
        if (!target.IsRepository)
            return ContinueWithoutGit("GIT_UNAVAILABLE", target, null, "Git repository is not configured; Web delivery will continue without a Git reference.", serverObservation);
        if (string.IsNullOrWhiteSpace(target.Branch) || target.Branch == "HEAD")
            return ContinueWithoutGit("GIT_DETACHED", target, null, "No branch is available; Web delivery will continue without a remote Git reference.", serverObservation);
        if (string.IsNullOrWhiteSpace(target.HeadSha))
            return ContinueWithoutGit("GIT_LOCAL_HEAD_UNKNOWN", target, null, "Local HEAD SHA could not be read; Web delivery will continue without a Git reference.", serverObservation);

        var status = RunGit(target.ProjectPath, cancellationToken, "status", "--porcelain=v1", "--untracked-files=all");
        if (status is null)
            return ContinueWithoutGit("GIT_STATUS_UNKNOWN", target, null, "Working tree status could not be read; Web delivery will continue without a Git reference.", serverObservation);
        if (!string.IsNullOrWhiteSpace(status))
            return ContinueWithoutGit("GIT_DIRTY", target, null, "Working tree has local changes; Web delivery will use the current Worker result instead of a Git commit.", serverObservation);

        if (string.IsNullOrWhiteSpace(target.RepositoryUrl))
            return ContinueWithoutGit("GIT_REMOTE_UNCONFIGURED", target, null, "No origin repository is configured; Web delivery will continue without a remote Git reference.", serverObservation);

        var remoteResult = RunGit(target.ProjectPath, cancellationToken, "ls-remote", "origin", "refs/heads/" + target.Branch);
        if (remoteResult is null)
            return ContinueWithoutGit("GIT_REMOTE_UNREACHABLE", target, null, "Remote branch SHA could not be confirmed; Web delivery will continue without a Git reference.", serverObservation);
        var remoteHead = remoteResult.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(remoteHead))
            return ContinueWithoutGit("GIT_REMOTE_BRANCH_UNKNOWN", target, null, "Remote branch was not found; Web delivery will continue without a Git reference.", serverObservation);
        if (!string.Equals(target.HeadSha, remoteHead, StringComparison.OrdinalIgnoreCase))
            return ContinueWithoutGit("GIT_SYNC_MISMATCH", target, remoteHead, "Local and remote SHA differ; Web delivery will continue without an exact Git reference.", serverObservation);

        return new("REMOTE_CONFIRMED", target.RepositoryUrl, target.Branch, target.HeadSha, remoteHead,
            "REMOTE_CONFIRMED (Worker did not run push)", serverObservation, target.HeadSha, null);
    }

    private static GitReviewCheckpoint ContinueWithoutGit(string state, GitTargetSnapshot target, string? remoteHead, string reason, string serverObservation) =>
        new(state, target.RepositoryUrl, target.Branch, target.HeadSha, remoteHead, "NOT_CONFIRMED", serverObservation, null, reason);

    private static string? RunGit(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("safe.directory=" + workingDirectory);
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(8000) || cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}