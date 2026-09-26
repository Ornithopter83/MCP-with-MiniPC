using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProjectHub.Worker;

public sealed record GitCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool Canceled = false);

public interface IGitWorktreeCommandRunner
{
    Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessGitWorktreeCommandRunner : IGitWorktreeCommandRunner
{
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return new GitCommandResult(-1, string.Empty, "GIT_WORKING_DIRECTORY_MISSING");
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

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
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            if (!process.Start())
                return new GitCommandResult(-1, string.Empty, "GIT_PROCESS_START_FAILED");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

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
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                return new GitCommandResult(
                    -1,
                    stdout.Trim(),
                    stderr.Trim(),
                    TimedOut: timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested,
                    Canceled: cancellationToken.IsCancellationRequested);
            }

            return new GitCommandResult(
                process.ExitCode,
                (await stdoutTask.ConfigureAwait(false)).Trim(),
                (await stderrTask.ConfigureAwait(false)).Trim());
        }
        catch (Exception ex)
        {
            return new GitCommandResult(-1, string.Empty, ex.GetType().Name + ": " + ex.Message);
        }
    }
}

public sealed record GitWorktreePreparationResult(
    bool Success,
    string? ErrorCode,
    string RepositoryRoot,
    string WorktreePath,
    string Branch,
    string BaseRef,
    string? BaseCommit,
    string? HeadCommit,
    bool Reused,
    string? ErrorDetail = null);

public sealed record GitWorktreeInspectionResult(
    bool Success,
    string? ErrorCode,
    string WorktreePath,
    string? Branch,
    string? HeadCommit,
    bool IsClean);

public sealed record GitWorktreeRemovalResult(
    bool Success,
    string? ErrorCode,
    string WorktreePath,
    string Branch);

public sealed record GitWorktreeCheckpointResult(
    bool Success,
    string? ErrorCode,
    string WorktreePath,
    string? Branch,
    string? HeadCommit,
    bool CreatedCommit);

public sealed record GitIntegrationLandingResult(
    bool Success,
    string? ErrorCode,
    string RepositoryRoot,
    string IntegrationRef,
    string? IntegrationCommit,
    string? TargetBranch,
    string? BeforeHead,
    string? AfterHead,
    bool FastForwarded);

