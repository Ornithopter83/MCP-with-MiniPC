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

    [Theory]
    [InlineData(true, "design body", false)]
    [InlineData(false, "design body", true)]
    [InlineData(false, "", false)]
    [InlineData(true, "", false)]
    public void InitialPlanWrite_RespectsReadOnlyMilestone(
        bool readOnly, string plan, bool expected)
    {
        Assert.Equal(expected,
            MilestoneFinalizationPolicy.ShouldWriteInitialPlan(readOnly, plan));
    }

    [Theory]
    [InlineData("COMPLETED", false, false, "GIT_FINALIZE_FAILED")]
    [InlineData("COMPLETED", false, true, "COMPLETED")]
    [InlineData("COMPLETED", true, false, "COMPLETED")]
    [InlineData("SV_BLOCKED:1", false, false, "SV_BLOCKED:1")]
    [InlineData("BUILD_FAILED", false, false, "BUILD_FAILED")]
    public void GitFailure_CannotBeReportedAsCompleted(
        string outcome, bool readOnly, bool gitSucceeded, string expected)
    {
        Assert.Equal(expected,
            MilestoneFinalizationPolicy.ResolveGitOutcome(
                outcome, readOnly, gitSucceeded));
    }

    [Fact]
    public void GitOperationMarker_BlocksWithoutModifyingMarker()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "projecthub-git-guard-" + Guid.NewGuid().ToString("N"));
        var git = Path.Combine(root, ".git");
        Directory.CreateDirectory(git);
        try
        {
            var mergeHead = Path.Combine(git, "MERGE_HEAD");
            File.WriteAllText(mergeHead, "conflict");
            var rebase = Path.Combine(git, "rebase-merge");
            Directory.CreateDirectory(rebase);
            Assert.True(MilestoneMechanicalExecutor.IsGitOperationMarkerPresent(
                root, ".git/MERGE_HEAD"));
            Assert.True(MilestoneMechanicalExecutor.IsGitOperationMarkerPresent(
                root, rebase));
            Assert.False(MilestoneMechanicalExecutor.IsGitOperationMarkerPresent(
                root, ".git/CHERRY_PICK_HEAD"));
            Assert.Contains("MERGE_HEAD",
                MilestoneMechanicalExecutor.InProgressGitOperationMarkers);
            Assert.Contains("rebase-merge",
                MilestoneMechanicalExecutor.InProgressGitOperationMarkers);
            Assert.Equal("conflict", File.ReadAllText(mergeHead));
            Assert.True(Directory.Exists(rebase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
