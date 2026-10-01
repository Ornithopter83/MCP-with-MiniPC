using System.Text;

namespace ProjectHub.Worker;

public static class HqSessionRollover
{
    public const long MaxAccumulatedTextBytes = 128 * 1024;
    private const int RecentTerminalItemLimit = 4;
    private const int ResultSummaryCharacterLimit = 3000;

    public static bool ShouldRollover(
        string? sessionId,
        long accumulatedTextBytes)
        => !string.IsNullOrWhiteSpace(sessionId) &&
           accumulatedTextBytes >= MaxAccumulatedTextBytes;

    public static long AddTurnBytes(
        long accumulatedTextBytes,
        string prompt,
        string response)
        => accumulatedTextBytes +
           Encoding.UTF8.GetByteCount(prompt ?? string.Empty) +
           Encoding.UTF8.GetByteCount(response ?? string.Empty);

    public static string BuildHandoff(
        string currentUserRequest,
        WorkGraphSnapshot snapshot,
        string fallbackBaseRef)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var currentBaseRef = snapshot.Items
            .Where(item =>
                item.Kind == WorkItemKind.Integration &&
                item.State == WorkItemState.Completed &&
                !string.IsNullOrWhiteSpace(item.ResultRef))
            .OrderByDescending(item => item.FinishedAtUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.CreatedOrder)
            .Select(item => item.ResultRef)
            .FirstOrDefault() ??
            (string.IsNullOrWhiteSpace(fallbackBaseRef) ? "HEAD" : fallbackBaseRef.Trim());

        var openItems = snapshot.Items
            .Where(item => item.State is
                WorkItemState.Planned or
                WorkItemState.Ready or
                WorkItemState.Running or
                WorkItemState.Blocked)
            .OrderBy(item => item.CreatedOrder)
            .ToArray();

        var terminalItems = snapshot.Items
            .Where(item => item.State is
                WorkItemState.Completed or
                WorkItemState.Failed or
                WorkItemState.Canceled)
            .OrderByDescending(item => item.FinishedAtUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.CreatedOrder)
            .ToArray();

        var recentTerminal = terminalItems
            .Take(RecentTerminalItemLimit)
            .ToArray();
        var recentIds = recentTerminal
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        var builder = new StringBuilder();
        builder.AppendLine("# HQ Session Handoff");
        builder.AppendLine();
        builder.AppendLine("이 문서는 Worker가 현재 WorkGraph에서 기계적으로 생성한 새 HQ 세션용 상태 요약이다.");
        builder.AppendLine("과거 HQ 자연어 대화보다 아래 WorkGraph 사실을 우선한다.");
        builder.AppendLine();
        builder.AppendLine("## 현재 요청");
        builder.AppendLine();
        builder.AppendLine(string.IsNullOrWhiteSpace(currentUserRequest)
            ? "없음"
            : currentUserRequest.Trim());
        builder.AppendLine();
        builder.AppendLine("## WorkGraph");
        builder.AppendLine();
        builder.AppendLine($"jobId: {snapshot.JobId}");
        builder.AppendLine($"revision: {snapshot.Revision}");
        builder.AppendLine($"maxConcurrentWork: {snapshot.MaxConcurrentWork}");
        builder.AppendLine($"currentBaseRef: {currentBaseRef}");
        builder.AppendLine($"open: {openItems.Length}");
        builder.AppendLine($"terminal: {terminalItems.Length}");

        builder.AppendLine();
        builder.AppendLine("## Open WorkItems");
        builder.AppendLine();
        if (openItems.Length == 0)
        {
            builder.AppendLine("없음");
        }
        else
        {
            foreach (var item in openItems)
                AppendOpenItem(builder, item);
        }

        builder.AppendLine();
        builder.AppendLine("## Recent Terminal WorkItems");
        builder.AppendLine();
        if (recentTerminal.Length == 0)
        {
            builder.AppendLine("없음");
        }
        else
        {
            foreach (var item in recentTerminal.OrderBy(item => item.CreatedOrder))
                AppendTerminalItem(builder, item, includeSummary: true);
        }