public sealed class GitWorktreeManager
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CreateTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RemoveTimeout = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryPreparationGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly IGitWorktreeCommandRunner _runner;

    public GitWorktreeManager(IGitWorktreeCommandRunner? runner = null)
    {
        _runner = runner ?? new ProcessGitWorktreeCommandRunner();
    }

    public async Task<GitWorktreePreparationResult> PrepareAsync(
        string workspace,
        string jobId,
        string workItemId,
        string baseRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return Failure("WORKTREE_WORKSPACE_MISSING", workspace, jobId, workItemId, baseRef);
        if (string.IsNullOrWhiteSpace(baseRef))
            return Failure("WORKTREE_BASE_REF_MISSING", workspace, jobId, workItemId, baseRef);
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(workItemId))
            return Failure("WORKTREE_ID_MISSING", workspace, jobId, workItemId, baseRef);

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);

        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return Failure("WORKTREE_GIT_REPOSITORY_REQUIRED", workspace, jobId, workItemId, baseRef);

        var repositoryRoot = Path.GetFullPath(rootResult.StandardOutput.Trim());
        var branch = BuildBranchName(jobId, workItemId);
        var worktreePath = BuildWorktreePath(repositoryRoot, jobId, workItemId);

        var baseResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            baseRef.Trim() + "^{commit}").ConfigureAwait(false);

        if (baseResult.ExitCode != 0 || string.IsNullOrWhiteSpace(baseResult.StandardOutput))
            return new GitWorktreePreparationResult(
                false,
                "WORKTREE_BASE_REF_INVALID",
                repositoryRoot,
                worktreePath,
                branch,
                baseRef.Trim(),
                null,
                null,
                false,
                BuildGitFailureDetail("git rev-parse --verify", baseResult));

        var baseCommit = FirstLine(baseResult.StandardOutput);
        var preparationGate = GetRepositoryPreparationGate(repositoryRoot);
        await preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var listResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "worktree",
                "list",
                "--porcelain").ConfigureAwait(false);

            if (listResult.ExitCode != 0)
                return new GitWorktreePreparationResult(
                    false,
                    "WORKTREE_LIST_FAILED",
                    repositoryRoot,
                    worktreePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    null,
                    false,
                    BuildGitFailureDetail("git worktree list --porcelain", listResult));

            var existing = ParseWorktrees(listResult.StandardOutput)
                .FirstOrDefault(entry => PathsEqual(entry.Path, worktreePath));

            if (existing is not null)
            {
                if (!Directory.Exists(worktreePath))
                    return new GitWorktreePreparationResult(
                        false,
                        "WORKTREE_REGISTERED_PATH_MISSING",
                        repositoryRoot,
                        worktreePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        existing.Head,
                        false);

                if (!string.Equals(existing.Branch, branch, StringComparison.Ordinal))
                    return new GitWorktreePreparationResult(
                        false,
                        "WORKTREE_REGISTERED_BRANCH_MISMATCH",
                        repositoryRoot,
                        worktreePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        existing.Head,
                        false);

                return new GitWorktreePreparationResult(
                    true,
                    null,
                    repositoryRoot,
                    worktreePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    existing.Head,
                    true);
            }

            if (Directory.Exists(worktreePath) || File.Exists(worktreePath))
                return new GitWorktreePreparationResult(
                    false,
                    "WORKTREE_PATH_OCCUPIED",
                    repositoryRoot,
                    worktreePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    null,
                    false);

            var branchResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "show-ref",
                "--verify",
                "--quiet",
                "refs/heads/" + branch).ConfigureAwait(false);

            var reuseExistingBranch = false;
            if (branchResult.ExitCode == 0)
            {
                var branchOwner = ParseWorktrees(listResult.StandardOutput)
                    .FirstOrDefault(entry =>
                        string.Equals(entry.Branch, branch, StringComparison.Ordinal));
                if (branchOwner is not null)
                    return new GitWorktreePreparationResult(
                        false,
                        "WORKTREE_BRANCH_IN_USE",
                        repositoryRoot,
                        worktreePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        branchOwner.Head,
                        false);

                var branchCommitResult = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "rev-parse",
                    "--verify",
                    "refs/heads/" + branch + "^{commit}").ConfigureAwait(false);
                if (branchCommitResult.ExitCode != 0 ||
                    string.IsNullOrWhiteSpace(branchCommitResult.StandardOutput))
                    return new GitWorktreePreparationResult(
                        false,
                        "WORKTREE_BRANCH_CHECK_FAILED",
                        repositoryRoot,
                        worktreePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        null,
                        false,
                        BuildGitFailureDetail("git rev-parse existing WorkItem branch", branchCommitResult));

                var branchCommit = FirstLine(branchCommitResult.StandardOutput);
                if (!string.Equals(branchCommit, baseCommit, StringComparison.OrdinalIgnoreCase))
                    return new GitWorktreePreparationResult(
                        false,
                        "WORKTREE_BRANCH_EXISTS",
                        repositoryRoot,
                        worktreePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        branchCommit,
                        false,
                        $"기존 WorkItem branch가 현재 base와 다릅니다. branchCommit={branchCommit} baseCommit={baseCommit}");

                reuseExistingBranch = true;
            }
            else if (branchResult.ExitCode != 1)
            {
                return new GitWorktreePreparationResult(
                    false,
                    "WORKTREE_BRANCH_CHECK_FAILED",
                    repositoryRoot,
                    worktreePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    null,
                    false,
                    BuildGitFailureDetail("git show-ref --verify", branchResult));
            }

            var parent = Directory.GetParent(worktreePath)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
                return new GitWorktreePreparationResult(
                    false,
                    "WORKTREE_PARENT_INVALID",
                    repositoryRoot,
                    worktreePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    null,
                    false);

            Directory.CreateDirectory(parent);

            var addResult = reuseExistingBranch
                ? await RunAsync(
                    repositoryRoot,
                    CreateTimeout,
                    cancellationToken,
                    "worktree",
                    "add",
                    worktreePath,
                    branch).ConfigureAwait(false)
                : await RunAsync(
                    repositoryRoot,
                    CreateTimeout,
                    cancellationToken,
                    "worktree",
                    "add",
                    "-b",
                    branch,
                    worktreePath,
                    baseCommit).ConfigureAwait(false);

            if (addResult.ExitCode != 0)
                return new GitWorktreePreparationResult(
                    false,
                    addResult.TimedOut ? "WORKTREE_CREATE_TIMEOUT"
                        : addResult.Canceled ? "WORKTREE_CREATE_CANCELED"
                        : "WORKTREE_CREATE_FAILED",
                    repositoryRoot,
                    worktreePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    null,
                    false,
                    BuildGitFailureDetail("git worktree add", addResult));

            var headResult = await RunAsync(
                worktreePath,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD").ConfigureAwait(false);

            return new GitWorktreePreparationResult(
                true,
                null,
                repositoryRoot,
                worktreePath,
                branch,
                baseRef.Trim(),
                baseCommit,
                headResult.ExitCode == 0 ? FirstLine(headResult.StandardOutput) : baseCommit,
                false);
        }
        finally
        {
            preparationGate.Release();
        }
    }

    public async Task<GitWorktreeInspectionResult> InspectAsync(
        string worktreePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(worktreePath) || !Directory.Exists(worktreePath))
            return new(false, "WORKTREE_PATH_MISSING", worktreePath ?? string.Empty, null, null, false);

        var headResult = await RunAsync(
            worktreePath,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);

        if (headResult.ExitCode != 0)
            return new(false, "WORKTREE_HEAD_UNAVAILABLE", worktreePath, null, null, false);

        var branchResult = await RunAsync(
            worktreePath,
            ReadTimeout,
            cancellationToken,
            "symbolic-ref",
            "--quiet",
            "--short",
            "HEAD").ConfigureAwait(false);

        if (branchResult.ExitCode != 0 || string.IsNullOrWhiteSpace(branchResult.StandardOutput))
            return new(false, "WORKTREE_BRANCH_UNAVAILABLE", worktreePath, null, FirstLine(headResult.StandardOutput), false);

        var statusResult = await RunAsync(
            worktreePath,
            ReadTimeout,
            cancellationToken,
            "status",
            "--porcelain=v1",
            "--untracked-files=all").ConfigureAwait(false);

        if (statusResult.ExitCode != 0)
            return new(false, "WORKTREE_STATUS_UNAVAILABLE", worktreePath, FirstLine(branchResult.StandardOutput), FirstLine(headResult.StandardOutput), false);

        return new(
            true,
            null,
            Path.GetFullPath(worktreePath),
            FirstLine(branchResult.StandardOutput),
            FirstLine(headResult.StandardOutput),
            string.IsNullOrWhiteSpace(statusResult.StandardOutput));
    }

    public async Task<GitWorktreeCheckpointResult> CreateCheckpointAsync(
        string worktreePath,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workItemId))
            return new(false, "WORKTREE_CHECKPOINT_ID_MISSING", worktreePath, null, null, false);

        var before = await InspectAsync(worktreePath, cancellationToken).ConfigureAwait(false);
        if (!before.Success)
            return new(false, before.ErrorCode, worktreePath, before.Branch, before.HeadCommit, false);

        if (before.IsClean)
            return new(true, null, before.WorktreePath, before.Branch, before.HeadCommit, false);

        var addResult = await RunAsync(
            worktreePath,
            ReadTimeout,
            cancellationToken,
            "add",
            "--all").ConfigureAwait(false);

        if (addResult.ExitCode != 0)
        {
            return new(
                false,
                addResult.TimedOut ? "WORKTREE_CHECKPOINT_ADD_TIMEOUT"
                    : addResult.Canceled ? "WORKTREE_CHECKPOINT_ADD_CANCELED"
                    : "WORKTREE_CHECKPOINT_ADD_FAILED",
                worktreePath,
                before.Branch,
                before.HeadCommit,
                false);
        }

        var message = "ProjectHub WorkItem " + SafeCommitLabel(workItemId) + " checkpoint";
        var commitResult = await RunAsync(
            worktreePath,
            CreateTimeout,
            cancellationToken,
            "-c",
            "user.name=ProjectHub",
            "-c",
            "user.email=projecthub@local",
            "commit",
            "--no-gpg-sign",
            "-m",
            message).ConfigureAwait(false);

        if (commitResult.ExitCode != 0)
        {
            return new(
                false,
                commitResult.TimedOut ? "WORKTREE_CHECKPOINT_COMMIT_TIMEOUT"
                    : commitResult.Canceled ? "WORKTREE_CHECKPOINT_COMMIT_CANCELED"
                    : "WORKTREE_CHECKPOINT_COMMIT_FAILED",
                worktreePath,
                before.Branch,
                before.HeadCommit,
                false);
        }

        var after = await InspectAsync(worktreePath, cancellationToken).ConfigureAwait(false);
        if (!after.Success)
            return new(false, after.ErrorCode, worktreePath, after.Branch, after.HeadCommit, true);
        if (!after.IsClean)
            return new(false, "WORKTREE_CHECKPOINT_NOT_CLEAN", worktreePath, after.Branch, after.HeadCommit, true);

        return new(true, null, after.WorktreePath, after.Branch, after.HeadCommit, true);
    }

    public Task<GitIntegrationLandingResult> LandIntegrationAsync(
        string workspace,
        string integrationRef,
        CancellationToken cancellationToken = default)
        => LandIntegrationAsync(
            workspace,
            integrationRef,
            expectedTargetBranch: null,
            cancellationToken);

    public async Task<GitIntegrationLandingResult> LandIntegrationAsync(
        string workspace,
        string integrationRef,
        string? expectedTargetBranch,
        CancellationToken cancellationToken = default)
    {
        var normalizedRef = integrationRef?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return new(false, "INTEGRATION_TARGET_WORKSPACE_MISSING", string.Empty, normalizedRef, null, null, null, null, false);
        if (string.IsNullOrWhiteSpace(normalizedRef))
            return new(false, "INTEGRATION_RESULT_REF_MISSING", Path.GetFullPath(workspace), normalizedRef, null, null, null, null, false);

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);

        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return new(false, "INTEGRATION_TARGET_REPOSITORY_REQUIRED", Path.GetFullPath(workspace), normalizedRef, null, null, null, null, false);

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));

        var statusBefore = await ReadPrimaryWorkspaceStatusAsync(
            repositoryRoot,
            workspace,
            cancellationToken).ConfigureAwait(false);

        if (statusBefore.ExitCode != 0)
            return new(false, "INTEGRATION_TARGET_STATUS_UNAVAILABLE", repositoryRoot, normalizedRef, null, null, null, null, false);
        if (!string.IsNullOrWhiteSpace(statusBefore.StandardOutput))
            return new(false, "INTEGRATION_TARGET_DIRTY", repositoryRoot, normalizedRef, null, null, null, null, false);

        var branchResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "symbolic-ref",
            "--quiet",
            "--short",
            "HEAD").ConfigureAwait(false);

        var targetBranch = branchResult.ExitCode == 0
            ? FirstLine(branchResult.StandardOutput)
            : null;
        if (string.IsNullOrWhiteSpace(targetBranch))
            return new(false, "INTEGRATION_TARGET_BRANCH_REQUIRED", repositoryRoot, normalizedRef, null, null, null, null, false);

        var expectedBranch = string.IsNullOrWhiteSpace(expectedTargetBranch)
            ? null
            : expectedTargetBranch.Trim();
        if (expectedBranch is not null &&
            !string.Equals(targetBranch, expectedBranch, StringComparison.Ordinal))
        {
            return new(
                false,
                "INTEGRATION_TARGET_BRANCH_CHANGED",
                repositoryRoot,
                normalizedRef,
                null,
                targetBranch,
                null,
                null,
                false);
        }

        var headBeforeResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);

        var beforeHead = headBeforeResult.ExitCode == 0
            ? FirstLine(headBeforeResult.StandardOutput)
            : null;
        if (string.IsNullOrWhiteSpace(beforeHead))
            return new(false, "INTEGRATION_TARGET_HEAD_UNAVAILABLE", repositoryRoot, normalizedRef, null, targetBranch, null, null, false);

        var integrationResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            normalizedRef + "^{commit}").ConfigureAwait(false);

        var integrationCommit = integrationResult.ExitCode == 0
            ? FirstLine(integrationResult.StandardOutput)
            : null;
        if (string.IsNullOrWhiteSpace(integrationCommit))
            return new(false, "INTEGRATION_RESULT_REF_INVALID", repositoryRoot, normalizedRef, null, targetBranch, beforeHead, null, false);

        if (string.Equals(beforeHead, integrationCommit, StringComparison.OrdinalIgnoreCase))
        {
            return new(
                true,
                null,
                repositoryRoot,
                normalizedRef,
                integrationCommit,
                targetBranch,
                beforeHead,
                beforeHead,
                false);
        }

        var ancestorResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "merge-base",
            "--is-ancestor",
            beforeHead,
            integrationCommit).ConfigureAwait(false);

        if (ancestorResult.ExitCode == 1)
        {
            return new(
                false,
                "INTEGRATION_NOT_FAST_FORWARD",
                repositoryRoot,
                normalizedRef,
                integrationCommit,
                targetBranch,
                beforeHead,
                beforeHead,
                false);
        }

        if (ancestorResult.ExitCode != 0)
        {
            return new(
                false,
                "INTEGRATION_ANCESTRY_CHECK_FAILED",
                repositoryRoot,
                normalizedRef,
                integrationCommit,
                targetBranch,
                beforeHead,
                beforeHead,
                false);
        }

        var mergeResult = await RunAsync(
            repositoryRoot,
            CreateTimeout,
            cancellationToken,
            "merge",
            "--ff-only",
            integrationCommit).ConfigureAwait(false);

        if (mergeResult.ExitCode != 0)
        {
            return new(
                false,
                mergeResult.TimedOut ? "INTEGRATION_FAST_FORWARD_TIMEOUT"
                    : mergeResult.Canceled ? "INTEGRATION_FAST_FORWARD_CANCELED"
                    : "INTEGRATION_FAST_FORWARD_FAILED",
                repositoryRoot,
                normalizedRef,
                integrationCommit,
                targetBranch,
                beforeHead,
                beforeHead,
                false);
        }

        var headAfterResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);

        var afterHead = headAfterResult.ExitCode == 0
            ? FirstLine(headAfterResult.StandardOutput)
            : null;
        if (string.IsNullOrWhiteSpace(afterHead))
        {
            return new(
                false,
                "INTEGRATION_TARGET_HEAD_UNAVAILABLE",
                repositoryRoot,
                normalizedRef,
                integrationCommit,
                targetBranch,
                beforeHead,
                null,
                true);
        }

        if (!string.Equals(afterHead, integrationCommit, StringComparison.OrdinalIgnoreCase))
        {
            return new(
                false,
                "INTEGRATION_TARGET_HEAD_MISMATCH",
                repositoryRoot,
                normalizedRef,
                integrationCommit,
                targetBranch,
                beforeHead,
                afterHead,
                true);
        }

        var statusAfter = await ReadPrimaryWorkspaceStatusAsync(
            repositoryRoot,
            workspace,
            cancellationToken).ConfigureAwait(false);

        if (statusAfter.ExitCode != 0)
        {
            return new(
                false,
                "INTEGRATION_TARGET_STATUS_UNAVAILABLE",
                repositoryRoot,
                normalizedRef,
                integrationCommit,
                targetBranch,
                beforeHead,
                afterHead,
                true);
        }

        if (!string.IsNullOrWhiteSpace(statusAfter.StandardOutput))
        {
            return new(
                false,
                "INTEGRATION_TARGET_NOT_CLEAN",
                repositoryRoot,
                normalizedRef,
                integrationCommit,
                targetBranch,
                beforeHead,
                afterHead,
                true);
        }

        return new(
            true,
            null,
            repositoryRoot,
            normalizedRef,
            integrationCommit,
            targetBranch,
            beforeHead,
            afterHead,
            true);
    }

    public async Task<GitWorktreeRemovalResult> RemoveAsync(
        string repositoryRoot,
        string worktreePath,
        string branch,
        CancellationToken cancellationToken = default)
    {
        var inspection = await InspectAsync(worktreePath, cancellationToken).ConfigureAwait(false);
        if (!inspection.Success)
            return new(false, inspection.ErrorCode, worktreePath, branch);
        if (!inspection.IsClean)
            return new(false, "WORKTREE_DIRTY", worktreePath, branch);
        if (!string.Equals(inspection.Branch, branch, StringComparison.Ordinal))
            return new(false, "WORKTREE_BRANCH_MISMATCH", worktreePath, branch);

        var removeResult = await RunAsync(
            repositoryRoot,
            RemoveTimeout,
            cancellationToken,
            "worktree",
            "remove",
            Path.GetFullPath(worktreePath)).ConfigureAwait(false);

        return removeResult.ExitCode == 0
            ? new(true, null, worktreePath, branch)
            : new(
                false,
                removeResult.TimedOut ? "WORKTREE_REMOVE_TIMEOUT"
                    : removeResult.Canceled ? "WORKTREE_REMOVE_CANCELED"
                    : "WORKTREE_REMOVE_FAILED",
                worktreePath,
                branch);
    }

    public static string BuildBranchName(string jobId, string workItemId)
        => "projecthub/" + StableSegment(jobId, 36) + "/" + StableSegment(workItemId, 36);

    public static string BuildWorktreePath(string repositoryRoot, string jobId, string workItemId)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var parent = Directory.GetParent(root)?.FullName
            ?? throw new InvalidOperationException("저장소 상위 경로를 계산할 수 없습니다.");
        var repository = StableSegment(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), 36);

        return Path.Combine(
            parent,
            ".projecthub-worktrees",
            repository,
            StableSegment(jobId, 36),
            StableSegment(workItemId, 36));
    }

    private Task<GitCommandResult> ReadPrimaryWorkspaceStatusAsync(
        string repositoryRoot,
        string workspace,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "status",
            "--porcelain=v1",
            "--untracked-files=all",
            "--",
            "."
        };

        var stateDirectory = Path.Combine(
            Path.GetFullPath(workspace),
            ".projecthub");
        var relativeState = Path.GetRelativePath(
                Path.GetFullPath(repositoryRoot),
                stateDirectory)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        if (!string.Equals(relativeState, "..", StringComparison.Ordinal) &&
            !relativeState.StartsWith("../", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relativeState))
        {
            arguments.Add(":(exclude)" + relativeState);
            arguments.Add(":(exclude)" + relativeState.TrimEnd('/') + "/**");
        }

        return _runner.RunAsync(
            repositoryRoot,
            arguments,
            ReadTimeout,
            cancellationToken);
    }

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        params string[] arguments)
        => _runner.RunAsync(workingDirectory, arguments, timeout, cancellationToken);

    private static SemaphoreSlim GetRepositoryPreparationGate(string repositoryRoot)
    {
        var key = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return RepositoryPreparationGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

    private static string BuildGitFailureDetail(string command, GitCommandResult result)
    {
        var detail = $"{command} 실패: exitCode={result.ExitCode}";
        if (result.TimedOut)
            detail += " timedOut=true";
        if (result.Canceled)
            detail += " canceled=true";
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            detail += Environment.NewLine + "stderr:" + Environment.NewLine + LimitDiagnostic(result.StandardError);
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            detail += Environment.NewLine + "stdout:" + Environment.NewLine + LimitDiagnostic(result.StandardOutput);
        return detail;
    }

    private static string LimitDiagnostic(string value)
    {
        const int limit = 8000;
        var normalized = value.Trim();
        return normalized.Length <= limit
            ? normalized
            : normalized[..limit] + Environment.NewLine + "...(truncated)";
    }

    private static GitWorktreePreparationResult Failure(
        string errorCode,
        string workspace,
        string jobId,
        string workItemId,
        string? baseRef)
    {
        var fallbackRoot = string.IsNullOrWhiteSpace(workspace)
            ? string.Empty
            : Path.GetFullPath(workspace);
        var branch = string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(workItemId)
            ? string.Empty
            : BuildBranchName(jobId, workItemId);

        string worktreePath;
        try
        {
            worktreePath = string.IsNullOrWhiteSpace(fallbackRoot) || string.IsNullOrWhiteSpace(branch)
                ? string.Empty
                : BuildWorktreePath(fallbackRoot, jobId, workItemId);
        }
        catch
        {
            worktreePath = string.Empty;
        }

        return new(
            false,
            errorCode,
            fallbackRoot,
            worktreePath,
            branch,
            baseRef?.Trim() ?? string.Empty,
            null,
            null,
            false);
    }

    private static string FirstLine(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<WorktreeEntry> ParseWorktrees(string text)
    {
        var result = new List<WorktreeEntry>();
        string? path = null;
        string? head = null;
        string? branch = null;

        void Flush()
        {
            if (!string.IsNullOrWhiteSpace(path))
                result.Add(new WorktreeEntry(path, head, branch));
            path = null;
            head = null;
            branch = null;
        }

        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
                path = line["worktree ".Length..].Trim();
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
                head = line["HEAD ".Length..].Trim();
            else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal))
                branch = line["branch refs/heads/".Length..].Trim();
        }

        Flush();
        return result;
    }

    private static string SafeCommitLabel(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                builder.Append(character);
            else
                builder.Append('-');
            if (builder.Length >= 48)
                break;
        }

        return builder.Length == 0 ? "item" : builder.ToString();
    }

    private static string StableSegment(string value, int maxReadableLength)
    {
        var original = value?.Trim() ?? string.Empty;
        var readable = new StringBuilder();

        foreach (var character in original)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                readable.Append(character);
            else if (readable.Length == 0 || readable[^1] != '-')
                readable.Append('-');
        }

        var cleaned = readable.ToString().Trim('-', '.', '_');
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "item";
        if (cleaned.Length > maxReadableLength)
            cleaned = cleaned[..maxReadableLength].TrimEnd('-', '.', '_');

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original)))[..8].ToLowerInvariant();
        return cleaned + "-" + hash;
    }

    private sealed record WorktreeEntry(string Path, string? Head, string? Branch);
}
