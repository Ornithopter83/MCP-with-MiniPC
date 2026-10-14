using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityRoleMessageComposerTests
{
    [Fact]
    public void MissingMessageMarker_IsPrependedExactlyOnce()
    {
        var text = UnityRoleMessageComposer.Compose("목표: Unity UI 수정");
        Assert.Equal("@@MESSAGE\n목표: Unity UI 수정", text);
        Assert.Equal(text, UnityRoleMessageComposer.Compose(text));
    }

    [Fact]
    public void DuplicateMessageMarkers_KeepContentAndCollapseToOne()
    {
        var input = "@@MESSAGE\n목표: 화면 수정\n@@MESSAGE\n빌드 결과: 미검증";
        var output = UnityRoleMessageComposer.Compose(input);
        Assert.Equal(1, output.Split('\n').Count(line => line == "@@MESSAGE"));
        Assert.Contains("목표: 화면 수정", output);
        Assert.Contains("빌드 결과: 미검증", output);
        Assert.Contains("추가 작업 맥락:", output);
        Assert.Equal(output, UnityRoleMessageComposer.Compose(output));
    }

    [Fact]
    public void StructuredReportTemplate_IsNeverChanged()
    {
        var input = "@@MESSAGE\n결과 형식:\n[GOTO=WORKER]\n@@REPORT\n<STATUS>completed</>\n[RESPONSE=OK]";
        Assert.Equal(input, UnityRoleMessageComposer.Compose(input));
    }

    [Fact]
    public void AllSpecialistPromptBuilders_UseOneMessageHeader()
    {
        var work = RoleContractLoader.BuildDirectWorkPrompt(
            "10", "작업 목표: 게임 규칙 구현", new[] { "Assets/Scripts/Play.cs" }, "/tmp/unity");
        var high = RoleContractLoader.BuildHighPrompt("빌드 검증 결과: 미확인");
        var qa = RoleContractLoader.BuildQaPrompt("화면 테스트");
        var design = UnityDesignPromptPolicy.Build(
            new MilestoneDesignDefinition("docs/projecthub/visual.md", "게임 UI 제작"),
            Path.GetTempPath());
        foreach (var prompt in new[] { work, high, qa, design })
        {
            var composed = UnityRoleMessageComposer.Compose(prompt);
            Assert.Equal(1, composed.Split('\n')
                .Count(line => line.Trim() == "@@MESSAGE"));
            Assert.DoesNotContain("@@WRITE_PATH", composed);
            Assert.DoesNotContain("@@DESIGN_METHOD", composed);
            Assert.DoesNotContain("@@RESULT_CONTRACT", composed);
        }
    }
}
