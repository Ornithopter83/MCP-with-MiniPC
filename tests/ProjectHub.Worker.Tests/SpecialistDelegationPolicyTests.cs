using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class SpecialistDelegationPolicyTests
{
    [Fact]
    public void SupervisorMustStartReadOnly_AndRecoveryMustBeSeparateWritableTurn()
    {
        Assert.Equal(CodexSandboxMode.ReadOnly,
            SpecialistDelegationPolicy.DelegationSandbox);
        Assert.Equal(CodexSandboxMode.WorkspaceWrite,
            SpecialistDelegationPolicy.ApprovedRecoverySandbox);
    }

    [Theory]
    [InlineData(false, 1, 0, 1, 0, false, true)]
    [InlineData(false, 3, 1, 2, 0, false, true)]
    [InlineData(false, 0, 0, 0, 0, false, false)]
    [InlineData(false, 1, 0, 0, 1, false, false)]
    [InlineData(false, 2, 1, 1, 1, false, false)]
    [InlineData(false, 1, 0, 1, 0, true, false)]
    [InlineData(true, 1, 0, 1, 0, false, false)]
    public void DirectFallback_RequiresRealSafeFailedDelegation(
        bool readOnly, int attempts, int successful, int recoverable,
        int unsafeCount, bool canceled, bool expected)
    {
        Assert.Equal(expected, SpecialistDelegationPolicy.CanDirectFallback(
            readOnly, attempts, successful, recoverable, unsafeCount, canceled));
    }

    [Theory]
    [InlineData("TEMP_WORK_FAILED: WORK blocked", true)]
    [InlineData("WORK_SCOPE_VIOLATION: changed other files", false)]
    [InlineData("WORK_IN_PROGRESS: partial changes", false)]
    [InlineData("WORK_REPORT_INVALID: invalid format", false)]
    [InlineData("WORK_PATH_INVALID_OR_CONFLICT", false)]
    public void McpFailureClassification_DeniesUnsafeDirectFallback(
        string message, bool expected)
    {
        Assert.Equal(expected, BridgeServer.IsRecoverableWorkFailure(
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
