using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class RoleContractResponsibilityTests
{
    [Fact]
    public void HqContractDefinesCurrentUnityRouting()
    {
        var hq = RoleContractLoader.LoadHqFooter();
        Assert.Contains("@@WORK=10", hq);
        Assert.Contains("@@HIGH", hq);
        Assert.Contains("@@DESIGN=0", hq);
        Assert.Contains("@@ASSET_MANAGER=1", hq);
        Assert.Contains("[GOTO=WORKER]", hq);
        Assert.DoesNotContain("@@MANAGER", hq);
    }

    [Fact]
    public void HqContractPausesAfterTwoMilestonesWithoutGoalProgress()
    {
        var contract = RoleContractLoader.LoadHqFooter();
        Assert.Contains("제3조의2 (원래 목표 대비 정체 감지와 PAUSE)", contract);
        Assert.Contains("연속 2개 완료된 마일스톤", contract);
        Assert.Contains("[ACTION=PAUSE]", contract);
        Assert.Contains("새 진단 WORK", contract);
        Assert.Contains("@@RESUME", contract);
        Assert.Contains("END로 가장하지 않는다", contract);
    }

    [Fact]
    public void HqRoutingContractIsOnlyInjectedForNewUserTask()
    {
        const string header = "역할: HQ\n입력 본문: 작업을 시작한다";
        var initial = RoleContractLoader.AppendHqContractForUserStart(
            "USER_REQUEST", header);
        Assert.Equal(1, initial.Split(RoleContractLoader.LoadHqFooter(),
            StringSplitOptions.None).Length - 1);

        foreach (var followupType in new[]
                 { "MILESTONE_REPORT", "HQ_RESPONSE_RECOVERY", "USER_FOLLOWUP" })
        {
            Assert.Equal(header, RoleContractLoader.AppendHqContractForUserStart(
                followupType, header));
        }
    }

    [Fact]
    public void WorkAndHighUseStandAloneGotoAndReportLines()
    {
        foreach (var contract in new[]
                 { RoleContractLoader.LoadWorkFooter(), RoleContractLoader.LoadHighFooter() })
        {
            Assert.Contains("[GOTO=WORKER]\n@@REPORT\n",
                contract.Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.Contains("[RESPONSE=OK]", contract);
        }
    }

    [Fact]
    public void UnityProgramsDefaultToFullHdOnlyWhenResolutionUnspecified()
    {
        foreach (var contract in new[]
                 {
                     RoleContractLoader.LoadHqFooter(),
                     RoleContractLoader.LoadDesignFooter(),
                     RoleContractLoader.LoadWorkFooter(),
                     RoleContractLoader.LoadHighFooter()
                 })
        {
            Assert.Contains("1920×1080", contract);
            Assert.DoesNotContain("1280×720", contract);
        }
        Assert.Contains("해상도가 없으면",
            RoleContractLoader.LoadHqFooter());
        Assert.Contains("명시된 규격",
            RoleContractLoader.LoadDesignFooter());
    }

    [Fact]
    public void AssetManagerContractIsNotRepeatedInSameSession()
    {
        const string prompt = "@@MESSAGE\nPHASE: INTEGRATE";
        var initial = RoleContractLoader.AppendAssetManagerContractForNewSession(
            prompt, null);
        Assert.Contains(RoleContractLoader.LoadAssetManagerFooter(), initial);
        Assert.Equal(prompt, RoleContractLoader.AppendAssetManagerContractForNewSession(
            prompt, "existing-session"));
    }

    [Fact]
    public void WorkContinuationAvoidsRepeatedContractAndEncoding()
    {
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            "10", "작업을 이어가라", new[] { "Assets/Scripts/A.cs" }, "/workspace",
            includeRoleContract: false, includeEncodingGuidance: false);
        Assert.DoesNotContain(RoleContractLoader.LoadWorkFooter(), prompt);
        Assert.DoesNotContain("문자 인코딩:", prompt);
    }

    [Fact]
    public void WorkPermissionSentenceIsBoundedByWorkerSettings()
    {
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            "10", "@@GOAL\n코드를 구현하라.", new[] { "src/a.cs" }, "/project");
        Assert.Contains("실행 권한은 Worker 설정과 사용자 승인 범위를 따른다.", prompt);
        Assert.DoesNotContain("작업에 필요한 모든 권한은 승인한다.", prompt);
    }
}
