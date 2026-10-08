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
        Parse(raw, new[] { "completed", "in_progress", "blocked" });

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
        // All machine-readable response fields are middle fields inside one
        // major @@REPORT section; issue detail remains opaque free text.
        var b = new System.Text.StringBuilder();
        b.AppendLine("[ACTION=RESULT]");
        b.AppendLine();
        b.AppendLine("@@REPORT");
        b.AppendLine("<STATUS>" + status.Trim() + "</>");
        AppendMultiline(b, "SUMMARY", summary);
        foreach (var path in changedPaths ?? Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(path))
                b.AppendLine("<CHANGED_PATH>" + path.Trim() + "</>");
        AppendMultiline(b, "ISSUES",
            string.Join("\n", (issues ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))));
        b.Append(WebCorrelationContract.ResponseOkMarker);
        return b.ToString();
    }

    private static void AppendMultiline(
        System.Text.StringBuilder b, string name, string? body)
    {
        b.AppendLine("<" + name + ">");
        b.AppendLine(string.IsNullOrWhiteSpace(body) ? "없음" : body.Trim());
        b.AppendLine("</>");
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

        var lines = RepairMechanicalEnvelope(normalized).Split('\n');
        if (lines.Any(line => line.Trim() == "@@REPORT"))
        {
            var index = Array.FindIndex(lines, line => line.Trim() == "@@REPORT");
            var payload = string.Join("\n", lines.Skip(index + 1)
                .TakeWhile(line => line.Trim() != WebCorrelationContract.ResponseOkMarker));
            if (!StructuredRoleFields.TryParse(payload, out var structured, out var fieldError))
                return new("", normalized, Array.Empty<string>(), Array.Empty<string>(),
                    new[] { fieldError });
            var newErrors = new List<string>();
            var leading = lines.Take(index).Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim()).ToArray();
            if (leading.Length != 1 || leading[0] != "[ACTION=RESULT]")
                newErrors.Add("ACTION_RESULT_REQUIRED");
            var statusValue = (structured.Get("STATUS") ?? "").ToLowerInvariant();
            if (!allowedStatuses.Contains(statusValue, StringComparer.OrdinalIgnoreCase))
                newErrors.Add("STATUS");
            if (string.IsNullOrWhiteSpace(structured.Get("SUMMARY")))
                newErrors.Add("SUMMARY");
            if (structured.Names.Any(name => name is not
                    ("STATUS" or "SUMMARY" or "CHANGED_PATH" or "ISSUES")))
                newErrors.Add("REPORT_UNKNOWN_FIELD");
            return new(statusValue, structured.Get("SUMMARY") ?? "",
                structured.GetMany("CHANGED_PATH").Where(x => x.Length > 0).ToArray(),
                (structured.Get("ISSUES") ?? "").Split('\n')
                    .Select(line => line.Trim()).Where(line => line.Length > 0 && line != "없음").ToArray(),
                newErrors);
        }
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

    // Repair deterministic *formatting* mistakes only. Never infer status
    // or change the report's evidence, prose, or requested actions.
    private static string RepairMechanicalEnvelope(string raw)
    {
        if (!raw.Split('\n').Any(line => line.Trim() == "@@REPORT"))
            return raw;
        var lines = raw.Split('\n').ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim() is "</STATUS>" or "</SUMMARY>" or
                "</CHANGED_PATH>" or "</ISSUES>")
                lines[i] = "</>";
        }

        var first = lines.FindIndex(line => !string.IsNullOrWhiteSpace(line));
        if (first >= 0 && lines[first].Trim() == WebCorrelationContract.ResponseOkMarker)
        {
            lines.RemoveAt(first); // stray terminator from a previous Web turn
            first = lines.FindIndex(line => !string.IsNullOrWhiteSpace(line));
        }
        if (first >= 0 && lines[first].Trim() == "@@REPORT")
            lines.Insert(first, "[ACTION=RESULT]");
        return string.Join("\n", lines);
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
