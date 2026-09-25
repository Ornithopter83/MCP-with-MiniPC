using System.Diagnostics;
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
    bool Reused);

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

public sealed class GitWorktreeManager
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CreateTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RemoveTimeout = TimeSpan.FromMinutes(1);

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
                false);

        var baseCommit = FirstLine(baseResult.StandardOutput);
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
                false);

        var existing = ParseWorktrees(listResult.StandardOutput)
            .FirstOrDefault(entry => PathsEqual(entry.Path, worktreePath));

        if (existing is not null)
        {
            if (!Directory.Exists(worktreePath))
            {
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
            }

            if (!string.Equals(existing.Branch, branch, StringComparison.Ordinal))
            {
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
            }

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
        {
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
        }

        var branchResult = await RunAsync(
            repositoryRoot,
            ReadTimeout,
            cancellationToken,
            "show-ref",
            "--verify",
            "--quiet",
            "refs/heads/" + branch).ConfigureAwait(false);

        if (branchResult.ExitCode == 0)
        {
            return new GitWorktreePreparationResult(
                false,
                "WORKTREE_BRANCH_EXISTS",
                repositoryRoot,
                worktreePath,
                branch,
                baseRef.Trim(),
                baseCommit,
                null,
                false);
        }

        if (branchResult.ExitCode != 1)
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
                false);
        }

        var parent = Directory.GetParent(worktreePath)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
        {
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
        }

        Directory.CreateDirectory(parent);

        var addResult = await RunAsync(
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
        {
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
                false);
        }

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

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        params string[] arguments)
        => _runner.RunAsync(workingDirectory, arguments, timeout, cancellationToken);

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
