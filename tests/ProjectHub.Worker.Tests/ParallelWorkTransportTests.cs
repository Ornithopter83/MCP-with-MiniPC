using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkTransportTests
{
    [Fact]
    public void ParallelWorkContractDefinesIntegrationAsSameWorkRole()
    {
        var footer = RoleContractLoader.LoadWorkFooter();

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
    public void WorkGraphTransportFindsPatchAfterExplanatoryTextAndIgnoresTrailingText()
    {
        const string body = """
            기준 ref abc123에서 확인한 결과를 먼저 설명합니다.

            HQ 확정 설계:
            - 공통 데이터 계약을 먼저 만든다.
            - 이후 구현 WorkItem을 분리한다.

            WORK_GRAPH_PATCH:
            {"expectedRevision":0,"operations":[]}

            위 패치 기준으로 작업을 진행하세요.
            """;

        Assert.True(WorkGraphTransportContract.TryParse(body, out var patch, out var error), error);
        Assert.Null(error);
        Assert.NotNull(patch);
        Assert.Equal(0, patch!.ExpectedRevision);
        Assert.Empty(patch.Operations);
    }

    [Fact]
    public void WorkGraphTransportAcceptsInlineJsonAfterMarker()
    {
        const string body = """
            설계 설명
            WORK_GRAPH_PATCH: {"expectedRevision":4,"operations":[]}
            추가 설명
            """;

        Assert.True(WorkGraphTransportContract.TryParse(body, out var patch, out var error), error);
        Assert.Null(error);
        Assert.Equal(4, patch!.ExpectedRevision);
    }

    [Fact]
    public void WorkGraphTransportRejectsDuplicateMarkers()
    {
        const string body = """
            WORK_GRAPH_PATCH:
            {"expectedRevision":1,"operations":[]}
            WORK_GRAPH_PATCH:
            {"expectedRevision":1,"operations":[]}
            """;

        Assert.False(WorkGraphTransportContract.TryParse(body, out _, out var error));
        Assert.Equal("WORK_GRAPH_PATCH_MARKER_DUPLICATE", error);
    }

    [Fact]
    public void WorkGraphTransportAcceptsExplicitNoOpContinue()
    {
        const string body = """
            WORK_GRAPH_PATCH:
            {"expectedRevision":3,"operations":[]}
            """;

        Assert.True(WorkGraphTransportContract.TryParse(body, out var patch, out var error));
        Assert.Null(error);
        Assert.NotNull(patch);
        Assert.Equal(3, patch!.ExpectedRevision);
        Assert.Empty(patch.Operations);
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
    public void WorkItemReportFindsStatusAfterExplanatoryTextAndPreservesReportBody()
    {
        const string body = """
            구현 결과를 먼저 요약합니다.
            WORK_ITEM_STATUS: COMPLETED
            검증 결과: 성공
            """;

        Assert.True(WorkItemReportContract.TryParse(body, out var report, out var error), error);
        Assert.Null(error);
        Assert.Equal(WorkItemReportStatus.Completed, report!.Status);
        Assert.Contains("구현 결과를 먼저 요약합니다.", report.Body);
        Assert.Contains("검증 결과: 성공", report.Body);
        Assert.DoesNotContain("WORK_ITEM_STATUS:", report.Body);
    }

    [Fact]
    public void WorkItemReportRejectsDuplicateStatusMarkers()
    {
        const string body = """
            WORK_ITEM_STATUS: COMPLETED
            설명
            WORK_ITEM_STATUS: FAILED
            """;

        Assert.False(WorkItemReportContract.TryParse(body, out _, out var error));
        Assert.Equal("WORK_ITEM_STATUS_DUPLICATE", error);
    }

    [Fact]
    public void WorkItemPromptAlwaysUsesWorkGraphContextWithoutAvailabilityInjection()
    {
        var prompt = RoleContractLoader.BuildWorkPrompt(
            "WORK_ITEM",
            "수행하세요.",
            new WorkItemPromptContext(
                "W17",
                WorkItemKind.Normal,
                "기능 구현",
                new[] { "W3" },
                "abc123",
                "projecthub/job/W17",
                "C:/wt/W17"),
            "C:/obs");

        Assert.Contains("workItemId: W17", prompt);
        Assert.Contains("WorkItem 목표: 기능 구현", prompt);
        Assert.Contains("WORK_ITEM_STATUS: COMPLETED", prompt);
        Assert.DoesNotContain("병렬 WorkItem 사용:", prompt);
        Assert.DoesNotContain("판정 사용 가능:", prompt);
        Assert.DoesNotContain("리소스 사용 가능:", prompt);
    }

    [Fact]
    public void ParallelHqContractRequiresIntegrationBeforeEndWhenFinalCodeNeedsMultipleResults()
    {
        var footer = RoleContractLoader.LoadHqFooter();

        Assert.Contains("INTEGRATION WorkItem을 END 전에 추가", footer);
        Assert.Contains("INTEGRATION_LANDING_FAILED", footer);
        Assert.Contains("fast-forward", footer);
        Assert.Contains("force/reset", footer);
    }

    [Fact]
    public void HqPromptAlwaysUsesWorkGraphContract()
    {
        var prompt = RoleContractLoader.BuildHqPrompt(
            "USER",
            "요청",
            new WorkGraphPromptContext(2, 1, "abc123"));

        Assert.Contains("WorkGraph revision: 2", prompt);
        Assert.Contains("최대 동시 WORK: 1", prompt);
        Assert.Contains("WORK_GRAPH_PATCH:", prompt);
        Assert.DoesNotContain("병렬 WorkGraph 사용:", prompt);
        Assert.DoesNotContain("PARALLEL_ON", prompt);
    }
}
