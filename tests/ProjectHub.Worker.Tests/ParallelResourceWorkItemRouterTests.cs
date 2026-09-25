using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelResourceWorkItemRouterTests
{
    [Fact]
    public async Task ResourceFailureReturnsOnlyToOwningBlockedWorkItem()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-parallel-resource-router-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var host = new FakeHost();
        var registry = new MechanicalWorkRegistry();

        try
        {
            await using var queue = new ResourceSidecarQueue(
                bridgeServer: null,
                workingDirectory: root,
                jobCancellation: cts.Token,
                mechanicalWork: registry);
            await using var router = new ParallelResourceWorkItemRouter(
                host,
                queue,
                cts.Token);

            host.Publish(new ParallelWorkExternalBlock(
                "W9",
                "RESOURCE_REQUEST",
                "RESOURCE_TYPE: IMAGE\n작은 아이콘을 생성해 주세요.",
                null,
                "branch-W9",
                root,
                "session-W9"));

            var resume = await host.Resume.Task.WaitAsync(cts.Token);

            Assert.Equal("W9", resume.WorkItemId);
            Assert.Equal("RESOURCE_RESULT", resume.InputType);
            Assert.Contains("workItemId: W9", resume.Body);
            Assert.Contains("status: FAILED", resume.Body);
            Assert.Contains("RESOURCE_WEB_UNAVAILABLE", resume.Body);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task MissingWorktreeReturnsFailureWithoutWritingToMainWorkspace()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-parallel-resource-missing-worktree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var host = new FakeHost();

        try
        {
            await using var queue = new ResourceSidecarQueue(
                bridgeServer: null,
                workingDirectory: root,
                jobCancellation: cts.Token);
            await using var router = new ParallelResourceWorkItemRouter(
                host,
                queue,
                cts.Token);

            host.Publish(new ParallelWorkExternalBlock(
                "W3",
                "RESOURCE_REQUEST",
                "RESOURCE_TYPE: IMAGE\n아이콘을 생성해 주세요.",
                null,
                "branch-W3",
                Path.Combine(root, "missing"),
                "session-W3"));

            var resume = await host.Resume.Task.WaitAsync(cts.Token);

            Assert.Equal("W3", resume.WorkItemId);
            Assert.Contains("RESOURCE_WORKTREE_MISSING", resume.Body);
            Assert.Equal(0, queue.OutstandingCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InvalidResourceRequestIsReturnedAsMechanicalFailureWithoutQueueing()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-parallel-resource-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var host = new FakeHost();

        try
        {
            await using var queue = new ResourceSidecarQueue(
                bridgeServer: null,
                workingDirectory: root,
                jobCancellation: cts.Token);
            await using var router = new ParallelResourceWorkItemRouter(
                host,
                queue,
                cts.Token);

            host.Publish(new ParallelWorkExternalBlock(
                "W2",
                "RESOURCE_REQUEST",
                "형식 없는 요청",
                null,
                null,
                null,
                "session-W2"));

            var resume = await host.Resume.Task.WaitAsync(cts.Token);

            Assert.Equal("W2", resume.WorkItemId);
            Assert.Contains("RESOURCE_TYPE_MISSING", resume.Body);
            Assert.Equal(0, queue.OutstandingCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private sealed record ResumeCall(
        string WorkItemId,
        string InputType,
        string Body);

    private sealed class FakeHost : IParallelExternalBlockHost
    {
        public event Action<ParallelWorkExternalBlock>? ExternalBlockAvailable;

        public TaskCompletionSource<ResumeCall> Resume { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Publish(ParallelWorkExternalBlock block)
            => ExternalBlockAvailable?.Invoke(block);

        public Task<bool> ResumeExternalWorkItemAsync(
            string workItemId,
            string inputType,
            string body,
            CancellationToken cancellationToken = default)
        {
            Resume.TrySetResult(new ResumeCall(workItemId, inputType, body));
            return Task.FromResult(true);
        }
    }
}
