using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class TargetWorkspaceFinalizerPublishTests
{
    [Fact]
    public async Task FinalizerRequiresRepublishWhenLandedCodeIsNewerThanLastPublish()
    {
        var root = CreateRoot();
        try
        {
            var graph = CreateCompletedGraph("code-new");
            var publish = new WorkspacePublishState(root, "job");
            await publish.MarkCodeLandedAsync("code-old");
            await publish.MarkPublishedAsync(3);
            await publish.MarkCodeLandedAsync("code-new");

            var runner = CreateContainedResultRunner(root, "code-new");
            var finalizer = new TargetWorkspaceFinalizer(
                root,
                "main",
                new GitWorktreeManager(runner));
            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.False(result.Success);
            Assert.Equal("TARGET_PUBLISH_STALE", result.ErrorCode);
            Assert.Contains("#9 BUILD/PUBLISH", result.Message);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FinalizerAllowsEndAfterRepublishOfLatestLandedCode()
    {
        var root = CreateRoot();
        try
        {
            var graph = CreateCompletedGraph("code-new");
            var publish = new WorkspacePublishState(root, "job");
            await publish.MarkCodeLandedAsync("code-old");
            await publish.MarkPublishedAsync(3);
            await publish.MarkCodeLandedAsync("code-new");
            await publish.MarkPublishedAsync(5);

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

    private static WorkGraph CreateCompletedGraph(string resultRef)
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(
                new WorkItemSpec("10", "code", Kind: WorkItemKind.Normal, BaseRef: "base"))
        })).Success);
        Assert.True(graph.TryMarkRunning("10"));
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
        runner.Enqueue(0, root);
        runner.Enqueue(0, "main");
        runner.Enqueue(0, resultRef);
        runner.Enqueue(0, "head");
        runner.Enqueue(0, string.Empty);
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

    private sealed class SequenceRunner : IGitWorktreeCommandRunner
    {
        private readonly Queue<GitCommandResult> _results = new();

        public void Enqueue(int exitCode, string stdout, string stderr = "")
            => _results.Enqueue(new GitCommandResult(exitCode, stdout, stderr));

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (_results.Count == 0)
                throw new InvalidOperationException(
                    "예상하지 않은 Git 호출입니다: " + string.Join(" ", arguments));
            return Task.FromResult(_results.Dequeue());
        }
    }
}
