using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkSidecarTests
{
    [Fact]
    public async Task ResourceQueuePreservesWorkItemOwnerThroughCompletionAndRegistry()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-resource-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var registry = new MechanicalWorkRegistry();

        try
        {
            await using var queue = new ResourceSidecarQueue(
                bridgeServer: null,
                workingDirectory: root,
                jobCancellation: cts.Token,
                mechanicalWork: registry);

            var request = queue.Enqueue(
                "IMAGE",
                "테스트 이미지 생성",
                workItemId: "W17");

            Assert.Equal("W17", request.WorkItemId);
            Assert.Null(request.TargetWorkingDirectory);

            await queue.WaitForIdleAsync(cts.Token);

            Assert.True(queue.TryDequeueCompletion(out var completion));
            Assert.Equal(request.Id, completion.RequestId);
            Assert.Equal("W17", completion.WorkItemId);
            Assert.False(completion.Success);
            Assert.Equal("RESOURCE_WEB_UNAVAILABLE", completion.ErrorCode);

            var mechanical = Assert.Single(registry.DrainCompletions(
                "RESOURCE",
                MechanicalWorkCompletionMode.FinalizeOnly,
                "W17"));
            Assert.Equal(request.Id, mechanical.Id);
            Assert.Equal("W17", mechanical.OwnerId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
