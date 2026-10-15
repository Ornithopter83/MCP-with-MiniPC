namespace ProjectHub.Worker;

/// <summary>
/// An HQ WEB binding remains in its browser conversation. Replaying a
/// historical PAUSE/RESUME handoff on each followup can make HQ mistake a
/// stale decision for the current Editor state. CLI HQ may start with no
/// session, so only that transport receives the saved project handoff.
/// </summary>
internal static class HqTransportHandoffPolicy
{
    private const int MaxSavedHandoffCharacters = 16000;

    internal static string ComposeWebFollowup(string userMessage) =>
        userMessage;

    internal static string ComposeNewCliSession(
        string userMessage, string? savedHandoff)
    {
        if (string.IsNullOrWhiteSpace(savedHandoff))
            return userMessage;

        var handoff = savedHandoff.Trim();
        if (handoff.Length > MaxSavedHandoffCharacters)
            handoff = handoff[..MaxSavedHandoffCharacters] +
                Environment.NewLine + "(이후 오래된 인계 내용 생략)";

        return "CLI HQ 새 세션 참고 인계 (과거 기록이며 현재 판단이 아님):" +
            Environment.NewLine +
            "이전 PAUSE/END, Editor 연결 상태 또는 검증 실패를 현재 상태로 " +
            "자동 승계하지 마라. 새 사용자 지시와 현재 확인된 증거를 우선하라." +
            Environment.NewLine + Environment.NewLine +
            handoff +
            Environment.NewLine + Environment.NewLine +
            "새 사용자 지시:" + Environment.NewLine +
            userMessage;
    }
}
