using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class MilestoneStateConsistencyTests
{
    [Theory]
    [InlineData("completed", "COMPLETED")]
    [InlineData("blocked", "BLOCKED")]
    [InlineData("in_progress", "IN_PROGRESS")]
    [InlineData(null, "UNVERIFIED")]
    public void ReportStatuses_AreNotAlwaysCompleted(
        string? status, string expected)
    {
        Assert.Equal(expected, MilestoneGraphStatePolicy.RoleState(status));
    }

    [Theory]
    [InlineData("FINALIZED", "COMPLETED")]
    [InlineData("FINALIZE_FAILED", "FAILED")]
    [InlineData("SKIPPED_READ_ONLY", "SKIPPED")]
    [InlineData("SKIPPED_BLOCKED", "BLOCKED")]
    [InlineData("RUNNING", "PLANNED")]
    public void GitGraph_NeverCallsFailureCompleted(string state, string expected)
    {
        Assert.Equal(expected, MilestoneGraphStatePolicy.GitState(state));
    }

    [Fact]
    public void WorkerBlockedDiagnostics_SurviveNonzeroExit()
    {
        var raw = RoleTextProtocol.BuildResult(
            "blocked", "SV_WORK_DELEGATION_UNSAFE_TO_FALLBACK",
            issues: new[] { "WORK_SCOPE_VIOLATION" });
        var normalized = MilestoneDefinitionContract.NormalizeWorkReport(
            1, raw, "UI callback NotSupportedException");
        var parsed = RoleTextProtocol.ParseWork(normalized);
        Assert.True(parsed.IsValid);
        Assert.Equal("blocked", parsed.Status);
        Assert.Contains("SV_WORK_DELEGATION_UNSAFE_TO_FALLBACK", parsed.Summary);
        Assert.Contains(parsed.Issues, item => item.Contains("NotSupportedException"));
    }

    [Fact]
    public void HighBlockedDiagnostics_SurviveNonzeroExit()
    {
        var raw = RoleTextProtocol.BuildResult(
            "blocked", "HIGH_WORK_DELEGATION_REQUIRED");
        var normalized = MilestoneDefinitionContract.NormalizeHighReport(1, raw, "");
        Assert.Contains("HIGH_WORK_DELEGATION_REQUIRED", normalized);
    }

    [Fact]
    public void LegacyNamedClosingTags_WorkResultIsParsed()
    {
        var text = "[ACTION=RESULT]\n\n@@REPORT\n" +
                   "<STATUS>completed</STATUS>\n" +
                   "<SUMMARY>read-only audit</SUMMARY>\n" +
                   "<CHANGED_PATH>없음</CHANGED_PATH>\n" +
                   "<ISSUES>없음</ISSUES>\n\n[RESPONSE=OK]";
        var parsed = RoleTextProtocol.ParseWork(text);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.Equal("completed", parsed.Status);
    }
}
