namespace ProjectHub.Worker;

/// <summary>
/// Keeps only meaningful Worker handoffs and decisions in the dashboard.
/// Detailed command/progress events remain in the append-only transcript.
/// </summary>
public static class WorkerHistoryCardPolicy
{
    public static bool ShouldShowDataFlow(
        string title,
        string? status,
        string? persistenceSource)
    {
        if (title is "전달 데이터" or "재요청 데이터" or "Git 최종화")
            return true;

        // WORK dispatch is followed by the real CLI send card; showing both
        // would double the cards for every concurrently dispatched WORKITEM.
        if (title == "Worker 분배")
            return persistenceSource == "WORKER RESOURCE QUEUE";

        return status is "RECOVERED" or "BLOCKED" or "FAILED" or "FORWARDED";
    }

    public static string DataFlowTitle(
        string title,
        string? status,
        string? persistenceSource,
        string? workItemId)
    {
        if (title is "전달 데이터" or "재요청 데이터")
        {
            var target = persistenceSource?.StartsWith(
                "WORKER → ", StringComparison.Ordinal) == true
                ? persistenceSource["WORKER → ".Length..].Split(' ')[0]
                : "역할";
            var label = target == "WORK" &&
                !string.IsNullOrWhiteSpace(workItemId)
                    ? "WORK #" + workItemId
                    : target;
            return "Worker → " + label +
                (title == "재요청 데이터" ? " 재요청" : " 전달");
        }

        if (title == "Worker 분배")
            return "Worker → RESOURCE 배정";
        if (title == "Git 최종화")
            return "Worker · Git 최종화";
        return status switch
        {
            "RECOVERED" => "Worker · 복구 완료",
            "BLOCKED" or "FAILED" => "Worker · 차단 판단",
            "FORWARDED" => "Worker · 검토 전달",
            _ => title
        };
    }

    public static string NewInvocationReference(string milestoneId, string roleId) =>
        milestoneId + ":" + roleId + ":" + Guid.NewGuid().ToString("N");

    public static bool IsDuplicateCoordinatorResponse(
        MainWindow.WorkerHistoryEvent candidate,
        MainWindow.WorkerHistoryEvent existing) =>
        candidate.StageKey == "Coordinator" &&
        candidate.EventType == "ROLE_RESPONSE" &&
        string.IsNullOrWhiteSpace(candidate.ReferenceId) &&
        existing.StageKey == "Coordinator" &&
        existing.EventType == "ROLE_RESPONSE" &&
        string.IsNullOrWhiteSpace(existing.ReferenceId) &&
        string.Equals(candidate.FullMessage, existing.FullMessage, StringComparison.Ordinal) &&
        Math.Abs((candidate.Timestamp - existing.Timestamp).TotalSeconds) <= 2;
}
