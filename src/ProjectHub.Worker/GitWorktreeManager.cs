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

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                return new GitCommandResult(
                    -1,
                    stdout.Trim(),
                    stderr.Trim(),
                    TimedOut: timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested,
                    Canceled: cancellationToken.IsCancellationRequested);
            }

            var exitCode = process.ExitCode;
            processJob.Dispose();
            return new GitCommandResult(
                exitCode,
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

public sealed record GitIntegrationPrimaryPublishResult(
    bool Success,
    string? ErrorCode,
    string RepositoryRoot,
    string IntegrationWorktreePath,
    string ResultCommit,
    string TargetBranch,
    string? PreviousRemoteHead,
    string? ErrorDetail = null);

public sealed record GitWorktreeCheckpointResult(
    bool Success,
    string? ErrorCode,
    string WorktreePath,
    string? Branch,
    string? HeadCommit,
    bool CreatedCommit,
    string? ErrorDetail = null);

public sealed record GitInitialBaselineResult(
    bool Success,
    string? ErrorCode,
    string RepositoryRoot,
    string Branch,
    string? HeadCommit,
    string? ErrorDetail = null);

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

public sealed record GitTargetContainmentResult(
    bool Success,
    string? ErrorCode,
    string RepositoryRoot,
    string ResultRef,
    string? ResultCommit,
    string? TargetBranch,
    string? TargetHead,
    bool IsContained);

public sealed record GitTargetCheckoutResult(
    bool Success,
    string? ErrorCode,
    string RepositoryRoot,
    string ResultRef,
    string ResultBranch,
    string? ResultCommit,
    string? PreviousBranch,
    string? PreviousHead,
    string? CurrentBranch,
    string? CurrentHead,
    bool Switched,
    string? ErrorDetail = null);

public sealed class GitWorktreeManager
{
    private const int MaximumCheckpointAttempts = 3;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CreateTimeout = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryPreparationGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryNetworkGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryPrimaryMutationGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly IGitCommandRunner _runner;

    public GitWorktreeManager(IGitCommandRunner? runner = null)
    {
        _runner = runner ?? new ProcessGitCommandRunner();
    }

    public async Task<GitInitialBaselineResult> PrepareInitialBaselineAsync(
        string workspace,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return new(false, "INITIAL_BASE_WORKSPACE_MISSING", workspace ?? string.Empty, string.Empty, null);
        if (string.IsNullOrWhiteSpace(jobId))
            return new(false, "INITIAL_BASE_JOB_ID_MISSING", Path.GetFullPath(workspace), string.Empty, null);

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
                "INITIAL_BASE_REPOSITORY_REQUIRED",
                Path.GetFullPath(workspace),
                string.Empty,
                null,
                BuildGitFailureDetail("git rev-parse --show-toplevel", rootResult));
        }

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        WorkerPaths.EnsureProjectHubGitIgnore(repositoryRoot);
        var branch = BuildBranchName(jobId, "base");
        var clonePath = BuildWorktreePath(repositoryRoot, jobId, "base");

        var remoteResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "remote",
            "get-url",
            "origin").ConfigureAwait(false);
        if (remoteResult.ExitCode != 0 || string.IsNullOrWhiteSpace(remoteResult.StandardOutput))
        {
            return new(
                false,
                "INITIAL_BASE_REMOTE_REQUIRED",
                repositoryRoot,
                branch,
                null,
                BuildGitFailureDetail("git remote get-url origin", remoteResult));
        }

        var originUrl = FirstLine(remoteResult.StandardOutput);
        if (!GitRemoteAddressPolicy.IsNetworkRemote(originUrl))
        {
            return new(
                false,
                "INITIAL_BASE_NETWORK_REMOTE_REQUIRED",
                repositoryRoot,
                branch,
                null,
                "origin은 로컬 경로가 아닌 네트워크 Git 원격이어야 합니다.");
        }

        var preparationGate = GetRepositoryPreparationGate(repositoryRoot);
        await preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existingRemote = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "ls-remote",
                "--exit-code",
                "origin",
                "refs/heads/" + branch).ConfigureAwait(false);
            if (existingRemote.ExitCode == 0 &&
                !string.IsNullOrWhiteSpace(existingRemote.StandardOutput))
            {
                var remoteHead = FirstLine(existingRemote.StandardOutput)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(remoteHead))
                    return new(true, null, repositoryRoot, branch, remoteHead);
            }

            var deleteError = await DeleteDirectoryTreeWithRetriesAsync(
                clonePath,
                cancellationToken).ConfigureAwait(false);
            if (deleteError is not null)
            {
                return new(
                    false,
                    "INITIAL_BASE_CLONE_DELETE_FAILED",
                    repositoryRoot,
                    branch,
                    null,
                    deleteError);
            }

            var parent = Directory.GetParent(clonePath)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
                return new(false, "INITIAL_BASE_CLONE_PARENT_INVALID", repositoryRoot, branch, null);
            Directory.CreateDirectory(parent);

            var cloneResult = await RunAsync(
                repositoryRoot,
                CreateTimeout,
                cancellationToken,
                "clone",
                "--no-checkout",
                originUrl,
                clonePath).ConfigureAwait(false);
            if (cloneResult.ExitCode != 0)
            {
                return new(
                    false,
                    "INITIAL_BASE_CLONE_FAILED",
                    repositoryRoot,
                    branch,
                    null,
                    BuildGitFailureDetail("git clone --no-checkout origin", cloneResult));
            }

            var orphanResult = await RunAsync(
                clonePath,
                ReadTimeout,
                cancellationToken,
                "switch",
                "--orphan",
                branch).ConfigureAwait(false);
            if (orphanResult.ExitCode != 0)
            {
                return new(
                    false,
                    "INITIAL_BASE_ORPHAN_BRANCH_FAILED",
                    repositoryRoot,
                    branch,
                    null,
                    BuildGitFailureDetail("git switch --orphan", orphanResult));
            }

            var filesResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "ls-files",
                "--cached",
                "--others",
                "--exclude-standard",
                "-z",
                "--",
                ".",
                ":(exclude,glob).projecthub/**").ConfigureAwait(false);
            if (filesResult.ExitCode != 0)
            {
                return new(
                    false,
                    "INITIAL_BASE_FILE_LIST_FAILED",
                    repositoryRoot,
                    branch,
                    null,
                    BuildGitFailureDetail("git ls-files", filesResult));
            }

            foreach (var relative in filesResult.StandardOutput
                         .Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var normalizedRelative = relative
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Trim();
                if (string.IsNullOrWhiteSpace(normalizedRelative))
                    continue;

                var source = Path.GetFullPath(Path.Combine(repositoryRoot, normalizedRelative));
                var destination = Path.GetFullPath(Path.Combine(clonePath, normalizedRelative));
                if (!IsPathWithin(source, repositoryRoot) ||
                    !IsPathWithin(destination, clonePath) ||
                    !File.Exists(source))
                {
                    return new(
                        false,
                        "INITIAL_BASE_FILE_PATH_INVALID",
                        repositoryRoot,
                        branch,
                        null,
                        normalizedRelative);
                }

                var destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);
                File.Copy(source, destination, overwrite: true);
            }

            var addResult = await RunAsync(
                clonePath,
                ReadTimeout,
                cancellationToken,
                "add",
                "--all",
                "--",
                ".").ConfigureAwait(false);
            if (addResult.ExitCode != 0)
            {
                return new(
                    false,
                    "INITIAL_BASE_ADD_FAILED",
                    repositoryRoot,
                    branch,
                    null,
                    BuildGitFailureDetail("git add --all -- .", addResult));
            }

            var commitResult = await RunAsync(
                clonePath,
                CreateTimeout,
                cancellationToken,
                "-c",
                "user.name=ProjectHub",
                "-c",
                "user.email=projecthub@local",
                "commit",
                "--allow-empty",
                "--no-gpg-sign",
                "-m",
                "ProjectHub initial repository baseline").ConfigureAwait(false);
            if (commitResult.ExitCode != 0)
            {
                return new(
                    false,
                    "INITIAL_BASE_COMMIT_FAILED",
                    repositoryRoot,
                    branch,
                    null,
                    BuildGitFailureDetail("git commit --allow-empty", commitResult));
            }

            var headResult = await RunAsync(
                clonePath,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD").ConfigureAwait(false);
            if (headResult.ExitCode != 0 || string.IsNullOrWhiteSpace(headResult.StandardOutput))
            {
                return new(
                    false,
                    "INITIAL_BASE_HEAD_UNAVAILABLE",
                    repositoryRoot,
                    branch,
                    null,
                    BuildGitFailureDetail("git rev-parse HEAD", headResult));
            }

            var head = FirstLine(headResult.StandardOutput);
            var pushResult = await RunAsync(
                clonePath,
                CreateTimeout,
                cancellationToken,
                "push",
                "origin",
                "HEAD:refs/heads/" + branch).ConfigureAwait(false);
            if (pushResult.ExitCode != 0)
            {
                return new(
                    false,
                    "INITIAL_BASE_PUSH_FAILED",
                    repositoryRoot,
                    branch,
                    head,
                    BuildGitFailureDetail("git push origin initial baseline", pushResult));
            }

            var verifyResult = await RunAsync(
                clonePath,
                ReadTimeout,
                cancellationToken,
                "ls-remote",
                "--exit-code",
                "origin",
                "refs/heads/" + branch).ConfigureAwait(false);
            var verifiedHead = verifyResult.ExitCode == 0 &&
                               !string.IsNullOrWhiteSpace(verifyResult.StandardOutput)
                ? FirstLine(verifyResult.StandardOutput)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()
                : null;
            if (!string.Equals(head, verifiedHead, StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    false,
                    "INITIAL_BASE_REMOTE_VERIFY_FAILED",
                    repositoryRoot,
                    branch,
                    head,
                    BuildGitFailureDetail("git ls-remote initial baseline", verifyResult));
            }

            var sourceFetch = await FetchOriginAsync(
                repositoryRoot,
                cancellationToken).ConfigureAwait(false);
            if (sourceFetch.ExitCode != 0)
            {
                return new(
                    false,
                    "INITIAL_BASE_SOURCE_FETCH_FAILED",
                    repositoryRoot,
                    branch,
                    head,
                    BuildGitFailureDetail("git fetch initial baseline", sourceFetch));
            }

            var adoptResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "reset",
                "--mixed",
                head).ConfigureAwait(false);
            if (adoptResult.ExitCode != 0)
            {
                return new(
                    false,
                    "INITIAL_BASE_LOCAL_ADOPT_FAILED",
                    repositoryRoot,
                    branch,
                    head,
                    BuildGitFailureDetail("git reset --mixed initial baseline", adoptResult));
            }

            var adoptedStatus = await ReadPrimaryWorkspaceStatusAsync(
                repositoryRoot,
                workspace,
                cancellationToken).ConfigureAwait(false);
            if (adoptedStatus.ExitCode != 0 ||
                !string.IsNullOrWhiteSpace(adoptedStatus.StandardOutput))
            {
                return new(
                    false,
                    "INITIAL_BASE_LOCAL_ADOPT_DIRTY",
                    repositoryRoot,
                    branch,
                    head,
                    adoptedStatus.ExitCode == 0
                        ? adoptedStatus.StandardOutput
                        : BuildGitFailureDetail("git status after initial baseline", adoptedStatus));
            }

            _ = await DeleteDirectoryTreeWithRetriesAsync(
                clonePath,
                CancellationToken.None).ConfigureAwait(false);

            return new(true, null, repositoryRoot, branch, head);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new(
                false,
                "INITIAL_BASE_COPY_FAILED",
                repositoryRoot,
                branch,
                null,
                exception.GetType().Name + ": " + exception.Message);
        }
        finally
        {
            preparationGate.Release();
        }
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
        var fetchResult = await FetchOriginAsync(
            repositoryRoot,
            cancellationToken).ConfigureAwait(false);
        if (fetchResult.ExitCode != 0)
        {
            return new(
                false,
                fetchResult.TimedOut ? "REMOTE_BASE_FETCH_TIMEOUT"
                    : fetchResult.Canceled ? "REMOTE_BASE_FETCH_CANCELED"
                    : "REMOTE_BASE_FETCH_FAILED",
                null,
                BuildGitFailureDetail("git fetch --prune origin", fetchResult));
        }

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
            return Failure("WORK_CLONE_WORKSPACE_MISSING", workspace, jobId, workItemId, baseRef);
        if (string.IsNullOrWhiteSpace(baseRef))
            return Failure("WORK_CLONE_BASE_REF_MISSING", workspace, jobId, workItemId, baseRef);
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(workItemId))
            return Failure("WORK_CLONE_ID_MISSING", workspace, jobId, workItemId, baseRef);

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);
        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return Failure("WORK_CLONE_GIT_REPOSITORY_REQUIRED", workspace, jobId, workItemId, baseRef);

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        var branch = BuildBranchName(jobId, workItemId);
        var clonePath = BuildWorktreePath(repositoryRoot, jobId, workItemId);

        var remoteResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "remote",
            "get-url",
            "origin").ConfigureAwait(false);
        if (remoteResult.ExitCode != 0 || string.IsNullOrWhiteSpace(remoteResult.StandardOutput))
        {
            return new(
                false,
                "WORK_CLONE_REMOTE_REQUIRED",
                repositoryRoot,
                clonePath,
                branch,
                baseRef.Trim(),
                null,
                null,
                false,
                BuildGitFailureDetail("git remote get-url origin", remoteResult));
        }

        if (!GitRemoteAddressPolicy.IsNetworkRemote(FirstLine(remoteResult.StandardOutput)))
        {
            return new(
                false,
                "WORK_CLONE_NETWORK_REMOTE_REQUIRED",
                repositoryRoot,
                clonePath,
                branch,
                baseRef.Trim(),
                null,
                null,
                false,
                "origin은 로컬 경로가 아닌 네트워크 Git 원격이어야 합니다.");
        }

        var fetchResult = await FetchOriginAsync(
            repositoryRoot,
            cancellationToken).ConfigureAwait(false);
        if (fetchResult.ExitCode != 0)
        {
            return new(
                false,
                fetchResult.TimedOut ? "WORK_CLONE_FETCH_TIMEOUT"
                    : fetchResult.Canceled ? "WORK_CLONE_FETCH_CANCELED"
                    : "WORK_CLONE_FETCH_FAILED",
                repositoryRoot,
                clonePath,
                branch,
                baseRef.Trim(),
                null,
                null,
                false,
                BuildGitFailureDetail("git fetch --prune origin", fetchResult));
        }

        var baseResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            baseRef.Trim() + "^{commit}").ConfigureAwait(false);
        if (baseResult.ExitCode != 0 || string.IsNullOrWhiteSpace(baseResult.StandardOutput))
        {
            return new(
                false,
                "WORK_CLONE_BASE_REF_INVALID",
                repositoryRoot,
                clonePath,
                branch,
                baseRef.Trim(),
                null,
                null,
                false,
                BuildGitFailureDetail("git rev-parse --verify baseRef", baseResult));
        }

        var baseCommit = FirstLine(baseResult.StandardOutput);
        var preparationGate = GetRepositoryPreparationGate(repositoryRoot);
        await preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(clonePath))
            {
                var cloneRootResult = await RunAsync(
                    clonePath,
                    ReadTimeout,
                    cancellationToken,
                    "rev-parse",
                    "--show-toplevel").ConfigureAwait(false);
                var cloneRoot = cloneRootResult.ExitCode == 0 &&
                                !string.IsNullOrWhiteSpace(cloneRootResult.StandardOutput)
                    ? Path.GetFullPath(FirstLine(cloneRootResult.StandardOutput))
                    : null;
                if (cloneRoot is null || !PathsEqual(cloneRoot, clonePath))
                {
                    return new(
                        false,
                        "WORK_CLONE_REPOSITORY_INVALID",
                        repositoryRoot,
                        clonePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        null,
                        true,
                        BuildGitFailureDetail("git rev-parse --show-toplevel", cloneRootResult));
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
                if (actualGitDir is null || !PathsEqual(actualGitDir, expectedGitDir))
                {
                    return new(
                        false,
                        "WORK_CLONE_GITDIR_NOT_LOCAL",
                        repositoryRoot,
                        clonePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        null,
                        true);
                }

                var branchResult = await RunAsync(
                    clonePath,
                    ReadTimeout,
                    cancellationToken,
                    "symbolic-ref",
                    "--quiet",
                    "--short",
                    "HEAD").ConfigureAwait(false);
                var currentBranch = branchResult.ExitCode == 0
                    ? FirstLine(branchResult.StandardOutput)
                    : null;
                if (!string.Equals(currentBranch, branch, StringComparison.Ordinal))
                {
                    return new(
                        false,
                        "WORK_CLONE_BRANCH_CHANGED",
                        repositoryRoot,
                        clonePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        null,
                        true);
                }

                var headResult = await RunAsync(
                    clonePath,
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
                    return new(
                        false,
                        "WORK_CLONE_HEAD_UNAVAILABLE",
                        repositoryRoot,
                        clonePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        null,
                        true);
                }

                var ancestry = await RunAsync(
                    clonePath,
                    ReadTimeout,
                    cancellationToken,
                    "merge-base",
                    "--is-ancestor",
                    baseCommit,
                    head).ConfigureAwait(false);
                if (ancestry.ExitCode != 0)
                {
                    return new(
                        false,
                        "WORK_CLONE_BASE_MISMATCH",
                        repositoryRoot,
                        clonePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        head,
                        true,
                        BuildGitFailureDetail("git merge-base --is-ancestor", ancestry));
                }

                return new(
                    true,
                    null,
                    repositoryRoot,
                    clonePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    head,
                    true);
            }

            if (File.Exists(clonePath))
            {
                return new(
                    false,
                    "WORK_CLONE_PATH_OCCUPIED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    null,
                    false);
            }

            var parent = Directory.GetParent(clonePath)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
            {
                return new(
                    false,
                    "WORK_CLONE_PARENT_INVALID",
                    repositoryRoot,
                    clonePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    null,
                    false);
            }

            Directory.CreateDirectory(parent);
            var originUrl = FirstLine(remoteResult.StandardOutput);
            var cloneResult = await RunAsync(
                repositoryRoot,
                CreateTimeout,
                cancellationToken,
                "clone",
                "--no-checkout",
                originUrl,
                clonePath).ConfigureAwait(false);
            if (cloneResult.ExitCode != 0)
            {
                return new(
                    false,
                    cloneResult.TimedOut ? "WORK_CLONE_CREATE_TIMEOUT"
                        : cloneResult.Canceled ? "WORK_CLONE_CREATE_CANCELED"
                        : "WORK_CLONE_CREATE_FAILED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    null,
                    false,
                    BuildGitFailureDetail("git clone --no-checkout origin", cloneResult));
            }

            var checkoutResult = await RunAsync(
                clonePath,
                CreateTimeout,
                cancellationToken,
                "checkout",
                "-b",
                branch,
                baseCommit).ConfigureAwait(false);
            if (checkoutResult.ExitCode != 0)
            {
                return new(
                    false,
                    "WORK_CLONE_CHECKOUT_FAILED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    null,
                    false,
                    BuildGitFailureDetail("git checkout -b", checkoutResult));
            }

            foreach (var pair in new[]
            {
                ("user.name", "ProjectHub"),
                ("user.email", "projecthub@local")
            })
            {
                var configResult = await RunAsync(
                    clonePath,
                    ReadTimeout,
                    cancellationToken,
                    "config",
                    pair.Item1,
                    pair.Item2).ConfigureAwait(false);
                if (configResult.ExitCode != 0)
                {
                    return new(
                        false,
                        "WORK_CLONE_GIT_IDENTITY_FAILED",
                        repositoryRoot,
                        clonePath,
                        branch,
                        baseRef.Trim(),
                        baseCommit,
                        null,
                        false,
                        BuildGitFailureDetail("git config " + pair.Item1, configResult));
                }
            }

            var headResultAfter = await RunAsync(
                clonePath,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD").ConfigureAwait(false);
            var cloneHead = headResultAfter.ExitCode == 0
                ? FirstLine(headResultAfter.StandardOutput)
                : null;
            if (string.IsNullOrWhiteSpace(cloneHead) ||
                !string.Equals(cloneHead, baseCommit, StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    false,
                    "WORK_CLONE_HEAD_MISMATCH",
                    repositoryRoot,
                    clonePath,
                    branch,
                    baseRef.Trim(),
                    baseCommit,
                    cloneHead,
                    false);
            }

            return new(
                true,
                null,
                repositoryRoot,
                clonePath,
                branch,
                baseRef.Trim(),
                baseCommit,
                cloneHead,
                false);
        }
        finally
        {
            preparationGate.Release();
        }
    }

    public Task<GitWorktreePreparationResult> PrepareIntegrationAsync(
        string workspace,
        string jobId,
        string workItemId,
        string? expectedTargetBranch,
        CancellationToken cancellationToken = default)
        => PrepareIntegrationAsync(
            workspace,
            jobId,
            workItemId,
            null,
            expectedTargetBranch,
            cancellationToken);

    public async Task<GitWorktreePreparationResult> PrepareIntegrationAsync(
        string workspace,
        string jobId,
        string workItemId,
        string? baseRef,
        string? expectedTargetBranch,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return IntegrationFailure("INTEGRATION_REMOTE_WORKSPACE_MISSING", workspace, jobId, workItemId, baseRef);
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(workItemId))
            return IntegrationFailure("INTEGRATION_REMOTE_ID_MISSING", workspace, jobId, workItemId, baseRef);

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);
        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return IntegrationFailure("INTEGRATION_REMOTE_REPOSITORY_REQUIRED", workspace, jobId, workItemId, baseRef);

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        var branch = BuildBranchName(jobId, workItemId);
        var clonePath = BuildIntegrationClonePath(repositoryRoot, jobId, workItemId);
        var requestedBaseRef = string.IsNullOrWhiteSpace(baseRef)
            ? null
            : baseRef.Trim();
        var preparationGate = GetRepositoryPreparationGate(repositoryRoot);
        await preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? integrationBaseCommit = null;

            // Compatibility path for callers that do not provide a WorkItem baseRef.
            // Production WorkItem execution always supplies baseRef and therefore does
            // not depend on the user's current branch name or HEAD.
            if (requestedBaseRef is null)
            {
                var targetBranchResult = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "symbolic-ref",
                    "--quiet",
                    "--short",
                    "HEAD").ConfigureAwait(false);
                var targetBranch = targetBranchResult.ExitCode == 0
                    ? FirstLine(targetBranchResult.StandardOutput)
                    : null;
                if (string.IsNullOrWhiteSpace(targetBranch))
                    return IntegrationFailure("INTEGRATION_BASE_BRANCH_REQUIRED", repositoryRoot, jobId, workItemId, null);

                var expectedBranch = string.IsNullOrWhiteSpace(expectedTargetBranch)
                    ? null
                    : expectedTargetBranch.Trim();
                if (expectedBranch is not null &&
                    !string.Equals(targetBranch, expectedBranch, StringComparison.Ordinal))
                {
                    return IntegrationFailure("INTEGRATION_BASE_BRANCH_CHANGED", repositoryRoot, jobId, workItemId, null);
                }

                var headResult = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "rev-parse",
                    "--verify",
                    "HEAD").ConfigureAwait(false);
                integrationBaseCommit = headResult.ExitCode == 0
                    ? FirstLine(headResult.StandardOutput)
                    : null;
                if (string.IsNullOrWhiteSpace(integrationBaseCommit))
                    return IntegrationFailure("INTEGRATION_BASE_HEAD_UNAVAILABLE", repositoryRoot, jobId, workItemId, null);
            }

            var remoteResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "remote",
                "get-url",
                "origin").ConfigureAwait(false);
            if (remoteResult.ExitCode != 0 || string.IsNullOrWhiteSpace(remoteResult.StandardOutput))
                return IntegrationFailure(
                    "INTEGRATION_REMOTE_ORIGIN_REQUIRED",
                    repositoryRoot,
                    jobId,
                    workItemId,
                    requestedBaseRef ?? integrationBaseCommit);
            if (!GitRemoteAddressPolicy.IsNetworkRemote(FirstLine(remoteResult.StandardOutput)))
                return IntegrationFailure(
                    "INTEGRATION_NETWORK_REMOTE_REQUIRED",
                    repositoryRoot,
                    jobId,
                    workItemId,
                    requestedBaseRef ?? integrationBaseCommit);

            var fetchResult = await FetchOriginAsync(
                repositoryRoot,
                cancellationToken).ConfigureAwait(false);
            if (fetchResult.ExitCode != 0)
            {
                return new(
                    false,
                    fetchResult.TimedOut ? "INTEGRATION_REMOTE_FETCH_TIMEOUT"
                        : fetchResult.Canceled ? "INTEGRATION_REMOTE_FETCH_CANCELED"
                        : "INTEGRATION_REMOTE_FETCH_FAILED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    requestedBaseRef ?? integrationBaseCommit ?? string.Empty,
                    integrationBaseCommit,
                    null,
                    false,
                    BuildGitFailureDetail("git fetch --prune origin", fetchResult));
            }

            if (requestedBaseRef is not null)
            {
                var baseResult = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "rev-parse",
                    "--verify",
                    requestedBaseRef + "^{commit}").ConfigureAwait(false);
                integrationBaseCommit = baseResult.ExitCode == 0
                    ? FirstLine(baseResult.StandardOutput)
                    : null;
                if (string.IsNullOrWhiteSpace(integrationBaseCommit))
                {
                    return new(
                        false,
                        "INTEGRATION_BASE_REMOTE_CHANGED",
                        repositoryRoot,
                        clonePath,
                        branch,
                        requestedBaseRef,
                        null,
                        null,
                        false,
                        BuildGitFailureDetail("git rev-parse --verify baseRef", baseResult));
                }
            }

            if (string.IsNullOrWhiteSpace(integrationBaseCommit))
                return IntegrationFailure(
                    "INTEGRATION_BASE_HEAD_UNAVAILABLE",
                    repositoryRoot,
                    jobId,
                    workItemId,
                    requestedBaseRef);

            var effectiveBaseRef = requestedBaseRef ?? integrationBaseCommit;
            var remoteRefsResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "for-each-ref",
                "--format=%(objectname) %(refname)",
                "refs/remotes/origin").ConfigureAwait(false);
            var matchingRemoteRefs = remoteRefsResult.ExitCode == 0
                ? remoteRefsResult.StandardOutput
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value =>
                        value.StartsWith(
                            integrationBaseCommit + " refs/remotes/origin/",
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<string>();
            if (remoteRefsResult.ExitCode != 0 ||
                matchingRemoteRefs.Length == 0)
            {
                return new(
                    false,
                    "INTEGRATION_BASE_REMOTE_CHANGED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    effectiveBaseRef,
                    integrationBaseCommit,
                    null,
                    false,
                    BuildGitFailureDetail(
                        "git for-each-ref exact remote baseRef",
                        remoteRefsResult));
            }

            if (Directory.Exists(clonePath) || File.Exists(clonePath))
            {
                return new(
                    false,
                    "INTEGRATION_CLONE_PATH_OCCUPIED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    effectiveBaseRef,
                    integrationBaseCommit,
                    null,
                    false);
            }

            var parent = Directory.GetParent(clonePath)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
            {
                return new(
                    false,
                    "INTEGRATION_CLONE_PARENT_INVALID",
                    repositoryRoot,
                    clonePath,
                    branch,
                    effectiveBaseRef,
                    integrationBaseCommit,
                    null,
                    false);
            }

            Directory.CreateDirectory(parent);
            var cloneResult = await RunAsync(
                repositoryRoot,
                CreateTimeout,
                cancellationToken,
                "clone",
                "--no-checkout",
                FirstLine(remoteResult.StandardOutput),
                clonePath).ConfigureAwait(false);
            if (cloneResult.ExitCode != 0)
            {
                return new(
                    false,
                    cloneResult.TimedOut ? "INTEGRATION_CLONE_CREATE_TIMEOUT"
                        : cloneResult.Canceled ? "INTEGRATION_CLONE_CREATE_CANCELED"
                        : "INTEGRATION_CLONE_CREATE_FAILED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    effectiveBaseRef,
                    integrationBaseCommit,
                    null,
                    false,
                    BuildGitFailureDetail("git clone --no-checkout origin", cloneResult));
            }

            var checkoutResult = await RunAsync(
                clonePath,
                CreateTimeout,
                cancellationToken,
                "checkout",
                "-b",
                branch,
                integrationBaseCommit).ConfigureAwait(false);
            if (checkoutResult.ExitCode != 0)
            {
                return new(
                    false,
                    "INTEGRATION_CLONE_CHECKOUT_FAILED",
                    repositoryRoot,
                    clonePath,
                    branch,
                    effectiveBaseRef,
                    integrationBaseCommit,
                    null,
                    false,
                    BuildGitFailureDetail("git checkout -b", checkoutResult));
            }

            foreach (var pair in new[]
            {
                ("user.name", "ProjectHub"),
                ("user.email", "projecthub@local")
            })
            {
                var configResult = await RunAsync(
                    clonePath,
                    ReadTimeout,
                    cancellationToken,
                    "config",
                    pair.Item1,
                    pair.Item2).ConfigureAwait(false);
                if (configResult.ExitCode != 0)
                {
                    return new(
                        false,
                        "INTEGRATION_CLONE_GIT_IDENTITY_FAILED",
                        repositoryRoot,
                        clonePath,
                        branch,
                        effectiveBaseRef,
                        integrationBaseCommit,
                        null,
                        false,
                        BuildGitFailureDetail("git config " + pair.Item1, configResult));
                }
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
            if (actualGitDir is null || !PathsEqual(actualGitDir, expectedGitDir))
            {
                return new(
                    false,
                    "INTEGRATION_CLONE_GITDIR_NOT_LOCAL",
                    repositoryRoot,
                    clonePath,
                    branch,
                    effectiveBaseRef,
                    integrationBaseCommit,
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
                !string.Equals(cloneHead, integrationBaseCommit, StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    false,
                    "INTEGRATION_CLONE_HEAD_MISMATCH",
                    repositoryRoot,
                    clonePath,
                    branch,
                    effectiveBaseRef,
                    integrationBaseCommit,
                    cloneHead,
                    false);
            }

            return new(
                true,
                null,
                repositoryRoot,
                clonePath,
                branch,
                effectiveBaseRef,
                integrationBaseCommit,
                cloneHead,
                false);
        }
        finally
        {
            preparationGate.Release();
        }
    }

    public async Task<GitWorktreePreparationResult> EnsureIntegrationWorkspaceAsync(
        string workspace,
        string jobId,
        string workItemId,
        string? baseRef,
        string? existingClonePath,
        string? existingBranch,
        string? expectedTargetBranch,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(existingClonePath) ||
            !Directory.Exists(existingClonePath))
        {
            return await PrepareIntegrationAsync(
                workspace,
                jobId,
                workItemId,
                baseRef,
                expectedTargetBranch,
                cancellationToken).ConfigureAwait(false);
        }

        var resumed = await ResumeIntegrationAsync(
            workspace,
            existingClonePath,
            existingBranch,
            baseRef,
            cancellationToken).ConfigureAwait(false);
        if (resumed.Success ||
            !string.Equals(
                resumed.ErrorCode,
                "INTEGRATION_CLONE_PATH_MISSING",
                StringComparison.Ordinal))
        {
            return resumed;
        }

        return await PrepareIntegrationAsync(
            workspace,
            jobId,
            workItemId,
            baseRef,
            expectedTargetBranch,
            cancellationToken).ConfigureAwait(false);
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
            "--untracked-files=all",
            "--",
            ".",
            ":(exclude,glob).projecthub/**").ConfigureAwait(false);

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

    public Task<GitWorktreeCheckpointResult> CreateCheckpointAsync(
        string worktreePath,
        string workItemId,
        CancellationToken cancellationToken = default)
        => CreateCheckpointAsync(
            worktreePath,
            workItemId,
            publishCleanHead: false,
            cancellationToken);

    public Task<GitWorktreeCheckpointResult> EnsureCheckpointAsync(
        string worktreePath,
        string workItemId,
        CancellationToken cancellationToken = default)
        => EnsureCheckpointAsync(
            worktreePath,
            workItemId,
            publishCleanHead: false,
            cancellationToken);

    public async Task<GitWorktreeCheckpointResult> EnsureCheckpointAsync(
        string worktreePath,
        string workItemId,
        bool publishCleanHead,
        CancellationToken cancellationToken = default)
    {
        GitWorktreeCheckpointResult? last = null;
        var createdCommit = false;
        var requireCleanHeadPublish = publishCleanHead;

        for (var attempt = 1; attempt <= MaximumCheckpointAttempts; attempt++)
        {
            last = await CreateCheckpointAsync(
                worktreePath,
                workItemId,
                requireCleanHeadPublish,
                cancellationToken).ConfigureAwait(false);

            createdCommit |= last.CreatedCommit;
            requireCleanHeadPublish |= last.CreatedCommit;

            if (last.Success)
                return createdCommit && !last.CreatedCommit
                    ? last with { CreatedCommit = true }
                    : last;

            if (!IsRetryableCheckpointFailure(last.ErrorCode) ||
                attempt == MaximumCheckpointAttempts)
                return last;

            await Task.Delay(
                TimeSpan.FromMilliseconds(attempt * 150),
                cancellationToken).ConfigureAwait(false);
        }

        return last ?? new(
            false,
            "WORKTREE_CHECKPOINT_FAILED",
            worktreePath,
            null,
            null,
            createdCommit);
    }

    public async Task<GitWorktreeCheckpointResult> EnsureTargetWorkspaceCheckpointAsync(
        string workspace,
        string baseRef,
        string managedBranch,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        GitWorktreeCheckpointResult? last = null;
        var createdCommit = false;

        for (var attempt = 1; attempt <= MaximumCheckpointAttempts; attempt++)
        {
            last = await CreateTargetWorkspaceCheckpointAsync(
                workspace,
                baseRef,
                managedBranch,
                workItemId,
                cancellationToken).ConfigureAwait(false);

            createdCommit |= last.CreatedCommit;
            if (last.Success)
                return createdCommit && !last.CreatedCommit
                    ? last with { CreatedCommit = true }
                    : last;

            if (!IsRetryableCheckpointFailure(last.ErrorCode) ||
                attempt == MaximumCheckpointAttempts)
                return last;

            await Task.Delay(
                TimeSpan.FromMilliseconds(attempt * 150),
                cancellationToken).ConfigureAwait(false);
        }

        return last ?? new(
            false,
            "TARGET_WORKSPACE_CHECKPOINT_FAILED",
            workspace,
            managedBranch,
            null,
            createdCommit);
    }

    public async Task<GitWorktreeCheckpointResult> CreateTargetWorkspaceCheckpointAsync(
        string workspace,
        string baseRef,
        string managedBranch,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return new(false, "TARGET_WORKSPACE_CHECKPOINT_MISSING", workspace ?? string.Empty, managedBranch, null, false);
        if (string.IsNullOrWhiteSpace(baseRef))
            return new(false, "TARGET_WORKSPACE_CHECKPOINT_BASE_MISSING", Path.GetFullPath(workspace), managedBranch, null, false);
        if (string.IsNullOrWhiteSpace(managedBranch) ||
            !managedBranch.StartsWith("projecthub/", StringComparison.Ordinal))
            return new(false, "TARGET_WORKSPACE_CHECKPOINT_BRANCH_INVALID", Path.GetFullPath(workspace), managedBranch, null, false);
        if (string.IsNullOrWhiteSpace(workItemId))
            return new(false, "TARGET_WORKSPACE_CHECKPOINT_ID_MISSING", Path.GetFullPath(workspace), managedBranch, null, false);

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
                "TARGET_WORKSPACE_CHECKPOINT_REPOSITORY_REQUIRED",
                Path.GetFullPath(workspace),
                managedBranch,
                null,
                false,
                BuildGitFailureDetail("git rev-parse --show-toplevel", rootResult));
        }

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        var primaryGate = GetRepositoryPrimaryMutationGate(repositoryRoot);
        await primaryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fetchResult = await FetchOriginAsync(
                repositoryRoot,
                cancellationToken).ConfigureAwait(false);
            if (fetchResult.ExitCode != 0)
            {
                return new(
                    false,
                    fetchResult.TimedOut ? "TARGET_WORKSPACE_CHECKPOINT_FETCH_TIMEOUT"
                        : fetchResult.Canceled ? "TARGET_WORKSPACE_CHECKPOINT_FETCH_CANCELED"
                        : "TARGET_WORKSPACE_CHECKPOINT_FETCH_FAILED",
                    repositoryRoot,
                    managedBranch,
                    null,
                    false,
                    BuildGitFailureDetail("git fetch --prune origin", fetchResult));
            }

            var baseResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                baseRef.Trim() + "^{commit}").ConfigureAwait(false);
            var baseCommit = baseResult.ExitCode == 0
                ? FirstLine(baseResult.StandardOutput)
                : null;
            if (string.IsNullOrWhiteSpace(baseCommit))
            {
                return new(
                    false,
                    "TARGET_WORKSPACE_CHECKPOINT_BASE_INVALID",
                    repositoryRoot,
                    managedBranch,
                    null,
                    false,
                    BuildGitFailureDetail("git rev-parse --verify baseRef", baseResult));
            }

            var branchResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "symbolic-ref",
                "--quiet",
                "--short",
                "HEAD").ConfigureAwait(false);
            var currentBranch = branchResult.ExitCode == 0
                ? FirstLine(branchResult.StandardOutput)
                : null;
            if (string.IsNullOrWhiteSpace(currentBranch))
                return new(false, "TARGET_WORKSPACE_CHECKPOINT_CURRENT_BRANCH_REQUIRED", repositoryRoot, managedBranch, null, false);

            var headResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD").ConfigureAwait(false);
            var currentHead = headResult.ExitCode == 0
                ? FirstLine(headResult.StandardOutput)
                : null;
            if (string.IsNullOrWhiteSpace(currentHead))
                return new(false, "TARGET_WORKSPACE_CHECKPOINT_CURRENT_HEAD_REQUIRED", repositoryRoot, managedBranch, null, false);

            var workspaceStatus = await ReadPrimaryWorkspaceStatusAsync(
                repositoryRoot,
                workspace,
                cancellationToken).ConfigureAwait(false);
            if (workspaceStatus.ExitCode != 0)
            {
                return new(
                    false,
                    "TARGET_WORKSPACE_CHECKPOINT_STATUS_UNAVAILABLE",
                    repositoryRoot,
                    managedBranch,
                    currentHead,
                    false,
                    BuildGitFailureDetail("git status", workspaceStatus));
            }

            if (!string.Equals(currentBranch, managedBranch, StringComparison.Ordinal))
            {
                if (!string.Equals(currentHead, baseCommit, StringComparison.OrdinalIgnoreCase))
                {
                    return new(
                        false,
                        "TARGET_WORKSPACE_CHECKPOINT_BASE_CHANGED",
                        repositoryRoot,
                        managedBranch,
                        currentHead,
                        false,
                        "현재 작업 폴더 HEAD가 #8 baseRef와 다릅니다." + Environment.NewLine +
                        "currentHead=" + currentHead + Environment.NewLine +
                        "baseCommit=" + baseCommit);
                }

                if (string.IsNullOrWhiteSpace(workspaceStatus.StandardOutput))
                {
                    return new(
                        true,
                        null,
                        repositoryRoot,
                        currentBranch,
                        currentHead,
                        false);
                }

                var switchResult = await RunAsync(
                    repositoryRoot,
                    CreateTimeout,
                    cancellationToken,
                    "switch",
                    "-c",
                    managedBranch,
                    baseCommit).ConfigureAwait(false);
                if (switchResult.ExitCode != 0)
                {
                    return new(
                        false,
                        switchResult.TimedOut ? "TARGET_WORKSPACE_CHECKPOINT_SWITCH_TIMEOUT"
                            : switchResult.Canceled ? "TARGET_WORKSPACE_CHECKPOINT_SWITCH_CANCELED"
                            : "TARGET_WORKSPACE_CHECKPOINT_SWITCH_FAILED",
                        repositoryRoot,
                        managedBranch,
                        currentHead,
                        false,
                        BuildGitFailureDetail("git switch -c managed branch", switchResult));
                }
            }
            else
            {
                var lineageResult = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "merge-base",
                    "--is-ancestor",
                    baseCommit,
                    currentHead).ConfigureAwait(false);
                if (lineageResult.ExitCode != 0)
                {
                    return new(
                        false,
                        "TARGET_WORKSPACE_CHECKPOINT_BASE_DIVERGED",
                        repositoryRoot,
                        managedBranch,
                        currentHead,
                        false,
                        BuildGitFailureDetail(
                            "git merge-base --is-ancestor bootstrap base",
                            lineageResult));
                }
            }

            return await CreateCheckpointAsync(
                repositoryRoot,
                workItemId,
                publishCleanHead: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            primaryGate.Release();
        }
    }

    public async Task<GitWorktreeCheckpointResult> CreateCheckpointAsync(
        string worktreePath,
        string workItemId,
        bool publishCleanHead,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workItemId))
            return new(false, "WORKTREE_CHECKPOINT_ID_MISSING", worktreePath, null, null, false);

        var before = await InspectAsync(worktreePath, cancellationToken).ConfigureAwait(false);
        if (!before.Success)
            return new(false, before.ErrorCode, worktreePath, before.Branch, before.HeadCommit, false);

        var managedPathSafety = await ValidateCheckpointManagedPathAsync(
            worktreePath,
            before,
            cancellationToken).ConfigureAwait(false);
        if (managedPathSafety is not null)
            return managedPathSafety;

        if (before.IsClean)
        {
            if (string.IsNullOrWhiteSpace(before.HeadCommit))
                return new(false, "WORKTREE_CHECKPOINT_HEAD_UNAVAILABLE", worktreePath, before.Branch, null, false);

            if (!publishCleanHead)
            {
                return new(
                    true,
                    null,
                    before.WorktreePath,
                    before.Branch,
                    before.HeadCommit,
                    false);
            }

            return await PublishCheckpointToRemoteAsync(
                before.WorktreePath,
                before.Branch,
                before.HeadCommit,
                createdCommit: false,
                cancellationToken).ConfigureAwait(false);
        }

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
                false,
                BuildGitFailureDetail("git add --all", addResult));
        }

        var stagedManagedPath = await RunAsync(
            worktreePath,
            ReadTimeout,
            cancellationToken,
            "diff",
            "--cached",
            "--name-only",
            "-z",
            "--",
            ".projecthub").ConfigureAwait(false);
        if (stagedManagedPath.ExitCode != 0)
        {
            return new(
                false,
                "WORKTREE_CHECKPOINT_MANAGED_PATH_VERIFY_FAILED",
                worktreePath,
                before.Branch,
                before.HeadCommit,
                false,
                BuildGitFailureDetail(
                    "git diff --cached --name-only -- .projecthub",
                    stagedManagedPath));
        }
        if (!string.IsNullOrWhiteSpace(stagedManagedPath.StandardOutput))
        {
            return new(
                false,
                "WORKTREE_CHECKPOINT_MANAGED_PATH_STAGED",
                worktreePath,
                before.Branch,
                before.HeadCommit,
                false,
                "checkpoint staging에 .projecthub 경로가 포함되었습니다.");
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
                false,
                BuildGitFailureDetail("git commit", commitResult));
        }

        var committedHead = await RunAsync(
            worktreePath,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD").ConfigureAwait(false);
        if (committedHead.ExitCode != 0 || string.IsNullOrWhiteSpace(committedHead.StandardOutput))
        {
            return new(
                false,
                "WORKTREE_CHECKPOINT_HEAD_UNAVAILABLE",
                worktreePath,
                before.Branch,
                before.HeadCommit,
                true,
                BuildGitFailureDetail("git rev-parse HEAD", committedHead));
        }

        return await PublishCheckpointToRemoteAsync(
            worktreePath,
            before.Branch,
            FirstLine(committedHead.StandardOutput),
            createdCommit: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<GitWorktreeCheckpointResult> PublishCheckpointToRemoteAsync(
        string worktreePath,
        string? branch,
        string resultCommit,
        bool createdCommit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return new(
                false,
                "WORKTREE_CHECKPOINT_BRANCH_REQUIRED",
                worktreePath,
                branch,
                resultCommit,
                createdCommit,
                "원격 checkpoint를 게시할 branch를 확인하지 못했습니다.");
        }

        var pushResult = await RunAsync(
            worktreePath,
            CreateTimeout,
            cancellationToken,
            "push",
            "origin",
            "HEAD:refs/heads/" + branch).ConfigureAwait(false);
        if (pushResult.ExitCode != 0)
        {
            return new(
                false,
                pushResult.TimedOut ? "WORKTREE_CHECKPOINT_PUSH_TIMEOUT"
                    : pushResult.Canceled ? "WORKTREE_CHECKPOINT_PUSH_CANCELED"
                    : "WORKTREE_CHECKPOINT_PUSH_FAILED",
                worktreePath,
                branch,
                resultCommit,
                createdCommit,
                BuildGitFailureDetail("git push origin", pushResult));
        }

        var remoteResult = await RunAsync(
            worktreePath,
            ReadTimeout,
            cancellationToken,
            "ls-remote",
            "--exit-code",
            "origin",
            "refs/heads/" + branch).ConfigureAwait(false);
        var remoteCommit = remoteResult.ExitCode == 0
            ? remoteResult.StandardOutput
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(remoteCommit) ||
            !string.Equals(remoteCommit, resultCommit, StringComparison.OrdinalIgnoreCase))
        {
            return new(
                false,
                "WORKTREE_CHECKPOINT_REMOTE_VERIFY_FAILED",
                worktreePath,
                branch,
                resultCommit,
                createdCommit,
                BuildGitFailureDetail("git ls-remote origin", remoteResult));
        }

        var after = await InspectAsync(worktreePath, cancellationToken).ConfigureAwait(false);
        if (!after.Success)
            return new(false, after.ErrorCode, worktreePath, after.Branch, resultCommit, createdCommit);
        if (!after.IsClean)
            return new(false, "WORKTREE_CHECKPOINT_NOT_CLEAN", worktreePath, after.Branch, resultCommit, createdCommit);

        return new(true, null, after.WorktreePath, after.Branch, resultCommit, createdCommit);
    }

    private async Task<GitWorktreeCheckpointResult?> ValidateCheckpointManagedPathAsync(
        string worktreePath,
        GitWorktreeInspectionResult before,
        CancellationToken cancellationToken)
    {
        var trackedManagedPath = await RunAsync(
            worktreePath,
            ReadTimeout,
            cancellationToken,
            "ls-files",
            "--",
            ".projecthub").ConfigureAwait(false);
        if (trackedManagedPath.ExitCode != 0)
        {
            return new(
                false,
                "WORKTREE_CHECKPOINT_MANAGED_PATH_TRACK_CHECK_FAILED",
                worktreePath,
                before.Branch,
                before.HeadCommit,
                false,
                BuildGitFailureDetail("git ls-files -- .projecthub", trackedManagedPath));
        }
        if (!string.IsNullOrWhiteSpace(trackedManagedPath.StandardOutput))
        {
            return new(
                false,
                "WORKTREE_CHECKPOINT_MANAGED_PATH_TRACKED",
                worktreePath,
                before.Branch,
                before.HeadCommit,
                false,
                ".projecthub 아래에 tracked 파일이 있어 자동 checkpoint를 중단했습니다.");
        }

        var ignoredManagedPath = await RunAsync(
            worktreePath,
            ReadTimeout,
            cancellationToken,
            "check-ignore",
            "-q",
            "--no-index",
            "--",
            ".projecthub/projecthub-checkpoint-probe").ConfigureAwait(false);
        if (ignoredManagedPath.ExitCode != 0)
        {
            return new(
                false,
                "WORKTREE_CHECKPOINT_MANAGED_PATH_NOT_IGNORED",
                worktreePath,
                before.Branch,
                before.HeadCommit,
                false,
                ".projecthub가 현재 저장소 ignore 규칙으로 보호되지 않습니다.");
        }

        return null;
    }

    private static bool IsRetryableCheckpointFailure(string? errorCode)
        => errorCode is
            "WORKTREE_CHECKPOINT_ADD_FAILED" or
            "WORKTREE_CHECKPOINT_ADD_TIMEOUT" or
            "WORKTREE_CHECKPOINT_PUSH_FAILED" or
            "WORKTREE_CHECKPOINT_PUSH_TIMEOUT" or
            "WORKTREE_CHECKPOINT_REMOTE_VERIFY_FAILED";

    private static string[] BuildCheckpointAddArguments()
        => new[]
        {
            "add",
            "--all"
        };

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

        var cloneParent = Directory.GetParent(normalizedClone)?.FullName;
        if (string.IsNullOrWhiteSpace(cloneParent))
        {
            return new(
                false,
                "INTEGRATION_INPUT_ROOT_UNAVAILABLE",
                new Dictionary<string, string>(),
                normalizedClone);
        }

        var inputRoot = Path.Combine(
            cloneParent,
            ".inputs-" + Path.GetFileName(normalizedClone));
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

        var fetchResult = await FetchOriginAsync(
            repositoryRoot,
            cancellationToken).ConfigureAwait(false);
        if (fetchResult.ExitCode != 0)
            return new(false, "TARGET_REMOTE_FETCH_FAILED", repositoryRoot, normalizedRef, null, targetBranch, null, false);

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
            return new(false, "TARGET_HEAD_UNAVAILABLE", repositoryRoot, normalizedRef, null, targetBranch, null, false);

        var remoteHeadResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            $"refs/remotes/origin/{targetBranch}^{{commit}}").ConfigureAwait(false);
        var remoteHead = remoteHeadResult.ExitCode == 0
            ? FirstLine(remoteHeadResult.StandardOutput)
            : null;
        if (!string.IsNullOrWhiteSpace(remoteHead) &&
            !string.Equals(targetHead, remoteHead, StringComparison.OrdinalIgnoreCase))
            return new(false, "TARGET_REMOTE_HEAD_MISMATCH", repositoryRoot, normalizedRef, null, targetBranch, targetHead, false);

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
            return new(false, "TARGET_RESULT_REF_INVALID", repositoryRoot, normalizedRef, null, targetBranch, targetHead, false);

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

    public async Task<GitTargetCheckoutResult> SwitchTargetToRemoteResultAsync(
        string workspace,
        string resultRef,
        string resultBranch,
        string? expectedCurrentBranch = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedRef = resultRef?.Trim() ?? string.Empty;
        var normalizedResultBranch = resultBranch?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return new(false, "TARGET_CHECKOUT_WORKSPACE_MISSING", string.Empty, normalizedRef, normalizedResultBranch, null, null, null, null, null, false);
        if (string.IsNullOrWhiteSpace(normalizedRef))
            return new(false, "TARGET_CHECKOUT_RESULT_REF_MISSING", Path.GetFullPath(workspace), normalizedRef, normalizedResultBranch, null, null, null, null, null, false);
        if (string.IsNullOrWhiteSpace(normalizedResultBranch) ||
            !normalizedResultBranch.StartsWith("projecthub/", StringComparison.Ordinal))
        {
            return new(false, "TARGET_CHECKOUT_MANAGED_BRANCH_REQUIRED", Path.GetFullPath(workspace), normalizedRef, normalizedResultBranch, null, null, null, null, null, false);
        }

        var rootResult = await RunAsync(
            workspace,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--show-toplevel").ConfigureAwait(false);
        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
            return new(false, "TARGET_CHECKOUT_REPOSITORY_REQUIRED", Path.GetFullPath(workspace), normalizedRef, normalizedResultBranch, null, null, null, null, null, false, BuildGitFailureDetail("git rev-parse --show-toplevel", rootResult));

        var repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        var primaryGate = GetRepositoryPrimaryMutationGate(repositoryRoot);
        await primaryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var status = await ReadPrimaryWorkspaceStatusAsync(
                repositoryRoot,
                workspace,
                cancellationToken).ConfigureAwait(false);
            if (status.ExitCode != 0)
                return new(false, "TARGET_CHECKOUT_STATUS_UNAVAILABLE", repositoryRoot, normalizedRef, normalizedResultBranch, null, null, null, null, null, false, BuildGitFailureDetail("git status", status));
            if (!string.IsNullOrWhiteSpace(status.StandardOutput))
                return new(false, "TARGET_CHECKOUT_WORKSPACE_DIRTY", repositoryRoot, normalizedRef, normalizedResultBranch, null, null, null, null, null, false);

            var branchResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "symbolic-ref",
                "--quiet",
                "--short",
                "HEAD").ConfigureAwait(false);
            var currentBranch = branchResult.ExitCode == 0
                ? FirstLine(branchResult.StandardOutput)
                : null;
            if (string.IsNullOrWhiteSpace(currentBranch))
                return new(false, "TARGET_CHECKOUT_CURRENT_BRANCH_REQUIRED", repositoryRoot, normalizedRef, normalizedResultBranch, null, null, null, null, null, false);

            var headResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD").ConfigureAwait(false);
            var currentHead = headResult.ExitCode == 0
                ? FirstLine(headResult.StandardOutput)
                : null;
            if (string.IsNullOrWhiteSpace(currentHead))
                return new(false, "TARGET_CHECKOUT_CURRENT_HEAD_UNAVAILABLE", repositoryRoot, normalizedRef, normalizedResultBranch, null, currentBranch, null, currentBranch, null, false);

            var fetchResult = await FetchOriginAsync(
                repositoryRoot,
                cancellationToken).ConfigureAwait(false);
            if (fetchResult.ExitCode != 0)
            {
                return new(
                    false,
                    fetchResult.TimedOut ? "TARGET_CHECKOUT_FETCH_TIMEOUT"
                        : fetchResult.Canceled ? "TARGET_CHECKOUT_FETCH_CANCELED"
                        : "TARGET_CHECKOUT_FETCH_FAILED",
                    repositoryRoot,
                    normalizedRef,
                    normalizedResultBranch,
                    null,
                    currentBranch,
                    currentHead,
                    currentBranch,
                    currentHead,
                    false,
                    BuildGitFailureDetail("git fetch --prune origin", fetchResult));
            }

            var currentRemoteResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                $"refs/remotes/origin/{currentBranch}^{{commit}}").ConfigureAwait(false);
            var currentRemoteHead = currentRemoteResult.ExitCode == 0
                ? FirstLine(currentRemoteResult.StandardOutput)
                : null;
            if (!string.IsNullOrWhiteSpace(currentRemoteHead) &&
                !string.Equals(currentHead, currentRemoteHead, StringComparison.OrdinalIgnoreCase))
                return new(false, "TARGET_CHECKOUT_CURRENT_REMOTE_CHANGED", repositoryRoot, normalizedRef, normalizedResultBranch, null, currentBranch, currentHead, currentBranch, currentHead, false);

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
                return new(false, "TARGET_CHECKOUT_RESULT_REF_INVALID", repositoryRoot, normalizedRef, normalizedResultBranch, null, currentBranch, currentHead, currentBranch, currentHead, false);

            if (string.IsNullOrWhiteSpace(currentRemoteHead))
            {
                var localAncestor = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "merge-base",
                    "--is-ancestor",
                    currentHead,
                    resultCommit).ConfigureAwait(false);
                if (localAncestor.ExitCode != 0)
                {
                    return new(
                        false,
                        "TARGET_CHECKOUT_LOCAL_BASE_DIVERGED",
                        repositoryRoot,
                        normalizedRef,
                        normalizedResultBranch,
                        resultCommit,
                        currentBranch,
                        currentHead,
                        currentBranch,
                        currentHead,
                        false,
                        BuildGitFailureDetail("git merge-base --is-ancestor local baseline", localAncestor));
                }
            }

            var remoteResultBranchResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                $"refs/remotes/origin/{normalizedResultBranch}^{{commit}}").ConfigureAwait(false);
            var remoteResultHead = remoteResultBranchResult.ExitCode == 0
                ? FirstLine(remoteResultBranchResult.StandardOutput)
                : null;
            if (string.IsNullOrWhiteSpace(remoteResultHead))
                return new(false, "TARGET_CHECKOUT_RESULT_REMOTE_BRANCH_REQUIRED", repositoryRoot, normalizedRef, normalizedResultBranch, resultCommit, currentBranch, currentHead, currentBranch, currentHead, false);
            if (!string.Equals(remoteResultHead, resultCommit, StringComparison.OrdinalIgnoreCase))
                return new(false, "TARGET_CHECKOUT_RESULT_REMOTE_MISMATCH", repositoryRoot, normalizedRef, normalizedResultBranch, resultCommit, currentBranch, currentHead, currentBranch, currentHead, false);

            if (string.Equals(currentBranch, normalizedResultBranch, StringComparison.Ordinal) &&
                string.Equals(currentHead, resultCommit, StringComparison.OrdinalIgnoreCase))
            {
                return new(true, null, repositoryRoot, normalizedRef, normalizedResultBranch, resultCommit, currentBranch, currentHead, currentBranch, currentHead, false);
            }

            var expectedBranch = string.IsNullOrWhiteSpace(expectedCurrentBranch)
                ? null
                : expectedCurrentBranch.Trim();
            if (expectedBranch is not null &&
                !string.Equals(currentBranch, expectedBranch, StringComparison.Ordinal))
            {
                return new(false, "TARGET_CHECKOUT_CURRENT_BRANCH_CHANGED", repositoryRoot, normalizedRef, normalizedResultBranch, resultCommit, currentBranch, currentHead, currentBranch, currentHead, false);
            }

            var localBranchResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "show-ref",
                "--verify",
                "--quiet",
                "refs/heads/" + normalizedResultBranch).ConfigureAwait(false);

            GitCommandResult switchResult;
            if (localBranchResult.ExitCode == 0)
            {
                var localBranchCommitResult = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "rev-parse",
                    "--verify",
                    "refs/heads/" + normalizedResultBranch + "^{commit}").ConfigureAwait(false);
                var localBranchCommit = localBranchCommitResult.ExitCode == 0
                    ? FirstLine(localBranchCommitResult.StandardOutput)
                    : null;
                if (!string.Equals(localBranchCommit, resultCommit, StringComparison.OrdinalIgnoreCase))
                {
                    return new(false, "TARGET_CHECKOUT_LOCAL_BRANCH_CONFLICT", repositoryRoot, normalizedRef, normalizedResultBranch, resultCommit, currentBranch, currentHead, currentBranch, currentHead, false);
                }

                switchResult = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "switch",
                    normalizedResultBranch).ConfigureAwait(false);
            }
            else if (localBranchResult.ExitCode == 1)
            {
                switchResult = await RunAsync(
                    repositoryRoot,
                    ReadTimeout,
                    cancellationToken,
                    "switch",
                    "-c",
                    normalizedResultBranch,
                    "--track",
                    "origin/" + normalizedResultBranch).ConfigureAwait(false);
            }
            else
            {
                return new(false, "TARGET_CHECKOUT_LOCAL_BRANCH_CHECK_FAILED", repositoryRoot, normalizedRef, normalizedResultBranch, resultCommit, currentBranch, currentHead, currentBranch, currentHead, false, BuildGitFailureDetail("git show-ref --verify", localBranchResult));
            }

            if (switchResult.ExitCode != 0)
                return new(false, "TARGET_CHECKOUT_SWITCH_FAILED", repositoryRoot, normalizedRef, normalizedResultBranch, resultCommit, currentBranch, currentHead, currentBranch, currentHead, false, BuildGitFailureDetail("git switch", switchResult));

            var afterBranchResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "symbolic-ref",
                "--quiet",
                "--short",
                "HEAD").ConfigureAwait(false);
            var afterBranch = afterBranchResult.ExitCode == 0
                ? FirstLine(afterBranchResult.StandardOutput)
                : null;
            var afterHeadResult = await RunAsync(
                repositoryRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD").ConfigureAwait(false);
            var afterHead = afterHeadResult.ExitCode == 0
                ? FirstLine(afterHeadResult.StandardOutput)
                : null;
            var afterStatus = await ReadPrimaryWorkspaceStatusAsync(
                repositoryRoot,
                workspace,
                cancellationToken).ConfigureAwait(false);

            if (!string.Equals(afterBranch, normalizedResultBranch, StringComparison.Ordinal) ||
                !string.Equals(afterHead, resultCommit, StringComparison.OrdinalIgnoreCase) ||
                afterStatus.ExitCode != 0 ||
                !string.IsNullOrWhiteSpace(afterStatus.StandardOutput))
            {
                return new(false, "TARGET_CHECKOUT_VERIFY_FAILED", repositoryRoot, normalizedRef, normalizedResultBranch, resultCommit, currentBranch, currentHead, afterBranch, afterHead, true);
            }

            return new(true, null, repositoryRoot, normalizedRef, normalizedResultBranch, resultCommit, currentBranch, currentHead, afterBranch, afterHead, true);
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
        if (string.IsNullOrWhiteSpace(repositoryRoot) ||
            string.IsNullOrWhiteSpace(worktreePath) ||
            string.IsNullOrWhiteSpace(branch))
            return new(false, "WORK_CLONE_CLEANUP_INPUT_MISSING", worktreePath ?? string.Empty, branch ?? string.Empty);

        var runtime = WorkerPaths.GetRepositoryRuntimePaths(repositoryRoot);
        var normalizedPath = Path.GetFullPath(worktreePath);
        if (!IsPathWithin(normalizedPath, runtime.Worktrees))
            return new(false, "WORK_CLONE_CLEANUP_OUTSIDE_RUNTIME", normalizedPath, branch);

        var inspection = await InspectAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        if (!inspection.Success)
            return new(false, inspection.ErrorCode, normalizedPath, branch);
        if (!inspection.IsClean)
            return new(false, "WORK_CLONE_DIRTY", normalizedPath, branch);
        if (!string.Equals(inspection.Branch, branch, StringComparison.Ordinal))
            return new(false, "WORK_CLONE_BRANCH_MISMATCH", normalizedPath, branch);

        var error = await DeleteDirectoryTreeWithRetriesAsync(
            normalizedPath,
            cancellationToken).ConfigureAwait(false);
        return error is null
            ? new(true, null, normalizedPath, branch)
            : new(false, "WORK_CLONE_CLEANUP_DELETE_FAILED", normalizedPath, branch);
    }

    public async Task<GitIntegrationPrimaryPublishResult> PublishIntegrationResultToPrimaryAsync(
        string repositoryRoot,
        string integrationWorktreePath,
        string resultRef,
        string? targetBranch,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoot = string.IsNullOrWhiteSpace(repositoryRoot)
            ? string.Empty
            : Path.GetFullPath(repositoryRoot);
        var normalizedWorktree = string.IsNullOrWhiteSpace(integrationWorktreePath)
            ? string.Empty
            : Path.GetFullPath(integrationWorktreePath);
        var normalizedRef = resultRef?.Trim() ?? string.Empty;
        var normalizedBranch = targetBranch?.Trim() ?? string.Empty;

        GitIntegrationPrimaryPublishResult Fail(
            string errorCode,
            string? resultCommit = null,
            string? previousRemoteHead = null,
            string? detail = null)
            => new(
                false,
                errorCode,
                normalizedRoot,
                normalizedWorktree,
                resultCommit ?? normalizedRef,
                normalizedBranch,
                previousRemoteHead,
                detail);

        if (string.IsNullOrWhiteSpace(normalizedRoot) ||
            !Directory.Exists(normalizedRoot))
            return Fail("INTEGRATION_PRIMARY_WORKSPACE_MISSING");
        if (string.IsNullOrWhiteSpace(normalizedWorktree) ||
            !Directory.Exists(normalizedWorktree))
            return Fail("INTEGRATION_PRIMARY_RESULT_WORKTREE_MISSING");
        if (string.IsNullOrWhiteSpace(normalizedRef))
            return Fail("INTEGRATION_PRIMARY_RESULT_REF_MISSING");
        if (string.IsNullOrWhiteSpace(normalizedBranch))
            return Fail("INTEGRATION_PRIMARY_BRANCH_REQUIRED");

        var resultInspection = await InspectAsync(
            normalizedWorktree,
            cancellationToken).ConfigureAwait(false);
        if (!resultInspection.Success)
            return Fail(
                resultInspection.ErrorCode ?? "INTEGRATION_PRIMARY_RESULT_INSPECTION_FAILED",
                resultInspection.HeadCommit);
        if (!resultInspection.IsClean)
            return Fail(
                "INTEGRATION_PRIMARY_RESULT_DIRTY",
                resultInspection.HeadCommit,
                detail: "Integration 결과 worktree가 clean 상태가 아닙니다.");

        var resultCommitResult = await RunAsync(
            normalizedWorktree,
            ReadTimeout,
            cancellationToken,
            "rev-parse",
            "--verify",
            normalizedRef + "^{commit}").ConfigureAwait(false);
        var resultCommit = resultCommitResult.ExitCode == 0
            ? FirstLine(resultCommitResult.StandardOutput)
            : null;
        if (string.IsNullOrWhiteSpace(resultCommit))
            return Fail(
                "INTEGRATION_PRIMARY_RESULT_REF_INVALID",
                detail: BuildGitFailureDetail(
                    "git rev-parse --verify integration resultRef",
                    resultCommitResult));
        if (!string.Equals(
                resultInspection.HeadCommit,
                resultCommit,
                StringComparison.OrdinalIgnoreCase))
        {
            return Fail(
                "INTEGRATION_PRIMARY_RESULT_HEAD_MISMATCH",
                resultCommit,
                detail:
                    $"integrationHead={resultInspection.HeadCommit ?? "없음"}{Environment.NewLine}" +
                    $"resultCommit={resultCommit}");
        }

        var primaryGate = GetRepositoryPrimaryMutationGate(normalizedRoot);
        await primaryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var primaryStatus = await ReadPrimaryWorkspaceStatusAsync(
                normalizedRoot,
                normalizedRoot,
                cancellationToken).ConfigureAwait(false);
            if (primaryStatus.ExitCode != 0)
                return Fail(
                    "INTEGRATION_PRIMARY_STATUS_UNAVAILABLE",
                    resultCommit,
                    detail: BuildGitFailureDetail("git status", primaryStatus));
            if (!string.IsNullOrWhiteSpace(primaryStatus.StandardOutput))
                return Fail(
                    "INTEGRATION_PRIMARY_WORKSPACE_DIRTY",
                    resultCommit,
                    detail: primaryStatus.StandardOutput.Trim());

            var currentBranchResult = await RunAsync(
                normalizedRoot,
                ReadTimeout,
                cancellationToken,
                "symbolic-ref",
                "--quiet",
                "--short",
                "HEAD").ConfigureAwait(false);
            var currentBranch = currentBranchResult.ExitCode == 0
                ? FirstLine(currentBranchResult.StandardOutput)
                : null;
            var onPrimaryBranch = string.Equals(
                currentBranch,
                normalizedBranch,
                StringComparison.Ordinal);
            var onManagedResultBranch =
                !string.IsNullOrWhiteSpace(currentBranch) &&
                currentBranch.StartsWith("projecthub/", StringComparison.Ordinal);
            if (!onPrimaryBranch && !onManagedResultBranch)
            {
                return Fail(
                    "INTEGRATION_PRIMARY_BRANCH_CHANGED",
                    resultCommit,
                    detail:
                        $"currentBranch={currentBranch ?? "없음"}{Environment.NewLine}" +
                        $"expectedBranch={normalizedBranch}");
            }

            var localPrimaryHeadResult = await RunAsync(
                normalizedRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                onPrimaryBranch
                    ? "HEAD"
                    : $"refs/heads/{normalizedBranch}^{{commit}}").ConfigureAwait(false);
            var localHead = localPrimaryHeadResult.ExitCode == 0
                ? FirstLine(localPrimaryHeadResult.StandardOutput)
                : null;
            if (string.IsNullOrWhiteSpace(localHead))
                return Fail(
                    "INTEGRATION_PRIMARY_HEAD_UNAVAILABLE",
                    resultCommit,
                    detail: BuildGitFailureDetail(
                        onPrimaryBranch
                            ? "git rev-parse HEAD"
                            : "git rev-parse primary branch",
                        localPrimaryHeadResult));

            var fetchResult = await FetchOriginAsync(
                normalizedRoot,
                cancellationToken).ConfigureAwait(false);
            if (fetchResult.ExitCode != 0)
            {
                return Fail(
                    fetchResult.TimedOut ? "INTEGRATION_PRIMARY_FETCH_TIMEOUT"
                        : fetchResult.Canceled ? "INTEGRATION_PRIMARY_FETCH_CANCELED"
                        : "INTEGRATION_PRIMARY_FETCH_FAILED",
                    resultCommit,
                    detail: BuildGitFailureDetail("git fetch --prune origin", fetchResult));
            }

            var remoteHeadResult = await RunAsync(
                normalizedRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                $"refs/remotes/origin/{normalizedBranch}^{{commit}}").ConfigureAwait(false);
            var previousRemoteHead = remoteHeadResult.ExitCode == 0
                ? FirstLine(remoteHeadResult.StandardOutput)
                : null;

            var effectivePrimaryHead = localHead;
            if (!string.IsNullOrWhiteSpace(previousRemoteHead) &&
                !string.Equals(previousRemoteHead, localHead, StringComparison.OrdinalIgnoreCase))
            {
                var localBehindRemote = await RunAsync(
                    normalizedRoot,
                    ReadTimeout,
                    cancellationToken,
                    "merge-base",
                    "--is-ancestor",
                    localHead,
                    previousRemoteHead).ConfigureAwait(false);
                if (localBehindRemote.ExitCode == 0)
                {
                    effectivePrimaryHead = previousRemoteHead;
                }
                else if (localBehindRemote.ExitCode == 1)
                {
                    return Fail(
                        "INTEGRATION_PRIMARY_REMOTE_CHANGED",
                        resultCommit,
                        previousRemoteHead,
                        $"localHead={localHead}{Environment.NewLine}remoteHead={previousRemoteHead}");
                }
                else
                {
                    return Fail(
                        "INTEGRATION_PRIMARY_ANCESTRY_CHECK_FAILED",
                        resultCommit,
                        previousRemoteHead,
                        BuildGitFailureDetail(
                            "git merge-base --is-ancestor local primary remote primary",
                            localBehindRemote));
                }
            }

            var primaryAncestor = await RunAsync(
                normalizedRoot,
                ReadTimeout,
                cancellationToken,
                "merge-base",
                "--is-ancestor",
                effectivePrimaryHead,
                resultCommit).ConfigureAwait(false);
            if (primaryAncestor.ExitCode == 1)
            {
                return Fail(
                    "INTEGRATION_PRIMARY_DIVERGED",
                    resultCommit,
                    previousRemoteHead,
                    $"primaryHead={effectivePrimaryHead}{Environment.NewLine}integrationResult={resultCommit}");
            }
            if (primaryAncestor.ExitCode != 0)
            {
                return Fail(
                    "INTEGRATION_PRIMARY_ANCESTRY_CHECK_FAILED",
                    resultCommit,
                    previousRemoteHead,
                    BuildGitFailureDetail("git merge-base --is-ancestor", primaryAncestor));
            }

            var networkGate = GetRepositoryNetworkGate(normalizedRoot);
            await networkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!string.Equals(
                        previousRemoteHead,
                        resultCommit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    var pushResult = await RunAsync(
                        normalizedWorktree,
                        CreateTimeout,
                        cancellationToken,
                        "push",
                        "origin",
                        resultCommit + ":refs/heads/" + normalizedBranch).ConfigureAwait(false);
                    if (pushResult.ExitCode != 0)
                    {
                        return Fail(
                            pushResult.TimedOut ? "INTEGRATION_PRIMARY_PUSH_TIMEOUT"
                                : pushResult.Canceled ? "INTEGRATION_PRIMARY_PUSH_CANCELED"
                                : "INTEGRATION_PRIMARY_PUSH_FAILED",
                            resultCommit,
                            previousRemoteHead,
                            BuildGitFailureDetail(
                                "git push origin integration result to primary",
                                pushResult));
                    }
                }

                var verifyRemote = await RunAsync(
                    normalizedWorktree,
                    ReadTimeout,
                    cancellationToken,
                    "ls-remote",
                    "--exit-code",
                    "origin",
                    "refs/heads/" + normalizedBranch).ConfigureAwait(false);
                var verifiedRemoteHead = verifyRemote.ExitCode == 0
                    ? verifyRemote.StandardOutput
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()
                    : null;
                if (!string.Equals(
                        verifiedRemoteHead,
                        resultCommit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Fail(
                        "INTEGRATION_PRIMARY_REMOTE_VERIFY_FAILED",
                        resultCommit,
                        previousRemoteHead,
                        BuildGitFailureDetail(
                            "git ls-remote integration primary",
                            verifyRemote));
                }
            }
            finally
            {
                networkGate.Release();
            }

            if (!onPrimaryBranch)
            {
                var switchPrimary = await RunAsync(
                    normalizedRoot,
                    ReadTimeout,
                    cancellationToken,
                    "switch",
                    normalizedBranch).ConfigureAwait(false);
                if (switchPrimary.ExitCode != 0)
                {
                    return Fail(
                        "INTEGRATION_PRIMARY_SWITCH_FAILED",
                        resultCommit,
                        previousRemoteHead,
                        BuildGitFailureDetail("git switch primary branch", switchPrimary));
                }
            }

            if (!string.Equals(localHead, resultCommit, StringComparison.OrdinalIgnoreCase))
            {
                var fastForward = await RunAsync(
                    normalizedRoot,
                    CreateTimeout,
                    cancellationToken,
                    "merge",
                    "--ff-only",
                    resultCommit).ConfigureAwait(false);
                if (fastForward.ExitCode != 0)
                {
                    return Fail(
                        "INTEGRATION_PRIMARY_FAST_FORWARD_FAILED",
                        resultCommit,
                        previousRemoteHead,
                        BuildGitFailureDetail("git merge --ff-only", fastForward));
                }
            }

            var afterBranchResult = await RunAsync(
                normalizedRoot,
                ReadTimeout,
                cancellationToken,
                "symbolic-ref",
                "--quiet",
                "--short",
                "HEAD").ConfigureAwait(false);
            var afterHeadResult = await RunAsync(
                normalizedRoot,
                ReadTimeout,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD").ConfigureAwait(false);
            var afterStatus = await ReadPrimaryWorkspaceStatusAsync(
                normalizedRoot,
                normalizedRoot,
                cancellationToken).ConfigureAwait(false);
            var afterBranch = afterBranchResult.ExitCode == 0
                ? FirstLine(afterBranchResult.StandardOutput)
                : null;
            var afterHead = afterHeadResult.ExitCode == 0
                ? FirstLine(afterHeadResult.StandardOutput)
                : null;

            if (!string.Equals(afterBranch, normalizedBranch, StringComparison.Ordinal) ||
                !string.Equals(afterHead, resultCommit, StringComparison.OrdinalIgnoreCase) ||
                afterStatus.ExitCode != 0 ||
                !string.IsNullOrWhiteSpace(afterStatus.StandardOutput))
            {
                return Fail(
                    "INTEGRATION_PRIMARY_VERIFY_FAILED",
                    resultCommit,
                    previousRemoteHead,
                    $"branch={afterBranch ?? "없음"}{Environment.NewLine}" +
                    $"head={afterHead ?? "없음"}{Environment.NewLine}" +
                    $"status={afterStatus.StandardOutput.Trim()}");
            }

            return new(
                true,
                null,
                normalizedRoot,
                normalizedWorktree,
                resultCommit,
                normalizedBranch,
                previousRemoteHead);
        }
        finally
        {
            primaryGate.Release();
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
        if (error is not null)
        {
            return new(
                false,
                "INTEGRATION_CLONE_CLEANUP_DELETE_FAILED",
                normalizedClone,
                error);
        }

        var inputError = await DeleteIntegrationInputSiblingAsync(
            normalizedClone,
            runtime,
            cancellationToken).ConfigureAwait(false);
        return inputError is null
            ? new(true, null, normalizedClone)
            : new(
                false,
                "INTEGRATION_INPUT_CLEANUP_DELETE_FAILED",
                normalizedClone,
                inputError);
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
            return new(true, null, runtime.Root, Array.Empty<string>(), false);

        var removed = new List<string>();
        var errors = new List<string>();
        var preparationGate = GetRepositoryPreparationGate(repositoryRoot);
        await preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var clonePath in EnumerateOwnedCloneDirectories(runtime))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var inspection = await InspectAsync(
                    clonePath,
                    cancellationToken).ConfigureAwait(false);
                if (!inspection.Success)
                {
                    errors.Add(
                        $"clone={clonePath}: " +
                        (inspection.ErrorCode ?? "WORK_CLONE_INSPECTION_FAILED"));
                    continue;
                }

                if (!inspection.IsClean)
                    continue;

                if (!await IsCloneHeadPublishedAsync(
                        clonePath,
                        inspection,
                        cancellationToken).ConfigureAwait(false))
                    continue;

                var deleteError = await DeleteDirectoryTreeWithRetriesAsync(
                    clonePath,
                    cancellationToken).ConfigureAwait(false);
                if (deleteError is null)
                {
                    removed.Add(clonePath);
                    var inputError = await DeleteIntegrationInputSiblingAsync(
                        clonePath,
                        runtime,
                        cancellationToken).ConfigureAwait(false);
                    if (inputError is not null)
                        errors.Add(clonePath + " inputs: " + inputError);
                }
                else
                {
                    errors.Add(clonePath + ": " + deleteError);
                }
            }

            foreach (var path in new[]
            {
                runtime.NuGetRoot,
                runtime.DotNetHome,
                runtime.TempRoot
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
        if (rootResult.ExitCode == 0 &&
            !string.IsNullOrWhiteSpace(rootResult.StandardOutput))
        {
            repositoryRoot = Path.GetFullPath(FirstLine(rootResult.StandardOutput));
        }

        var runtime = WorkerPaths.GetRepositoryRuntimePaths(repositoryRoot);
        var removed = EnumerateOwnedCloneDirectories(runtime).ToList();
        var preparationGate = GetRepositoryPreparationGate(repositoryRoot);
        await preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var deleteError = await DeleteDirectoryTreeWithRetriesAsync(
                runtime.Root,
                cancellationToken).ConfigureAwait(false);

            return new(
                deleteError is null,
                deleteError is null ? null : "RUNTIME_RESET_FAILED",
                runtime.Root,
                removed,
                !Directory.Exists(runtime.Root),
                deleteError);
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
            return new(true, null, runtime.Root, Array.Empty<string>(), false);

        var removed = new List<string>();
        var dirty = new List<string>();
        var unpublished = new List<string>();
        var preparationGate = GetRepositoryPreparationGate(repositoryRoot);
        await preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var clonePath in EnumerateOwnedCloneDirectories(runtime))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var inspection = await InspectAsync(
                    clonePath,
                    cancellationToken).ConfigureAwait(false);
                if (!inspection.Success)
                {
                    return new(
                        false,
                        "RUNTIME_CLEANUP_CLONE_INSPECTION_FAILED",
                        runtime.Root,
                        removed,
                        false,
                        $"clone={clonePath}{Environment.NewLine}gitError={inspection.ErrorCode ?? "WORK_CLONE_INSPECTION_FAILED"}");
                }

                if (!inspection.IsClean)
                {
                    dirty.Add(clonePath);
                    continue;
                }

                if (!await IsCloneHeadPublishedAsync(
                        clonePath,
                        inspection,
                        cancellationToken).ConfigureAwait(false))
                {
                    unpublished.Add(clonePath);
                    continue;
                }

                var cloneDeleteError = await DeleteDirectoryTreeWithRetriesAsync(
                    clonePath,
                    cancellationToken).ConfigureAwait(false);
                if (cloneDeleteError is not null)
                {
                    return new(
                        false,
                        "RUNTIME_CLEANUP_CLONE_DELETE_FAILED",
                        runtime.Root,
                        removed,
                        false,
                        clonePath + ": " + cloneDeleteError);
                }

                removed.Add(clonePath);
                var inputError = await DeleteIntegrationInputSiblingAsync(
                    clonePath,
                    runtime,
                    cancellationToken).ConfigureAwait(false);
                if (inputError is not null)
                {
                    return new(
                        false,
                        "RUNTIME_CLEANUP_INPUT_DELETE_FAILED",
                        runtime.Root,
                        removed,
                        false,
                        clonePath + " inputs: " + inputError);
                }
            }

            if (dirty.Count > 0 || unpublished.Count > 0)
            {
                var disposableCleanupErrors = await CleanupDisposableRuntimeDirectoriesAsync(
                    runtime,
                    cancellationToken).ConfigureAwait(false);
                var detail = new StringBuilder();
                if (dirty.Count > 0)
                {
                    detail.AppendLine("ProjectHub 격리 clone에 미커밋 변경이 남아 runtime을 보존했습니다.");
                    foreach (var path in dirty)
                        detail.AppendLine("- " + path);
                }
                if (unpublished.Count > 0)
                {
                    detail.AppendLine("원격에서 HEAD가 확인되지 않은 clean clone을 보존했습니다.");
                    foreach (var path in unpublished)
                        detail.AppendLine("- " + path);
                }
                if (disposableCleanupErrors.Count > 0)
                {
                    detail.AppendLine("disposableCleanupErrors:");
                    foreach (var error in disposableCleanupErrors)
                        detail.AppendLine("- " + error);
                }

                return new(
                    false,
                    dirty.Count > 0
                        ? "RUNTIME_CLEANUP_CLONE_DIRTY"
                        : "RUNTIME_CLEANUP_CLONE_UNPUBLISHED",
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
                    Array.Empty<string>(),
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

    private async Task<bool> IsCloneHeadPublishedAsync(
        string clonePath,
        GitWorktreeInspectionResult inspection,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inspection.Branch) ||
            string.IsNullOrWhiteSpace(inspection.HeadCommit) ||
            !inspection.Branch.StartsWith("projecthub/", StringComparison.Ordinal))
            return false;

        var remoteResult = await RunAsync(
            clonePath,
            ReadTimeout,
            cancellationToken,
            "ls-remote",
            "--exit-code",
            "origin",
            "refs/heads/" + inspection.Branch).ConfigureAwait(false);
        if (remoteResult.ExitCode != 0)
            return false;

        var remoteHead = remoteResult.StandardOutput
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return !string.IsNullOrWhiteSpace(remoteHead) &&
               string.Equals(
                   remoteHead,
                   inspection.HeadCommit,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateOwnedCloneDirectories(
        RepositoryRuntimePaths runtime)
    {
        foreach (var root in new[] { runtime.Worktrees, runtime.IntegrationClones })
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var jobDirectory in Directory.EnumerateDirectories(root))
            {
                foreach (var candidate in Directory.EnumerateDirectories(jobDirectory))
                {
                    if (Directory.Exists(Path.Combine(candidate, ".git")))
                        yield return Path.GetFullPath(candidate);
                }
            }
        }
    }

    private static async Task<string?> DeleteIntegrationInputSiblingAsync(
        string clonePath,
        RepositoryRuntimePaths runtime,
        CancellationToken cancellationToken)
    {
        if (!IsPathWithin(clonePath, runtime.IntegrationClones))
            return null;

        var parent = Directory.GetParent(clonePath)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
            return null;

        var inputRoot = Path.Combine(
            parent,
            ".inputs-" + Path.GetFileName(clonePath));
        return await DeleteDirectoryTreeWithRetriesAsync(
            inputRoot,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> CleanupDisposableRuntimeDirectoriesAsync(
        RepositoryRuntimePaths runtime,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var path in new[]
        {
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

    private Task<GitCommandResult> ReadPrimaryWorkspaceStatusAsync(
        string repositoryRoot,
        string workspace,
        CancellationToken cancellationToken)
    {
        _ = workspace;
        return _runner.RunAsync(
            repositoryRoot,
            new[]
            {
                "status",
                "--porcelain=v1",
                "--untracked-files=all",
                "--",
                ".",
                ":(exclude,glob).projecthub/**"
            },
            ReadTimeout,
            cancellationToken);
    }

    private async Task<GitCommandResult> FetchOriginAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var networkGate = GetRepositoryNetworkGate(repositoryRoot);
        await networkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunAsync(
                repositoryRoot,
                CreateTimeout,
                cancellationToken,
                "fetch",
                "--prune",
                "origin").ConfigureAwait(false);
        }
        finally
        {
            networkGate.Release();
        }
    }

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        params string[] arguments)
        => _runner.RunAsync(workingDirectory, arguments, timeout, cancellationToken);

    private static SemaphoreSlim GetRepositoryNetworkGate(string repositoryRoot)
    {
        var key = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return RepositoryNetworkGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

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

}
