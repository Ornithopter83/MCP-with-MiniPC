using System.Diagnostics;

namespace ProjectHub.Worker.Tests;

public sealed class TrustedTrackedArtifactCleanupTests
{
    private const string Artifact = "generated/editor-build.bin";
    private const string OtherArtifact = "generated/other-package.zip";

    [Fact]
    public async Task ScopedCachedDeletion_KeepsLocalFileAndUnrelatedStagedChanges()
    {
        using var fixture = new GitFixture();
        fixture.StageCachedDeletion(Artifact);
        fixture.SetWorkChange();
        fixture.StageUnrelated();

        var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone fixture final",
            new[] { "src/work.txt", Artifact }, null, CancellationToken.None);

        Assert.True(result.Success, result.Summary);
        Assert.Contains("cachedOnlyDeletions=1", result.Summary);
        Assert.Contains("cachedDeletionRemoteVerified=YES", result.Summary);
        Assert.Contains("commitCreated=YES", result.Summary);
        Assert.Equal("updated", fixture.Git("show", "HEAD~1:src/work.txt").Trim());
        Assert.Equal("", fixture.Git("ls-tree", "-r", "--name-only",
            "HEAD", "--", Artifact).Trim());
        Assert.Equal("", fixture.GitBare("ls-tree", "-r", "--name-only",
            "main", "--", Artifact).Trim());
        Assert.Equal("", fixture.Git("ls-files", "--", Artifact).Trim());
        Assert.Equal("original artifact", File.ReadAllText(fixture.Local(Artifact)));
        Assert.Equal("unrelated.txt", fixture.Git(
            "diff", "--cached", "--name-only").Trim());
        Assert.Equal("old", fixture.GitBare("show", "main:unrelated.txt").Trim());
        Assert.Equal("generated/historical-evidence.log",
            fixture.GitBare("ls-tree", "-r", "--name-only", "main",
                "--", "generated/historical-evidence.log").Trim());

