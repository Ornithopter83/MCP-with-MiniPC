using System.Diagnostics;

namespace ProjectHub.Worker.Tests;

public sealed class TrustedTrackedArtifactCleanupTests
{
    [Fact]
    public async Task Finalize_RemovesOnlyApprovedTrackedBinary_AndPreservesStagedWork()
    {
        using var fixture = new GitFixture(withIntent: true);
        fixture.SetWorkChange();
        fixture.StageUnrelated();

        var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone fixture final",
            new[] { "src/work.txt" }, null, CancellationToken.None);

        Assert.True(result.Success, result.Summary);
        Assert.Contains("trustedCleanup=REMOVED_AND_REMOTE_VERIFIED", result.Summary);
        Assert.Contains("commitCreated=YES", result.Summary);
        Assert.Equal("updated", fixture.Git("show", "HEAD~1:src/work.txt").Trim());
        Assert.Equal("", fixture.Git("ls-tree", "-r", "--name-only",
            "HEAD", "--", TrustedTrackedArtifactCleanup.TargetPath).Trim());
        Assert.Equal("", fixture.GitBare("ls-tree", "-r", "--name-only",
            "main", "--", TrustedTrackedArtifactCleanup.TargetPath).Trim());
        Assert.Equal("", fixture.Git("ls-files", "--",
            TrustedTrackedArtifactCleanup.TargetPath).Trim());
        Assert.Equal("original executable", File.ReadAllText(fixture.Target));
        Assert.Equal("unrelated.txt", fixture.Git(
            "diff", "--cached", "--name-only").Trim());
        Assert.Equal("old", fixture.GitBare("show", "main:unrelated.txt").Trim());
        Assert.Equal(".qa_logs/historical-preserved.log",
            fixture.GitBare("ls-tree", "-r", "--name-only", "main", "--",
                ".qa_logs/historical-preserved.log").Trim());

