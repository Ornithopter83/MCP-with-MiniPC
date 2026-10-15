using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityFreeDesignToolPolicyTests
{
    [Fact]
    public void FindsKnownLocalExecutableWithoutLaunchingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "design-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "fake-unity-design-tool.exe");
            File.WriteAllBytes(executable, new byte[] { 1 });
            Assert.Equal(executable, UnityFreeDesignToolPolicy.FindExecutable(
                "fake-unity-design-tool.exe", new[] { executable }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DesignGuidanceNeverAddsNewControlMarkersOrPaidModelDependency()
    {
        var guidance = UnityFreeDesignToolPolicy.BuildGuidance();
        Assert.Contains("Inkscape:", guidance);
        Assert.Contains("Blender", guidance);
        Assert.Contains("ComfyUI", guidance);
        Assert.Contains("로컬 생성 모델", guidance);
        Assert.DoesNotContain("@@", guidance);
        Assert.Contains("Editor 조작 중에는 다른 AI 작업과 병렬 실행하지 마라", guidance);
    }
}
