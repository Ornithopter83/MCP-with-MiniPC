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
        Assert.Contains("도구가 반환할 때까지 기다리고 실제 성공", sv);
        Assert.Contains("직접 조사·명령 실행·파일 수정", sv);
        Assert.Contains("Git add/commit/push/reset/rebase", sv);
    }

    [Fact]
    public void HighContract_ReportsFixesInsteadOfMutatingSource()
    {
        var high = RoleContractLoader.LoadHighFooter();
        Assert.Contains("직접 구현·파일 수정·테스트 명령은 금지", high);
        Assert.Contains("MCP delegate_work(task, paths, read_only=true)", high);
        Assert.Contains("결과가 이 HIGH 세션에 반환된다", high);
        Assert.Contains("<STATUS>completed</>", high);
    }
}
