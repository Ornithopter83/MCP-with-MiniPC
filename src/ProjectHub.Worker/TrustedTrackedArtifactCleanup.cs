using System.IO;

namespace ProjectHub.Worker;

// Trusted Git-finalize repair for the explicitly approved BeltScroll generated
// executable. It must never run in the WORK/QA/HIGH sandbox.
internal static class TrustedTrackedArtifactCleanup
{
    internal const string TargetPath =
        ".qa_logs/editor-publish-current/BeltScrollEditor.exe";
    private const string IntentScript = "tools/untrack_editor_publish_binary.ps1";
    private const string IntentDocument = "docs/review/m6e_repository_hygiene_gate.md";

    internal sealed record Result(
        bool Success,
        bool Removed = false,
        string? ErrorStage = null,
        GitCommandResult? Error = null)
    {
        public static Result NoChange() => new(true);
        public static Result Failure(string stage, GitCommandResult? error = null) =>
            new(false, ErrorStage: stage, Error: error);
    }

    // A disposable index makes the deletion-only tree; original staged files
    // cannot leak into the commit. Commit is created before modifying live index.
    internal static async Task<Result> ApplyAsync(
        string workingDirectory,
        ProcessGitCommandRunner git,
        Func<string[], Task<GitCommandResult>> run,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(Path.Combine(workingDirectory,
                IntentScript.Replace('/', Path.DirectorySeparatorChar))) ||
            !File.Exists(Path.Combine(workingDirectory,
                IntentDocument.Replace('/', Path.DirectorySeparatorChar))))
            return Result.NoChange();

        var trackedHead = await run(new[] {
            "ls-tree", "-r", "--name-only", "HEAD", "--", TargetPath
        }).ConfigureAwait(false);
        if (trackedHead.ExitCode != 0)
            return Result.Failure("CLEANUP_HEAD_INSPECT_FAILED", trackedHead);
        if (!string.Equals(trackedHead.StandardOutput.Trim(), TargetPath,
                StringComparison.Ordinal))
            return Result.NoChange();

        var ignored = await run(new[] {
            "check-ignore", "--no-index", "-q", "--", TargetPath
        }).ConfigureAwait(false);
        if (ignored.ExitCode != 0)
            return Result.Failure("CLEANUP_TARGET_NOT_IGNORED", ignored);

        var localPath = Path.Combine(workingDirectory,
            TargetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(localPath))
            return Result.Failure("CLEANUP_LOCAL_FILE_MISSING");
        var preservedSize = new FileInfo(localPath).Length;

        var indexTarget = await run(new[] {
            "ls-files", "--", TargetPath
        }).ConfigureAwait(false);
        if (indexTarget.ExitCode != 0)
            return Result.Failure("CLEANUP_INDEX_INSPECT_FAILED", indexTarget);
        var liveIndexContainsTarget =
            string.Equals(indexTarget.StandardOutput.Trim(), TargetPath,
                StringComparison.Ordinal);
        var stagedTarget = await run(new[] {
            "diff", "--cached", "--name-only", "--", TargetPath
        }).ConfigureAwait(false);
        if (stagedTarget.ExitCode != 0)
            return Result.Failure("CLEANUP_STAGED_INSPECT_FAILED", stagedTarget);
        if (liveIndexContainsTarget &&
            !string.IsNullOrWhiteSpace(stagedTarget.StandardOutput))
            return Result.Failure("CLEANUP_STAGED_TARGET_CONFLICT");

        var oldHead = await run(new[] { "rev-parse", "HEAD" })
            .ConfigureAwait(false);
        if (oldHead.ExitCode != 0 ||
            string.IsNullOrWhiteSpace(oldHead.StandardOutput))
            return Result.Failure("CLEANUP_HEAD_READ_FAILED", oldHead);

        var temporaryIndex = Path.Combine(
            Path.GetTempPath(),
            "projecthub-tracked-cleanup-" + Guid.NewGuid().ToString("N"));
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
            var read = await WithIndex("read-tree", "HEAD").ConfigureAwait(false);
            if (read.ExitCode != 0)
                return Result.Failure("CLEANUP_READ_TREE_FAILED", read);
            var remove = await WithIndex(
                "rm", "--cached", "--", TargetPath).ConfigureAwait(false);
            if (remove.ExitCode != 0)
                return Result.Failure("CLEANUP_TEMP_REMOVE_FAILED", remove);
            var tree = await WithIndex("write-tree").ConfigureAwait(false);
            if (tree.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(tree.StandardOutput))
                return Result.Failure("CLEANUP_WRITE_TREE_FAILED", tree);

            var commit = await run(new[] {
                "-c", "user.name=ProjectHub",
                "-c", "user.email=projecthub@localhost",
                "commit-tree", tree.StandardOutput.Trim(),
                "-p", oldHead.StandardOutput.Trim(),
                "-m", "chore: untrack generated editor executable"
            }).ConfigureAwait(false);
            if (commit.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(commit.StandardOutput))
                return Result.Failure("CLEANUP_COMMIT_TREE_FAILED", commit);

            // Only the approved path is removed from the live index.
            // No ordinary role is granted write access to .git.
            if (liveIndexContainsTarget)
            {
                var stage = await run(new[] {
                    "rm", "--cached", "--", TargetPath
                }).ConfigureAwait(false);
                if (stage.ExitCode != 0)
                    return Result.Failure("CLEANUP_INDEX_REMOVE_FAILED", stage);
            }

            var update = await run(new[] {
                "update-ref", "refs/heads/main", commit.StandardOutput.Trim(),
                oldHead.StandardOutput.Trim()
            }).ConfigureAwait(false);
            if (update.ExitCode != 0)
                return Result.Failure("CLEANUP_HEAD_UPDATE_FAILED", update);

            var indexedAfter = await run(new[] {
                "ls-files", "--", TargetPath
            }).ConfigureAwait(false);
            var headAfter = await run(new[] {
                "ls-tree", "-r", "--name-only", "HEAD", "--", TargetPath
            }).ConfigureAwait(false);
            if (indexedAfter.ExitCode != 0 || headAfter.ExitCode != 0 ||
                !string.IsNullOrWhiteSpace(indexedAfter.StandardOutput) ||
                !string.IsNullOrWhiteSpace(headAfter.StandardOutput) ||
                !File.Exists(localPath) ||
                new FileInfo(localPath).Length != preservedSize)
                return Result.Failure("CLEANUP_POST_VERIFY_FAILED");

            return new(true, Removed: true);
        }
        finally
        {
            try { File.Delete(temporaryIndex); }
            catch { }
            try { File.Delete(temporaryIndex + ".lock"); }
            catch { }
        }
    }
}
