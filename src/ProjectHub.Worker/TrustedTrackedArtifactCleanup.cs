using System.IO;
using System.Text;

namespace ProjectHub.Worker;

// Git index deletions are explicit intent: a trusted operator has already
// staged them. Never infer removals from filenames, project names, or prompts.
internal static class TrustedTrackedArtifactCleanup
{
    internal sealed record Discovery(
        bool Success,
        IReadOnlyList<string> Paths,
        string? ErrorStage = null,
        GitCommandResult? Error = null)
    {
        public static Discovery Failure(string stage, GitCommandResult? error = null) =>
            new(false, Array.Empty<string>(), stage, error);
    }

    internal sealed record Result(
        bool Success,
        IReadOnlyList<string> RemovedPaths,
        string? ErrorStage = null,
        GitCommandResult? Error = null)
    {
        public static Result NoChange() => new(true, Array.Empty<string>());
        public static Result Failure(string stage, GitCommandResult? error = null) =>
            new(false, Array.Empty<string>(), stage, error);
    }

    // Only explicitly staged cached-only deletions that remain present on disk,
    // are ignored by this repository, and belong to the given commit scope.
    internal static async Task<Discovery> DiscoverAsync(
        string workingDirectory,
        Func<string[], Task<GitCommandResult>> run,
        IReadOnlyCollection<string> approvedScopes)
    {
        if (approvedScopes.Count == 0)
            return new(true, Array.Empty<string>());

        var staged = await run(new[] {
            "diff", "--cached", "--diff-filter=D", "--name-only", "-z"
        }).ConfigureAwait(false);
        if (staged.ExitCode != 0)
            return Discovery.Failure("CACHED_DELETE_SCAN_FAILED", staged);

        var candidates = new List<string>();
        foreach (var raw in staged.StandardOutput.Split(
                     '\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = raw.Replace('\\', '/');
            if (!MilestoneMechanicalExecutor.IsPathWithinScopes(
                    path, approvedScopes))
                continue;
            var localPath = Path.GetFullPath(Path.Combine(workingDirectory,
                path.Replace('/', Path.DirectorySeparatorChar)));
            if (!MilestoneDefinitionContract.IsPathInsideRoot(
                    workingDirectory, localPath) || !File.Exists(localPath))
                continue;

            var ignored = await run(new[] {
                "check-ignore", "--no-index", "-q", "--", path
            }).ConfigureAwait(false);
            if (ignored.ExitCode == 1)
                continue;
            if (ignored.ExitCode != 0)
                return Discovery.Failure("CACHED_DELETE_IGNORE_CHECK_FAILED", ignored);
            candidates.Add(path);
        }

        return new(true, candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    // Commit only the selected pre-staged deletions using a disposable index.
    // Other staged entries cannot enter the tree or be unstaged by this step.
    internal static async Task<Result> ApplyAsync(
        string workingDirectory,
        ProcessGitCommandRunner git,
        Func<string[], Task<GitCommandResult>> run,
        IReadOnlyList<string> requestedPaths,
        CancellationToken cancellationToken)
    {
        if (requestedPaths.Count == 0)
            return Result.NoChange();

        var realLock = Path.Combine(workingDirectory, ".git", "index.lock");
        if (File.Exists(realLock))
            return Result.Failure("CACHED_DELETE_INDEX_LOCK_PRESENT");

        var selected = requestedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var staged = await run(new[] {
            "diff", "--cached", "--diff-filter=D", "--name-only", "-z"
        }).ConfigureAwait(false);
        if (staged.ExitCode != 0)
            return Result.Failure("CACHED_DELETE_STAGE_RECHECK_FAILED", staged);
        var stillStaged = staged.StandardOutput.Split(
            '\0', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Any(path => !stillStaged.Contains(path)))
            return Result.Failure("CACHED_DELETE_STAGE_CHANGED");

        var originalSizes = new Dictionary<string, long>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var path in selected)
        {
            var fullPath = Path.GetFullPath(Path.Combine(workingDirectory,
                path.Replace('/', Path.DirectorySeparatorChar)));
            if (!MilestoneDefinitionContract.IsPathInsideRoot(
                    workingDirectory, fullPath) || !File.Exists(fullPath))
                return Result.Failure("CACHED_DELETE_LOCAL_FILE_CHANGED");
            originalSizes[path] = new FileInfo(fullPath).Length;
        }

        var previousHead = await run(new[] { "rev-parse", "HEAD" })
            .ConfigureAwait(false);
        if (previousHead.ExitCode != 0 ||
            string.IsNullOrWhiteSpace(previousHead.StandardOutput))
            return Result.Failure("CACHED_DELETE_HEAD_READ_FAILED", previousHead);

        var temporaryIndex = Path.Combine(Path.GetTempPath(),
            "projecthub-cached-index-" + Guid.NewGuid().ToString("N"));
        var temporaryPathspec = Path.GetTempFileName();
        var environment = new Dictionary<string, string>
        {
            ["GIT_INDEX_FILE"] = temporaryIndex
        };
        async Task<GitCommandResult> WithIndex(params string[] args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await git.RunAsync(workingDirectory, args,
                TimeSpan.FromMinutes(3), cancellationToken, environment)
                .ConfigureAwait(false);
        }

        try
        {
            await File.WriteAllBytesAsync(temporaryPathspec,
                new UTF8Encoding(false).GetBytes(string.Join('\0', selected) + "\0"),
                cancellationToken).ConfigureAwait(false);

            var read = await WithIndex("read-tree", "HEAD").ConfigureAwait(false);
            if (read.ExitCode != 0)
                return Result.Failure("CACHED_DELETE_READ_TREE_FAILED", read);
            var remove = await WithIndex(
                "--literal-pathspecs", "rm", "--cached", "-r",
                "--ignore-unmatch",
                "--pathspec-from-file=" + temporaryPathspec,
                "--pathspec-file-nul").ConfigureAwait(false);
            if (remove.ExitCode != 0)
                return Result.Failure("CACHED_DELETE_TEMP_REMOVE_FAILED", remove);
            var tree = await WithIndex("write-tree").ConfigureAwait(false);
            if (tree.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(tree.StandardOutput))
                return Result.Failure("CACHED_DELETE_WRITE_TREE_FAILED", tree);

            var commit = await run(new[] {
                "-c", "user.name=ProjectHub",
                "-c", "user.email=projecthub@localhost",
                "commit-tree", tree.StandardOutput.Trim(),
                "-p", previousHead.StandardOutput.Trim(),
                "-m", "ProjectHub finalize staged tracked-file removals"
            }).ConfigureAwait(false);
            if (commit.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(commit.StandardOutput))
                return Result.Failure("CACHED_DELETE_COMMIT_TREE_FAILED", commit);

            var update = await run(new[] {
                "update-ref", "refs/heads/main", commit.StandardOutput.Trim(),
                previousHead.StandardOutput.Trim()
            }).ConfigureAwait(false);
            if (update.ExitCode != 0)
                return Result.Failure("CACHED_DELETE_HEAD_UPDATE_FAILED", update);

            foreach (var path in selected)
            {
                var indexed = await run(new[] {
                    "ls-files", "--", path
                }).ConfigureAwait(false);
                var headFile = await run(new[] {
                    "ls-tree", "-r", "--name-only", "HEAD", "--", path
                }).ConfigureAwait(false);
                var localPath = Path.Combine(workingDirectory,
                    path.Replace('/', Path.DirectorySeparatorChar));
                if (indexed.ExitCode != 0 || headFile.ExitCode != 0 ||
                    !string.IsNullOrWhiteSpace(indexed.StandardOutput) ||
                    !string.IsNullOrWhiteSpace(headFile.StandardOutput) ||
                    !File.Exists(localPath) ||
                    new FileInfo(localPath).Length != originalSizes[path])
                    return Result.Failure("CACHED_DELETE_POST_VERIFY_FAILED");
            }
            return new(true, selected);
        }
        finally
        {
            try { File.Delete(temporaryPathspec); } catch { }
            try { File.Delete(temporaryIndex); } catch { }
            try { File.Delete(temporaryIndex + ".lock"); } catch { }
        }
    }
}
