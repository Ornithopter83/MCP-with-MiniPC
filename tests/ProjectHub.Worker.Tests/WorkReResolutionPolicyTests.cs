using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkReResolutionPolicyTests
{
    [Theory]
    [InlineData("completed", "없음", false)]
    [InlineData("completed", "Godot 인증서 저장소 오류는 있지만 테스트는 PASS", false)]
    [InlineData("completed", "실제 키보드 입력과 사람 승인 미검증", false)]
    [InlineData("completed", "Window 스모크를 추가했지만 이번 작업에서는 실행하지 않았습니다.", true)]
    [InlineData("completed", "추가한 회귀 검증은 미실행", true)]
    [InlineData("completed", "The integration test was not run.", true)]
    [InlineData("blocked", "동작하지 않는 구현이 남아 있습니다.", true)]
    [InlineData("blocked", "index.lock: Permission denied", false)]
    [InlineData("in_progress", "잔여 코드 구현과 테스트가 필요합니다.", true)]
    [InlineData("in_progress", "환경 장애 EPERM으로 멈췄습니다.", false)]
    public void OnlyUnresolvedActionableReportsAreReSolved(
        string status, string issues, bool expected)
    {
        var parsed = RoleTextProtocol.ParseWork(
            RoleTextProtocol.BuildResult(status, "1차 작업 마무리", issues: new[] { issues }));
        Assert.True(parsed.IsValid);
        Assert.Equal(expected, WorkReResolutionPolicy.IsEligible(0, parsed));
    }

    [Fact]
    public void CleanCompletionHasNoReviewEvenWhenSummaryMentionsOldFailure()
    {
        var parsed = RoleTextProtocol.ParseWork(RoleTextProtocol.BuildResult(
            "completed", "이전 실패를 모두 해결했습니다.",
            issues: Array.Empty<string>()));
        Assert.False(WorkReResolutionPolicy.IsEligible(0, parsed));
    }

    [Fact]
    public void AbnormalCliExitAndInvalidProtocolAreNotRepeated()
    {
        var valid = RoleTextProtocol.ParseWork(RoleTextProtocol.BuildResult(
            "blocked", "미해결 구현"));
        Assert.False(WorkReResolutionPolicy.IsEligible(1, valid));
        var invalid = RoleTextProtocol.ParseWork("malformed result");
        Assert.False(WorkReResolutionPolicy.IsEligible(0, invalid));
    }

    [Fact]
    public void ReResolutionReinjectsOriginalMissionAndFirstResultWithoutNewForm()
    {
        var original = "[ACTION=WORK]\nWORK_ID: 628\n" +
                       "@@WRITE_PATH\n- scripts/player.gd\n" +
                       "@@GOAL\nFix collision\n";
        var report = RoleTextProtocol.BuildResult("completed", "Partial implementation",
            issues: new[] { "Window 스모크는 실행하지 않았습니다." });
        var review = WorkReResolutionPolicy.BuildPrompt(original, report, "628");

        Assert.Contains(original.Trim(), review);
        Assert.Contains(report.Trim(), review);
        Assert.Contains("WORKITEM #628", review);
        Assert.Contains("같은 모델·세션·WORK_ID·WRITE_PATH", review);
        Assert.Contains("최대 1회", review);
        Assert.Equal(1, review.Split("=== 첫 번째 마무리 보고서 ===", StringSplitOptions.None).Length - 1);
    }
}
