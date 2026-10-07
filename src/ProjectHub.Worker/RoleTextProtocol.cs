namespace ProjectHub.Worker;

internal sealed record RoleTextResult(
    string Status,
    string Summary,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

internal static class RoleTextProtocol
{
    public static RoleTextResult ParseWork(string? raw) =>
        Parse(raw, new[] { "completed", "blocked" });

    public static RoleTextResult ParseQa(string? raw) =>
        Parse(raw, new[] { "passed", "issue", "blocked" });

    public static RoleTextResult ParseHigh(string? raw) =>
        Parse(raw, new[] { "completed", "blocked" });

    public static string BuildResult(
        string status,
        string summary,
        IReadOnlyCollection<string>? changedPaths = null,
        IReadOnlyCollection<string>? issues = null)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("[ACTION=RESULT]");
        builder.AppendLine("STATUS: " + status.Trim());
        builder.AppendLine();
        builder.AppendLine("@@SUMMARY");
        builder.AppendLine(summary?.Trim() ?? string.Empty);
        builder.AppendLine();
        builder.AppendLine("@@CHANGED_PATHS");
        AppendItems(builder, changedPaths);
        builder.AppendLine();
        builder.AppendLine("@@ISSUES");
        AppendItems(builder, issues);
        builder.Append(WebCorrelationContract.ResponseOkMarker);
        return builder.ToString();
    }

    private static RoleTextResult Parse(
        string? raw,
        IReadOnlyCollection<string> allowedStatuses)
    {
        var normalized = (raw ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        var errors = new List<string>();
        if (normalized.Length == 0)
            return new("", "", Array.Empty<string>(), Array.Empty<string>(), new[] { "RESULT_EMPTY" });

        var lines = normalized.Split('\n');
        var significant = lines
            .Select((line, index) => new { Text = line.Trim(), Index = index })
            .Where(item => item.Text.Length > 0)
            .ToArray();
        if (significant.Length == 0 ||
            !string.Equals(significant[0].Text, "[ACTION=RESULT]", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ACTION_RESULT_REQUIRED");
        }

        var status = string.Empty;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("STATUS:", StringComparison.OrdinalIgnoreCase))
                continue;
            status = trimmed["STATUS:".Length..].Trim().ToLowerInvariant();
            break;
        }

        if (!allowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            errors.Add("STATUS");

        var sections = ReadSections(lines);
        sections.TryGetValue("SUMMARY", out var summary);
        sections.TryGetValue("CHANGED_PATHS", out var changed);
        sections.TryGetValue("ISSUES", out var issues);

        if (string.IsNullOrWhiteSpace(summary))
            errors.Add("SUMMARY");

        return new(
            status,
            summary?.Trim() ?? string.Empty,
            ReadItems(changed),
            ReadItems(issues),
            errors);
    }

    private static Dictionary<string, string> ReadSections(string[] lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var buffer = new List<string>();

        void Flush()
        {
            if (current is null)
                return;
            result[current] = string.Join("\n", buffer).Trim();
            buffer.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("@@", StringComparison.Ordinal))
            {
                Flush();
                current = trimmed[2..].Trim().ToUpperInvariant();
                continue;
            }

            if (current is not null &&
                !string.Equals(trimmed, WebCorrelationContract.ResponseOkMarker, StringComparison.Ordinal))
            {
                buffer.Add(line);
            }
        }

        Flush();
        return result;
    }

    private static IReadOnlyList<string> ReadItems(string? content) =>
        (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !string.Equals(line, "없음", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.StartsWith("- ", StringComparison.Ordinal) ? line[2..].Trim() : line)
            .Where(line => line.Length > 0)
            .ToArray();

    private static void AppendItems(
        System.Text.StringBuilder builder,
        IReadOnlyCollection<string>? items)
    {
        var values = (items ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
        if (values.Length == 0)
        {
            builder.AppendLine("없음");
            return;
        }

        foreach (var value in values)
            builder.AppendLine("- " + value);
    }
}
