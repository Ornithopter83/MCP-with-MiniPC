namespace ProjectHub.Worker;

public sealed record CoordinatorContinuationState(
    string JobId,
    string WorkingDirectory,
    WorkerAiRoleSettings Coordinator,
    WorkerAiRoleSettings Implementer,
    string? CoordinatorSessionId,
    string? WorkSessionId,
    string Status,
    string LastHqMessage,
    WorkerAiRoleSettings? HighLevel = null);

public static class TaskContinuationContract
{
    public static bool IsResumableStatus(string? status)
        => status is "PAUSED";

    public static bool IsFreshStartStatus(string? status)
        => status is "DONE" or "DONE_WITH_ERROR";

    public static bool CanAcceptFollowupStatus(string? status)
        => IsResumableStatus(status) || IsFreshStartStatus(status);

    public static bool CanEditTaskConfiguration(
        bool executionActive,
        bool canceling,
        bool taskHistoryVisible)
        => !executionActive && !canceling && !taskHistoryVisible;

    public static string BuildHqFollowupInput(
        string status,
        string? lastHqMessage,
        string userFollowup)
    {
        if (!IsResumableStatus(status))
            throw new InvalidOperationException("FOLLOWUP_STATUS_NOT_RESUMABLE");

        var followup = userFollowup?.Trim() ?? string.Empty;
        if (followup.Length == 0)
            throw new InvalidOperationException("FOLLOWUP_EMPTY");

        _ = lastHqMessage;
        return $"사용자 추가 요청:{Environment.NewLine}{followup}";
    }
}