        var repeated = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone fixture repeat",
            Array.Empty<string>(), null, CancellationToken.None);
        Assert.True(repeated.Success, repeated.Summary);
        Assert.Contains("cachedOnlyDeletions=0", repeated.Summary);
        Assert.Contains("noChanges=YES", repeated.Summary);
        Assert.Equal("unrelated.txt", fixture.Git(
            "diff", "--cached", "--name-only").Trim());
    }

    [Fact]
    public async Task TrackedIgnoredFile_IsNotAutoRemovedWithoutStagedIntent()
    {
        using var fixture = new GitFixture();
        var oldHead = fixture.Git("rev-parse", "HEAD").Trim();

        var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone no-op",
            new[] { Artifact }, null, CancellationToken.None);

        Assert.True(result.Success, result.Summary);
        Assert.Contains("commitCreated=NO", result.Summary);
        Assert.Contains("cachedOnlyDeletions=0", result.Summary);
        Assert.Contains("noChanges=YES", result.Summary);
        Assert.Equal(oldHead, fixture.GitBare("rev-parse", "main").Trim());
        Assert.Equal(Artifact,
            fixture.GitBare("ls-tree", "-r", "--name-only",
                "main", "--", Artifact).Trim());
    }

    [Fact]
    public async Task ScopedCachedDeletion_LeavesOtherStagedDeletionsUntouched()
    {
        using var fixture = new GitFixture();
        fixture.StageCachedDeletion(Artifact);
        fixture.StageCachedDeletion(OtherArtifact);

        var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone selected removal",
            new[] { Artifact }, null, CancellationToken.None);

        Assert.True(result.Success, result.Summary);
        Assert.Contains("cachedOnlyDeletions=1", result.Summary);
        Assert.Equal("", fixture.GitBare("ls-tree", "-r",
            "--name-only", "main", "--", Artifact).Trim());
        Assert.Equal(OtherArtifact, fixture.GitBare("ls-tree", "-r",
            "--name-only", "main", "--", OtherArtifact).Trim());
        Assert.Equal(OtherArtifact, fixture.Git(
            "diff", "--cached", "--name-only").Trim());
        Assert.True(File.Exists(fixture.Local(Artifact)));
        Assert.True(File.Exists(fixture.Local(OtherArtifact)));
    }

    [Fact]
    public async Task ScopedCachedDeletion_FailsOnRealGitIndexLockWithoutPush()
    {
        using var fixture = new GitFixture();
        fixture.StageCachedDeletion(Artifact);
        var previous = fixture.Git("rev-parse", "HEAD").Trim();
        var lockPath = Path.Combine(fixture.Repo, ".git", "index.lock");
        File.WriteAllText(lockPath, "lock");

        var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone locked index",
            new[] { Artifact }, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("CACHED_DELETE_INDEX_LOCK_PRESENT", result.Summary);
        Assert.Equal(previous, fixture.Git("rev-parse", "HEAD").Trim());
        Assert.Equal(previous, fixture.GitBare("rev-parse", "main").Trim());
        Assert.True(File.Exists(fixture.Local(Artifact)));
    }

    [Fact]
    public async Task NormalSourceDeletion_IsHandledByExistingSourceCommitFlow()
    {
        using var fixture = new GitFixture();
        File.Delete(fixture.Local("src/work.txt"));

        var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
            fixture.Repo, "ProjectHub milestone source removal",
            new[] { "src/work.txt" }, null, CancellationToken.None);

        Assert.True(result.Success, result.Summary);
        Assert.Contains("cachedOnlyDeletions=0", result.Summary);
        Assert.Equal("", fixture.GitBare("ls-tree", "-r",
            "--name-only", "main", "--", "src/work.txt").Trim());
        Assert.Equal(Artifact, fixture.GitBare("ls-tree", "-r",
            "--name-only", "main", "--", Artifact).Trim());
    }

    private sealed class GitFixture : IDisposable
    {
        public string Repo { get; }
        private readonly string _base;
        private readonly string _bare;

        public GitFixture()
        {
            _base = Path.Combine(Path.GetTempPath(),
                "projecthub-scoped-index-test-" + Guid.NewGuid().ToString("N"));
            Repo = Path.Combine(_base, "working");
            _bare = Path.Combine(_base, "remote.git");
            Directory.CreateDirectory(Repo);
            Git("init", "-q");
            Git("checkout", "-q", "-b", "main");
            Git("config", "user.name", "ProjectHub Test");
            Git("config", "user.email", "projecthub-test@example.invalid");

            Write(".gitignore", "generated/\n");
            Write("src/work.txt", "original");
            Write("unrelated.txt", "old");
            Write("generated/historical-evidence.log", "historic report");
            Write(Artifact, "original artifact");
            Write(OtherArtifact, "another artifact");

            Git("add", "-A");
            Git("add", "-f", "--",
                Artifact, OtherArtifact, "generated/historical-evidence.log");
            Git("commit", "-qm", "fixture initial revision");
            Run(_base, "init", "--bare", "-q", _bare);
            Git("remote", "add", "origin", _bare);
            Git("push", "-q", "-u", "origin", "main");
        }

        public string Local(string path) => Path.Combine(Repo,
            path.Replace('/', Path.DirectorySeparatorChar));

        public void StageCachedDeletion(string path)
        {
            Git("rm", "--cached", "--", path);
            Assert.True(File.Exists(Local(path)));
        }

        public void StageUnrelated()
        {
            Write("unrelated.txt", "staged but unrelated");
            Git("add", "--", "unrelated.txt");
        }

        public void SetWorkChange() => Write("src/work.txt", "updated");

        private void Write(string path, string content)
        {
            var full = Local(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public string Git(params string[] args) => Run(Repo, args);
        public string GitBare(params string[] args) =>
            Run(_base, new[] { "--git-dir=" + _bare }.Concat(args).ToArray());

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
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "git " + string.Join(" ", args) + ": " + stderr + " " + stdout);
            return stdout;
        }

        public void Dispose()
        {
            if (!Directory.Exists(_base))
                return;
            foreach (var file in Directory.EnumerateFiles(
                         _base, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_base, recursive: true);
        }
    }
}
