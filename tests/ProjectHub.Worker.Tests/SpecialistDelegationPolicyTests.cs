using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class SpecialistDelegationPolicyTests
{
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
