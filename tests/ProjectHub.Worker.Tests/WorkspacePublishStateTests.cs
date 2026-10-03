using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkspacePublishStateTests
{
    [Fact]
    public async Task PublishBecomesStaleOnlyAfterLaterCodeLanding()
    {
        var root = CreateRoot();
        try
        {
            var state = new WorkspacePublishState(root, "job");

            var first = await state.MarkCodeLandedAsync("code-a");
            Assert.False(first.IsStale);
            Assert.Equal(1, first.CodeGeneration);

            var published = await state.MarkPublishedAsync(4);
            Assert.False(published.IsStale);
            Assert.True(published.HasSuccessfulPublish);
            Assert.Equal(published.CodeGeneration, published.PublishedCodeGeneration);

            var later = await state.MarkCodeLandedAsync("code-b");
            Assert.True(later.IsStale);
            Assert.Equal(2, later.CodeGeneration);
            Assert.Equal(1, later.PublishedCodeGeneration);

            var duplicate = await state.MarkCodeLandedAsync("code-b");
            Assert.Equal(2, duplicate.CodeGeneration);
            Assert.True(duplicate.IsStale);

            var republished = await state.MarkPublishedAsync(7);
            Assert.False(republished.IsStale);
            Assert.Equal(2, republished.PublishedCodeGeneration);
            Assert.Equal(7, republished.LastPublishInvocation);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LandedResultRefsArePersistedAcrossInstances()
    {
        var root = CreateRoot();
        try
        {
            var first = new WorkspacePublishState(root, "job");
            await first.MarkCodeLandedAsync("code-a");
            await first.MarkPublishedAsync(3);

            var second = new WorkspacePublishState(root, "job");
            var restored = await second.ReadAsync();

            Assert.Contains(
                restored.LandedCodeResultRefs,
                value => string.Equals(value, "code-a", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("code-a", restored.LastCodeResultRef);
            Assert.True(restored.HasSuccessfulPublish);
            Assert.False(restored.IsStale);
        }
        finally
        {
            Directory.Delete(root, true);
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
}
