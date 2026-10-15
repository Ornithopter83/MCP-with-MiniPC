using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class HqTransportHandoffPolicyTests
{
    [Fact]
    public void WebFollowupSendsOnlyNewUserInput()
    {
        const string followup = "재시도 요망";
        var sent = HqTransportHandoffPolicy.ComposeWebFollowup(followup);
        Assert.Equal(followup, sent);
        Assert.DoesNotContain("이전 HQ 상태", sent);
        Assert.DoesNotContain("[ACTION=PAUSE]", sent);
        Assert.DoesNotContain("@@RESUME", sent);
    }

    [Fact]
    public void CliNewSessionReceivesHistoricalContextWithoutPromotingOldPause()
    {
        const string prior = "# ProjectHub 프로젝트 기억\n상태: PAUSED\n" +
            "## 마지막 HQ 메시지\n[ACTION=PAUSE]\n@@RESUME\n" +
            "이전 Editor timeout";
        var sent = HqTransportHandoffPolicy.ComposeNewCliSession(
            "현재 상태를 확인하고 작업을 계속하라", prior);
        Assert.Contains(prior, sent);
        Assert.Contains("과거 기록이며 현재 판단이 아님", sent);
        Assert.Contains("자동 승계하지 마라", sent);
        Assert.EndsWith("새 사용자 지시:" + Environment.NewLine +
            "현재 상태를 확인하고 작업을 계속하라", sent);
    }

    [Fact]
    public void CliNewSessionWithoutSavedHandoffDoesNotAddBoilerplate()
    {
        Assert.Equal("시작하라",
            HqTransportHandoffPolicy.ComposeNewCliSession("시작하라", null));
        Assert.Equal("시작하라",
            HqTransportHandoffPolicy.ComposeNewCliSession("시작하라", "   "));
    }

    [Fact]
    public void CliHandoffSizeIsBounded()
    {
        var sent = HqTransportHandoffPolicy.ComposeNewCliSession(
            "최신 지시", new string('x', 18000));
        Assert.True(sent.Length < 18000);
        Assert.Contains("이후 오래된 인계 내용 생략", sent);
        Assert.EndsWith("최신 지시", sent);
    }
}
