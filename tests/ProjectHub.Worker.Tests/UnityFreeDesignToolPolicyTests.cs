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
    public void CliToolAvailabilityIncludesWindowsFallback()
    {
        var guidance = UnityFreeDesignToolPolicy.BuildCliToolGuidance();
        Assert.Contains("rg=", guidance);
        Assert.Contains("Get-ChildItem", guidance);
        Assert.DoesNotContain("@@", guidance);
    }

    [Fact]
    public void DesignRunnerReceivesStableExecutableEnvironmentNames()
    {
        var environment = UnityFreeDesignToolPolicy.BuildDesignToolEnvironment();
        Assert.Equal(2, environment.Count);
        Assert.False(string.IsNullOrWhiteSpace(
            environment[UnityFreeDesignToolPolicy.InkscapeVariable]));
        Assert.False(string.IsNullOrWhiteSpace(
            environment[UnityFreeDesignToolPolicy.BlenderVariable]));
    }

    [Fact]
    public void DesignAndHqPromptsNeverExposeGraphicsInstallationPaths()
    {
        var contract = RoleContractLoader.LoadDesignFooter();
        var design = RoleContractLoader.BuildDesignPrompt(
            new MilestoneDesignDefinition("docs/projecthub/visual.md", "UI 개선"),
            Path.GetTempPath());
        Assert.Contains("$env:PROJECTHUB_INKSCAPE", contract);
        Assert.Contains("$env:PROJECTHUB_BLENDER", contract);
        Assert.DoesNotContain("Inkscape:", design);
        Assert.DoesNotContain("Blender (선택):", design);
        Assert.DoesNotContain("NOT_INSTALLED", design);
        Assert.DoesNotContain("Program Files", contract);
        Assert.DoesNotContain("노트북", design);
        Assert.DoesNotContain("ComfyUI", design);
        Assert.Equal(1, design.Split('\n').Count(
            line => line.Trim() == "@@REPORT"));
    }
}
