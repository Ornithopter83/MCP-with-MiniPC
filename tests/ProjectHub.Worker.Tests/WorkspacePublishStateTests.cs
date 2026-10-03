using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkspacePublishStateTests
{
    [Fact]
    public async Task PublishFreshnessIsBoundToExactRemoteSourceCommit()
    {
        var root = CreateRoot();
        try
        {
            var state = new WorkspacePublishState(root, "job");

            var published = await state.MarkPublishedAsync("code-a", 4);
            Assert.True(published.HasSuccessfulPublish);
            Assert.Equal("code-a", published.PublishedSourceRef);
            Assert.Equal(4, published.LastPublishInvocation);
            Assert.False(published.IsStaleFor("code-a"));
            Assert.True(published.IsStaleFor("code-b"));

            var republished = await state.MarkPublishedAsync("code-b", 7);
            Assert.Equal("code-b", republished.PublishedSourceRef);
            Assert.Equal(7, republished.LastPublishInvocation);
            Assert.False(republished.IsStaleFor("code-b"));
            Assert.True(republished.IsStaleFor("code-a"));
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task PublishedSourceRefIsPersistedAcrossInstances()
    {
        var root = CreateRoot();
        try
        {
            var first = new WorkspacePublishState(root, "job");
            await first.MarkPublishedAsync("code-a", 3);

            var second = new WorkspacePublishState(root, "job");
            var restored = await second.ReadAsync();

            Assert.True(restored.HasSuccessfulPublish);
            Assert.Equal("code-a", restored.PublishedSourceRef);
            Assert.Equal(3, restored.LastPublishInvocation);
            Assert.False(restored.IsStaleFor("code-a"));
            Assert.True(restored.IsStaleFor("code-b"));
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-publish-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CleanupRoot(string root)
    {
        var runtime = WorkerPaths.GetRepositoryRuntimePaths(root).Root;
        if (Directory.Exists(runtime))
            Directory.Delete(runtime, true);
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
