using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class MilestoneFinalizationPolicyTests
{
    [Theory]
    [InlineData(0, 0, false, "COMPLETED", true)]
    [InlineData(1, 0, false, "SV_BLOCKED:1", false)]
    [InlineData(0, 1, false, "SV_IN_PROGRESS:1", false)]
    [InlineData(0, 0, true, "BUILD_FAILED", false)]
    [InlineData(0, 0, false, "HIGH_BLOCKED", false)]
    [InlineData(0, 0, false, "MILESTONE_EXECUTION_ERROR", false)]
    public void IncompleteMilestone_NeverPublishesPreviousHead(
        int blocked, int pending, bool buildFailed, string outcome, bool expected)
    {
        Assert.Equal(expected,
            MilestoneFinalizationPolicy.ShouldPublish(
                blocked, pending, buildFailed, outcome));
    }

    [Fact]
    public void GitFinalize_UsesFastForwardOnlyPush()
    {
        var arguments = MilestoneMechanicalExecutor.FastForwardPushArguments("main");
        Assert.Equal(new[] { "push", "origin", "HEAD:refs/heads/main" }, arguments);
        Assert.DoesNotContain("--force", arguments);
        Assert.DoesNotContain("--force-with-lease", arguments);
    }
}
