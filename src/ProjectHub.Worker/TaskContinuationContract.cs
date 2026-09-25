namespace ProjectHub.Worker;

public sealed record CoordinatorContinuationState(
    string JobId,
    string WorkingDirectory,
    WorkerAiRoleSettings Coordinator,
    WorkerAiRoleSettings Implementer,
    string? CoordinatorSessionId,
    string? WorkSessionId,
    string Status,
    string LastHqMessage);

public static class TaskContinuationContract
{
    public static bool IsResumableStatus(string? status)
        => status is "PAUSED" or "DONE" or "DONE_WITH_ERROR";

    public static string BuildHqFollowupInput(string status, string? lastHqMessage, string userFollowup)
    {
        if (!IsResumableStatus(status))
            throw new InvalidOperationException("FOLLOWUP_STATUS_NOT_RESUMABLE");

        var followup = userFollowup?.Trim() ?? string.Empty;
        if (followup.Length == 0)
            throw new InvalidOperationException("FOLLOWUP_EMPTY");

        var previous = string.IsNullOrWhiteSpace(lastHqMessage)
            ? "이전 HQ 메시지 없음"
            : lastHqMessage.Trim();

        return $"이전 작업 상태: {status}{Environment.NewLine}" +
               $"이전 HQ 메시지:{Environment.NewLine}{previous}{Environment.NewLine}{Environment.NewLine}" +
               $"사용자 추가 요청:{Environment.NewLine}{followup}";
    }
}