        var repeated = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone fixture repeated final",
            Array.Empty<string>(), null, CancellationToken.None);
        Assert.True(repeated.Success, repeated.Summary);
        Assert.Contains("trustedCleanup=NONE", repeated.Summary);
        Assert.Contains("noChanges=YES", repeated.Summary);
        Assert.Equal("unrelated.txt", fixture.Git(
            "diff", "--cached", "--name-only").Trim());
    }

    [Fact]
    public async Task Finalize_RespectsAnAlreadyStagedCachedOnlyDeletion()
    {
        using var fixture = new GitFixture(withIntent: true);
        fixture.Git("rm", "--cached", "--",
            TrustedTrackedArtifactCleanup.TargetPath);

        var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone cached removal",
            new[] { TrustedTrackedArtifactCleanup.TargetPath },
            null, CancellationToken.None);

        Assert.True(result.Success, result.Summary);
        Assert.Contains("trustedCleanup=REMOVED_AND_REMOTE_VERIFIED", result.Summary);
        Assert.True(File.Exists(fixture.Target));
        Assert.Equal("", fixture.Git("diff", "--cached", "--name-only").Trim());
        Assert.Equal("", fixture.GitBare("ls-tree", "-r", "--name-only",
            "main", "--", TrustedTrackedArtifactCleanup.TargetPath).Trim());
    }

    [Fact]
    public async Task Finalize_DoesNotRemoveArtifactWithoutExplicitIntent()
    {
        using var fixture = new GitFixture(withIntent: false);
        var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone no-op",
            Array.Empty<string>(), null, CancellationToken.None);
        Assert.True(result.Success, result.Summary);
        Assert.Contains("commitCreated=NO", result.Summary);
        Assert.Contains("noChanges=YES", result.Summary);
        Assert.Equal(TrustedTrackedArtifactCleanup.TargetPath,
            fixture.GitBare("ls-tree", "-r", "--name-only",
                "main", "--", TrustedTrackedArtifactCleanup.TargetPath).Trim());
    }

    [Fact]
    public async Task Finalize_WhenRealIndexIsLocked_DoesNotPushPreparedCleanup()
    {
        using var fixture = new GitFixture(withIntent: true);
        var previous = fixture.Git("rev-parse", "HEAD").Trim();
        var lockPath = Path.Combine(fixture.Repo, ".git", "index.lock");
        File.WriteAllText(lockPath, "lock");
        var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone cleanup blocked",
            Array.Empty<string>(), null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("CLEANUP_INDEX_REMOVE_FAILED", result.Summary);
        Assert.Equal(previous, fixture.Git("rev-parse", "HEAD").Trim());
        Assert.Equal(previous, fixture.GitBare("rev-parse", "refs/heads/main").Trim());
        Assert.True(File.Exists(fixture.Target));
    }

    [Fact]
    public async Task Finalize_RejectsExistingStagedChangesToCleanupTarget()
    {
        using var fixture = new GitFixture(withIntent: true);
        File.WriteAllText(fixture.Target, "modified executable");
        fixture.Git("add", "-f", "--", TrustedTrackedArtifactCleanup.TargetPath);
        var previous = fixture.Git("rev-parse", "HEAD").Trim();

        var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone cleanup conflict",
            Array.Empty<string>(), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("CLEANUP_STAGED_TARGET_CONFLICT", result.Summary);
        Assert.Equal(previous, fixture.GitBare("rev-parse", "refs/heads/main").Trim());
        Assert.Equal(TrustedTrackedArtifactCleanup.TargetPath,
            fixture.Git("diff", "--cached", "--name-only",
                "--", TrustedTrackedArtifactCleanup.TargetPath).Trim());
    }

    private sealed class GitFixture : IDisposable
    {
        public string Repo { get; }
        public string Target { get; }
        private readonly string _base;
        private readonly string _bare;

        public GitFixture(bool withIntent)
        {
            _base = Path.Combine(Path.GetTempPath(),
                "projecthub-git-cleanup-test-" + Guid.NewGuid().ToString("N"));
            Repo = Path.Combine(_base, "working");
            _bare = Path.Combine(_base, "remote.git");
            Directory.CreateDirectory(Repo);
            Git("init", "-q");
            Git("checkout", "-q", "-b", "main");
            Git("config", "user.name", "ProjectHub Test");
            Git("config", "user.email", "projecthub-test@example.invalid");

            Write(".gitignore", ".qa_logs/\n");
            Write("src/work.txt", "original");
            Write("unrelated.txt", "old");
            Write(".qa_logs/historical-preserved.log", "historical log");
            Target = Path.Combine(Repo,
                TrustedTrackedArtifactCleanup.TargetPath.Replace('/', Path.DirectorySeparatorChar));
            Write(TrustedTrackedArtifactCleanup.TargetPath, "original executable");

            if (withIntent)
            {
                Write("tools/untrack_editor_publish_binary.ps1", "# cleanup intent\n");
                Write("docs/review/m6e_repository_hygiene_gate.md", "# cleanup gate\n");
            }

            Git("add", "-A");
            Git("add", "-f", "--", TrustedTrackedArtifactCleanup.TargetPath,
                ".qa_logs/historical-preserved.log");
            Git("commit", "-qm", "initial fixture");
            Run(_base, "init", "--bare", "-q", _bare);
            Git("remote", "add", "origin", _bare);
            Git("push", "-q", "-u", "origin", "main");
        }

        public void StageUnrelated()
        {
            Write("unrelated.txt", "staged but unrelated");
            Git("add", "--", "unrelated.txt");
        }

        public void SetWorkChange() => Write("src/work.txt", "updated");

        private void Write(string path, string content)
        {
            var fullPath = Path.Combine(Repo,
                path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public string Git(params string[] args) => Run(Repo, args);
        public string GitBare(params string[] args)
        {
            var all = new[] { "--git-dir=" + _bare }.Concat(args).ToArray();
            return Run(_base, all);
        }

        private static string Run(string directory, params string[] args)
        {
            var info = new ProcessStartInfo("git")
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "git " + string.Join(" ", args) + ": " + error + " " + output);
            return output;
        }

        public void Dispose()
        {
            if (!Directory.Exists(_base))
                return;
            // Git objects are read-only on Windows. Clear those attributes
            // before deleting this test-owned isolated fixture directory.
            foreach (var file in Directory.EnumerateFiles(
                         _base, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_base, recursive: true);
        }
    }
}
