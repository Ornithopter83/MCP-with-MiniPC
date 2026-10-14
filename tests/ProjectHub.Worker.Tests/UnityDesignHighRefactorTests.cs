using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityDesignHighRefactorTests
{
    private static string Milestone(string sections) =>
        "[ACTION=WORK]\n@@MILESTONE\n<ID>M77</>\n" + sections +
        "\n@@HIGH\n<INSTRUCTIONS>최종 통합 확인과 수정</>\n[RESPONSE=OK]";

    [Theory]
    [InlineData("@@WORK=10\n<PATH>Assets/Scripts/Quiz.cs</>\n<INSTRUCTIONS>구현</>")]
    [InlineData("@@DESIGN=0\n<PATH>docs/projecthub/look.md</>\n<WRITE_PATH>Assets/UI/Panel.prefab</>\n<INSTRUCTIONS>UI 제작</>")]
    [InlineData("@@ASSET_MANAGER=1\n<REQUEST_ID>m77-assets</>\n<GOAL>이미지 에셋 가져오기</>\n<SCENE>Assets/Scenes/Game.unity</>\n<TARGET_PATH>Assets/Art</>\n<SOURCE_POLICY>PROJECT_ONLY</>\n<INSTRUCTIONS>프로젝트 에셋 연결</>")]
    [InlineData("@@DESIGN=0\n<PATH>docs/projecthub/look.md</>\n<WRITE_PATH>Assets/UI/Panel.prefab</>\n<INSTRUCTIONS>UI 제작</>\n@@WORK=10\n<PATH>Assets/Scripts/Quiz.cs</>\n<INSTRUCTIONS>구현</>")]
    public void NewHqProtocol_SupportsHighWithOptionalSpecialistRoles(string sections)
    {
        var parsed = HqTextProtocol.Parse(Milestone(sections));
        Assert.True(parsed.IsValid, string.Join(";", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(parsed.CompatibilityMessage,
            parsed.Parse, out var milestone, out var error), error);
        Assert.NotNull(milestone);
        Assert.False(milestone!.QaReserved);
    }

    [Fact]
    public void NewHqProtocol_RejectsQaSectionAndHighMissing()
    {
        var qa = HqTextProtocol.Parse(Milestone(
            "@@WORK=10\n<PATH>Assets/Quiz.cs</>\n<INSTRUCTIONS>구현</>\n@@QA\n<INSTRUCTIONS>테스트</>"));
        Assert.Contains("COMPACT_QA_REMOVED", qa.Errors);
        var noHigh = HqTextProtocol.Parse(Milestone(
            "@@DESIGN=0\n<PATH>docs/projecthub/look.md</>\n<INSTRUCTIONS>UI 제작</>")
            .Replace("@@HIGH\n<INSTRUCTIONS>최종 통합 확인과 수정</>", ""));
        Assert.False(noHigh.IsValid);
    }

    private const string Handoff = """
        [ACTION=RESULT]
        @@REPORT
        <STATUS>design_handoff</>
        <SUMMARY>실제 렌더 정답 O 배치 불량</>
        <DESIGN_GOAL>정답 O와 파티클이 중첩되지 않도록 UI 그래픽을 수정한다.</>
        <EVIDENCE_PATH>temp/projecthub/frames/answer.png</>
        <TARGET_PATH>Assets/UI/Quiz.prefab</>
        <EXPECTED_RESULT>정답 O, 폭죽이 2초간 자연스럽게 구분된다.</>
        <REASON>현재 Game View에서 O가 가려져 실제 프레임으로 확인됨</>
        <ISSUES>효과음 청취 미검증</>
        [RESPONSE=OK]
        """;

    [Fact]
    public void HighHandoff_NonterminalAndEvidenceBound()
    {
        var parsed = RoleTextProtocol.ParseHigh(Handoff);
        Assert.True(parsed.IsValid, string.Join(";", parsed.Errors));
        Assert.Equal("design_handoff", parsed.Status);
        Assert.True(HighDesignHandoffContract.TryRead(Handoff, out var request, out var error), error);
        Assert.Equal("Assets/UI/Quiz.prefab", request!.TargetPath);
        Assert.Equal(Handoff, MilestoneDefinitionContract.NormalizeHighReport(0, Handoff, null));
        Assert.Equal(2, HighDesignHandoffContract.MaxReviewHandoffs);
    }

    [Fact]
    public void HighHandoff_MissingEvidenceAndUnsafeTargetAreRejected()
    {
        Assert.False(HighDesignHandoffContract.TryRead(
            Handoff.Replace("<EVIDENCE_PATH>temp/projecthub/frames/answer.png</>", ""),
            out _, out _));
        Assert.False(HighDesignHandoffContract.TryRead(
            Handoff.Replace("Assets/UI/Quiz.prefab", "../other/Quiz.prefab"),
            out _, out _));
    }

    [Fact]
    public void ValidationBudget_DoesNotCountDebugCommandsAsFullRounds()
    {
        Assert.True(UnityValidationBudget.CanStart(1, false));
        Assert.True(UnityValidationBudget.CanStart(2, false));
        Assert.False(UnityValidationBudget.CanStart(3, false));
        Assert.True(UnityValidationBudget.CanStart(3, true));
        Assert.False(UnityValidationBudget.CanStart(4, true));
    }

    [Theory]
    [InlineData("신규 업데이트를 위해 임시 저장하고 중단합니다.", true)]
    [InlineData("저장 후 중단", true)]
    [InlineData("작업은 중단하지 마세요.", false)]
    [InlineData("UI에 효과음 추가해주세요", false)]
    public void PauseQueue_OnlyExplicitStopsPreempt(string message, bool expected) =>
        Assert.Equal(expected, UnityUserPausePolicy.IsPauseRequest(message));

    [Fact]
    public void DesignEvidence_DocumentAloneIsNotGraphicChange()
    {
        Assert.False(UnityDesignPromptPolicy.IsVisualArtifact("docs/design.md"));
        Assert.True(UnityDesignPromptPolicy.IsVisualArtifact("Assets/UI/Panel.prefab"));
        Assert.True(UnityDesignPromptPolicy.IsVisualArtifact("Assets/Scripts/ThemeEffects.cs"));
    }

    [Fact]
    public void BackgroundGuard_DeniesForegroundControlButNotConnectedEditorCommands()
    {
        var script = BuildExecutionPolicy.CreateUnityBackgroundPreToolHookScript();
        Assert.Contains("SetForegroundWindow", script);
        Assert.Contains("Unity", script);
        Assert.Contains("permissionDecision", script);
        Assert.DoesNotContain("(?<!", script);
    }
}
