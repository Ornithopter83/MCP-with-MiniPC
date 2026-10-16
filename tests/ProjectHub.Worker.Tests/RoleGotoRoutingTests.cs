using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class RoleGotoRoutingTests
{
    private const string Summary = """
        
        @@REPORT
        <STATUS>completed</>
        <SUMMARY>작업 결과</>
        [RESPONSE=OK]
        """;

    [Theory]
    [InlineData("[GOTO=DESIGN]", "DESIGN")]
    [InlineData("[GOTO : DESIGN]", "DESIGN")]
    [InlineData("[goto: design]", "DESIGN")]
    [InlineData("[Goto =  Design ]", "DESIGN")]
    [InlineData("[GOTO : ASSET MANAGER]", "ASSET_MANAGER")]
    [InlineData("[GOTO=WORKER]", "WORKER")]
    [InlineData("[GOTO : HQ]", "HQ")]
    public void CommonGotoParserStillNormalizesRoles(string line, string role)
    {
        Assert.True(RoleGotoContract.TryReadLeading(
            line + "\n" + Summary, out var actual, out var error), error);
        Assert.Equal(role, actual);
    }

    [Theory]
    [InlineData("[GOTO : DESIGN]\n[GOTO=HQ]\n")]
    [InlineData("[GOTO==DESIGN]\n")]
    [InlineData("[GOTO=UNKNOWN]\n")]
    [InlineData("[GOTO=../DESIGN]\n")]
    public void MalformedConflictingOrUnknownGoto_FailsClosed(string leading)
    {
        Assert.False(RoleGotoContract.TryReadLeading(
            leading + Summary, out _, out _));
        Assert.False(RoleTextProtocol.ParseWork(leading + Summary).IsValid);
    }

    [Theory]
    [InlineData("[GOTO=WORKER]  @@REPORT")]
    [InlineData("[GOTO : WORKER]@@REPORT")]
    [InlineData("[GOTO=WORKER]\\n @@REPORT")]
    [InlineData(" @@REPORT")]
    public void JoinedHeaderTokensAreRecoveredWithoutChangingContract(string header)
    {
        var report = header.Replace("\\n", "\n") + "\n" +
            "<STATUS>completed</>\n<SUMMARY>실제 제작 완료</>\n" +
            "<CHANGED_PATH>Assets/UI/Panel.prefab</>\n" +
            "<ISSUES>없음</>\n[RESPONSE=OK]";
        Assert.True(RoleTextProtocol.ParseWork(report).IsValid);
        Assert.Equal("completed", MilestoneDefinitionContract.ReadWorkStatus(report));
    }

    [Theory]
    [InlineData("[GOTO=WORKER] [GOTO=DESIGN]  @@REPORT")]
    [InlineData("[GOTO=UNKNOWN]  @@REPORT")]
    public void JoinedHeaderRejectsConflictingOrInvalidRoutes(string header)
    {
        var report = header + "\n" +
            "<STATUS>completed</>\n<SUMMARY>결과</>\n[RESPONSE=OK]";
        Assert.False(RoleTextProtocol.ParseWork(report).IsValid);
    }

    [Fact]
    public void GotoInSummaryIsNeverExecuted()
    {
        var report = "[GOTO=WORKER]  @@REPORT\n" +
            "<STATUS>completed</>\n" +
            "<SUMMARY>예시 문자열 [GOTO=DESIGN]은 작업 설명입니다.</>\n" +
            "[RESPONSE=OK]";
        Assert.True(RoleTextProtocol.ParseWork(report).IsValid);
        Assert.True(RoleGotoContract.TryReadLeading(
            report, out var target, out var error), error);
        Assert.Equal("WORKER", target);
    }

    [Fact]
    public void DuplicateMatchingGotoIsNotAmbiguous()
    {
        Assert.True(RoleGotoContract.TryReadLeading(
            "[GOTO:DESIGN]\n[GOTO=design]\n" + Summary,
            out var role, out var error), error);
        Assert.Equal("DESIGN", role);
    }

    [Theory]
    [InlineData("[GOTO=DESIGN]")]
    [InlineData("[GOTO : DESIGN]")]
    public void HighCannotRouteToDesign_AndReportsBlockedToWorker(string gotoLine)
    {
        var report = gotoLine + "\n@@REPORT\n<STATUS>completed</>\n" +
            "<SUMMARY>디자인 수정 요구</>\n[RESPONSE=OK]";
        Assert.Null(MilestoneDefinitionContract.ReadHighStatus(report));
        var normalized = MilestoneDefinitionContract.NormalizeHighReport(
            0, report, null);
        Assert.Equal("blocked", MilestoneDefinitionContract.ReadHighStatus(normalized));
        Assert.StartsWith("[GOTO=WORKER]", normalized);
        Assert.Contains("HIGH_DESIGN_ROUTE_REMOVED", normalized);
    }

    [Fact]
    public void HighCompletionMayReturnDirectlyToHq()
    {
        const string report = """
            [GOTO : HQ]
            @@REPORT
            <STATUS>completed</>
            <SUMMARY>검증 종료</>
            [RESPONSE=OK]
            """;
        Assert.Equal("completed", MilestoneDefinitionContract.ReadHighStatus(report));
        Assert.Equal("completed", MilestoneDefinitionContract.ReadHighStatus(
            MilestoneDefinitionContract.NormalizeHighReport(0, report, null)));
    }

    [Fact]
    public void HighInProgressIsNotAllowedWithoutDesignHandoff()
    {
        var response = RoleTextProtocol.BuildResult("in_progress", "unfinished");
        Assert.False(RoleTextProtocol.ParseHigh(response).IsValid);
        Assert.Null(MilestoneDefinitionContract.ReadHighStatus(response));
    }

    [Fact]
    public void WorkCannotRedirectSchedulerToAnotherRole()
    {
        Assert.False(RoleTextProtocol.ParseWork(
            "[GOTO=DESIGN]\n" + Summary).IsValid);
        var completed = "[GOTO : WORKER]\n" + Summary;
        Assert.True(RoleTextProtocol.ParseWork(completed).IsValid);
        var normalized = MilestoneDefinitionContract.NormalizeWorkReport(
            0, completed, null);
        Assert.StartsWith("[GOTO=WORKER]", normalized);
    }

    [Fact]
    public void WorkAndLegacyManagerUseCommonGotoParsing()
    {
        Assert.True(RoleTextProtocol.ParseWork("[GOTO=WORKER]\n" + Summary).IsValid);
        const string manager = """
            [GOTO : HQ]
            [ACTION=REPORT]
            {"status":"completed","content":"asset list"}
            """;
        var result = ActionBlockContract.ParseManager(manager);
        Assert.False(result.HasErrors);
        Assert.Equal("HQ", Assert.Single(result.ValidActions).GotoTarget);
    }

    [Fact]
    public void HqMilestoneStillAllowsHqDirectedDesign()
    {
        const string milestone = """
            [ACTION=WORK]
            @@MILESTONE
            <ID>M1</>
            @@DESIGN=0
            <PATH>docs/projecthub/design.md</>
            <INSTRUCTIONS>UI 제작</>
            @@HIGH
            <INSTRUCTIONS>완료 검토</>
            [RESPONSE=OK]
            """;
        foreach (var prefix in new[] { "[GOTO=WORKER]", "[GOTO : WORKER]" })
            Assert.True(HqTextProtocol.Parse(prefix + "\n" + milestone).IsValid);
        Assert.False(HqTextProtocol.Parse("[GOTO=DESIGN]\n" + milestone).IsValid);
    }
}
