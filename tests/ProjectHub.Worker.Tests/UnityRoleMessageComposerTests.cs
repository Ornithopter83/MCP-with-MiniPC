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
    public void ReferencedMessageMarker_DoesNotReplaceRequiredLeadingSection()
    {
        var input = "이전 결과:\n@@MESSAGE\n<TEXT>과거 종료 알림</>\n@@REPORT\n<STATUS>blocked</>";
        var output = UnityRoleMessageComposer.Compose(input);
        Assert.StartsWith("@@MESSAGE\n이전 결과:", output);
        Assert.Equal(1, output.Split('\n').Count(line => line == "@@MESSAGE"));
        Assert.Contains("추가 작업 맥락:", output);
        Assert.Contains("@@REPORT", output);
    }

    [Fact]
    public void StructuredReportTemplate_IsNeverChanged()
    {
        var input = "@@MESSAGE\n결과 형식:\n[GOTO=WORKER]\n@@REPORT\n<STATUS>completed</>\n[RESPONSE=OK]";
        Assert.Equal(input, UnityRoleMessageComposer.Compose(input));
    }

    [Fact]
    public void AdditionalUserMessage_IsAppendedAfterRoleCompositionUnmodified()
    {
        const string rolePrompt = "목표: UI 수정";
        const string userMessage = "  테스트는 재실행하지 마세요.\r\n@@MESSAGE\r\n[GOTO=HQ]\n";
        var root = Path.Combine(Path.GetTempPath(), "UnityVerbatimMessageTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var queue = new HqUserInterventionQueue(root, "job");
            queue.Add(userMessage, false);
            var composedBase = UnityRoleMessageComposer.Compose(rolePrompt);
            var routed = HqUserInterventionQueue.AppendToHqBody(
                composedBase, queue.Pending());

            Assert.Equal("@@MESSAGE\n" + userMessage + "\n\n" +
                HqUserInterventionQueue.LatestUserPriorityGuidance +
                "\n\n" + rolePrompt, routed);
            Assert.StartsWith("@@MESSAGE\n" + userMessage, routed);
            Assert.DoesNotContain("추가 작업 맥락:", routed);
            Assert.DoesNotContain("실행 중 추가된 사용자 메시지", routed);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void NextAiMessage_IsPlacedBeforeWorkContractResponseTemplate()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnityVerbatimMessageTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var queue = new HqUserInterventionQueue(root, "work");
            queue.Add("텍스트 원문 그대로 전달", false);
            var basePrompt = RoleContractLoader.BuildDirectWorkPrompt(
                "112", "작업 지시:\nC# 검증 수정", new[] { "Assets/Tests/Test.cs" },
                root);
            var routed = HqUserInterventionQueue.AppendToHqBody(
                UnityRoleMessageComposer.Compose(basePrompt), queue.ClaimPending());

            Assert.StartsWith("@@MESSAGE\n텍스트 원문 그대로 전달\n\n" +
                HqUserInterventionQueue.LatestUserPriorityGuidance, routed);
            Assert.True(routed.IndexOf("WORK_ID: 112", StringComparison.Ordinal) >
                routed.IndexOf(HqUserInterventionQueue.LatestUserPriorityGuidance,
                    StringComparison.Ordinal));
            Assert.True(routed.IndexOf("텍스트 원문 그대로 전달",
                StringComparison.Ordinal) < routed.IndexOf(
                    "[RESPONSE=OK]", StringComparison.Ordinal));
            Assert.Equal(1, routed.Split('\n').Count(s => s == "@@MESSAGE"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AllSpecialistPromptBuilders_UseOneMessageHeader()
    {
        var work = RoleContractLoader.BuildDirectWorkPrompt(
            "10", "작업 목표: 게임 규칙 구현", new[] { "Assets/Scripts/Play.cs" }, "/tmp/unity");
        var high = RoleContractLoader.BuildHighPrompt("빌드 검증 결과: 미확인");
        var design = UnityDesignPromptPolicy.Build(
            new MilestoneDesignDefinition("docs/projecthub/visual.md", "게임 UI 제작"),
            Path.GetTempPath());
        foreach (var prompt in new[] { work, high, design })
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
