using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class HqTransportPolicyTests
{
    [Theory]
    [InlineData("web", true)]
    [InlineData("WEB", true)]
    [InlineData("codex_cli", false)]
    [InlineData("opencode_cli", false)]
    [InlineData("", false)]
    public void OnlyWebHqNeedsBrowser(string transport, bool expected)
        => Assert.Equal(expected, HqTransportPolicy.NeedsManagedBrowser(
            new WorkerAiRoleSettings(Transport: transport)));

    [Fact]
    public void CliGuidanceSupportsDirectLocalInspection()
    {
        var guidance = HqTransportPolicy.BuildRepositoryGuidance(false, true);
        Assert.Contains("git status", guidance);
        Assert.Contains("로컬 진단 명령", guidance);
        Assert.Contains("프로젝트·Git·Unity Editor 상태를 수정하지 마라", guidance);
        Assert.DoesNotContain("Web에서는", guidance);
        Assert.DoesNotContain("@@", guidance);
    }

    [Fact]
    public void MilestoneFollowupLimitsDiscovery()
    {
        var guidance = HqTransportPolicy.BuildRepositoryGuidance(
            false, true, "MILESTONE_REPORT");
        Assert.Contains("전체 프로젝트를 다시 조사하지 말고", guidance);
        Assert.Contains("변경 파일, 미해결 항목만", guidance);
        Assert.Contains("git status", guidance);
    }

    [Fact]
    public void GrammarRecoveryDoesNotRedoInvestigation()
    {
        var guidance = HqTransportPolicy.BuildRepositoryGuidance(
            false, true, "HQ_RESPONSE_RECOVERY");
        Assert.Contains("조사나 검증을 다시 실행하지 말고", guidance);
    }

    [Fact]
    public void WebGuidanceDoesNotAssumeCli()
    {
        var guidance = HqTransportPolicy.BuildRepositoryGuidance(true, true);
        Assert.Contains("Web에서는", guidance);
        Assert.Contains("로컬 파일·CLI 실행이 가능한 것으로 가정하지 마라", guidance);
        Assert.DoesNotContain("git status", guidance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOriginIsNotConsideredVerified(bool web)
        => Assert.Contains("origin/main을 검증하지 못했다",
            HqTransportPolicy.BuildRepositoryGuidance(web, false));
}
