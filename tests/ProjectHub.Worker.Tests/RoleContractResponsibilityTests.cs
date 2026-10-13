using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class RoleContractResponsibilityTests
{
    [Fact]
    public void HqContractOnlyDefinesHqPlanningNotWorkerMechanicalExecution()
    {
        var hq = RoleContractLoader.LoadHqFooter();

        Assert.Contains("QA에는 실제 검증을, HIGH에는 심층 검토", hq);
        Assert.Contains("RESOURCE #0은 비차단 이미지 생성 요청", hq);
        Assert.DoesNotContain("진전 기반 연속 수행", hq);
        Assert.DoesNotContain("즉시 dispatch", hq);
        Assert.DoesNotContain("다음 Worker Git finalize", hq);
        Assert.Contains("아직 확보되지 않은 자산", hq);
    }

    [Fact]
    public void QaContractDoesNotAssignHighRoutingToQa()
    {
        var qa = RoleContractLoader.LoadQaFooter();

        Assert.DoesNotContain("HIGH에 그대로 전달", qa);
        Assert.Contains("실제 실행 결과 및 증거를 자세히 기록", qa);
        Assert.Contains("FULL_AUTONOMY에서는 결함 조사", qa);
        Assert.Contains("최종 commit/push는 Worker가 수행한다", qa);
    }

    [Fact]
    public void GeneralWorkContractDoesNotContainReservedBuildRepairRules()
    {
        var work = RoleContractLoader.LoadWorkFooter();

        Assert.DoesNotContain("WORK #8", work);
        Assert.DoesNotContain("환경 EPERM", work);
        Assert.Contains("WRITE_PATH는 작업 배정", work);
        Assert.DoesNotContain("WRITE_PATH 안에서만", work);
        Assert.Contains("<STATUS>completed</>", work);
    }

    [Fact]
    public void RequestedWorkPermissionSentenceRemainsUnchanged()
    {
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            "10", "@@GOAL\n코드를 구현하라.", new[] { "src/a.cs" }, "/project");

        Assert.Contains("작업에 필요한 모든 권한은 승인한다.", prompt);
    }
}
