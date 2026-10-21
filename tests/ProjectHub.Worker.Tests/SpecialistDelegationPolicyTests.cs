using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class SpecialistDelegationPolicyTests
{
    [Fact]
    public void SupervisorInitialTurn_IsReadOnlyAndNoDirectRecoveryIsConfigured()
    {
        Assert.Equal(CodexSandboxMode.ReadOnly,
            SpecialistDelegationPolicy.DelegationSandbox);
    }

    [Theory]
    [InlineData("TEMP_WORK_FAILED: WORK blocked", true)]
    [InlineData("WORK_SCOPE_VIOLATION: changed other files", false)]
    [InlineData("WORK_IN_PROGRESS: partial changes", false)]
    [InlineData("WORK_REPORT_INVALID: invalid format", false)]
    [InlineData("WORK_PATH_INVALID_OR_CONFLICT", false)]
    public void McpFailureClassification_DistinguishesExecutionFromSafetyFailures(
        string message, bool expected)
    {
        Assert.Equal(expected, BridgeServer.IsRecoverableWorkFailure(
            new InvalidOperationException(message)));
    }

    [Theory]
    [InlineData("WORK_PATH_NOT_FOUND: src/missing", true)]
    [InlineData("WORK_PATH_INVALID_OR_CONFLICT", true)]
    [InlineData("WORK_TASK_OR_PATHS_INVALID", true)]
    [InlineData("WORK_READ_ONLY_INVALID", true)]
    [InlineData("WORK_SCOPE_VIOLATION: illegal change", false)]
    [InlineData("WORK_READ_ONLY_VIOLATION: edit", false)]
    [InlineData("TEMP_WORK_FAILED: blocked", false)]
    public void MissingPath_IsRejectedPreflight_NotUnsafeWork(
        string message, bool expected)
    {
        Assert.Equal(expected, BridgeServer.IsRejectedDelegation(
            new InvalidOperationException(message)));
    }

    [Fact]
    public void WritableCompletion_WithoutAnySuccessfulWork_IsBlocked()
    {
        var result = new AiRoleRunResult(
            "openai", "test", "medium", null, 0, "", "",
            RoleTextProtocol.BuildResult("completed", "작업 완료"),
            Array.Empty<CodexCliFile>(), CodexUsage.Empty,
            Array.Empty<CodexCommandExecution>());
        var enforced = SpecialistDelegationPolicy.Enforce(result, true, 0);
        Assert.NotEqual(0, enforced.ExitCode);
        Assert.Equal("blocked", RoleTextProtocol.ParseWork(enforced.FinalMessage).Status);
        Assert.Contains(SpecialistDelegationPolicy.MissingWorkCode,
            enforced.FinalMessage);
    }

    [Fact]
    public void SuccessfulDelegation_AllowsSupervisorToReportCompletion()
    {
        var result = new AiRoleRunResult(
            "openai", "test", "medium", null, 0, "", "",
            RoleTextProtocol.BuildResult("completed", "작업 완료"),
            Array.Empty<CodexCliFile>(), CodexUsage.Empty,
            Array.Empty<CodexCommandExecution>());
        Assert.Same(result, SpecialistDelegationPolicy.Enforce(result, true, 1));
        Assert.Same(result, SpecialistDelegationPolicy.Enforce(result, false, 0));
    }
}
