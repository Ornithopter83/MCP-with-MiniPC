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
    public void RequestedWorkPermissionSentenceRemainsUnchanged()
    {
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            "10", "@@GOAL\n코드를 구현하라.", new[] { "src/a.cs" }, "/project");
        Assert.Contains("작업에 필요한 모든 권한은 승인한다.", prompt);
    }
}
