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
