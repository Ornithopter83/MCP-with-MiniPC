using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class GitWorkspaceBootstrapperTests
{
    [Fact]
    public async Task MissingRepositoryIsInitializedAndRequestsBaseline()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = new ScriptedRunner();
            runner.Enqueue("rev-parse --show-toplevel", Fail());
            runner.Enqueue("init", Ok());
            runner.Enqueue("rev-parse --show-toplevel", Ok(workspace));
            runner.Enqueue("symbolic-ref --quiet --short HEAD", Ok("main"));
            runner.Enqueue("rev-parse --verify HEAD", Fail());
            runner.Enqueue("status --porcelain=v1 --untracked-files=all", Ok("?? app.cs"));

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.True(state.Success);
            Assert.True(state.InitializedNow);
            Assert.False(state.HasHead);
            Assert.True(state.IsDirty);
            Assert.True(state.NeedsBaseline);
            Assert.Equal("main", state.Branch);
            Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(new[] { "init" }));
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task ExistingCleanRepositoryNeedsNoBaseline()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = new ScriptedRunner();
            runner.Enqueue("rev-parse --show-toplevel", Ok(workspace));
            runner.Enqueue("symbolic-ref --quiet --short HEAD", Ok("main"));
            runner.Enqueue("rev-parse --verify HEAD", Ok("abc123"));
            runner.Enqueue("status --porcelain=v1 --untracked-files=all", Ok());

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.True(state.Success);
            Assert.False(state.InitializedNow);
            Assert.True(state.HasHead);
            Assert.False(state.IsDirty);
            Assert.False(state.NeedsBaseline);
            Assert.Equal("abc123", state.HeadCommit);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task ExistingDirtyRepositoryRequestsNewBaseline()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = new ScriptedRunner();
            runner.Enqueue("rev-parse --show-toplevel", Ok(workspace));
            runner.Enqueue("symbolic-ref --quiet --short HEAD", Ok("main"));
            runner.Enqueue("rev-parse --verify HEAD", Ok("abc123"));
            runner.Enqueue("status --porcelain=v1 --untracked-files=all", Ok(" M app.cs"));

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.True(state.Success);
            Assert.True(state.HasHead);
            Assert.True(state.IsDirty);
            Assert.True(state.NeedsBaseline);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task BaselineStagesEverythingAndCreatesLocalProjectHubCommit()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = new ScriptedRunner();
            runner.Enqueue("add --all", Ok());
            runner.Enqueue("-c user.name=ProjectHub -c user.email=projecthub@local commit --allow-empty --no-gpg-sign -m ProjectHub initial baseline", Ok());
            runner.Enqueue("rev-parse --verify HEAD", Ok("def456"));
            runner.Enqueue("status --porcelain=v1 --untracked-files=all", Ok());

            var state = new GitWorkspaceBootstrapState(
                true,
                null,
                workspace,
                workspace,
                "main",
                null,
                true,
                true);

            var result = await new GitWorkspaceBootstrapper(runner).CreateBaselineAsync(state);

            Assert.True(result.Success);
            Assert.Equal("def456", result.HeadCommit);
            Assert.False(result.IsDirty);
            Assert.False(result.NeedsBaseline);
            Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(new[] { "add", "--all" }));
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task DetachedHeadIsRejectedWithoutMutation()
    {
        var workspace = CreateWorkspace();
        try
        {
            var runner = new ScriptedRunner();
            runner.Enqueue("rev-parse --show-toplevel", Ok(workspace));
            runner.Enqueue("symbolic-ref --quiet --short HEAD", Fail());

            var state = await new GitWorkspaceBootstrapper(runner).PrepareAsync(workspace);

            Assert.False(state.Success);
            Assert.Equal("GIT_BOOTSTRAP_ATTACHED_BRANCH_REQUIRED", state.ErrorCode);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.SequenceEqual(new[] { "add", "--all" }));
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    private static GitCommandResult Ok(string output = "")
        => new(0, output, string.Empty);

    private static GitCommandResult Fail()
        => new(1, string.Empty, "failed");

    private static string CreateWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "ProjectHubGitBootstrapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ScriptedRunner : IGitWorktreeCommandRunner
    {
        private readonly Dictionary<string, Queue<GitCommandResult>> _responses = new(StringComparer.Ordinal);
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
