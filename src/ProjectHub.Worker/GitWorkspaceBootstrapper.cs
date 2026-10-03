using System.IO;

namespace ProjectHub.Worker;

public sealed record GitWorkspaceBootstrapState(
    bool Success,
    string? ErrorCode,
    string WorkingDirectory,
    string RepositoryRoot,
    string? Branch,
    string? HeadCommit,
    bool InitializedNow,
    bool IsDirty,
    bool NeedsManagedIgnoreUpdate = false,
    bool NeedsManagedIndexCleanup = false)
{
    public bool HasHead => !string.IsNullOrWhiteSpace(HeadCommit);
    public bool NeedsBaseline => false;
}

public sealed class GitWorkspaceBootstrapper
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(2);
    private readonly IGitWorktreeCommandRunner _runner;

    public GitWorkspaceBootstrapper(IGitWorktreeCommandRunner? runner = null)
    {
        _runner = runner ?? new ProcessGitWorktreeCommandRunner();
    }

    public async Task<GitWorkspaceBootstrapState> PrepareAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return Failure("GIT_REMOTE_WORKSPACE_MISSING", workingDirectory);

        var workspace = Path.GetFullPath(workingDirectory);
        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);

        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return Failure("GIT_REMOTE_REPOSITORY_REQUIRED", workspace);

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        if (!PathsEqual(repositoryRoot, workspace))
            return Failure("GIT_REMOTE_EXACT_ROOT_REQUIRED", workspace);

        var branchResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "symbolic-ref",
            "--quiet",
            "--short",
            "HEAD").ConfigureAwait(false);

        if (branchResult.ExitCode != 0 || string.IsNullOrWhiteSpace(branchResult.StandardOutput))
            return RepositoryFailure("GIT_REMOTE_ATTACHED_BRANCH_REQUIRED", workspace, repositoryRoot);

        var branch = FirstLine(branchResult.StandardOutput);

        var headResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);

        if (headResult.ExitCode != 0 || string.IsNullOrWhiteSpace(headResult.StandardOutput))
            return RepositoryFailure("GIT_REMOTE_HEAD_REQUIRED", workspace, repositoryRoot, branch);

        var headCommit = FirstLine(headResult.StandardOutput);

        var remoteResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "remote",
            "get-url",
            "origin").ConfigureAwait(false);

        if (remoteResult.ExitCode != 0 || string.IsNullOrWhiteSpace(remoteResult.StandardOutput))
            return RepositoryFailure(
                "GIT_REMOTE_ORIGIN_REQUIRED",
                workspace,
                repositoryRoot,
                branch,
                headCommit);

        var statusResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "status",
            "--porcelain=v1",
            "--untracked-files=all").ConfigureAwait(false);

        if (statusResult.ExitCode != 0)
            return RepositoryFailure(
                "GIT_REMOTE_STATUS_UNAVAILABLE",
                workspace,
                repositoryRoot,
                branch,
                headCommit);

        if (!string.IsNullOrWhiteSpace(statusResult.StandardOutput))
        {
            return new GitWorkspaceBootstrapState(
                false,
                "GIT_REMOTE_WORKSPACE_DIRTY",
                workspace,
                repositoryRoot,
                branch,
                headCommit,
                false,
                true);
        }

        var fetchResult = await RunAsync(
            repositoryRoot,
            NetworkTimeout,
            cancellationToken,
            "fetch",
            "--prune",
            "origin").ConfigureAwait(false);

        if (fetchResult.ExitCode != 0)
        {
            return RepositoryFailure(
                fetchResult.TimedOut ? "GIT_REMOTE_FETCH_TIMEOUT"
                    : fetchResult.Canceled ? "GIT_REMOTE_FETCH_CANCELED"
                    : "GIT_REMOTE_FETCH_FAILED",
                workspace,
                repositoryRoot,
                branch,
                headCommit);
        }

        var remoteHeadResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            $"refs/remotes/origin/{branch}^{{commit}}").ConfigureAwait(false);

        if (remoteHeadResult.ExitCode != 0 ||
            string.IsNullOrWhiteSpace(remoteHeadResult.StandardOutput))
        {
            return RepositoryFailure(
                "GIT_REMOTE_BRANCH_REQUIRED",
                workspace,
                repositoryRoot,
                branch,
                headCommit);
        }

        var remoteHead = FirstLine(remoteHeadResult.StandardOutput);
        if (!string.Equals(headCommit, remoteHead, StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryFailure(
                "GIT_REMOTE_HEAD_MISMATCH",
                workspace,
                repositoryRoot,
                branch,
                headCommit);
        }

        return new GitWorkspaceBootstrapState(
            true,
            null,
            workspace,
            repositoryRoot,
            branch,
            headCommit,
            false,
            false);
    }

    // Local baseline creation is intentionally unsupported.
    // The remote branch is the only accepted launch baseline.
    public Task<GitWorkspaceBootstrapState> CreateBaselineAsync(
        GitWorkspaceBootstrapState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ = cancellationToken;
        return Task.FromResult(
            state.Success
                ? state
                : state with { ErrorCode = state.ErrorCode ?? "GIT_REMOTE_BASELINE_REQUIRED" });
    }

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        params string[] arguments)
        => _runner.RunAsync(workingDirectory, arguments, timeout, cancellationToken);

    private static bool PathsEqual(string left, string right)
    {
        static string Normalize(string value)
            => Path.GetFullPath(value)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(
            Normalize(left),
            Normalize(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static GitWorkspaceBootstrapState Failure(
        string errorCode,
        string? workingDirectory)
    {
        var workspace = string.IsNullOrWhiteSpace(workingDirectory)
            ? string.Empty
            : Path.GetFullPath(workingDirectory);
        return new GitWorkspaceBootstrapState(
            false,
            errorCode,
            workspace,
            workspace,
            null,
            null,
            false,
            false);
    }

    private static GitWorkspaceBootstrapState RepositoryFailure(
        string errorCode,
        string workspace,
        string repositoryRoot,
        string? branch = null,
        string? headCommit = null)
        => new(
            false,
            errorCode,
            workspace,
            repositoryRoot,
            branch,
            headCommit,
            false,
            false);

    private static string FirstLine(string value)
        => (value ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? string.Empty;
}
