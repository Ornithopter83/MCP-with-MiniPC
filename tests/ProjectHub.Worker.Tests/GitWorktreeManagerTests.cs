using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class GitWorktreeManagerTests
{
    [Fact]
    public void WorktreePathIsOutsideRepositoryAndBranchIsStable()
    {
        var parent = Path.Combine(Path.GetTempPath(), "projecthub-worktree-path-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(parent, "repo");
        Directory.CreateDirectory(repository);

        try
        {
            var path = GitWorktreeManager.BuildWorktreePath(repository, "job-1", "W1");
            var branchA = GitWorktreeManager.BuildBranchName("job-1", "W1");
            var branchB = GitWorktreeManager.BuildBranchName("job-1", "W1");

            Assert.StartsWith(
                Path.Combine(parent, ".projecthub-worktrees") + Path.DirectorySeparatorChar,
                path,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Assert.False(
                Path.GetFullPath(path).StartsWith(
                    Path.GetFullPath(repository) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            Assert.Equal(branchA, branchB);
            Assert.StartsWith("projecthub/", branchA, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task PrepareCreatesBranchAndWorktreeFromResolvedCommit()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "abc123");
        runner.Enqueue(0, "");
        runner.Enqueue(1, "");
        runner.Enqueue(0, "Preparing worktree");
        runner.Enqueue(0, "abc123");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.PrepareAsync(root, "job", "W1", "main");

            Assert.True(result.Success);
            Assert.False(result.Reused);
            Assert.Equal("abc123", result.BaseCommit);
            Assert.Equal("abc123", result.HeadCommit);
            Assert.Equal(GitWorktreeManager.BuildBranchName("job", "W1"), result.Branch);

            var add = runner.Calls.Single(call => call.Arguments.Count > 1 && call.Arguments[0] == "worktree" && call.Arguments[1] == "add");
            Assert.Contains("-b", add.Arguments);
            Assert.Contains(result.Branch, add.Arguments);
            Assert.Contains(result.WorktreePath, add.Arguments);
            Assert.Equal("abc123", add.Arguments[^1]);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task PrepareReusesOnlyRegisteredWorktreeWithExpectedBranch()
    {
        var root = CreateTempRepositoryDirectory();
        var expectedPath = GitWorktreeManager.BuildWorktreePath(root, "job", "W1");
        var expectedBranch = GitWorktreeManager.BuildBranchName("job", "W1");
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, $"worktree {expectedPath}\nHEAD head999\nbranch refs/heads/{expectedBranch}\n");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.PrepareAsync(root, "job", "W1", "main");

            Assert.True(result.Success);
            Assert.True(result.Reused);
            Assert.Equal("head999", result.HeadCommit);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 1 && call.Arguments[0] == "worktree" && call.Arguments[1] == "add");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task PrepareRejectsUnexpectedExistingBranchInsteadOfReusingIt()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "abc123");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.PrepareAsync(root, "job", "W1", "main");

            Assert.False(result.Success);
            Assert.Equal("WORKTREE_BRANCH_EXISTS", result.ErrorCode);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 1 && call.Arguments[0] == "worktree" && call.Arguments[1] == "add");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }


    [Fact]
    public async Task CheckpointCommitsDirtyWorktreeWithoutPushOrForce()
    {
        var root = CreateTempRepositoryDirectory();
        var worktree = Path.Combine(Directory.GetParent(root)!.FullName, "worktree");
        Directory.CreateDirectory(worktree);
        var runner = new FakeGitRunner(root);

        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, " M changed.cs");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "[projecthub/job/W1 new456] checkpoint");
        runner.Enqueue(0, "new456");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.CreateCheckpointAsync(worktree, "W1");

            Assert.True(result.Success);
            Assert.True(result.CreatedCommit);
            Assert.Equal("new456", result.HeadCommit);
            Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(new[] { "add", "--all" }));
            Assert.Contains(runner.Calls, call => call.Arguments.Contains("commit"));
            Assert.DoesNotContain(runner.Calls.SelectMany(call => call.Arguments), argument => argument == "push");
            Assert.DoesNotContain(runner.Calls.SelectMany(call => call.Arguments), argument => argument == "--force" || argument == "-f");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task CheckpointReusesCleanHeadWithoutCreatingCommit()
    {
        var root = CreateTempRepositoryDirectory();
        var worktree = Path.Combine(Directory.GetParent(root)!.FullName, "worktree");
        Directory.CreateDirectory(worktree);
        var runner = new FakeGitRunner(root);

        runner.Enqueue(0, "head123");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.CreateCheckpointAsync(worktree, "W1");

            Assert.True(result.Success);
            Assert.False(result.CreatedCommit);
            Assert.Equal("head123", result.HeadCommit);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("commit"));
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.SequenceEqual(new[] { "add", "--all" }));
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task RemoveRefusesDirtyWorktreeAndNeverUsesForce()
    {
        var root = CreateTempRepositoryDirectory();
        var worktree = Path.Combine(Path.GetTempPath(), "projecthub-worktree-dirty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(worktree);
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, "head123");
        runner.Enqueue(0, "projecthub/job/W1");
        runner.Enqueue(0, " M changed.cs");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.RemoveAsync(root, worktree, "projecthub/job/W1");

            Assert.False(result.Success);
            Assert.Equal("WORKTREE_DIRTY", result.ErrorCode);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("remove"));
            Assert.DoesNotContain(runner.Calls.SelectMany(call => call.Arguments), argument => argument == "--force" || argument == "-f");
        }
        finally
        {
            Directory.Delete(worktree, true);
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingFastForwardsOnlyCleanTargetBranch()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "integrated456");
        runner.Enqueue(0, "");
        runner.Enqueue(0, "Updating base123..integrated456");
        runner.Enqueue(0, "integrated456");
        runner.Enqueue(0, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationAsync(root, "integration-ref");

            Assert.True(result.Success);
            Assert.True(result.FastForwarded);
            Assert.Equal("main", result.TargetBranch);
            Assert.Equal("base123", result.BeforeHead);
            Assert.Equal("integrated456", result.AfterHead);

            var statusCalls = runner.Calls
                .Where(call => call.Arguments.Count > 0 && call.Arguments[0] == "status")
                .ToArray();
            Assert.Equal(2, statusCalls.Length);
            Assert.All(statusCalls, call =>
            {
                Assert.Contains(":(exclude).projecthub", call.Arguments);
                Assert.Contains(":(exclude).projecthub/**", call.Arguments);
            });

            var merge = runner.Calls.Single(call => call.Arguments.Count > 0 && call.Arguments[0] == "merge");
            Assert.Equal(new[] { "merge", "--ff-only", "integrated456" }, merge.Arguments);
            Assert.DoesNotContain(
                runner.Calls.SelectMany(call => call.Arguments),
                argument => argument is "push" or "reset" or "--force" or "-f");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingExcludesNestedProjectHubRuntimeState()
    {
        var root = CreateTempRepositoryDirectory();
        var workspace = Path.Combine(root, "src", "Game");
        Directory.CreateDirectory(workspace);
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "same123");
        runner.Enqueue(0, "same123");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationAsync(workspace, "integration-ref");

            Assert.True(result.Success);
            Assert.False(result.FastForwarded);

            var status = runner.Calls.Single(call =>
                call.Arguments.Count > 0 &&
                call.Arguments[0] == "status");
            Assert.Contains(":(exclude)src/Game/.projecthub", status.Arguments);
            Assert.Contains(":(exclude)src/Game/.projecthub/**", status.Arguments);
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingRejectsChangedPrimaryBranch()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "feature");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationAsync(
                root,
                "integration-ref",
                "main");

            Assert.False(result.Success);
            Assert.Equal("INTEGRATION_TARGET_BRANCH_CHANGED", result.ErrorCode);
            Assert.Equal("feature", result.TargetBranch);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 && call.Arguments[0] == "merge");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingRejectsDirtyTargetBeforeChangingHead()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, " M local-change.cs");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationAsync(root, "integration-ref");

            Assert.False(result.Success);
            Assert.Equal("INTEGRATION_TARGET_DIRTY", result.ErrorCode);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 0 && call.Arguments[0] == "merge");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    [Fact]
    public async Task IntegrationLandingRejectsNonFastForwardResult()
    {
        var root = CreateTempRepositoryDirectory();
        var runner = new FakeGitRunner(root);
        runner.Enqueue(0, root);
        runner.Enqueue(0, "");
        runner.Enqueue(0, "main");
        runner.Enqueue(0, "base123");
        runner.Enqueue(0, "other456");
        runner.Enqueue(1, "");

        try
        {
            var manager = new GitWorktreeManager(runner);
            var result = await manager.LandIntegrationAsync(root, "integration-ref");

            Assert.False(result.Success);
            Assert.Equal("INTEGRATION_NOT_FAST_FORWARD", result.ErrorCode);
            Assert.Equal("other456", result.IntegrationCommit);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 0 && call.Arguments[0] == "merge");
        }
        finally
        {
            DeleteTempTree(root);
        }
    }

    private static string CreateTempRepositoryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "projecthub-worktree-test-" + Guid.NewGuid().ToString("N"), "repo");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempTree(string repository)
    {
        var parent = Directory.GetParent(repository)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            Directory.Delete(parent, true);
    }

    private sealed class FakeGitRunner : IGitWorktreeCommandRunner
    {
        private readonly Queue<GitCommandResult> _results = new();

        public FakeGitRunner(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public List<GitCall> Calls { get; } = new();

        public void Enqueue(int exitCode, string stdout, string stderr = "")
            => _results.Enqueue(new GitCommandResult(exitCode, stdout, stderr));

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new GitCall(workingDirectory, arguments.ToArray()));
            if (_results.Count == 0)
                throw new InvalidOperationException("예상하지 않은 Git 호출입니다: " + string.Join(" ", arguments));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed record GitCall(string WorkingDirectory, IReadOnlyList<string> Arguments);
}
