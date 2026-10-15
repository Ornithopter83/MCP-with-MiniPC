using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerCardIconAssetsTests
{
    [Theory]
    [InlineData("current-console.png")]
    [InlineData("current-console-gray.png")]
    [InlineData("current-openai.png")]
    [InlineData("current-openai-gray.png")]
    [InlineData("current-muse.png")]
    [InlineData("current-web.png")]
    [InlineData("current-web-gray.png")]
    public void CardPngIsEmbeddedWithoutDependingOnCurrentDirectory(string file)
    {
        using var image = typeof(WorkerCardIconAssets).Assembly
            .GetManifestResourceStream("ProjectHub.Worker.Assets." + file);
        Assert.NotNull(image);
        Assert.True(image.Length > 0);
    }
}
