using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkTransportTests
{
    [Fact]
    public void WorkContractStaysGenericAcrossWorkItemKinds()
    {
        var footer = RoleContractLoader.LoadWorkFooter();

        Assert.Contains("현재 WorkItem을 수행하는 WORK", footer);
        Assert.Contains("WORK_ITEM_STATUS: COMPLETED", footer);
        Assert.Contains("WORK_ITEM_STATUS: BLOCKED", footer);
        Assert.Contains("WORK_ITEM_STATUS: FAILED", footer);
        Assert.DoesNotContain("MATERIALIZE / COPY", footer);
        Assert.DoesNotContain("BUILD / PUBLISH", footer);
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
    public void WorkGraphTransportAcceptsNumericWorkItemIdsAndDependencies()
    {
        const string body = """
            WORK_GRAPH_PATCH:
            {
              "expectedRevision": 0,
              "operations": [
                {
                  "type": "ADD",
                  "workItemId": 10,
                  "goal": "파일 저장",
                  "dependencies": [],
                  "kind": "NORMAL",
                  "baseRef": "abc123"
                },
                {
                  "type": "ADD",
                  "workItemId": "11",
                  "goal": "후속 작업",
                  "dependencies": [10],
                  "kind": "NORMAL",
                  "baseRef": "abc123"
                }
              ]
            }
            """;

        Assert.True(WorkGraphTransportContract.TryParse(body, out var patch, out var error), error);
        Assert.Null(error);
        Assert.NotNull(patch);
        Assert.Equal("10", patch!.Operations[0].WorkItemId);
        Assert.Equal("11", patch.Operations[1].WorkItemId);
        Assert.Equal(new[] { "10" }, patch.Operations[1].Item!.Dependencies);
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
    public void WorkGraphTransportRepairsOperationAliasToType()
    {
        const string body = """
            WORK_GRAPH_PATCH:
            {"expectedRevision":0,"operations":[{"operation":"ADD","workItemId":0,"goal":"RESOURCE 이미지 생성","kind":"NORMAL","baseRef":"abc123"}]}
            """;

        Assert.True(
            WorkGraphTransportContract.TryRepairOperationTypeAliases(
                body,
                out var repaired,
                out var summary));
        Assert.Equal(
            "operations[0].operation -> operations[0].type",
            summary);
        Assert.DoesNotContain("\"operation\"", repaired);
        Assert.Contains("\"type\":\"ADD\"", repaired);
        Assert.True(
            WorkGraphTransportContract.TryParseJsonPayload(
                repaired,
                out var patch,
                out var error),
            error);
        Assert.Equal(WorkGraphPatchOperationType.Add, Assert.Single(patch!.Operations).Type);
    }

    [Fact]
    public void WorkGraphUnsupportedOperationAliasIsNormalizedThenRejected()
    {
        const string body = """
            WORK_GRAPH_PATCH:
            {"expectedRevision":0,"operations":[{"operation":"BOGUS","workItemId":0,"kind":"NORMAL","goal":"test"}]}
            """;

        Assert.False(WorkGraphTransportContract.TryParse(body, out _, out var error));
        Assert.Equal("WORK_GRAPH_OPERATION_UNSUPPORTED", error);

        var detail = WorkGraphTransportContract.DescribeError(body, error);
        Assert.NotNull(detail);
        Assert.Contains("path=operations[0].type", detail);
        Assert.Contains("Unsupported WorkGraph operation \"BOGUS\"", detail);
    }

    [Fact]
    public void SetGoalSchemaDiagnosticPointsToMissingValue()
    {
        const string body = """
            WORK_GRAPH_PATCH:
            {"expectedRevision":4,"operations":[{"type":"SET_GOAL","workItemId":"W10"}]}
            """;

        Assert.False(WorkGraphTransportContract.TryParse(body, out _, out var error));
        Assert.Equal("WORK_GRAPH_SET_GOAL_SCHEMA_INVALID", error);

        var detail = WorkGraphTransportContract.DescribeError(body, error);
        Assert.NotNull(detail);
        Assert.Contains("path=operations[0].value", detail);
        Assert.Contains("requires a nonblank \"value\" field", detail);
    }

    [Theory]
    [InlineData(
        "{\"expectedRevision\":2,\"operations\":[{\"type\":\"SET_BASE_REF\",\"workItemId\":\"W10\",\"baseRef\":\"abc123\"}]}",
        WorkGraphPatchOperationType.SetBaseRef,
        "abc123")]
    [InlineData(
        "{\"expectedRevision\":2,\"operations\":[{\"type\":\"RELEASE\",\"workItemId\":\"W10\",\"body\":\"continue\"}]}",
        WorkGraphPatchOperationType.Release,
        "continue")]
    public void CommonOperationValueAliasesAreNormalized(
        string json,
        WorkGraphPatchOperationType expectedType,
        string expectedValue)
    {
        var body = "WORK_GRAPH_PATCH:" + Environment.NewLine + json;

        Assert.True(WorkGraphTransportContract.TryParse(body, out var patch, out var error), error);
        Assert.Null(error);
        var operation = Assert.Single(patch!.Operations);
        Assert.Equal(expectedType, operation.Type);
        Assert.Equal(expectedValue, operation.Value);
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

    [Fact]
    public void ReleasePatchPreservesJsonObjectValueForBuildAuthorization()
    {
        const string body = """
            WORK_GRAPH_PATCH:
            {
              "expectedRevision": 5,
              "operations": [
                {
                  "type": "RELEASE",
                  "workItemId": "W1",
                  "inputType": "BUILD_AUTHORIZED",
                  "value": {
                    "scope": "TARGET",
                    "target": "src/App/App.csproj",
                    "configuration": "Release",
                    "noRestore": true
                  }
                }
              ]
            }
            """;

        Assert.True(WorkGraphTransportContract.TryParse(body, out var patch, out var error), error);
        Assert.Null(error);
        var operation = Assert.Single(patch!.Operations);
        Assert.Equal("BUILD_AUTHORIZED", operation.InputType);
        using var document = JsonDocument.Parse(operation.Value!);
        Assert.Equal("TARGET", document.RootElement.GetProperty("scope").GetString());
        Assert.Equal("src/App/App.csproj", document.RootElement.GetProperty("target").GetString());
    }

    [Theory]
    [InlineData("COMPLETED", WorkItemReportStatus.Completed)]
    [InlineData("BLOCKED", WorkItemReportStatus.Blocked)]
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
    public void HqContractAssignsIntegrationAndFixedSlots()
    {
        var footer = RoleContractLoader.LoadHqFooter();

        Assert.Contains("#8은 MATERIALIZE/COPY", footer);
        Assert.Contains("#9는 BUILD/PUBLISH", footer);
        Assert.Contains("여러 독립 CODE_CHANGE 결과를 합치는 일은 별도 INTEGRATION WorkItem", footer);
        Assert.Contains("#0·#8·#9도 WorkGraph operation은 ADD", footer);
    }

    [Fact]
    public void HqContractDefinesSupportedOperationsAndFixedSlotAddRule()
    {
        var footer = RoleContractLoader.LoadHqFooter();

        Assert.Contains("operation은 ADD, CANCEL, SET_DEPENDENCIES, SET_GOAL, SET_BASE_REF, RELEASE", footer);
        Assert.Contains("#0·#8·#9도 WorkGraph operation은 ADD", footer);
        Assert.Contains("CONTINUE에는 WORK_GRAPH_PATCH를 정확히 하나 출력", footer);
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
