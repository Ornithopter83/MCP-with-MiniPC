using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkSidecarTests
{
    [Theory]
    [InlineData("IMAGE", "image")]
    [InlineData("AUDIO", "audio")]
    [InlineData("VIDEO", "video")]
    [InlineData("DOCUMENT", "document")]
    [InlineData("FILE", "file")]
    public void ResourceStagingUsesSharedTypedTempFolder(string resourceType, string expectedSegment)
    {
        var repository = Path.Combine(
            Path.GetTempPath(),
            "projecthub-resource-staging-" + Guid.NewGuid().ToString("N"),
            "sample");
        Directory.CreateDirectory(repository);

        try
        {
            var runtime = WorkerPaths.GetRepositoryRuntimePaths(repository);
            var path = WorkerPaths.BuildResourceStagingDirectory(
                runtime,
                resourceType,
                "abc123");

            Assert.Equal(
                Path.Combine(runtime.TempRoot, expectedSegment, "abc123"),
                path);
            Assert.DoesNotContain(
                Path.DirectorySeparatorChar + "worktrees" + Path.DirectorySeparatorChar,
                path,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            var parent = Directory.GetParent(repository)!.FullName;
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }

    [Theory]
    [InlineData("RESOURCE_CAPTURE_FAILED", "capture failed")]
    [InlineData("RESOURCE_DOWNLOAD_FAILED", "download failed")]
    [InlineData("RESOURCE_WEB_DELIVERY_FAILED", "send failed")]
    [InlineData("OTHER", "RESOURCE_URL_NOT_ALLOWED")]
    public void CaptureTransportFailureIsRetryable(string errorCode, string message)
    {
        var completion = new ResourceSidecarCompletion(
            "request",
            "IMAGE",
            false,
            message,
            errorCode,
            Array.Empty<string>());

        Assert.True(ResourceSidecarQueue.IsRetryableCaptureTransportFailure(completion));
    }

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
