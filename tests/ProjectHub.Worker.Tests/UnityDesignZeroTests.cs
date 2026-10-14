using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityDesignZeroTests
{
    private const string DesignMilestone = """
        [ACTION=WORK]
        @@MILESTONE
        <ID>M42</>
        @@DESIGN=0
        <PATH>docs/projecthub/design-m42.md</>
        <INSTRUCTIONS>퀴즈의 정답 O, 폭죽, 오답 X, UI 화면을 실제 캡처 기준으로 설계한다.</>
        @@WORK=42
        <PATH>Assets/Quiz/Presentation</>
        <INSTRUCTIONS>디자인 문서대로 보드를 구성하라.</>
        @@HIGH
        <INSTRUCTIONS>미확인 연출을 확인하라.</>
        [RESPONSE=OK]
        """;

    [Fact]
    public void DesignZero_ParsesAndReachesHigh()
    {
        var parsed = HqTextProtocol.Parse(DesignMilestone);
        Assert.True(parsed.IsValid, string.Join(";", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse,
            out var milestone, out var error), error);

        Assert.Empty(milestone!.Resources);
        Assert.Equal("docs/projecthub/design-m42.md", milestone.Design?.Path);
        Assert.Contains("정답 O", milestone.Design?.Instructions);
        Assert.Contains("@@DESIGN_REQUEST", MilestoneDefinitionContract.BuildQaContext(
            milestone, new Dictionary<string, string>()));
        Assert.Contains("@@DESIGN_REQUEST", MilestoneDefinitionContract.BuildHighContext(
            milestone, new Dictionary<string, string>(), null));
    }

    [Theory]
    [InlineData("Assets/Quiz/Presentation")]
    [InlineData("../other/design.md")]
    [InlineData("docs/projecthub/design.png")]
    public void DesignZero_RejectsMissingDocumentScope(string invalidPath)
    {
        var text = DesignMilestone.Replace("docs/projecthub/design-m42.md",
            invalidPath, StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(text);
        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, error => error.StartsWith("DESIGN.",
            StringComparison.Ordinal));
    }

    [Fact]
    public void DesignZero_DoesNotWriteToWorkScope()
    {
        var text = DesignMilestone.Replace(
            "<PATH>Assets/Quiz/Presentation</>",
            "<PATH>docs/projecthub</>", StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(text);
        Assert.False(parsed.IsValid);
        Assert.Contains("DESIGN_WORK_PATH_OVERLAP", parsed.Errors);
    }

    [Fact]
    public void BackgroundPolicy_DoesNotConfuseNoFocusWithNoGraphics()
    {
        var prompt = UnityDesignPromptPolicy.Build(
            new("docs/projecthub/test.md", "화면 품질을 설계하라."),
            Path.GetTempPath());
        Assert.Contains("Application.runInBackground", prompt);
        Assert.Contains("batchmode", prompt);
        Assert.Contains("-nographics", prompt);
        Assert.Contains("capture_game_view", prompt);
        Assert.Contains("실제로 지원되는", prompt);
        Assert.Contains("미검증", prompt);
        Assert.Contains("RESOURCE 이미지 추출", prompt);
    }

    [Fact]
    public void DesignEvidence_RequiresActualDocumentFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-design-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var design = new MilestoneDesignDefinition(
                "docs/projecthub/design.md", "테마");
            Assert.False(UnityDesignPromptPolicy.IsVerifiedDocument(root, design));
            Directory.CreateDirectory(Path.Combine(root, "docs", "projecthub"));
            File.WriteAllText(Path.Combine(root, "docs", "projecthub", "design.md"),
                "actual art direction");
            Assert.True(UnityDesignPromptPolicy.IsVerifiedDocument(root, design));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
