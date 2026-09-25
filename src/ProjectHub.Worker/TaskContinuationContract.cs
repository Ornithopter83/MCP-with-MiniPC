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
        => status is "PAUSED" or "CANCELED" or "DONE" or "DONE_WITH_ERROR";

    public static string BuildHqFollowupInput(
        string status,
        string? lastHqMessage,
        string userFollowup,
        string? projectMemoryPath = null,
        string? eventLogPath = null,
        string? workGraphSummary = null)
    {
        if (!IsResumableStatus(status))
            throw new InvalidOperationException("FOLLOWUP_STATUS_NOT_RESUMABLE");

        var followup = userFollowup?.Trim() ?? string.Empty;
        if (followup.Length == 0)
            throw new InvalidOperationException("FOLLOWUP_EMPTY");

        var previous = string.IsNullOrWhiteSpace(lastHqMessage)
            ? "이전 HQ 메시지 없음"
            : lastHqMessage.Trim();

        var memory = string.IsNullOrWhiteSpace(projectMemoryPath)
            ? string.Empty
            : $"{Environment.NewLine}{Environment.NewLine}프로젝트 기억 파일: {projectMemoryPath.Trim()}" +
              (string.IsNullOrWhiteSpace(eventLogPath)
                  ? string.Empty
                  : $"{Environment.NewLine}이벤트 로그: {eventLogPath.Trim()}") +
              $"{Environment.NewLine}이전 CLI 세션을 사용할 수 없으면 프로젝트 기억 파일과 이벤트 로그를 관제 문맥 복구에 사용한다.";

        var graph = string.IsNullOrWhiteSpace(workGraphSummary)
            ? string.Empty
            : $"{Environment.NewLine}{Environment.NewLine}병렬 WorkGraph 현재 상태:{Environment.NewLine}{workGraphSummary.Trim()}";

        return $"이전 작업 상태: {status}{Environment.NewLine}" +
               $"이전 HQ 메시지:{Environment.NewLine}{previous}" +
               memory +
               graph +
               $"{Environment.NewLine}{Environment.NewLine}사용자 추가 요청:{Environment.NewLine}{followup}";
    }
}
