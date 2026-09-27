using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerStructuredPayloadHelperTests
{
    [Fact]
    public async Task ValidPayloadReturnsWithoutRepairCall()
    {
        var runner = new FakeRunner(
            """{"expectedRevision":0,"operations":[]}""");
        var helper = new WorkerStructuredPayloadHelper(
            new AiRoleRunnerRegistry(new[] { runner }));

        var result = await helper.ProcessAsync<WorkGraphPatch>(
            Request(
                """
                WORK_GRAPH_PATCH:
                {"expectedRevision":0,"operations":[]}
                """),
            WorkGraphTransportContract.TryParse,
            WorkGraphTransportContract.TryParseJsonPayload);

        Assert.True(result.Success);
        Assert.False(result.RepairAttempted);
        Assert.False(result.Repaired);
        Assert.Equal(0, runner.CallCount);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task InvalidPayloadIsRepairedOnceAndRevalidated()
    {
        var runner = new FakeRunner(
            """{"expectedRevision":0,"operations":[{"type":"ADD","workItemId":10,"goal":"복구","dependencies":[],"kind":"NORMAL"}]}""");
        var helper = new WorkerStructuredPayloadHelper(
            new AiRoleRunnerRegistry(new[] { runner }));

        var result = await helper.ProcessAsync<WorkGraphPatch>(
            Request(
                """
                WORK_GRAPH_PATCH:
                {"expectedRevision":0,"operations":[
                """),
            WorkGraphTransportContract.TryParse,
            WorkGraphTransportContract.TryParseJsonPayload);

        Assert.True(result.Success);
        Assert.True(result.RepairAttempted);
        Assert.True(result.Repaired);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal("WORK_GRAPH_PATCH_JSON_INVALID", result.InitialErrorCode);
        Assert.Null(result.FinalErrorCode);
        Assert.Equal("10", Assert.Single(result.Value!.Operations).WorkItemId);
        Assert.NotNull(runner.LastRequest);
        Assert.Null(runner.LastRequest!.SessionId);
        Assert.Equal(CodexSandboxMode.ReadOnly, runner.LastRequest.Sandbox);
        Assert.True(runner.LastRequest.IgnoreProjectInstructions);
    }

    [Fact]
    public async Task InvalidRepairReturnsFinalFailureWithoutSecondAiAttempt()
    {
        var runner = new FakeRunner("not-json");
        var helper = new WorkerStructuredPayloadHelper(
            new AiRoleRunnerRegistry(new[] { runner }));

        var result = await helper.ProcessAsync<WorkGraphPatch>(
            Request("WORK_GRAPH_PATCH:\n{"),
            WorkGraphTransportContract.TryParse,
            WorkGraphTransportContract.TryParseJsonPayload);

        Assert.False(result.Success);
        Assert.True(result.RepairAttempted);
        Assert.False(result.Repaired);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal("WORK_GRAPH_PATCH_JSON_MISSING", result.FinalErrorCode);
    }

    private static StructuredPayloadRequest Request(string rawPayload)
        => new(
            "WORK_GRAPH_PATCH",
            rawPayload,
            new WorkerAiRoleSettings(
                Provider: "openai",
                Model: "test-model",
                Reasoning: "medium",
                Transport: "codex_cli"),
            Directory.GetCurrentDirectory(),
            "JSON 객체 하나만 반환한다.");

    private sealed class FakeRunner(string response) : IAiRoleRunner
    {
        public AiServiceProvider Provider => AiServiceProvider.OpenAI;
        public bool SupportsSessions => true;
        public int CallCount { get; private set; }
        public AiRoleRunRequest? LastRequest { get; private set; }

        public string? GetPreflightError(
            WorkerAiRoleSettings role,
            string workingDirectory,
            bool openAiAuthenticated) => null;

        public Task<AiRoleRunResult> RunAsync(AiRoleRunRequest request)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new AiRoleRunResult(
                "openai",
                request.Role.Model,
                request.Role.Reasoning,
                null,
                0,
                string.Empty,
                string.Empty,
                response,
                Array.Empty<CodexCliFile>(),
                CodexUsage.Empty,
                Array.Empty<CodexCommandExecution>()));
        }
    }
}
