using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityWorkSchedulingPolicyTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(16, 3, 1)]
    [InlineData(32, 30, 1)]
    [InlineData(16, 0, 0)]
    public void UnityEditorLaneSerializesWorkUntilExclusiveSchedulerIsReady(
        int configured, int runnableCount, int expected)
    {
        Assert.Equal(expected, UnityWorkSchedulingPolicy.MaxConcurrentWork(configured, runnableCount));
    }

    [Fact]
    public void InvalidConcurrencyIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UnityWorkSchedulingPolicy.MaxConcurrentWork(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnityWorkSchedulingPolicy.MaxConcurrentWork(1, -1));
    }

    [Fact]
    public void EmbeddedContractsDescribeUnityValidationBoundaries()
    {
        Assert.Contains("Unity HQ", RoleContractLoader.LoadHqFooter());
        Assert.Contains("Unity CLI/MCP", RoleContractLoader.LoadWorkFooter());
        Assert.Contains("PlayMode", RoleContractLoader.LoadQaFooter());
        Assert.Contains("GUID/.meta", RoleContractLoader.LoadHighFooter());
    }
}
