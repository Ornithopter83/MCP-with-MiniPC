using System.Collections.ObjectModel;

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
        if (title is "전달 데이터" or "재요청 데이터" or "Git 최종화" or "HQ 응답 수신")
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
        if (title == "HQ 응답 수신")
            return "Worker · HQ 응답 수신";
        return status switch
        {
            "RECOVERED" => "Worker · 복구 완료",
            "BLOCKED" or "FAILED" => "Worker · 차단 판단",
            "FORWARDED" => "Worker · 검토 전달",
            _ => title
        };
    }

    public static void Publish(
        ObservableCollection<MainWindow.WorkerHistoryEvent> history,
        MainWindow.WorkerHistoryEvent item)
    {
        var key = HistoryCardKey(item);
        for (var index = history.Count - 1; index >= 0; index--)
        {
            var existing = history[index];
            if ((key is not null &&
                 string.Equals(HistoryCardKey(existing), key, StringComparison.Ordinal)) ||
                IsDuplicateCoordinatorResponse(item, existing))
            {
                history[index] = item;
                if (index != history.Count - 1)
                    history.Move(index, history.Count - 1);
                return;
            }
        }
        history.Add(item);
    }

    /// <summary>
    /// One visible WORK card per invocation. Recent progress is shown in the
    /// compact card while double-clicking exposes the accumulated detail.
    /// The original complete detail remains in the task transcript/JSONL.
    /// </summary>
    public static void AccumulateWorkProgress(
        ObservableCollection<MainWindow.WorkerHistoryEvent> history,
        MainWindow.WorkerHistoryEvent incoming)
    {
        if (incoming.EventType != "ROLE_PROGRESS" ||
            incoming.StageKey != "Implementer" ||
            string.IsNullOrWhiteSpace(incoming.ReferenceId))
        {
            Publish(history, incoming);
            return;
        }

        // Async progress callbacks may be delivered after the final response.
        // Never recreate a RUNNING card for an already completed invocation.
        if (history.Any(item =>
            item.EventType == "ROLE_RESPONSE" &&
            item.StageKey == incoming.StageKey &&
            string.Equals(item.ReferenceId, incoming.ReferenceId, StringComparison.Ordinal)))
            return;

        var previous = history.LastOrDefault(item =>
            item.EventType == "ROLE_PROGRESS" &&
            item.StageKey == incoming.StageKey &&
            string.Equals(item.ReferenceId, incoming.ReferenceId, StringComparison.Ordinal));
        var fullMessage = previous is null
            ? incoming.FullMessage
            : previous.FullMessage + Environment.NewLine + incoming.FullMessage;
        const int maxDetailCharacters = 48_000;
        if (fullMessage.Length > maxDetailCharacters)
            fullMessage = "[이전 상세 내용은 작업 로그에 보존됨]" +
                Environment.NewLine + fullMessage[^45_000..];
        var visibleTail = fullMessage.Length > 420 ? fullMessage[^420..] : fullMessage;
        Publish(history, incoming with
        {
            FullMessage = fullMessage,
            Summary = WorkerHistoryCardFormatter.ProgressPreview(visibleTail)
        });
    }

    /// <summary>
    /// Finish the same role card that became visible when the request began.
    /// Results from separate invocations retain their own reference IDs.
    /// </summary>
    public static void PublishRoleResponse(
        ObservableCollection<MainWindow.WorkerHistoryEvent> history,
        MainWindow.WorkerHistoryEvent result)
    {
        if (!string.IsNullOrWhiteSpace(result.ReferenceId))
        {
            for (var i = history.Count - 1; i >= 0; i--)
            {
                var candidate = history[i];
                if (candidate.EventType != "ROLE_PROGRESS" ||
                    candidate.StageKey != result.StageKey ||
                    !string.Equals(candidate.ReferenceId, result.ReferenceId, StringComparison.Ordinal))
                    continue;
                history[i] = result;
                if (i != history.Count - 1)
                    history.Move(i, history.Count - 1);
                return;
            }
        }
        Publish(history, result);
    }

    public static string? HistoryCardKey(MainWindow.WorkerHistoryEvent item)
    {
        var kind = item.EventType == "DATA_FLOW"
            ? item.EventType + "|" + item.Title
            : item.EventType;
        if (!string.IsNullOrWhiteSpace(item.ReferenceId))
            return item.StageKey + "|" + kind + "|REF|" + item.ReferenceId.Trim();
        if (!string.IsNullOrWhiteSpace(item.WorkItemId))
            return item.StageKey + "|" + kind + "|WORK|" + item.WorkItemId.Trim();
        return null;
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
        string.Equals(candidate.Title, existing.Title, StringComparison.Ordinal) &&
        string.Equals(candidate.Status, existing.Status, StringComparison.Ordinal) &&
        string.Equals(candidate.FullMessage, existing.FullMessage, StringComparison.Ordinal) &&
        Math.Abs((candidate.Timestamp - existing.Timestamp).TotalSeconds) <= 2;
}
