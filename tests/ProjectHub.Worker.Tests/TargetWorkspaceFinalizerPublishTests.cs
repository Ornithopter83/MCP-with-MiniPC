using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class TargetWorkspaceFinalizerPublishTests
{
    [Fact]
    public async Task FinalizerRequiresRepublishWhenPublishedCommitDiffersFromFinalRemoteCommit()
    {
        var root = CreateRoot();
        try
        {
            var graph = CreateCompletedGraph("code-new");
            var publish = new WorkspacePublishState(root, "job");
            await publish.MarkPublishedAsync("code-old", 3);

            var runner = new SequenceRunner();
            var finalizer = new TargetWorkspaceFinalizer(
                root,
                "main",
                new GitWorktreeManager(runner));
            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.False(result.Success);
            Assert.Equal("TARGET_PUBLISH_STALE", result.ErrorCode);
            Assert.Contains("#9 BUILD/PUBLISH", result.Message);
            Assert.Contains("code-new", result.Message);
            Assert.Contains("code-old", result.Message);
            Assert.Empty(runner.Calls);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FinalizerAllowsEndWhenPublishUsesFinalRemoteCommit()
    {
        var root = CreateRoot();
        try
        {
            var graph = CreateCompletedGraph("code-new");
            var publish = new WorkspacePublishState(root, "job");
            await publish.MarkPublishedAsync("code-new", 5);

            var runner = CreateContainedResultRunner(root, "code-new");
            var finalizer = new TargetWorkspaceFinalizer(
                root,
                "main",
                new GitWorktreeManager(runner));
            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.True(result.Success);
            Assert.Null(result.ErrorCode);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FinalizerSwitchesCleanCheckoutToRemoteResultBranch()
    {
        var root = CreateRoot();
        var resultBranch = GitWorktreeManager.BuildBranchName("job", "10");
        try
        {
            var graph = CreateCompletedGraph("code-new", resultBranch);
            var runner = new SequenceRunner();

            // InspectTargetContainmentAsync: current main is remote-synced, result is not contained.
            runner.Enqueue(0, root);
            runner.Enqueue(0, "main");
            runner.Enqueue(0, string.Empty);
            runner.Enqueue(0, "base123");
            runner.Enqueue(0, "base123");
            runner.Enqueue(0, "code-new");
            runner.Enqueue(1, string.Empty);

            // SwitchTargetToRemoteResultAsync.
            runner.Enqueue(0, root);
            runner.Enqueue(0, string.Empty);
            runner.Enqueue(0, "main");
            runner.Enqueue(0, "base123");
            runner.Enqueue(0, string.Empty);
            runner.Enqueue(0, "base123");
            runner.Enqueue(0, "code-new");
            runner.Enqueue(0, "code-new");
            runner.Enqueue(1, string.Empty);
            runner.Enqueue(0, string.Empty);
            runner.Enqueue(0, resultBranch);
            runner.Enqueue(0, "code-new");
            runner.Enqueue(0, string.Empty);

            var finalizer = new TargetWorkspaceFinalizer(
                root,
                "main",
                new GitWorktreeManager(runner));
            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.True(result.Success);
            Assert.Null(result.ErrorCode);
            Assert.Equal("code-new", result.FinalResultRef);
            Assert.True(result.CheckoutSwitched);
            Assert.Contains("checkout을 전환", result.Message);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FinalizerRetryAcceptsAlreadySwitchedResultBranch()
    {
        var root = CreateRoot();
        var resultBranch = GitWorktreeManager.BuildBranchName("job", "10");
        try
        {
            var graph = CreateCompletedGraph("code-new", resultBranch);
            var runner = new SequenceRunner();
            runner.Enqueue(0, root);
            runner.Enqueue(0, resultBranch);
            runner.Enqueue(0, string.Empty);
            runner.Enqueue(0, "code-new");
            runner.Enqueue(0, "code-new");
            runner.Enqueue(0, "code-new");
            runner.Enqueue(0, string.Empty);

            var finalizer = new TargetWorkspaceFinalizer(
                root,
                "main",
                new GitWorktreeManager(runner));
            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.True(result.Success);
            Assert.Null(result.ErrorCode);
            Assert.False(result.CheckoutSwitched);
            Assert.Equal("code-new", result.FinalResultRef);
            Assert.Contains("이미 최종 원격 CODE_CHANGE branch", result.Message);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static WorkGraph CreateCompletedGraph(
        string resultRef,
        string? branch = null)
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(
                new WorkItemSpec("10", "code", Kind: WorkItemKind.Normal, BaseRef: "base"))
        })).Success);
        Assert.True(graph.TryMarkRunning("10", branch));
        Assert.True(graph.TryMarkCompleted(
            "10",
            resultRef,
            "done",
            WorkItemResultType.CodeChange,
            "manifest.json"));
        return graph;
    }

    private static SequenceRunner CreateContainedResultRunner(
        string root,
        string resultRef)
    {
        var runner = new SequenceRunner();
        runner.Enqueue(0, root);          // rev-parse --show-toplevel
        runner.Enqueue(0, "main");       // symbolic-ref
        runner.Enqueue(0, string.Empty); // fetch origin
        runner.Enqueue(0, "head");       // local HEAD
        runner.Enqueue(0, "head");       // origin/main HEAD
        runner.Enqueue(0, resultRef);     // resultRef commit
        runner.Enqueue(0, string.Empty); // resultRef is ancestor of HEAD
        return runner;
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-finalizer-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(root).Root;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
        if (Directory.Exists(runtime))
            Directory.Delete(runtime, true);
    }

    private sealed class SequenceRunner : IGitCommandRunner
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
                throw new InvalidOperationException(
                    "예상하지 않은 Git 호출입니다: " + string.Join(" ", arguments));
            return Task.FromResult(_results.Dequeue());
        }
    }
}
