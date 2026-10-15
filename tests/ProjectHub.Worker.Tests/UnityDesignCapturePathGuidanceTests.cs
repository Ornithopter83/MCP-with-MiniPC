using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityDesignCapturePathGuidanceTests
{
    [Fact]
    public void ScreenshotOutputRequiresAbsoluteProjectPath()
    {
        var guidance = UnityDesignPromptPolicy.BuildCapturePathGuidance(
            Path.Combine(Path.GetTempPath(), "unity-project"));
        Assert.Contains("절대 경로", guidance);
        Assert.Contains("--save_path", guidance);
        Assert.Contains("Assets/docs", guidance);
        Assert.DoesNotContain("@@", guidance);
    }
}
