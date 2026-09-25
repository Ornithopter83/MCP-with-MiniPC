using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkTransportTests
{
    [Fact]
    public void ParallelWorkContractDefinesIntegrationAsSameWorkRole()
    {
        var footer = RoleContractLoader.LoadWorkFooter(
            judgeAvailable: true,
            parallelWorkItem: true);

        Assert.Contains("workItemKind가 INTEGRATION", footer);
        Assert.Contains("resultRef", footer);
        Assert.Contains("Git 병합", footer);
        Assert.Contains("Worker에게 의미적 충돌 해결을 넘기지 않는다", footer);
    }

    [Fact]
    public void WorkGraphTransportParsesAddDependencyAndIntegration()
    {
        const string body = """
            WORK_GRAPH_PATCH:
            {
              "expectedRevision": 3,
              "operations": [
                {
                  "type": "ADD",
                  "workItemId": "W1",
                  "goal": "기능 구현",
                  "dependencies": [],
                  "kind": "NORMAL",
                  "baseRef": "abc123"
                },
                {
                  "type": "ADD",
                  "workItemId": "I1",
                  "goal": "통합",
                  "dependencies": ["W1"],
                  "kind": "INTEGRATION",
                  "baseRef": "abc123"
                }
              ]
            }
            """;

        Assert.True(WorkGraphTransportContract.TryParse(body, out var patch, out var error));
        Assert.Null(error);
        Assert.NotNull(patch);
        Assert.Equal(3, patch!.ExpectedRevision);
        Assert.Equal(2, patch.Operations.Count);
        Assert.Equal(WorkGraphPatchOperationType.Add, patch.Operations[0].Type);
        Assert.Equal(WorkItemKind.Integration, patch.Operations[1].Item!.Kind);
        Assert.Equal(new[] { "W1" }, patch.Operations[1].Item!.Dependencies);
    }

    [Fact]
    public void WorkGraphTransportRejectsHqConcurrencyChange()
    {
        const string body = """
            WORK_GRAPH_PATCH:
            {"expectedRevision":0,"operations":[{"type":"SET_MAX_CONCURRENCY","integerValue":8}]}
            """;

        Assert.False(WorkGraphTransportContract.TryParse(body, out _, out var error));
        Assert.Equal("WORK_GRAPH_HQ_CONCURRENCY_CHANGE_NOT_ALLOWED", error);
    }


    [Fact]
    public void ReleasePatchCarriesResumeInputForSameWorkSession()
    {
        const string body = """
            WORK_GRAPH_PATCH:
            {
              "expectedRevision": 5,
              "operations": [
                {
                  "type": "RELEASE",
                  "workItemId": "W1",
                  "inputType": "HQ_RESUME",
                  "value": "새 WorkItem을 추가했습니다. 기존 작업을 계속하세요."
                }
              ]
            }
            """;

        Assert.True(WorkGraphTransportContract.TryParse(body, out var patch, out var error));
        Assert.Null(error);
        var operation = Assert.Single(patch!.Operations);
        Assert.Equal(WorkGraphPatchOperationType.Release, operation.Type);
        Assert.Equal("HQ_RESUME", operation.InputType);
        Assert.Contains("기존 작업", operation.Value);
    }

    [Theory]
    [InlineData("COMPLETED", WorkItemReportStatus.Completed)]
    [InlineData("BLOCKED", WorkItemReportStatus.Blocked)]
    [InlineData("SPLIT_REQUEST", WorkItemReportStatus.SplitRequest)]
    [InlineData("FAILED", WorkItemReportStatus.Failed)]
    public void WorkItemReportParsesMechanicalStatus(string value, WorkItemReportStatus expected)
    {
        var body = $"WORK_ITEM_STATUS: {value}\n보고 본문";

        Assert.True(WorkItemReportContract.TryParse(body, out var report, out var error));
        Assert.Null(error);
        Assert.Equal(expected, report!.Status);
        Assert.Equal("보고 본문", report.Body);
    }

    [Fact]
    public void WorkItemPromptExposesParallelContextWithoutChangingLegacyPrompt()
    {
        var legacy = RoleContractLoader.BuildWorkPrompt("HQ_INSTRUCTION", "기존 작업", judgeAvailable: false);
        Assert.Contains("병렬 WorkItem 사용: 아니오", legacy);
        Assert.DoesNotContain("WORK_ITEM_STATUS: COMPLETED", legacy);

        var parallel = RoleContractLoader.BuildWorkPrompt(
            "WORK_ITEM",
            "수행하세요.",
            judgeAvailable: false,
            observationRequestDirectory: "C:/obs",
            workItem: new WorkItemPromptContext(
                "W17",
                WorkItemKind.Normal,
                "기능 구현",
                new[] { "W3" },
                "abc123",
                "projecthub/job/W17",
                "C:/wt/W17"));

        Assert.Contains("병렬 WorkItem 사용: 예", parallel);
        Assert.Contains("workItemId: W17", parallel);
        Assert.Contains("WorkItem 목표: 기능 구현", parallel);
        Assert.Contains("WORK_ITEM_STATUS: COMPLETED", parallel);
    }

    [Fact]
    public void ParallelHqContractRequiresIntegrationBeforeEndWhenFinalCodeNeedsMultipleResults()
    {
        var footer = RoleContractLoader.LoadHqFooter(parallelWorkGraph: true);

        Assert.Contains("INTEGRATION WorkItem을 END 전에 추가", footer);
        Assert.Contains("INTEGRATION_LANDING_FAILED", footer);
        Assert.Contains("fast-forward", footer);
        Assert.Contains("force/reset", footer);
    }

    [Fact]
    public void HqPromptExposesGraphPatchContractOnlyInParallelMode()
    {
        var legacy = RoleContractLoader.BuildHqPrompt("USER", "요청");
        Assert.Contains("병렬 WorkGraph 사용: 아니오", legacy);
        Assert.DoesNotContain("WORK_GRAPH_PATCH:", legacy);

        var parallel = RoleContractLoader.BuildHqPrompt(
            "USER",
            "요청",
            new WorkGraphPromptContext(2, 4, "abc123"));

        Assert.Contains("병렬 WorkGraph 사용: 예", parallel);
        Assert.Contains("WorkGraph revision: 2", parallel);
        Assert.Contains("최대 동시 WORK: 4", parallel);
        Assert.Contains("WORK_GRAPH_PATCH:", parallel);
        Assert.Contains("SET_MAX_CONCURRENCY", parallel);
        Assert.Contains("HQ가 변경하지 않는다", parallel);
    }
}
