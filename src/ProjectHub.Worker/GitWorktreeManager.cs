using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Tar;
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

                try
                {
                    using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
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

public sealed record GitRepositoryRuntimeCleanupResult(
    bool Success,
    string? ErrorCode,
    string RuntimeRoot,
    IReadOnlyList<string> RemovedWorktrees,
    bool RuntimeDeleted,
    string? ErrorDetail = null);

public sealed record GitIntegrationCloneCleanupResult(
    bool Success,
    string? ErrorCode,
    string ClonePath,
    string? ErrorDetail = null);

public sealed record GitWorktreeCheckpointResult(
    bool Success,
    string? ErrorCode,
    string WorktreePath,
    string? Branch,
    string? HeadCommit,
    bool CreatedCommit);

public sealed record GitNormalBaseResolutionResult(
    bool Success,
    string? ErrorCode,
    string? EffectiveBaseRef,
    string? ErrorDetail = null);

public sealed record GitIntegrationDependencyStageResult(
    bool Success,
    string? ErrorCode,
    IReadOnlyDictionary<string, string> SnapshotPaths,
    string? ErrorDetail = null);

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

public sealed record GitTargetContainmentResult(
    bool Success,
    string? ErrorCode,
    string RepositoryRoot,
    string ResultRef,
    string? ResultCommit,
    string? TargetBranch,
    string? TargetHead,
    bool IsContained);

public sealed class GitWorktreeManager
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CreateTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RemoveTimeout = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryPreparationGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryPrimaryMutationGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly IGitWorktreeCommandRunner _runner;

    public GitWorktreeManager(IGitWorktreeCommandRunner? runner = null)
    {
        _runner = runner ?? new ProcessGitWorktreeCommandRunner();
    }

    public async Task<GitNormalBaseResolutionResult> ResolveNormalBaseRefAsync(
        string workspace,
        string declaredBaseRef,
        IReadOnlyList<string>? codeDependencyRefs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return new(false, "WORKTREE_WORKSPACE_MISSING", null, "작업공간이 존재하지 않습니다.");
        if (string.IsNullOrWhiteSpace(declaredBaseRef))
            return new(false, "WORKTREE_BASE_REF_MISSING", null, "WorkItem baseRef가 없습니다.");

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);

        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return new(
                false,
                "WORKTREE_GIT_REPOSITORY_REQUIRED",
                null,
                BuildGitFailureDetail("git rev-parse --show-toplevel", rootResult));

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        var references = new List<(string Label, string Value)>
        {
            ("baseRef", declaredBaseRef.Trim())
        };
        foreach (var dependencyRef in (codeDependencyRefs ?? Array.Empty<string>())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim())
                     .Distinct(StringComparer.Ordinal))
        {
            references.Add(("dependency", dependencyRef));
        }

        var tips = new List<string>();
        foreach (var reference in references)
        {
            var resolve = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                reference.Value + "^{commit}").ConfigureAwait(false);

            if (resolve.ExitCode != 0 || string.IsNullOrWhiteSpace(resolve.StandardOutput))
            {
                var errorCode = reference.Label == "baseRef"
                    ? "WORKTREE_BASE_REF_INVALID"
                    : "NORMAL_CODE_DEPENDENCY_REF_INVALID";
                return new(
                    false,
                    errorCode,
                    null,
                    BuildGitFailureDetail(
                        $"git rev-parse --verify {reference.Label}",
                        resolve));
            }

            var commit = FirstLine(resolve.StandardOutput);
            var coveredByExistingTip = false;
            for (var index = tips.Count - 1; index >= 0; index--)
            {
                var existing = tips[index];
                if (string.Equals(existing, commit, StringComparison.OrdinalIgnoreCase))
                {
                    coveredByExistingTip = true;
                    break;
                }

                var existingAncestor = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "merge-base",
                    "--is-ancestor",
                    existing,
                    commit).ConfigureAwait(false);

                if (existingAncestor.ExitCode == 0)
                {
                    tips.RemoveAt(index);
                    continue;
                }

                if (existingAncestor.ExitCode != 1)
                {
                    return new(
                        false,
                        "NORMAL_CODE_DEPENDENCY_ANCESTRY_FAILED",
                        null,
                        BuildGitFailureDetail("git merge-base --is-ancestor", existingAncestor));
                }

                var newAncestor = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "merge-base",
                    "--is-ancestor",
                    commit,
                    existing).ConfigureAwait(false);

                if (newAncestor.ExitCode == 0)
                {
                    coveredByExistingTip = true;
                    break;
                }

                if (newAncestor.ExitCode != 1)
                {
                    return new(
                        false,
                        "NORMAL_CODE_DEPENDENCY_ANCESTRY_FAILED",
                        null,
                        BuildGitFailureDetail("git merge-base --is-ancestor", newAncestor));
                }
            }

            if (!coveredByExistingTip)
                tips.Add(commit);
        }

        if (tips.Count != 1)
        {
            return new(
                false,
                "NORMAL_MULTIPLE_CODE_BASES_REQUIRE_INTEGRATION",
                null,
                "NORMAL WorkItem이 하나의 코드 기준점으로 축약할 수 없는 서로 독립된 CODE_CHANGE 계보를 참조합니다. tips=" +
                string.Join(",", tips));
        }

        return new(true, null, tips[0]);
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

            var legacyWorktreePath = BuildLegacyWorktreePath(repositoryRoot, jobId, workItemId);
            var legacyExisting = ParseWorktrees(listResult.StandardOutput)
                .FirstOrDefault(entry =>
                    PathsEqual(entry.Path, legacyWorktreePath) &&
                    string.Equals(entry.Branch, branch, StringComparison.Ordinal));
            if (legacyExisting is not null && Directory.Exists(legacyWorktreePath))
            {
                return new GitWorktreePreparationResult(
                    true,
                    null,
                    repositoryRoot,
                    legacyWorktreePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    legacyExisting.Head,
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

    public async Task<GitWorktreePreparationResult> PrepareIntegrationAsync(
        string workspace,
        string jobId,
        string workItemId,
        string? expectedTargetBranch,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return IntegrationFailure("WORKTREE_WORKSPACE_MISSING", workspace, jobId, workItemId, null);
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(workItemId))
            return IntegrationFailure("WORKTREE_ID_MISSING", workspace, jobId, workItemId, null);

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);

        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return IntegrationFailure("WORKTREE_GIT_REPOSITORY_REQUIRED", workspace, jobId, workItemId, null);

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        var branch = BuildBranchName(jobId, workItemId);
        var clonePath = BuildIntegrationClonePath(repositoryRoot, jobId, workItemId);
        var primaryGate = GetRepositoryPrimaryMutationGate(repositoryRoot);
        await primaryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var statusResult = await ReadPrimaryWorkspaceStatusAsync(
                repositoryRoot,
                workspace,
                cancellationToken).ConfigureAwait(false);

            if (statusResult.ExitCode != 0)
                return IntegrationFailure(
                    "INTEGRATION_BASE_STATUS_UNAVAILABLE",
                    repositoryRoot,
                    jobId,
                    workItemId,
                    null);
            if (!string.IsNullOrWhiteSpace(statusResult.StandardOutput))
                return IntegrationFailure(
                    "INTEGRATION_BASE_TARGET_DIRTY",
                    repositoryRoot,
                    jobId,
                    workItemId,
                    null);

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
                return IntegrationFailure(
                    "INTEGRATION_BASE_BRANCH_REQUIRED",
                    repositoryRoot,
                    jobId,
                    workItemId,
                    null);

            var expectedBranch = string.IsNullOrWhiteSpace(expectedTargetBranch)
                ? null
                : expectedTargetBranch.Trim();
            if (expectedBranch is not null &&
                !string.Equals(targetBranch, expectedBranch, StringComparison.Ordinal))
            {
                return IntegrationFailure(
                    "INTEGRATION_BASE_BRANCH_CHANGED",
                    repositoryRoot,
                    jobId,
                    workItemId,
                    null);
            }

            var headResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD").ConfigureAwait(false);

            var primaryHead = headResult.ExitCode == 0
                ? FirstLine(headResult.StandardOutput)
                : null;
            if (string.IsNullOrWhiteSpace(primaryHead))
                return IntegrationFailure(
                    "INTEGRATION_BASE_HEAD_UNAVAILABLE",
                    repositoryRoot,
                    jobId,
                    workItemId,
                    null);

            if (Directory.Exists(clonePath) || File.Exists(clonePath))
            {
                return new GitWorktreePreparationResult(
                    false,
                    "INTEGRATION_CLONE_PATH_OCCUPIED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    primaryHead,
                    primaryHead,
                    null,
                    false);
            }

            var parent = Directory.GetParent(clonePath)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
            {
                return new GitWorktreePreparationResult(
                    false,
                    "INTEGRATION_CLONE_PARENT_INVALID",
                    repositoryRoot,
                    clonePath,
                    branch,
                    primaryHead,
                    primaryHead,
                    null,
                    false);
            }

            Directory.CreateDirectory(parent);

            var cloneResult = await RunAsync(
                repositoryRoot,
                CreateTimeout,
                cancellationToken,
                "clone",
                "--no-hardlinks",
                "--no-checkout",
                repositoryRoot,
                clonePath).ConfigureAwait(false);

            if (cloneResult.ExitCode != 0)
            {
                return new GitWorktreePreparationResult(
                    false,
                    cloneResult.TimedOut ? "INTEGRATION_CLONE_CREATE_TIMEOUT"
                        : cloneResult.Canceled ? "INTEGRATION_CLONE_CREATE_CANCELED"
                        : "INTEGRATION_CLONE_CREATE_FAILED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    primaryHead,
                    primaryHead,
                    null,
                    false,
                    BuildGitFailureDetail("git clone --no-hardlinks --no-checkout", cloneResult));
            }

            var checkoutResult = await RunAsync(
                clonePath,
                CreateTimeout,
                cancellationToken,
                "checkout",
                "-b",
                branch,
                primaryHead).ConfigureAwait(false);

            if (checkoutResult.ExitCode != 0)
            {
                return new GitWorktreePreparationResult(
                    false,
                    "INTEGRATION_CLONE_CHECKOUT_FAILED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    primaryHead,
                    primaryHead,
                    null,
                    false,
                    BuildGitFailureDetail("git checkout -b", checkoutResult));
            }

            var userNameResult = await RunAsync(
                clonePath,
                ReadTimeout,
                cancellationToken,
                "config",
                "user.name",
                "ProjectHub").ConfigureAwait(false);
            if (userNameResult.ExitCode != 0)
            {
                return new GitWorktreePreparationResult(
                    false,
                    "INTEGRATION_CLONE_GIT_IDENTITY_FAILED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    primaryHead,
                    primaryHead,
                    null,
                    false,
                    BuildGitFailureDetail("git config user.name", userNameResult));
            }

            var userEmailResult = await RunAsync(
                clonePath,
                ReadTimeout,
                cancellationToken,
                "config",
                "user.email",
                "projecthub@local").ConfigureAwait(false);
            if (userEmailResult.ExitCode != 0)
            {
                return new GitWorktreePreparationResult(
                    false,
                    "INTEGRATION_CLONE_GIT_IDENTITY_FAILED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    primaryHead,
                    primaryHead,
                    null,
                    false,
                    BuildGitFailureDetail("git config user.email", userEmailResult));
            }

            var gitDirResult = await RunAsync(
                clonePath,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--absolute-git-dir").ConfigureAwait(false);

            var expectedGitDir = Path.GetFullPath(Path.Combine(clonePath, ".git"));
            var actualGitDir = gitDirResult.ExitCode == 0 &&
                               !string.IsNullOrWhiteSpace(gitDirResult.StandardOutput)
                ? Path.GetFullPath(FirstLine(gitDirResult.StandardOutput))
                : null;
            if (actualGitDir is null ||
                !PathsEqual(actualGitDir, expectedGitDir))
            {
                return new GitWorktreePreparationResult(
                    false,
                    "INTEGRATION_CLONE_GITDIR_NOT_LOCAL",
                    repositoryRoot,
                    clonePath,
                    branch,
                    primaryHead,
                    primaryHead,
                    null,
                    false);
            }

            var cloneHeadResult = await RunAsync(
                clonePath,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD").ConfigureAwait(false);

            var cloneHead = cloneHeadResult.ExitCode == 0
                ? FirstLine(cloneHeadResult.StandardOutput)
                : null;
            if (string.IsNullOrWhiteSpace(cloneHead) ||
                !string.Equals(cloneHead, primaryHead, StringComparison.OrdinalIgnoreCase))
            {
                return new GitWorktreePreparationResult(
                    false,
                    "INTEGRATION_CLONE_HEAD_MISMATCH",
                    repositoryRoot,
                    clonePath,
                    branch,
                    primaryHead,
                    primaryHead,
                    cloneHead,
                    false);
            }

            return new GitWorktreePreparationResult(
                true,
                null,
                repositoryRoot,
                clonePath,
                branch,
                primaryHead,
                primaryHead,
                cloneHead,
                false);
        }
        finally
        {
            primaryGate.Release();
        }
    }

    public async Task<GitWorktreePreparationResult> ResumeIntegrationAsync(
        string workspace,
        string clonePath,
        string? expectedBranch,
        string? baseRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return Failure("WORKTREE_WORKSPACE_MISSING", workspace, string.Empty, string.Empty, baseRef);
        if (string.IsNullOrWhiteSpace(clonePath) || !Directory.Exists(clonePath))
            return Failure("INTEGRATION_CLONE_PATH_MISSING", workspace, string.Empty, string.Empty, baseRef);

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);
        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return Failure("WORKTREE_GIT_REPOSITORY_REQUIRED", workspace, string.Empty, string.Empty, baseRef);

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        var normalizedClone = Path.GetFullPath(clonePath);

        var cloneRootResult = await RunAsync(
            normalizedClone,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);
        var cloneRoot = cloneRootResult.ExitCode == 0 &&
                        !string.IsNullOrWhiteSpace(cloneRootResult.StandardOutput)
            ? Path.GetFullPath(FirstLine(cloneRootResult.StandardOutput))
            : null;
        if (cloneRoot is null || !PathsEqual(cloneRoot, normalizedClone))
        {
            return new GitWorktreePreparationResult(
                false,
                "INTEGRATION_CLONE_REPOSITORY_INVALID",
                repositoryRoot,
                normalizedClone,
                expectedBranch ?? string.Empty,
                baseRef ?? string.Empty,
                null,
                null,
                true);
        }

        var gitDirResult = await RunAsync(
            normalizedClone,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--absolute-git-dir").ConfigureAwait(false);
        var expectedGitDir = Path.GetFullPath(Path.Combine(normalizedClone, ".git"));
        var actualGitDir = gitDirResult.ExitCode == 0 &&
                           !string.IsNullOrWhiteSpace(gitDirResult.StandardOutput)
            ? Path.GetFullPath(FirstLine(gitDirResult.StandardOutput))
            : null;
        if (actualGitDir is null || !PathsEqual(actualGitDir, expectedGitDir))
        {
            return new GitWorktreePreparationResult(
                false,
                "INTEGRATION_CLONE_GITDIR_NOT_LOCAL",
                repositoryRoot,
                normalizedClone,
                expectedBranch ?? string.Empty,
                baseRef ?? string.Empty,
                null,
                null,
                true);
        }

        var branchResult = await RunAsync(
            normalizedClone,
            ReadTimeout,
            cancellationToken,
            "symbolic-ref",
            "--quiet",
            "--short",
            "HEAD").ConfigureAwait(false);
        var branch = branchResult.ExitCode == 0
            ? FirstLine(branchResult.StandardOutput)
            : null;
        if (string.IsNullOrWhiteSpace(branch))
        {
            return new GitWorktreePreparationResult(
                false,
                "INTEGRATION_CLONE_BRANCH_UNAVAILABLE",
                repositoryRoot,
                normalizedClone,
                expectedBranch ?? string.Empty,
                baseRef ?? string.Empty,
                null,
                null,
                true);
        }

        if (!string.IsNullOrWhiteSpace(expectedBranch) &&
            !string.Equals(branch, expectedBranch.Trim(), StringComparison.Ordinal))
        {
            return new GitWorktreePreparationResult(
                false,
                "INTEGRATION_CLONE_BRANCH_CHANGED",
                repositoryRoot,
                normalizedClone,
                branch,
                baseRef ?? string.Empty,
                null,
                null,
                true);
        }

        var headResult = await RunAsync(
            normalizedClone,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);
        var head = headResult.ExitCode == 0
            ? FirstLine(headResult.StandardOutput)
            : null;
        if (string.IsNullOrWhiteSpace(head))
        {
            return new GitWorktreePreparationResult(
                false,
                "INTEGRATION_CLONE_HEAD_UNAVAILABLE",
                repositoryRoot,
                normalizedClone,
                branch,
                baseRef ?? string.Empty,
                null,
                null,
                true);
        }

        var normalizedBase = string.IsNullOrWhiteSpace(baseRef)
            ? head
            : baseRef.Trim();

        return new GitWorktreePreparationResult(
            true,
            null,
            repositoryRoot,
            normalizedClone,
            branch,
            normalizedBase,
            normalizedBase,
            head,
            true);
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
            BuildCheckpointAddArguments()).ConfigureAwait(false);

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

    public Task<GitCommitManifestResult> CreateCommitManifestAsync(
        string worktreePath,
        string workspace,
        string jobId,
        string workItemId,
        string commit,
        CancellationToken cancellationToken = default)
        => new GitCommitManifestBuilder(_runner).BuildAsync(
            worktreePath,
            workspace,
            jobId,
            workItemId,
            commit,
            cancellationToken);

    public async Task<GitIntegrationDependencyStageResult> StageIntegrationDependenciesAsync(
        string clonePath,
        IReadOnlyList<WorkItemDependencyResult> dependencies,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clonePath) || !Directory.Exists(clonePath))
        {
            return new(
                false,
                "INTEGRATION_INPUT_CLONE_MISSING",
                new Dictionary<string, string>(),
                "Integration clone을 찾을 수 없습니다.");
        }

        var codeDependencies = (dependencies ?? Array.Empty<WorkItemDependencyResult>())
            .Where(dependency => dependency.ResultType == WorkItemResultType.CodeChange)
            .ToArray();
        if (codeDependencies.Length == 0)
        {
            return new(
                true,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        var normalizedClone = Path.GetFullPath(clonePath);
        var gitDirectory = Path.Combine(normalizedClone, ".git");
        if (!Directory.Exists(gitDirectory))
        {
            return new(
                false,
                "INTEGRATION_INPUT_GIT_METADATA_MISSING",
                new Dictionary<string, string>(),
                gitDirectory);
        }

        try
        {
            EnsureIntegrationInputExclude(gitDirectory);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new(
                false,
                "INTEGRATION_INPUT_EXCLUDE_FAILED",
                new Dictionary<string, string>(),
                exception.Message);
        }

        var inputRoot = Path.Combine(
            normalizedClone,
            ".projecthub-integration-inputs");
        Directory.CreateDirectory(inputRoot);

        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dependency in codeDependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.ResultRef))
            {
                return new(
                    false,
                    "INTEGRATION_INPUT_RESULT_REF_MISSING",
                    paths,
                    $"WorkItem {dependency.WorkItemId}의 CODE_CHANGE resultRef가 없습니다.");
            }

            var safeId = SafeCommitLabel(dependency.WorkItemId);
            var target = Path.Combine(inputRoot, safeId);
            var archive = Path.Combine(
                inputRoot,
                "." + safeId + "-" + Guid.NewGuid().ToString("N") + ".tar");

            try
            {
                if (Directory.Exists(target))
                    Directory.Delete(target, true);
                Directory.CreateDirectory(target);

                var archiveResult = await RunAsync(
                    normalizedClone,
                    CreateTimeout,
                    cancellationToken,
                    "archive",
                    "--format=tar",
                    "--output=" + archive,
                    dependency.ResultRef.Trim()).ConfigureAwait(false);

                if (archiveResult.ExitCode != 0 || !File.Exists(archive))
                {
                    return new(
                        false,
                        archiveResult.TimedOut ? "INTEGRATION_INPUT_ARCHIVE_TIMEOUT"
                            : archiveResult.Canceled ? "INTEGRATION_INPUT_ARCHIVE_CANCELED"
                            : "INTEGRATION_INPUT_ARCHIVE_FAILED",
                        paths,
                        BuildGitFailureDetail(
                            "git archive " + dependency.ResultRef.Trim(),
                            archiveResult));
                }

                TarFile.ExtractToDirectory(
                    archive,
                    target,
                    overwriteFiles: true);
                paths[dependency.WorkItemId] = target;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return new(
                    false,
                    "INTEGRATION_INPUT_EXTRACT_FAILED",
                    paths,
                    $"{dependency.WorkItemId}: {exception.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(archive))
                        File.Delete(archive);
                }
                catch (IOException)
                {
                }
            }
        }

        return new(true, null, paths);
    }

    public async Task<GitTargetContainmentResult> InspectTargetContainmentAsync(
        string workspace,
        string resultRef,
        string? expectedTargetBranch = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedRef = resultRef?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return new(false, "TARGET_WORKSPACE_MISSING", string.Empty, normalizedRef, null, null, null, false);
        if (string.IsNullOrWhiteSpace(normalizedRef))
            return new(false, "TARGET_RESULT_REF_MISSING", Path.GetFullPath(workspace), normalizedRef, null, null, null, false);

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);
        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return new(false, "TARGET_REPOSITORY_REQUIRED", Path.GetFullPath(workspace), normalizedRef, null, null, null, false);

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
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
            return new(false, "TARGET_BRANCH_REQUIRED", repositoryRoot, normalizedRef, null, null, null, false);

        var expectedBranch = string.IsNullOrWhiteSpace(expectedTargetBranch)
            ? null
            : expectedTargetBranch.Trim();
        if (expectedBranch is not null &&
            !string.Equals(targetBranch, expectedBranch, StringComparison.Ordinal))
            return new(false, "TARGET_BRANCH_CHANGED", repositoryRoot, normalizedRef, null, targetBranch, null, false);

        var resultCommitResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            normalizedRef + "^{commit}").ConfigureAwait(false);
        var resultCommit = resultCommitResult.ExitCode == 0
            ? FirstLine(resultCommitResult.StandardOutput)
            : null;
        if (string.IsNullOrWhiteSpace(resultCommit))
            return new(false, "TARGET_RESULT_REF_INVALID", repositoryRoot, normalizedRef, null, targetBranch, null, false);

        var headResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);
        var targetHead = headResult.ExitCode == 0
            ? FirstLine(headResult.StandardOutput)
            : null;
        if (string.IsNullOrWhiteSpace(targetHead))
            return new(false, "TARGET_HEAD_UNAVAILABLE", repositoryRoot, normalizedRef, resultCommit, targetBranch, null, false);

        var ancestorResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "merge-base",
            "--is-ancestor",
            resultCommit,
            targetHead).ConfigureAwait(false);

        if (ancestorResult.ExitCode == 0)
            return new(true, null, repositoryRoot, normalizedRef, resultCommit, targetBranch, targetHead, true);
        if (ancestorResult.ExitCode == 1)
            return new(true, null, repositoryRoot, normalizedRef, resultCommit, targetBranch, targetHead, false);

        return new(false, "TARGET_ANCESTRY_CHECK_FAILED", repositoryRoot, normalizedRef, resultCommit, targetBranch, targetHead, false);
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

    public Task<GitIntegrationLandingResult> LandIntegrationAsync(
        string workspace,
        string integrationRef,
        string? expectedTargetBranch,
        CancellationToken cancellationToken = default)
        => LandIntegrationCoreAsync(
            workspace,
            integrationRef,
            expectedTargetBranch,
            sourceClonePath: null,
            sourceBranch: null,
            cancellationToken);

    public Task<GitIntegrationLandingResult> LandIntegrationCloneAsync(
        string workspace,
        string sourceClonePath,
        string sourceBranch,
        string integrationRef,
        string? expectedTargetBranch,
        CancellationToken cancellationToken = default)
        => LandIntegrationCoreAsync(
            workspace,
            integrationRef,
            expectedTargetBranch,
            sourceClonePath,
            sourceBranch,
            cancellationToken);

    private async Task<GitIntegrationLandingResult> LandIntegrationCoreAsync(
        string workspace,
        string integrationRef,
        string? expectedTargetBranch,
        string? sourceClonePath,
        string? sourceBranch,
        CancellationToken cancellationToken)
    {
        var normalizedRef = integrationRef?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return new(false, "INTEGRATION_TARGET_WORKSPACE_MISSING", string.Empty, normalizedRef, null, null, null, null, false);
        if (string.IsNullOrWhiteSpace(normalizedRef))
            return new(false, "INTEGRATION_RESULT_REF_MISSING", Path.GetFullPath(workspace), normalizedRef, null, null, null, null, false);

        var importingClone = !string.IsNullOrWhiteSpace(sourceClonePath);
        var normalizedClone = importingClone
            ? Path.GetFullPath(sourceClonePath!)
            : null;
        var normalizedSourceBranch = string.IsNullOrWhiteSpace(sourceBranch)
            ? null
            : sourceBranch.Trim();

        if (importingClone &&
            (normalizedClone is null || !Directory.Exists(normalizedClone)))
        {
            return new(false, "INTEGRATION_SOURCE_CLONE_MISSING", Path.GetFullPath(workspace), normalizedRef, null, null, null, null, false);
        }

        if (importingClone && string.IsNullOrWhiteSpace(normalizedSourceBranch))
        {
            return new(false, "INTEGRATION_SOURCE_BRANCH_REQUIRED", Path.GetFullPath(workspace), normalizedRef, null, null, null, null, false);
        }

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);

        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return new(false, "INTEGRATION_TARGET_REPOSITORY_REQUIRED", Path.GetFullPath(workspace), normalizedRef, null, null, null, null, false);

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        var primaryGate = GetRepositoryPrimaryMutationGate(repositoryRoot);
        await primaryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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

            if (importingClone)
            {
                var sourceRootResult = await RunAsync(
                    normalizedClone!,
                    ReadTimeout,
                    cancellationToken,
                    "rev-parse",
                    "--show-toplevel").ConfigureAwait(false);
                var sourceRoot = sourceRootResult.ExitCode == 0 &&
                                 !string.IsNullOrWhiteSpace(sourceRootResult.StandardOutput)
                    ? Path.GetFullPath(FirstLine(sourceRootResult.StandardOutput))
                    : null;
                if (sourceRoot is null || !PathsEqual(sourceRoot, normalizedClone!))
                {
                    return new(false, "INTEGRATION_SOURCE_REPOSITORY_INVALID", repositoryRoot, normalizedRef, null, targetBranch, beforeHead, beforeHead, false);
                }

                var sourceGitDirResult = await RunAsync(
                    normalizedClone!,
                    ReadTimeout,
                    cancellationToken,
                    "rev-parse",
                    "--absolute-git-dir").ConfigureAwait(false);
                var expectedSourceGitDir = Path.GetFullPath(Path.Combine(normalizedClone!, ".git"));
                var sourceGitDir = sourceGitDirResult.ExitCode == 0 &&
                                   !string.IsNullOrWhiteSpace(sourceGitDirResult.StandardOutput)
                    ? Path.GetFullPath(FirstLine(sourceGitDirResult.StandardOutput))
                    : null;
                if (sourceGitDir is null || !PathsEqual(sourceGitDir, expectedSourceGitDir))
                {
                    return new(false, "INTEGRATION_SOURCE_GITDIR_NOT_LOCAL", repositoryRoot, normalizedRef, null, targetBranch, beforeHead, beforeHead, false);
                }

                var sourceBranchResult = await RunAsync(
                    normalizedClone!,
                    ReadTimeout,
                    cancellationToken,
                    "symbolic-ref",
                    "--quiet",
                    "--short",
                    "HEAD").ConfigureAwait(false);
                var actualSourceBranch = sourceBranchResult.ExitCode == 0
                    ? FirstLine(sourceBranchResult.StandardOutput)
                    : null;
                if (string.IsNullOrWhiteSpace(actualSourceBranch) ||
                    !string.Equals(actualSourceBranch, normalizedSourceBranch, StringComparison.Ordinal))
                {
                    return new(false, "INTEGRATION_SOURCE_BRANCH_CHANGED", repositoryRoot, normalizedRef, null, targetBranch, beforeHead, beforeHead, false);
                }

                var sourceHeadResult = await RunAsync(
                    normalizedClone!,
                    ReadTimeout,
                    cancellationToken,
                    "rev-parse",
                    "--verify",
                    "HEAD").ConfigureAwait(false);
                var sourceHead = sourceHeadResult.ExitCode == 0
                    ? FirstLine(sourceHeadResult.StandardOutput)
                    : null;
                if (string.IsNullOrWhiteSpace(sourceHead) ||
                    !string.Equals(sourceHead, normalizedRef, StringComparison.OrdinalIgnoreCase))
                {
                    return new(false, "INTEGRATION_SOURCE_HEAD_CHANGED", repositoryRoot, normalizedRef, sourceHead, targetBranch, beforeHead, beforeHead, false);
                }

                var fetchResult = await RunAsync(
                    repositoryRoot,
                    CreateTimeout,
                    cancellationToken,
                    "fetch",
                    "--no-tags",
                    "--no-write-fetch-head",
                    normalizedClone!,
                    "refs/heads/" + normalizedSourceBranch).ConfigureAwait(false);
                if (fetchResult.ExitCode != 0)
                {
                    return new(
                        false,
                        fetchResult.TimedOut ? "INTEGRATION_IMPORT_TIMEOUT"
                            : fetchResult.Canceled ? "INTEGRATION_IMPORT_CANCELED"
                            : "INTEGRATION_IMPORT_FAILED",
                        repositoryRoot,
                        normalizedRef,
                        null,
                        targetBranch,
                        beforeHead,
                        beforeHead,
                        false);
                }
            }

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
        finally
        {
            primaryGate.Release();
        }
    }

    public async Task<GitWorktreeRemovalResult> RemoveAsync(
        string repositoryRoot,
        string worktreePath,
        string branch,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoot = Path.GetFullPath(repositoryRoot);
        var preparationGate = GetRepositoryPreparationGate(normalizedRoot);
        await preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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
        finally
        {
            preparationGate.Release();
        }
    }

    public async Task<GitIntegrationCloneCleanupResult> CleanupIntegrationCloneAsync(
        string repositoryRoot,
        string clonePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) ||
            string.IsNullOrWhiteSpace(clonePath))
        {
            return new(
                false,
                "INTEGRATION_CLONE_CLEANUP_PATH_MISSING",
                clonePath ?? string.Empty);
        }

        var runtime = WorkerPaths.GetRepositoryRuntimePaths(repositoryRoot);
        var normalizedClone = Path.GetFullPath(clonePath);
        if (!IsPathWithin(normalizedClone, runtime.IntegrationClones))
        {
            return new(
                false,
                "INTEGRATION_CLONE_CLEANUP_OUTSIDE_RUNTIME",
                normalizedClone);
        }

        var error = await DeleteDirectoryTreeWithRetriesAsync(
            normalizedClone,
            cancellationToken).ConfigureAwait(false);
        return error is null
            ? new(true, null, normalizedClone)
            : new(
                false,
                "INTEGRATION_CLONE_CLEANUP_DELETE_FAILED",
                normalizedClone,
                error);
    }

    public async Task<GitRepositoryRuntimeCleanupResult> CompactRepositoryRuntimeAsync(
        string workspace,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
        {
            return new(
                false,
                "RUNTIME_COMPACT_WORKSPACE_MISSING",
                string.Empty,
                Array.Empty<string>(),
                false);
        }

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);
        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
        {
            return new(
                false,
                "RUNTIME_COMPACT_REPOSITORY_REQUIRED",
                string.Empty,
                Array.Empty<string>(),
                false,
                BuildGitFailureDetail("git rev-parse --show-toplevel", rootResult));
        }

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(repositoryRoot);
        if (!Directory.Exists(runtime.Root))
        {
            return new(
                true,
                null,
                runtime.Root,
                Array.Empty<string>(),
                false);
        }

        var removed = new List<string>();
        var errors = new List<string>();
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
            {
                return new(
                    false,
                    "RUNTIME_COMPACT_WORKTREE_LIST_FAILED",
                    runtime.Root,
                    removed,
                    false,
                    BuildGitFailureDetail("git worktree list --porcelain", listResult));
            }

            foreach (var entry in ParseWorktrees(listResult.StandardOutput)
                         .Where(entry => IsPathWithin(entry.Path, runtime.Worktrees)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var worktreePath = Path.GetFullPath(entry.Path);
                if (!Directory.Exists(worktreePath))
                    continue;

                var inspection = await InspectAsync(
                    worktreePath,
                    cancellationToken).ConfigureAwait(false);
                if (!inspection.Success)
                {
                    errors.Add(
                        $"worktree={worktreePath}: " +
                        (inspection.ErrorCode ?? "WORKTREE_INSPECTION_FAILED"));
                    continue;
                }

                if (!inspection.IsClean)
                    continue;

                var removeResult = await RunAsync(
                    repositoryRoot,
                    RemoveTimeout,
                    cancellationToken,
                    "worktree",
                    "remove",
                    worktreePath).ConfigureAwait(false);
                if (removeResult.ExitCode == 0)
                    removed.Add(worktreePath);
                else
                    errors.Add(BuildGitFailureDetail("git worktree remove", removeResult));
            }

            var pruneResult = await RunAsync(
                repositoryRoot,
                RemoveTimeout,
                cancellationToken,
                "worktree",
                "prune",
                "--expire",
                "now").ConfigureAwait(false);
            if (pruneResult.ExitCode != 0)
                errors.Add(BuildGitFailureDetail("git worktree prune --expire now", pruneResult));

            foreach (var path in new[]
            {
                runtime.NuGetRoot,
                runtime.DotNetHome
            })
            {
                var deleteError = await DeleteDirectoryTreeWithRetriesAsync(
                    path,
                    cancellationToken).ConfigureAwait(false);
                if (deleteError is not null)
                    errors.Add(path + ": " + deleteError);
            }

            return new(
                errors.Count == 0,
                errors.Count == 0 ? null : "RUNTIME_COMPACT_FAILED",
                runtime.Root,
                removed,
                false,
                errors.Count == 0 ? null : string.Join(Environment.NewLine, errors));
        }
        finally
        {
            preparationGate.Release();
        }
    }

    public async Task<GitRepositoryRuntimeCleanupResult> ResetRepositoryRuntimeAsync(
        string workspace,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
        {
            return new(
                false,
                "RUNTIME_RESET_WORKSPACE_MISSING",
                string.Empty,
                Array.Empty<string>(),
                false);
        }

        var repositoryRoot = Path.GetFullPath(workspace);
        var rootResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);
        var gitRepositoryAvailable =
            rootResult.ExitCode == 0 &&
            !string.IsNullOrWhiteSpace(rootResult.StandardOutput);
        if (gitRepositoryAvailable)
            repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));

        var runtime = WorkerPaths.GetRepositoryRuntimePaths(repositoryRoot);
        var legacyRuntimeRoot = WorkerPaths.GetLegacyRepositoryRuntimeRoot(repositoryRoot);
        var legacyWorktreeRoot = BuildLegacyWorktreeRoot(repositoryRoot);
        var removed = new List<string>();
        var errors = new List<string>();

        var preparationGate = GetRepositoryPreparationGate(repositoryRoot);
        await preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (gitRepositoryAvailable)
            {
                var listResult = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "worktree",
                    "list",
                    "--porcelain").ConfigureAwait(false);

                if (listResult.ExitCode == 0)
                {
                    var ownedWorktrees = ParseWorktrees(listResult.StandardOutput)
                        .Where(entry =>
                            IsPathWithin(entry.Path, runtime.Worktrees) ||
                            IsPathWithin(entry.Path, Path.Combine(legacyRuntimeRoot, "worktrees")) ||
                            IsPathWithin(entry.Path, legacyWorktreeRoot))
                        .Select(entry => Path.GetFullPath(entry.Path))
                        .Distinct(OperatingSystem.IsWindows()
                            ? StringComparer.OrdinalIgnoreCase
                            : StringComparer.Ordinal)
                        .ToArray();

                    foreach (var worktreePath in ownedWorktrees)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var removeResult = await RunAsync(
                            repositoryRoot,
                            RemoveTimeout,
                            cancellationToken,
                            "worktree",
                            "remove",
                            "--force",
                            worktreePath).ConfigureAwait(false);

                        if (removeResult.ExitCode != 0 && Directory.Exists(worktreePath))
                        {
                            var directDeleteError = await DeleteDirectoryTreeWithRetriesAsync(
                                worktreePath,
                                cancellationToken).ConfigureAwait(false);
                            if (directDeleteError is not null)
                            {
                                errors.Add(
                                    $"worktree={worktreePath}: " +
                                    (removeResult.StandardError.Trim().Length > 0
                                        ? removeResult.StandardError.Trim()
                                        : $"exitCode={removeResult.ExitCode}") +
                                    " / directDelete=" +
                                    directDeleteError);
                                continue;
                            }
                        }

                        if (!Directory.Exists(worktreePath))
                            removed.Add(worktreePath);
                    }

                    var pruneResult = await RunAsync(
                        repositoryRoot,
                        RemoveTimeout,
                        cancellationToken,
                        "worktree",
                        "prune",
                        "--expire",
                        "now").ConfigureAwait(false);
                    if (pruneResult.ExitCode != 0)
                    {
                        errors.Add(
                            BuildGitFailureDetail(
                                "git worktree prune --expire now",
                                pruneResult));
                    }
                }
                else
                {
                    errors.Add(
                        BuildGitFailureDetail(
                            "git worktree list --porcelain",
                            listResult));
                }
            }

            foreach (var path in new[]
            {
                runtime.Root,
                legacyRuntimeRoot,
                legacyWorktreeRoot
            }.Distinct(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal))
            {
                var deleteError = await DeleteDirectoryTreeWithRetriesAsync(
                    path,
                    cancellationToken).ConfigureAwait(false);
                if (deleteError is not null)
                    errors.Add(path + ": " + deleteError);
            }

            var remainingPaths = new[]
            {
                runtime.Root,
                legacyRuntimeRoot,
                legacyWorktreeRoot
            }
            .Where(Directory.Exists)
            .Distinct(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();

            if (remainingPaths.Length > 0)
            {
                foreach (var path in remainingPaths)
                    errors.Add("remaining=" + path);
            }

            return new(
                errors.Count == 0,
                errors.Count == 0 ? null : "RUNTIME_RESET_FAILED",
                runtime.Root,
                removed,
                !Directory.Exists(runtime.Root) &&
                !Directory.Exists(legacyRuntimeRoot) &&
                !Directory.Exists(legacyWorktreeRoot),
                errors.Count == 0
                    ? null
                    : string.Join(Environment.NewLine, errors));
        }
        finally
        {
            preparationGate.Release();
        }
    }

    public async Task<GitRepositoryRuntimeCleanupResult> CleanupRepositoryRuntimeAsync(
        string workspace,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
        {
            return new(
                false,
                "RUNTIME_CLEANUP_WORKSPACE_MISSING",
                string.Empty,
                Array.Empty<string>(),
                false);
        }

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);

        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
        {
            return new(
                false,
                "RUNTIME_CLEANUP_REPOSITORY_REQUIRED",
                string.Empty,
                Array.Empty<string>(),
                false,
                BuildGitFailureDetail("git rev-parse --show-toplevel", rootResult));
        }

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(repositoryRoot);
        if (!Directory.Exists(runtime.Root))
        {
            return new(
                true,
                null,
                runtime.Root,
                Array.Empty<string>(),
                false);
        }

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
            {
                return new(
                    false,
                    "RUNTIME_CLEANUP_WORKTREE_LIST_FAILED",
                    runtime.Root,
                    Array.Empty<string>(),
                    false,
                    BuildGitFailureDetail("git worktree list --porcelain", listResult));
            }

            var ownedWorktrees = ParseWorktrees(listResult.StandardOutput)
                .Where(entry => IsPathWithin(entry.Path, runtime.Worktrees))
                .ToArray();
            var removed = new List<string>();
            var dirty = new List<string>();

            foreach (var entry in ownedWorktrees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var worktreePath = Path.GetFullPath(entry.Path);
                if (!Directory.Exists(worktreePath))
                    continue;

                var inspection = await InspectAsync(
                    worktreePath,
                    cancellationToken).ConfigureAwait(false);

                if (!inspection.Success)
                {
                    return new(
                        false,
                        "RUNTIME_CLEANUP_WORKTREE_INSPECTION_FAILED",
                        runtime.Root,
                        removed,
                        false,
                        $"worktree={worktreePath}{Environment.NewLine}gitError={inspection.ErrorCode ?? "WORKTREE_INSPECTION_FAILED"}");
                }

                if (!inspection.IsClean)
                {
                    dirty.Add(worktreePath);
                    continue;
                }

                var removeResult = await RunAsync(
                    repositoryRoot,
                    RemoveTimeout,
                    cancellationToken,
                    "worktree",
                    "remove",
                    worktreePath).ConfigureAwait(false);

                if (removeResult.ExitCode != 0)
                {
                    return new(
                        false,
                        removeResult.TimedOut ? "RUNTIME_CLEANUP_WORKTREE_REMOVE_TIMEOUT"
                            : removeResult.Canceled ? "RUNTIME_CLEANUP_WORKTREE_REMOVE_CANCELED"
                            : "RUNTIME_CLEANUP_WORKTREE_REMOVE_FAILED",
                        runtime.Root,
                        removed,
                        false,
                        BuildGitFailureDetail("git worktree remove", removeResult));
                }

                removed.Add(worktreePath);
            }

            var pruneResult = await RunAsync(
                repositoryRoot,
                RemoveTimeout,
                cancellationToken,
                "worktree",
                "prune",
                "--expire",
                "now").ConfigureAwait(false);

            if (pruneResult.ExitCode != 0)
            {
                return new(
                    false,
                    pruneResult.TimedOut ? "RUNTIME_CLEANUP_PRUNE_TIMEOUT"
                        : pruneResult.Canceled ? "RUNTIME_CLEANUP_PRUNE_CANCELED"
                        : "RUNTIME_CLEANUP_PRUNE_FAILED",
                    runtime.Root,
                    removed,
                    false,
                    BuildGitFailureDetail("git worktree prune --expire now", pruneResult));
            }

            var verifyResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "worktree",
                "list",
                "--porcelain").ConfigureAwait(false);

            if (verifyResult.ExitCode != 0)
            {
                return new(
                    false,
                    "RUNTIME_CLEANUP_VERIFY_FAILED",
                    runtime.Root,
                    removed,
                    false,
                    BuildGitFailureDetail("git worktree list --porcelain", verifyResult));
            }

            var remaining = ParseWorktrees(verifyResult.StandardOutput)
                .Where(entry => IsPathWithin(entry.Path, runtime.Worktrees))
                .Select(entry => Path.GetFullPath(entry.Path))
                .ToArray();

            if (remaining.Length > 0)
            {
                var disposableCleanupErrors = await CleanupDisposableRuntimeDirectoriesAsync(
                    runtime,
                    cancellationToken).ConfigureAwait(false);
                var remainingDirty = remaining
                    .Where(path => dirty.Any(
                        candidate => PathsEqual(candidate, path)))
                    .ToArray();
                var errorCode = remainingDirty.Length == remaining.Length
                    ? "RUNTIME_CLEANUP_WORKTREE_DIRTY"
                    : "RUNTIME_CLEANUP_WORKTREE_STILL_REGISTERED";
                var detail = new StringBuilder();
                detail.AppendLine(
                    errorCode == "RUNTIME_CLEANUP_WORKTREE_DIRTY"
                        ? "정리 대상 ProjectHub worktree에 미커밋 변경이 남아 해당 worktree는 보존했습니다."
                        : "ProjectHub runtime 아래에 등록된 linked worktree가 남아 runtime 루트를 삭제하지 않았습니다.");
                foreach (var path in remaining)
                    detail.AppendLine("- " + path);
                if (disposableCleanupErrors.Count > 0)
                {
                    detail.AppendLine("disposableCleanupErrors:");
                    foreach (var error in disposableCleanupErrors)
                        detail.AppendLine("- " + error);
                }

                return new(
                    false,
                    errorCode,
                    runtime.Root,
                    removed,
                    false,
                    detail.ToString().TrimEnd());
            }

            var deleteError = await DeleteDirectoryTreeWithRetriesAsync(
                runtime.Root,
                cancellationToken).ConfigureAwait(false);
            if (deleteError is not null)
            {
                return new(
                    false,
                    "RUNTIME_CLEANUP_DELETE_FAILED",
                    runtime.Root,
                    removed,
                    false,
                    deleteError);
            }

            return new(
                true,
                null,
                runtime.Root,
                removed,
                true);
        }
        finally
        {
            preparationGate.Release();
        }
    }

    private static async Task<IReadOnlyList<string>> CleanupDisposableRuntimeDirectoriesAsync(
        RepositoryRuntimePaths runtime,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var path in new[]
        {
            runtime.IntegrationClones,
            runtime.TempRoot,
            runtime.NuGetRoot,
            runtime.DotNetHome
        })
        {
            var error = await DeleteDirectoryTreeWithRetriesAsync(
                path,
                cancellationToken).ConfigureAwait(false);
            if (error is not null)
                errors.Add(Path.GetFileName(path) + ": " + error);
        }

        return errors;
    }

    private static async Task<string?> DeleteDirectoryTreeWithRetriesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        Exception? lastException = null;
        const int attempts = 12;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ClearDeleteBlockingAttributes(new DirectoryInfo(path));
                Directory.Delete(path, recursive: true);
                return null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                lastException = exception;
                if (attempt < attempts)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(Math.Min(500, 100 * attempt)),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return lastException is null
            ? "runtime directory deletion failed"
            : lastException.GetType().Name + ": " + lastException.Message;
    }

    private static void ClearDeleteBlockingAttributes(DirectoryInfo directory)
    {
        if (!directory.Exists)
            return;

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child &&
                !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                ClearDeleteBlockingAttributes(child);
            }

            entry.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.System);
        }

        directory.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.System);
    }

    public static string BuildBranchName(string jobId, string workItemId)
        => "projecthub/" + StableSegment(jobId, 36) + "/" + StableSegment(workItemId, 36);

    public static string BuildIntegrationClonePath(string repositoryRoot, string jobId, string workItemId)
    {
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(repositoryRoot);
        return Path.Combine(
            runtime.IntegrationClones,
            StableSegment(jobId, 8),
            StableSegment(workItemId, 18));
    }

    public static string BuildWorktreePath(string repositoryRoot, string jobId, string workItemId)
    {
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(repositoryRoot);
        return Path.Combine(
            runtime.Worktrees,
            StableSegment(jobId, 8),
            StableSegment(workItemId, 18));
    }

    private static string BuildLegacyWorktreeRoot(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var parent = Directory.GetParent(root)?.FullName
            ?? throw new InvalidOperationException("저장소 상위 경로를 계산할 수 없습니다.");
        var repository = StableSegment(
            Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            12);

        return Path.Combine(
            parent,
            ".projecthub-worktrees",
            repository);
    }

    private static string BuildLegacyWorktreePath(string repositoryRoot, string jobId, string workItemId)
    {
        return Path.Combine(
            BuildLegacyWorktreeRoot(repositoryRoot),
            StableSegment(jobId, 8),
            StableSegment(workItemId, 18));
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

    private static SemaphoreSlim GetRepositoryPrimaryMutationGate(string repositoryRoot)
    {
        var key = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return RepositoryPrimaryMutationGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
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

    private static GitWorktreePreparationResult IntegrationFailure(
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

        string clonePath;
        try
        {
            clonePath = string.IsNullOrWhiteSpace(fallbackRoot) || string.IsNullOrWhiteSpace(branch)
                ? string.Empty
                : BuildIntegrationClonePath(fallbackRoot, jobId, workItemId);
        }
        catch
        {
            clonePath = string.Empty;
        }

        return new(
            false,
            errorCode,
            fallbackRoot,
            clonePath,
            branch,
            baseRef ?? string.Empty,
            null,
            null,
            false);
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

    private static bool IsPathWithin(string candidate, string root)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var fullCandidate = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(fullCandidate, fullRoot, comparison))
                return true;

            return fullCandidate.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                comparison);
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

    private static void EnsureIntegrationInputExclude(string gitDirectory)
    {
        var infoDirectory = Path.Combine(gitDirectory, "info");
        Directory.CreateDirectory(infoDirectory);
        var excludePath = Path.Combine(infoDirectory, "exclude");
        var existing = File.Exists(excludePath)
            ? File.ReadAllText(excludePath)
            : string.Empty;
        const string rule = ".projecthub-integration-inputs/";
        var lines = existing
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Any(line => string.Equals(line.Trim(), rule, StringComparison.Ordinal)))
            return;

        var updated = existing.TrimEnd('\r', '\n');
        if (updated.Length > 0)
            updated += Environment.NewLine;
        updated += rule + Environment.NewLine;
        File.WriteAllText(excludePath, updated, new UTF8Encoding(false));
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
