using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class GitWorkspaceBootstrapperTests
{
    [Fact]
    public async Task MissingRepositoryIsRejectedWithoutLocalInit()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = new ScriptedRunner();
            runner.Enqueue("rev-parse --show-toplevel", Fail());

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.False(state.Success);
            Assert.Equal("GIT_REMOTE_REPOSITORY_REQUIRED", state.ErrorCode);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("init"));
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task ParentRepositoryIsRejectedInsteadOfBeingAdopted()
    {
        var workspace = CreateWorkspace();
        var parent = Directory.GetParent(workspace)!.FullName;
        try
        {
            var runner = new ScriptedRunner();
            runner.Enqueue("rev-parse --show-toplevel", Ok(parent));

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.False(state.Success);
            Assert.Equal("GIT_REMOTE_EXACT_ROOT_REQUIRED", state.ErrorCode);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task ExistingRepositoryWithoutOriginIsRejected()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = BaseRepositoryRunner(workspace);
            runner.Enqueue("remote get-url origin", Fail());

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.False(state.Success);
            Assert.Equal("GIT_REMOTE_ORIGIN_REQUIRED", state.ErrorCode);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Theory]
    [InlineData("../local-repo")]
    [InlineData("C:/local/repo")]
    [InlineData("file:///C:/local/repo")]
    public async Task LocalOriginIsRejected(string origin)
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = BaseRepositoryRunner(workspace);
            runner.Enqueue("remote get-url origin", Ok(origin));

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.False(state.Success);
            Assert.Equal("GIT_REMOTE_ORIGIN_NETWORK_REQUIRED", state.ErrorCode);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task DirtyWorkspaceIsRejectedBeforeRemoteFetch()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = BaseRepositoryRunner(workspace);
            runner.Enqueue("remote get-url origin", Ok("https://example.invalid/repo.git"));
            runner.Enqueue("status --porcelain=v1 --untracked-files=all", Ok(" M app.cs"));

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.False(state.Success);
            Assert.Equal("GIT_REMOTE_WORKSPACE_DIRTY", state.ErrorCode);
            Assert.True(state.IsDirty);
            Assert.DoesNotContain(
                runner.Calls,
                call => call.Arguments.Count > 0 && call.Arguments[0] == "fetch");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task RemoteFetchFailureBlocksLaunch()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = BaseRepositoryRunner(workspace);
            runner.Enqueue("remote get-url origin", Ok("https://example.invalid/repo.git"));
            runner.Enqueue("status --porcelain=v1 --untracked-files=all", Ok());
            runner.Enqueue("fetch --prune origin", Fail());

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.False(state.Success);
            Assert.Equal("GIT_REMOTE_FETCH_FAILED", state.ErrorCode);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task MissingMatchingRemoteBranchBlocksLaunch()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = BaseRepositoryRunner(workspace);
            runner.Enqueue("remote get-url origin", Ok("git@github.com:owner/repo.git"));
            runner.Enqueue("status --porcelain=v1 --untracked-files=all", Ok());
            runner.Enqueue("fetch --prune origin", Ok());
            runner.Enqueue("rev-parse --verify refs/remotes/origin/main^{commit}", Fail());

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.False(state.Success);
            Assert.Equal("GIT_REMOTE_BRANCH_REQUIRED", state.ErrorCode);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task LocalHeadMustExactlyMatchFetchedRemoteHead()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = BaseRepositoryRunner(workspace, "local123");
            runner.Enqueue("remote get-url origin", Ok("ssh://git@example.invalid/repo.git"));
            runner.Enqueue("status --porcelain=v1 --untracked-files=all", Ok());
            runner.Enqueue("fetch --prune origin", Ok());
            runner.Enqueue("rev-parse --verify refs/remotes/origin/main^{commit}", Ok("remote456"));

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.False(state.Success);
            Assert.Equal("GIT_REMOTE_HEAD_MISMATCH", state.ErrorCode);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task CleanRepositorySyncedWithOriginIsAccepted()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = BaseRepositoryRunner(workspace, "abc123");
            runner.Enqueue("remote get-url origin", Ok("https://example.invalid/repo.git"));
            runner.Enqueue("status --porcelain=v1 --untracked-files=all", Ok());
            runner.Enqueue("fetch --prune origin", Ok());
            runner.Enqueue("rev-parse --verify refs/remotes/origin/main^{commit}", Ok("abc123"));

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.True(state.Success);
            Assert.Equal("main", state.Branch);
            Assert.Equal("abc123", state.HeadCommit);
            Assert.False(state.IsDirty);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("init"));
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("add"));
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("commit"));
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    private static ScriptedRunner BaseRepositoryRunner(
        string workspace,
        string head = "abc123")
    {
        var runner = new ScriptedRunner();
        runner.Enqueue("rev-parse --show-toplevel", Ok(workspace));
        runner.Enqueue("symbolic-ref --quiet --short HEAD", Ok("main"));
        runner.Enqueue("rev-parse --verify HEAD", Ok(head));
        return runner;
    }

    private static GitCommandResult Ok(string output = "")
        => new(0, output, string.Empty);

    private static GitCommandResult Fail()
        => new(1, string.Empty, "failed");

    private static string CreateWorkspace()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubGitBootstrapTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ScriptedRunner : IGitWorktreeCommandRunner
    {
        private readonly Dictionary<string, Queue<GitCommandResult>> _responses =
            new(StringComparer.Ordinal);

        public List<(string WorkingDirectory, IReadOnlyList<string> Arguments)> Calls { get; } = new();

        public void Enqueue(string command, GitCommandResult result)
        {
            if (!_responses.TryGetValue(command, out var queue))
            {
                queue = new Queue<GitCommandResult>();
                _responses[command] = queue;
            }

            queue.Enqueue(result);
        }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((workingDirectory, arguments.ToArray()));
            var key = string.Join(" ", arguments);
            if (!_responses.TryGetValue(key, out var queue) || queue.Count == 0)
                throw new InvalidOperationException("예상하지 않은 Git 명령: " + key);
            return Task.FromResult(queue.Dequeue());
        }
    }
}
