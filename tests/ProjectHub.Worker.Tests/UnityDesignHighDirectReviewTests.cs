using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityDesignHighDirectReviewTests
{
    private static string Milestone(string sections) =>
        "[ACTION=WORK]\n@@MILESTONE\n<ID>M77</>\n" + sections +
        "\n@@HIGH\n<INSTRUCTIONS>최종 통합 확인과 수정</>\n[RESPONSE=OK]";

    [Theory]
    [InlineData("@@WORK=10\n<PATH>Assets/Scripts/Quiz.cs</>\n<INSTRUCTIONS>구현</>")]
    [InlineData("@@DESIGN=0\n<PATH>docs/projecthub/look.md</>\n<WRITE_PATH>Assets/UI/Panel.prefab</>\n<INSTRUCTIONS>UI 제작</>")]
    [InlineData("@@ASSET_MANAGER=1\n<REQUEST_ID>m77-assets</>\n<GOAL>이미지 에셋 가져오기</>\n<SCENE>Assets/Scenes/Game.unity</>\n<TARGET_PATH>Assets/Art</>\n<SOURCE_POLICY>PROJECT_ONLY</>\n<INSTRUCTIONS>프로젝트 에셋 연결</>")]
    [InlineData("@@DESIGN=0\n<PATH>docs/projecthub/look.md</>\n<WRITE_PATH>Assets/UI/Panel.prefab</>\n<INSTRUCTIONS>UI 제작</>\n@@WORK=10\n<PATH>Assets/Scripts/Quiz.cs</>\n<INSTRUCTIONS>구현</>")]
    public void HqCanAssignDesignInitially_AndEveryMilestoneNeedsHigh(string sections)
    {
        var parsed = HqTextProtocol.Parse(Milestone(sections));
        Assert.True(parsed.IsValid, string.Join(";", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(parsed.CompatibilityMessage,
            parsed.Parse, out var milestone, out var error), error);
        Assert.NotNull(milestone);
    }

    [Fact]
    public void NoIndependentQaOrMissingHigh()
    {
        var qa = HqTextProtocol.Parse(Milestone(
            "@@WORK=10\n<PATH>Assets/Quiz.cs</>\n<INSTRUCTIONS>구현</>\n@@QA\n<INSTRUCTIONS>테스트</>"));
        Assert.Contains("COMPACT_SECTION_FORBIDDEN", qa.Errors);
        var noHigh = HqTextProtocol.Parse(Milestone(
            "@@DESIGN=0\n<PATH>docs/projecthub/look.md</>\n<INSTRUCTIONS>UI 제작</>")
            .Replace("@@HIGH\n<INSTRUCTIONS>최종 통합 확인과 수정</>", ""));
        Assert.False(noHigh.IsValid);
    }

    [Fact]
    public void HighAndDesignContracts_OnlyReturnToWorker()
    {
        foreach (var contract in new[] {
            RoleContractLoader.LoadHqFooter(),
            RoleContractLoader.LoadHighFooter(),
            RoleContractLoader.LoadDesignFooter(),
            RoleContractLoader.LoadWorkFooter(),
            RoleContractLoader.LoadAssetManagerFooter()
        })
        {
            Assert.Contains("[RESPONSE=OK]", contract);
            Assert.Contains("개행", contract);
            Assert.DoesNotContain("[ACTION=RESULT]", contract);
        }
        var high = RoleContractLoader.LoadHighFooter();
        var design = RoleContractLoader.LoadDesignFooter();
        Assert.Contains("[GOTO=WORKER]\n@@REPORT", high.Replace("\r\n", "\n"));
        Assert.Contains("[GOTO=WORKER]\n@@REPORT", design.Replace("\r\n", "\n"));
        Assert.DoesNotContain("DESIGN 인계가 필요하면", high);
        Assert.Contains("재호출하지 않는다", high);
        Assert.Contains("HQ가 마일스톤", design);
    }

    [Fact]
    public void HighOnlyReportsCompletedOrBlocked_NoDesignRouteOrReviewRounds()
    {
        var high = RoleContractLoader.LoadHighFooter();
        var hq = RoleContractLoader.LoadHqFooter();
        Assert.DoesNotContain("최대 2회", high);
        Assert.DoesNotContain("같은 마일스톤에서 재수정", hq);
        Assert.False(RoleTextProtocol.ParseHigh(
            RoleTextProtocol.BuildResult("in_progress", "send to design", gotoTarget: "DESIGN")).IsValid);

        foreach (var status in new[] { "completed", "blocked" })
        {
            var redirect = RoleTextProtocol.BuildResult(status,
                "old handoff", gotoTarget: "DESIGN");
            Assert.Null(MilestoneDefinitionContract.ReadHighStatus(redirect));
            var normalized = MilestoneDefinitionContract.NormalizeHighReport(
                0, redirect, null);
            Assert.Equal("blocked", MilestoneDefinitionContract.ReadHighStatus(normalized));
            Assert.Contains("HIGH_DESIGN_ROUTE_REMOVED", normalized);
            Assert.StartsWith("[GOTO=WORKER]", normalized);
        }
    }

    [Theory]
    [InlineData("신규 업데이트를 위해 임시 저장하고 중단합니다.")]
    [InlineData("저장 후 중단")]
    [InlineData("작업은 중단하지 마세요.")]
    [InlineData("UI에 효과음 추가해주세요")]
    public void LiveUserMessage_IsQueuedVerbatimWithoutWorkerPauseClassification(string message)
    {
        var root = Path.Combine(Path.GetTempPath(), "UnityRoleMessageQueueTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var queue = new HqUserInterventionQueue(root, "job-message");
            queue.Add(message, false);
            var pending = queue.ClaimPending();
            Assert.Equal(message, Assert.Single(pending).Message);
            queue.Acknowledge(pending);
            Assert.Empty(queue.Pending());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DesignRequiresGraphicFilesNotJustDocumentation()
    {
        Assert.False(UnityDesignPromptPolicy.IsVisualArtifact("docs/design.md"));
        Assert.True(UnityDesignPromptPolicy.IsVisualArtifact("Assets/UI/Panel.prefab"));
        Assert.True(UnityDesignPromptPolicy.IsVisualArtifact("Assets/Scripts/ThemeEffects.cs"));
    }

    [Fact]
    public void HighFinalBuild_RecompilesSourceButNotUnaffectedVisualAssets()
    {
        Assert.False(HighFinalBuildPolicy.RequiresBuild(
            new[] { "Assets/Art/Fireworks.png", "docs/projecthub/design.md" }));
        // Visual prefab changes still need targeted HIGH inspection, not
        // an unrelated C# script recompile.
        Assert.False(HighFinalBuildPolicy.RequiresBuild(
            new[] { "Assets/UI/Panel.prefab" }));
        Assert.True(HighFinalBuildPolicy.RequiresBuild(
            new[] { "Assets/Scripts/UI/Effects.cs" }));
        Assert.True(HighFinalBuildPolicy.RequiresBuild(
            new[] { "Packages/manifest.json" }));
        Assert.False(HighFinalBuildPolicy.RequiresBuild(Array.Empty<string>()));
    }

    [Fact]
    public void FailedFinalBuild_InvalidatesHighCompletion()
    {
        var done = RoleTextProtocol.BuildResult("completed", "review finished");
        var invalidated = HighFinalBuildPolicy.InvalidateCompletion(
            done, "HIGH_FINAL_BUILD_FAILED");
        Assert.Equal("blocked", MilestoneDefinitionContract.ReadHighStatus(invalidated));
        Assert.Contains("HIGH_FINAL_BUILD_FAILED", invalidated);
        var alreadyBlocked = RoleTextProtocol.BuildResult("blocked", "original fault");
        Assert.Equal(alreadyBlocked, HighFinalBuildPolicy.InvalidateCompletion(
            alreadyBlocked, "HIGH_FINAL_BUILD_FAILED"));
    }
}
