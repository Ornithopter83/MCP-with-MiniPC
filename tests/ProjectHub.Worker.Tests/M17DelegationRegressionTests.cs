using System.Diagnostics;
using System.Text;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class M17DelegationRegressionTests
{
    private static AiRoleRunResult Result(int exit, string status) =>
        new("openai", "test", "medium", null, exit, "", "",
            RoleTextProtocol.BuildResult(status, "M17 검증 결과"),
            Array.Empty<CodexCliFile>(), CodexUsage.Empty,
            Array.Empty<CodexCommandExecution>());

    [Fact]
    public void M17_TwoSuccessfulWorkCallsAndOneDeniedPath_PreserveOriginalSvReport()
    {
        var metrics = new DelegationMetrics(3, 2, 0, 1, 0, true,
        [
            new("M17:WORK:A", "COMPLETED", "read-only audit"),
            new("M17:WORK:B", "COMPLETED", "isolated audit"),
            new("M17:SV", "REJECTED", "WORK_PATH_INVALID_OR_CONFLICT")
        ]);
        Assert.False(SpecialistDelegationPolicy.MustBlockForUnsafeDelegation(metrics));
        Assert.True(BridgeServer.IsRejectedDelegation(
            new InvalidOperationException("WORK_PATH_INVALID_OR_CONFLICT")));
        Assert.False(BridgeServer.IsRecoverableWorkFailure(
            new InvalidOperationException("WORK_PATH_INVALID_OR_CONFLICT")));

        var sv = Result(0, "blocked");
        var recorded = SpecialistDelegationPolicy.Enforce(sv, mandatory: true,
            successfulDelegations: metrics.Successful);
        Assert.Same(sv, recorded);
        Assert.Contains("M17 검증 결과", recorded.FinalMessage);

        var report = new StringBuilder();
        MilestoneDefinitionContract.AppendDelegatedChildren(report, "SV", metrics);
        Assert.Contains("attempts=3 completed=2 rejected=1", report.ToString());
        Assert.Contains("M17:WORK:A COMPLETED", report.ToString());
        Assert.Contains("M17:WORK:B COMPLETED", report.ToString());
    }

    [Fact]
    public void GenuineScopeViolation_StillBlocksEvenAfterSuccess()
    {
        var metrics = new DelegationMetrics(2, 1, 0, 0, 1, true,
            Array.Empty<DelegatedWorkEvidence>());
        Assert.True(SpecialistDelegationPolicy.MustBlockForUnsafeDelegation(metrics));
        Assert.False(BridgeServer.IsRejectedDelegation(
            new InvalidOperationException("WORK_SCOPE_VIOLATION: source")));
    }

    [Fact]
    public void HighChildSucceededButHighProcessExitedOne_RequiresOneReadOnlyRetry()
    {
        var metrics = new DelegationMetrics(1, 1, 0, 0, 0, true,
            Array.Empty<DelegatedWorkEvidence>());
        Assert.True(SpecialistDelegationPolicy.ShouldRetryHighReview(
            metrics, Result(1, "completed"), canceled: false));
        Assert.False(SpecialistDelegationPolicy.ShouldRetryHighReview(
            metrics, Result(0, "completed"), canceled: false));
        Assert.False(SpecialistDelegationPolicy.ShouldRetryHighReview(
            metrics, Result(1, "completed"), canceled: true));
    }

    [Fact]
    public void NoWorkDelegation_ForReadOnlyMilestoneStillCannotClaimCompletion()
    {
        var blocked = SpecialistDelegationPolicy.Enforce(
            Result(0, "completed"), mandatory: true, successfulDelegations: 0);
        Assert.Equal("blocked", RoleTextProtocol.ParseWork(blocked.FinalMessage).Status);
    }

    [Fact]
    public void BuildNotExecuted_MustBeMarkedSkippedNotCompleted()
    {
        var report = MilestoneDefinitionContract.FormatMechanicalResult(
            new MilestoneMechanicalResult(
                "BUILD", true, 0, "", "",
                "BUILD_SKIPPED: 변경 범위 내에서 실행 가능한 빌드 대상 없음"));
        Assert.Contains("status=SKIPPED", report);
        Assert.DoesNotContain("status=COMPLETED", report);
    }

    [Theory]
    [InlineData(".GIT/HEAD")]
    [InlineData(".Github/workflows/x.yml")]
    [InlineData(".ProjectHub/state")]
    [InlineData("TEMP/trace")]
    [InlineData("Master-POLISH.md")]
    public void ProtectedPath_CaseInsensitiveOnWindows(string path) =>
        Assert.False(TemporaryWorkScopeRegistry.IsAllowedPath(path));

    [Fact]
    public void ParallelWorkScopes_DoNotMisattributeCompletedSiblingChanges()
    {
        var registry = new TemporaryWorkScopeRegistry();
        var root = Path.Combine(Path.GetTempPath(), "projecthub-cohort");
        Assert.True(registry.TryAcquire(root, new[] { "apps" }, out var first));
        Assert.True(registry.TryAcquire(root, new[] { "docs" }, out var sibling));
        Assert.NotNull(first);
        Assert.NotNull(sibling);
        try
        {
            sibling!.Dispose();
            Assert.Contains("docs", registry.OtherConcurrentScopes(root, new[] { "apps" }));
            Assert.DoesNotContain("apps", registry.OtherConcurrentScopes(root, new[] { "apps" }));
        }
        finally
        {
            first!.Dispose();
        }
        Assert.Empty(registry.OtherConcurrentScopes(root, new[] { "apps" }));
    }

    [Fact]
    public async Task IgnoredTestArtifacts_AppearInScopedChangeAudit()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "projecthub-m17-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("init");
            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            await File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), "test-results/\n");
            string[] scopes = ["test-results/m17"];
            var before = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                root, CancellationToken.None, requireSuccess: true, scopedPaths: scopes);
            var folder = Path.Combine(root, "test-results", "m17");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "probe.txt"), "m17");
            var after = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                root, CancellationToken.None, requireSuccess: true, scopedPaths: scopes);
            Assert.Contains("test-results/m17/probe.txt",
                MilestoneMechanicalExecutor.DiffChangeStates(before, after));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