        var olderTerminal = terminalItems
            .Where(item => !recentIds.Contains(item.Id))
            .OrderBy(item => item.CreatedOrder)
            .ToArray();
        builder.AppendLine();
        builder.AppendLine("## Older Terminal WorkItems");
        builder.AppendLine();
        if (olderTerminal.Length == 0)
        {
            builder.AppendLine("없음");
        }
        else
        {
            foreach (var item in olderTerminal)
                AppendTerminalItem(builder, item, includeSummary: false);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendOpenItem(
        StringBuilder builder,
        WorkItemSnapshot item)
    {
        builder.AppendLine(
            $"- #{item.Id} kind={item.Kind.ToString().ToUpperInvariant()} state={item.State.ToString().ToUpperInvariant()}");
        builder.AppendLine($"  goal: {SingleLine(item.Goal)}");
        builder.AppendLine(
            "  dependencies: " +
            (item.Dependencies.Count == 0 ? "없음" : string.Join(",", item.Dependencies)));
        builder.AppendLine($"  baseRef: {item.BaseRef ?? "없음"}");

        if (item.Checklist is { Count: > 0 })
        {
            builder.AppendLine("  checklist:");
            for (var index = 0; index < item.Checklist.Count; index++)
                builder.AppendLine($"    [{index + 1}] {SingleLine(item.Checklist[index])}");
        }

        if (!string.IsNullOrWhiteSpace(item.BlockCode))
            builder.AppendLine($"  blockCode: {item.BlockCode}");
        if (!string.IsNullOrWhiteSpace(item.BlockDetailCode))
            builder.AppendLine($"  blockDetailCode: {item.BlockDetailCode}");
        if (!string.IsNullOrWhiteSpace(item.FailureCode))
            builder.AppendLine($"  failureCode: {item.FailureCode}");
        if (!string.IsNullOrWhiteSpace(item.ResultRef))
            builder.AppendLine($"  resultRef: {item.ResultRef}");
        if (!string.IsNullOrWhiteSpace(item.ResultSummary))
        {
            builder.AppendLine("  currentReport:");
            builder.AppendLine(Indent(Limit(item.ResultSummary)));
        }

        builder.AppendLine();
    }

    private static void AppendTerminalItem(
        StringBuilder builder,
        WorkItemSnapshot item,
        bool includeSummary)
    {
        builder.Append(
            $"- #{item.Id} kind={item.Kind.ToString().ToUpperInvariant()} state={item.State.ToString().ToUpperInvariant()}");
        if (item.ResultType != WorkItemResultType.None)
            builder.Append($" resultType={WorkItemResultTypeContract.ToToken(item.ResultType)}");
        if (!string.IsNullOrWhiteSpace(item.ResultRef))
            builder.Append($" resultRef={item.ResultRef}");
        if (!string.IsNullOrWhiteSpace(item.FailureCode))
            builder.Append($" failureCode={item.FailureCode}");
        builder.AppendLine();

        if (includeSummary)
        {
            builder.AppendLine($"  goal: {SingleLine(item.Goal)}");
            if (!string.IsNullOrWhiteSpace(item.ResultSummary))
            {
                builder.AppendLine("  result:");
                builder.AppendLine(Indent(Limit(item.ResultSummary)));
            }
        }
    }

    private static string Limit(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= ResultSummaryCharacterLimit
            ? normalized
            : normalized[..ResultSummaryCharacterLimit] + Environment.NewLine + "...(truncated)";
    }

    private static string SingleLine(string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= 1000
            ? normalized
            : normalized[..1000] + "...";
    }

    private static string Indent(string value)
        => string.Join(
            Environment.NewLine,
            value.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n')
                .Select(line => "    " + line));
}
