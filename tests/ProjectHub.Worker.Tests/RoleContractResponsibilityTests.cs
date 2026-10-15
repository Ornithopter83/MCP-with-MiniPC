using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class RoleContractResponsibilityTests
{
    [Fact]
    public void HqContractOnlyDefinesHqPlanningNotWorkerMechanicalExecution()
    {
        var hq = RoleContractLoader.LoadHqFooter();

        Assert.Contains("QA 독립 역할은 없다", hq);
        Assert.Contains("DESIGN #0", hq);
        Assert.Contains("이미지 추출 프로세스는 사용하지 않는다", hq);
        Assert.DoesNotContain("진전 기반 연속 수행", hq);
        Assert.DoesNotContain("즉시 dispatch", hq);
        Assert.DoesNotContain("다음 Worker Git finalize", hq);
        Assert.Contains("실제 Game View 캡처", hq);
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
        Assert.Contains("<SUMMARY>", work);
        Assert.Contains("<CHANGED_PATH>", work);
        Assert.Contains("<ISSUES>", work);
        Assert.Contains("in_progress", work);
        Assert.Contains("blocked", work);
        Assert.DoesNotContain("기존 WORK 체크포인트", work);
        Assert.DoesNotContain("Unity Editor 직접 작업 지침:", work);
        Assert.DoesNotContain("프로젝트 수정 권한:", work);
    }

    [Fact]
    public void WorkContext_UsesOnlyInstructionsEvenWhenCompactFieldsRepeat()
    {
        var json = """
            {"goal":"같은 설명","instructions":"같은 설명","completionCriteria":["같은 설명"]}
            """;
        var work = new MilestoneWorkDefinition("112",
            new[] { "Assets/Tests/PlayMode/QuizFeedbackIntegrationTests.cs" },
            false, true, json, json);

        Assert.Equal("작업 지시:" + Environment.NewLine + "같은 설명",
            MilestoneDefinitionContract.BuildWorkContext(work));
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            work.Id, MilestoneDefinitionContract.BuildWorkContext(work),
            work.WritePaths, @"C:\AI-AGENT\worker\worker-projects\Unity3DSample");
        Assert.Equal(1, prompt.Split("같은 설명", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("작업 목표:", prompt);
        Assert.DoesNotContain("완료 기준:", prompt);
        Assert.DoesNotContain("DESIGN 가이드:", prompt);
        Assert.DoesNotContain("Unity Editor 직접 작업 지침:", prompt);
        Assert.DoesNotContain("프로젝트 수정 권한:", prompt);
        Assert.StartsWith("@@MESSAGE" + Environment.NewLine + "WORK_ID: 112", prompt);
        Assert.Contains("<STATUS>completed</>", prompt);
        Assert.Contains("<SUMMARY>", prompt);
        Assert.Contains("<CHANGED_PATH>", prompt);
        Assert.Contains("<ISSUES>", prompt);
    }

    [Fact]
    public void RequestedWorkPermissionSentenceRemainsUnchanged()
    {
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            "10", "작업 목표:\n코드를 구현하라.", new[] { "src/a.cs" }, "/project");

        Assert.Contains("작업에 필요한 모든 권한은 승인한다.", prompt);
    }
}
