using System.Formats.Tar;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class GitIsolationAndManifestTests
{
    [Fact]
    public void GitMetadataIsDetachedDuringWorkAndRestoredAfterward()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "projecthub-git-isolation-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "work");
        Directory.CreateDirectory(workspace);
        var gitPath = Path.Combine(workspace, ".git");
        File.WriteAllText(gitPath, "gitdir: ../metadata");

        try
        {
            var lease = GitMetadataIsolationLease.Detach(
                workspace,
                "job",
                "W10");

            Assert.False(File.Exists(gitPath));
            Assert.False(Directory.Exists(gitPath));

            var environment = GitMetadataIsolationLease.BuildGitNetworkDenyEnvironment();
            Assert.Equal("never", environment["GIT_CONFIG_VALUE_1"]);
            Assert.Equal("protocol.https.allow", environment["GIT_CONFIG_KEY_1"]);

            var restored = lease.Restore();

            Assert.True(restored.Success);
            Assert.True(File.Exists(gitPath));
            Assert.Equal("gitdir: ../metadata", File.ReadAllText(gitPath));
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task CommitManifestContainsChangedPathsAndInlineText()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "projecthub-commit-manifest-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "repo");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "README.md"),
            "# Current\n");
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "renamed.txt"),
            "renamed\n");

        var git = new QueueGitRunner();
        git.Enqueue(0, "commit123 parent456");
        git.Enqueue(0, "tree789");
        git.Enqueue(
            0,
            "M\tREADME.md\n" +
            "D\told.bin\n" +
            "R100\told.txt\trenamed.txt\n" +
            "M\tpublish/DesignTool.exe\n" +
            "M\tartifacts/package.zip\n");

        try
        {
            var builder = new GitCommitManifestBuilder(git);
            var result = await builder.BuildAsync(
                workspace,
                workspace,
                "job",
                "W10",
                "commit123");

            Assert.True(result.Success);
            Assert.NotNull(result.Manifest);
            Assert.NotNull(result.ManifestPath);
            Assert.True(File.Exists(result.ManifestPath));
            Assert.Equal("parent456", result.Manifest!.ParentCommit);
            Assert.Equal("tree789", result.Manifest.Tree);
            Assert.Contains(
                git.Calls,
                call => call.Count >= 3 &&
                        call[0] == "-c" &&
                        call[1] == "core.quotepath=false" &&
                        call[2] == "diff-tree");

            var readme = result.Manifest.ChangedFiles.Single(
                file => file.Path == "README.md");
            Assert.Equal("MODIFY", readme.ChangeType);
            Assert.True(readme.IsText);
            Assert.Equal("# Current\n", readme.Content);
            Assert.False(string.IsNullOrWhiteSpace(readme.Sha256));

            var deleted = result.Manifest.ChangedFiles.Single(
                file => file.Path == "old.bin");
            Assert.Equal("DELETE", deleted.ChangeType);
            Assert.Null(deleted.Content);

            var renamed = result.Manifest.ChangedFiles.Single(
                file => file.Path == "renamed.txt");
            Assert.Equal("RENAME", renamed.ChangeType);
            Assert.Equal("old.txt", renamed.PreviousPath);
            Assert.DoesNotContain(
                result.Manifest.ChangedFiles,
                file => file.Path.StartsWith("publish/", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                result.Manifest.ChangedFiles,
                file => file.Path.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }


    [Fact]
    public void GraphDeltaIncludesCommitManifestBody()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "projecthub-manifest-delta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var manifestPath = Path.Combine(parent, "manifest.json");
        File.WriteAllText(manifestPath, "{\"commit\":\"abc123\",\"changedFiles\":[]}");

        try
        {
            var item = new WorkItemSnapshot(
                "W10",
                "기능 구현",
                Array.Empty<string>(),
                WorkItemKind.Normal,
                WorkItemState.Completed,
                0,
                "base123",
                "branch-W10",
                "worktree-W10",
                "session-W10",
                "abc123",
                "완료",
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                ResultType: WorkItemResultType.CodeChange,
                CommitManifestPath: manifestPath);
            var previous = new WorkGraphSnapshot(
                "job",
                0,
                1,
                Array.Empty<WorkItemSnapshot>());
            var currentGraph = new WorkGraphSnapshot(
                "job",
                1,
                1,
                new[] { item });
            var snapshot = new ParallelWorkSchedulerSnapshot(
                currentGraph,
                Array.Empty<RunningWorkItemSnapshot>());

            var body = ParallelWorkSupervisor.FormatMechanicalGraphDeltaEvent(
                new[] { "WorkItem W10 완료" },
                previous,
                snapshot);

            Assert.Contains("resultType=CODE_CHANGE", body);
            Assert.Contains("commitManifest=" + manifestPath, body);
            Assert.DoesNotContain("commitManifests:", body);
            Assert.DoesNotContain("\"commit\":\"abc123\"", body);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }


    [Fact]
    public async Task IntegrationDependenciesAreExpandedAsIgnoredFileSnapshots()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "projecthub-integration-input-" + Guid.NewGuid().ToString("N"));
        var clone = Path.Combine(parent, "clone");
        var source = Path.Combine(parent, "source");
        Directory.CreateDirectory(Path.Combine(clone, ".git", "info"));
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "feature.txt"),
            "dependency content\n");
        await File.WriteAllBytesAsync(
            Path.Combine(source, "binary.dat"),
            new byte[] { 1, 2, 3, 4 });

        try
        {
            var runner = new ArchiveGitRunner(source);
            var manager = new GitWorktreeManager(runner);
            var result = await manager.StageIntegrationDependenciesAsync(
                clone,
                new[]
                {
                    new WorkItemDependencyResult(
                        "W10",
                        "commit123",
                        "선행 변경",
                        WorkItemResultType.CodeChange,
                        "manifest.json")
                });

            Assert.True(result.Success);
            Assert.True(result.SnapshotPaths.TryGetValue("W10", out var snapshot));
            Assert.True(File.Exists(Path.Combine(snapshot!, "feature.txt")));
            Assert.Equal(
                "dependency content\n",
                await File.ReadAllTextAsync(Path.Combine(snapshot!, "feature.txt")));
            Assert.Equal(
                new byte[] { 1, 2, 3, 4 },
                await File.ReadAllBytesAsync(Path.Combine(snapshot!, "binary.dat")));

            var exclude = await File.ReadAllTextAsync(
                Path.Combine(clone, ".git", "info", "exclude"));
            Assert.Contains(".projecthub-integration-inputs/", exclude);
            Assert.Contains(
                runner.Calls,
                call => call.Count > 0 && call[0] == "archive");
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }


    private sealed class ArchiveGitRunner : IGitWorktreeCommandRunner
    {
        private readonly string _source;

        public ArchiveGitRunner(string source)
        {
            _source = source;
        }

        public List<IReadOnlyList<string>> Calls { get; } = new();

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments.ToArray());
            if (arguments.Count > 0 &&
                string.Equals(arguments[0], "archive", StringComparison.Ordinal))
            {
                var output = arguments
                    .FirstOrDefault(argument => argument.StartsWith("--output=", StringComparison.Ordinal));
                if (output is null)
                    return Task.FromResult(new GitCommandResult(2, string.Empty, "missing output"));

                var archivePath = output["--output=".Length..];
                TarFile.CreateFromDirectory(
                    _source,
                    archivePath,
                    includeBaseDirectory: false);
                return Task.FromResult(new GitCommandResult(0, string.Empty, string.Empty));
            }

            return Task.FromResult(new GitCommandResult(
                2,
                string.Empty,
                "unexpected command: " + string.Join(" ", arguments)));
        }
    }

    private sealed class QueueGitRunner : IGitWorktreeCommandRunner
    {
        private readonly Queue<GitCommandResult> _results = new();

        public List<IReadOnlyList<string>> Calls { get; } = new();

        public void Enqueue(int exitCode, string stdout, string stderr = "")
            => _results.Enqueue(new GitCommandResult(exitCode, stdout, stderr));

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments.ToArray());
            if (_results.Count == 0)
            {
                throw new InvalidOperationException(
                    "예상하지 않은 Git 호출입니다: " +
                    string.Join(" ", arguments));
            }

            return Task.FromResult(_results.Dequeue());
        }
    }
}
