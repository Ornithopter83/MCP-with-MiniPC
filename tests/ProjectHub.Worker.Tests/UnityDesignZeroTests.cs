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
        Assert.Contains("DESIGN 요청:", MilestoneDefinitionContract.BuildHighContext(
            milestone, new Dictionary<string, string>()));
        Assert.Contains("DESIGN 요청:", MilestoneDefinitionContract.BuildHighContext(
            milestone, new Dictionary<string, string>(), null));
    }

    [Theory]
    [InlineData("Assets/Quiz/Presentation")]
    [InlineData("../other/design.md")]
    [InlineData("docs/projecthub/design.png")]
    public void DesignZero_InvalidDocumentScopeFallsBackToHigh(string invalidPath)
    {
        var text = DesignMilestone.Replace("docs/projecthub/design-m42.md",
            invalidPath, StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(text);
        Assert.True(parsed.IsValid, string.Join(";", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(parsed.CompatibilityMessage,
            parsed.Parse, out var milestone, out var error), error);
        Assert.Null(milestone!.Design);
        Assert.Contains("DESIGN 문서 경로가 불명확", milestone.HighInstructions);
    }

    [Fact]
    public void DesignZero_OverlappingWorkScopeIsReadOnly()
    {
        var text = DesignMilestone.Replace(
            "<PATH>Assets/Quiz/Presentation</>",
            "<PATH>docs/projecthub</>", StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(text);
        Assert.True(parsed.IsValid, string.Join(";", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(parsed.CompatibilityMessage,
            parsed.Parse, out var milestone, out var error), error);
        Assert.True(milestone!.WorkItems["42"].ReadOnly);
        Assert.Empty(milestone.WorkItems["42"].WritePaths);
    }

    [Fact]
    public void BackgroundPolicy_DoesNotConfuseNoFocusWithNoGraphics()
    {
        var prompt = UnityDesignPromptPolicy.Build(
            new("docs/projecthub/test.md", "화면 품질을 설계하라."),
            Path.GetTempPath());
        Assert.Contains("DESIGN_DOCUMENT:", prompt);
        Assert.Contains("PROJECT_ROOT:", prompt);
        Assert.Contains("실제 시각 결과물을 제작·적용", prompt);
        Assert.Contains("--save_path/--output", prompt);
        Assert.DoesNotContain("노트북", prompt);
        Assert.DoesNotContain("RESOURCE 이미지 추출", prompt);
        var contract = RoleContractLoader.LoadDesignFooter();
        Assert.Contains("Editor 조작은 병렬로 수행하지 않고", contract);
        Assert.Contains("Game View", contract);
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
