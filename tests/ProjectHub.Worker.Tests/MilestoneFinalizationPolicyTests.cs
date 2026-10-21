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

    [Theory]
    [InlineData(false, 0, 0, false, "GIT_FINALIZE_FAILED", false, "FINALIZE_FAILED")]
    [InlineData(false, 0, 0, false, "COMPLETED", true, "FINALIZED")]
    [InlineData(false, 1, 0, false, "SV_BLOCKED:1", false, "SKIPPED_BLOCKED")]
    [InlineData(true, 0, 0, false, "COMPLETED", true, "SKIPPED_READ_ONLY")]
    public void GraphShowsFailedAttemptInsteadOfSkippedOperation(
        bool readOnly, int blocked, int pending, bool buildFailed,
        string outcome, bool gitSucceeded, string expected)
    {
        Assert.Equal(expected,
            MilestoneFinalizationPolicy.ResolveGitGraphState(
                readOnly, blocked, pending, buildFailed, outcome, gitSucceeded));
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
    public async Task GitFinalizer_RefusesToAbortAnExistingMerge()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "projecthub-merge-protect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add("init");
            start.ArgumentList.Add("-b");
            start.ArgumentList.Add("main");
            using (var init = System.Diagnostics.Process.Start(start)!)
            {
                await init.WaitForExitAsync();
                Assert.Equal(0, init.ExitCode);
            }

            var marker = Path.Combine(root, ".git", "MERGE_HEAD");
            var original = new string('a', 40) + Environment.NewLine;
            await File.WriteAllTextAsync(marker, original);
            var result = await MilestoneMechanicalExecutor.ForceCommitPushAsync(
                root, "must not push", new[] { "README.md" },
                configuredRepositoryUrl: null, cancellationToken: CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("step=GIT_OPERATION_IN_PROGRESS:MERGE_HEAD", result.Summary);
            Assert.Equal(original, await File.ReadAllTextAsync(marker));
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
