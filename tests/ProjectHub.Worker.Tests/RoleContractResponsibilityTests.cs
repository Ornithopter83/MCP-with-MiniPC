using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class RoleContractResponsibilityTests
{
    [Fact]
    public void HqContract_OnlyAssignsSupervisor()
    {
        var hq = RoleContractLoader.LoadHqFooter();
        Assert.Contains("@@SV", hq);
        Assert.Contains("@@HIGH", hq);
        Assert.DoesNotContain("@@WORK=10", hq);
        Assert.DoesNotContain("@@QA", hq);
    }

    [Fact]
    public void SvContract_OwnsImplementationAndSubagents()
    {
        var sv = RoleContractLoader.LoadSvFooter();
        Assert.Contains("delegate_work", sv);
        Assert.Contains("SuperVisor(#1)", sv);
        Assert.Contains("실행 작업은 우선 MCP delegate_work", sv);
        Assert.Contains("별도 새 세션에 직접 복구", sv);
        Assert.Contains("Git commit", sv);
    }

    [Fact]
    public void HighContract_ReportsFixesInsteadOfMutatingSource()
    {
        var high = RoleContractLoader.LoadHighFooter();
        Assert.Contains("직접 구현하지 않는다", high);
        Assert.Contains("HIGH는 구현용 WORK를 위임하지 않는다", high);
        Assert.Contains("<STATUS>completed</>", high);
    }
}
