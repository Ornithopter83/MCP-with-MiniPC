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
