using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class SpecialistDelegationPolicyTests
{
    private const string Complete = """
        [GOTO=WORKER]
        @@REPORT
        <STATUS>completed</>
        <SUMMARY>수정 완료</>
        <ISSUES>없음</>
        [RESPONSE=OK]
        """;

    private const string Blocked = """
        [GOTO=WORKER]
        @@REPORT
        <STATUS>blocked</>
        <SUMMARY>MCP 연결 실패</>
        <ISSUES>도구 연결이 되지 않음</>
        [RESPONSE=OK]
        """;

    [Theory]
    [InlineData(true, 0, 0, true)]
    [InlineData(true, 1, 0, false)]
    [InlineData(true, 3, 0, false)]
    [InlineData(true, 0, 1, false)]
    [InlineData(false, 0, 0, false)]
    public void CompleteSpecialistRequiresOneSuccessfulTemporaryWork(
        bool mandatory, int count, int exitCode, bool blocked)
    {
        Assert.Equal(blocked, SpecialistDelegationPolicy.MustBlock(
            mandatory, count, exitCode, Complete));
    }

    [Fact]
    public void GenuineBlockedTurnNeedsNoWastefulDelegationRetry()
    {
        Assert.False(SpecialistDelegationPolicy.MustBlock(
            true, 0, 0, Blocked));
    }

    [Fact]
    public void VisualPolishAfterPreviousWorkCanSkipAnotherDelegation()
    {
        Assert.False(SpecialistDelegationPolicy.MustBlock(
            false, 0, 0, Complete));
    }

    [Fact]
    public void MissingToolMustNotPassForInProgressOrMalformedOutput()
    {
        Assert.True(SpecialistDelegationPolicy.MustBlock(
            true, 0, 0, "<STATUS>in_progress</>"));
        Assert.True(SpecialistDelegationPolicy.MustBlock(
            true, 0, 0, "done"));
    }
}
