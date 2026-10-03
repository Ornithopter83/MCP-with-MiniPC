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
    public async Task IntegrationDependenciesAreExpandedOutsideCloneWithoutGitExcludeMutation()
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

            Assert.False(
                Path.GetFullPath(snapshot!).StartsWith(
                    Path.GetFullPath(clone) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            Assert.False(File.Exists(Path.Combine(clone, ".git", "info", "exclude")));
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


}
