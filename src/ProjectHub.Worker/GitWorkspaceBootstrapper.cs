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
    bool IsDirty)
{
    public bool HasHead => !string.IsNullOrWhiteSpace(HeadCommit);
    public bool NeedsBaseline => Success && (!HasHead || IsDirty);
}

public sealed class GitWorkspaceBootstrapper
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMinutes(2);

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
            return Failure("GIT_BOOTSTRAP_WORKSPACE_MISSING", workingDirectory);

        var workspace = Path.GetFullPath(workingDirectory);
        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);

        var initializedNow = false;
        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
        {
            var initResult = await RunAsync(
                workspace,
                WriteTimeout,
                cancellationToken,
                "init").ConfigureAwait(false);

            if (initResult.ExitCode != 0)
            {
                return Failure(
                    initResult.TimedOut ? "GIT_BOOTSTRAP_INIT_TIMEOUT"
                        : initResult.Canceled ? "GIT_BOOTSTRAP_INIT_CANCELED"
                        : "GIT_BOOTSTRAP_INIT_FAILED",
                    workspace);
            }

            initializedNow = true;
            rootResult = await RunAsync(
                workspace,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--show-toplevel").ConfigureAwait(false);
        }

        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return Failure("GIT_BOOTSTRAP_ROOT_UNAVAILABLE", workspace);

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));

        var branchResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "symbolic-ref",
            "--quiet",
            "--short",
            "HEAD").ConfigureAwait(false);

        if (branchResult.ExitCode != 0 || string.IsNullOrWhiteSpace(branchResult.StandardOutput))
        {
            return new GitWorkspaceBootstrapState(
                false,
                "GIT_BOOTSTRAP_ATTACHED_BRANCH_REQUIRED",
                workspace,
                repositoryRoot,
                null,
                null,
                initializedNow,
                false);
        }

        var branch = FirstLine(branchResult.StandardOutput);

        var headResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);

        var headCommit = headResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(headResult.StandardOutput)
            ? FirstLine(headResult.StandardOutput)
            : null;

        var statusResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "status",
            "--porcelain=v1",
            "--untracked-files=all").ConfigureAwait(false);

        if (statusResult.ExitCode != 0)
        {
            return new GitWorkspaceBootstrapState(
                false,
                "GIT_BOOTSTRAP_STATUS_UNAVAILABLE",
                workspace,
                repositoryRoot,
                branch,
                headCommit,
                initializedNow,
                false);
        }

        return new GitWorkspaceBootstrapState(
            true,
            null,
            workspace,
            repositoryRoot,
            branch,
            headCommit,
            initializedNow,
            !string.IsNullOrWhiteSpace(statusResult.StandardOutput));
    }

    public async Task<GitWorkspaceBootstrapState> CreateBaselineAsync(
        GitWorkspaceBootstrapState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.Success)
            return state;
        if (!state.NeedsBaseline)
            return state;

        var addResult = await RunAsync(
            state.RepositoryRoot,
            WriteTimeout,
            cancellationToken,
            "add",
            "--all").ConfigureAwait(false);

        if (addResult.ExitCode != 0)
            return state with
            {
                Success = false,
                ErrorCode = addResult.TimedOut ? "GIT_BASELINE_ADD_TIMEOUT"
                    : addResult.Canceled ? "GIT_BASELINE_ADD_CANCELED"
                    : "GIT_BASELINE_ADD_FAILED"
            };

        var message = state.HasHead
            ? "ProjectHub baseline before parallel work"
            : "ProjectHub initial baseline";

        var commitResult = await RunAsync(
            state.RepositoryRoot,
            WriteTimeout,
            cancellationToken,
            "-c",
            "user.name=ProjectHub",
            "-c",
            "user.email=projecthub@local",
            "commit",
            "--allow-empty",
            "--no-gpg-sign",
            "-m",
            message).ConfigureAwait(false);

        if (commitResult.ExitCode != 0)
            return state with
            {
                Success = false,
                ErrorCode = commitResult.TimedOut ? "GIT_BASELINE_COMMIT_TIMEOUT"
                    : commitResult.Canceled ? "GIT_BASELINE_COMMIT_CANCELED"
                    : "GIT_BASELINE_COMMIT_FAILED"
            };

        var headResult = await RunAsync(
            state.RepositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);

        if (headResult.ExitCode != 0 || string.IsNullOrWhiteSpace(headResult.StandardOutput))
            return state with { Success = false, ErrorCode = "GIT_BASELINE_HEAD_UNAVAILABLE" };

        var statusResult = await RunAsync(
            state.RepositoryRoot,
            ReadTimeout,
            cancellationToken,
            "status",
            "--porcelain=v1",
            "--untracked-files=all").ConfigureAwait(false);

        if (statusResult.ExitCode != 0)
            return state with { Success = false, ErrorCode = "GIT_BASELINE_STATUS_UNAVAILABLE" };
        if (!string.IsNullOrWhiteSpace(statusResult.StandardOutput))
            return state with
            {
                Success = false,
                ErrorCode = "GIT_BASELINE_NOT_CLEAN",
                HeadCommit = FirstLine(headResult.StandardOutput),
                IsDirty = true
            };

        return state with
        {
            Success = true,
            ErrorCode = null,
            HeadCommit = FirstLine(headResult.StandardOutput),
            IsDirty = false
        };
    }

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        params string[] arguments)
        => _runner.RunAsync(workingDirectory, arguments, timeout, cancellationToken);

    private static GitWorkspaceBootstrapState Failure(string errorCode, string? workingDirectory)
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

    private static string FirstLine(string value)
        => value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? string.Empty;
}
