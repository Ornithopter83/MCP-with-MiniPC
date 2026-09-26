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

        _ = lastHqMessage;

        var sections = new List<string>();

        if (!string.IsNullOrWhiteSpace(projectMemoryPath))
        {
            var memory = $"프로젝트 기억 파일: {projectMemoryPath.Trim()}";
            if (!string.IsNullOrWhiteSpace(eventLogPath))
                memory += $"{Environment.NewLine}이벤트 로그: {eventLogPath.Trim()}";
            sections.Add(memory);
        }

        if (!string.IsNullOrWhiteSpace(workGraphSummary))
            sections.Add($"WorkGraph 현재 상태:{Environment.NewLine}{workGraphSummary.Trim()}");

        sections.Add($"사용자 추가 요청:{Environment.NewLine}{followup}");
        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }
}
