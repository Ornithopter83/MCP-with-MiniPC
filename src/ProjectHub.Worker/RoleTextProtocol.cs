using System.Text.Json;
using System.Text.RegularExpressions;

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
                ReadChangedPaths(structured.GetMany("CHANGED_PATH")),
                (structured.Get("ISSUES") ?? "").Split('\n')
                    .Select(line => line.Trim()).Where(line => line.Length > 0 && line != "없음").ToArray(),
                newErrors);
        }
        if (TryParseLegacyJsonResult(lines, allowedStatuses, out var jsonResult))
            return jsonResult;

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
            ReadChangedPaths(ReadItems(changed)),
            ReadItems(issues),
            errors);
    }

    // Accept middle-field XML closers from older models without changing the
    // report's status or prose. This adapter is deliberately report-only:
    // HQ field parsing and WORK routing remain strict.
    private static string RepairMechanicalEnvelope(string raw)
    {
        if (!raw.Split('\n').Any(line => line.Trim() == "@@REPORT"))
            return raw;

        var lines = raw.Split('\n').ToList();
        string? openField = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            var legacyInline = Regex.Match(trimmed,
                @"^<(STATUS|SUMMARY|CHANGED_PATH|ISSUES)>(.*?)</\1>$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (legacyInline.Success && openField is null)
            {
                lines[i] = "<" + legacyInline.Groups[1].Value.ToUpperInvariant() +
                    ">" + legacyInline.Groups[2].Value + "</>";
                continue;
            }

            if (openField is not null)
            {
                if (trimmed == "</>")
                    openField = null;
                else if (string.Equals(trimmed, "</" + openField + ">",
                    StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "</>";
                    openField = null;
                }
                else
                {
                    var trailing = Regex.Match(trimmed,
                        @"^(.*?)</(STATUS|SUMMARY|CHANGED_PATH|ISSUES)>$",
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                    if (trailing.Success && string.Equals(
                        trailing.Groups[2].Value, openField,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = trailing.Groups[1].Value;
                        lines.Insert(i + 1, "</>");
                        openField = null;
                        i++;
                    }
                }
                continue;
            }

            var opening = Regex.Match(trimmed,
                @"^<(STATUS|SUMMARY|CHANGED_PATH|ISSUES)>$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (opening.Success)
            {
                openField = opening.Groups[1].Value.ToUpperInvariant();
                lines[i] = "<" + openField + ">";
                continue;
            }

            // Some models put the first value on the opening-tag line and
            // the legacy closing tag after the final value on a later line.
            if (!trimmed.EndsWith("</>", StringComparison.Ordinal))
            {
                var openingWithValue = Regex.Match(trimmed,
                    @"^<(STATUS|SUMMARY|CHANGED_PATH|ISSUES)>(.+)$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (openingWithValue.Success)
                {
                    openField = openingWithValue.Groups[1].Value.ToUpperInvariant();
                    lines[i] = "<" + openField + ">";
                    lines.Insert(i + 1, openingWithValue.Groups[2].Value);
                }
            }
        }

        var first = lines.FindIndex(line => !string.IsNullOrWhiteSpace(line));
        if (first >= 0 && lines[first].Trim() == WebCorrelationContract.ResponseOkMarker)
        {
            lines.RemoveAt(first); // stray terminator from an earlier Web turn
            first = lines.FindIndex(line => !string.IsNullOrWhiteSpace(line));
        }
        if (first >= 0 && lines[first].Trim() == "@@REPORT")
            lines.Insert(first, "[ACTION=RESULT]");
        return string.Join("\n", lines);
    }

    // The old ActionBlock wire format used a JSON object after ACTION=RESULT.
    // Read it without invoking legacy role validators, whose HIGH statuses
    // (verified/modified/incomplete) differ from the current text contract.
    // Require explicit status and summary; never infer success from prose.
    private static bool TryParseLegacyJsonResult(
        string[] lines,
        IReadOnlyCollection<string> allowedStatuses,
        out RoleTextResult result)
    {
        result = new("", "", Array.Empty<string>(), Array.Empty<string>(),
            new[] { "JSON_REPORT_INVALID" });
        var significant = lines.Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim()).ToArray();
        if (significant.Length < 2 ||
            !string.Equals(significant[0], "[ACTION=RESULT]",
                StringComparison.OrdinalIgnoreCase) ||
            !significant[1].StartsWith("{", StringComparison.Ordinal))
            return false;

        var responseEnd = Array.FindIndex(lines,
            line => line.Trim() == WebCorrelationContract.ResponseOkMarker);
        if (responseEnd >= 0 &&
            lines.Skip(responseEnd + 1).Any(line => !string.IsNullOrWhiteSpace(line)))
            return true;

        var firstBodyLine = Array.FindIndex(lines,
            line => line.TrimStart().StartsWith("{", StringComparison.Ordinal));
        var bodyEnd = responseEnd >= 0 ? responseEnd : lines.Length;
        if (firstBodyLine < 0 || bodyEnd <= firstBodyLine)
            return true;

        try
        {
            using var document = JsonDocument.Parse(
                string.Join("\n", lines.Skip(firstBodyLine)
                    .Take(bodyEnd - firstBodyLine)));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("status", out var statusJson) ||
                statusJson.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("summary", out var summaryJson) ||
                summaryJson.ValueKind != JsonValueKind.String)
                return true;

            var status = (statusJson.GetString() ?? "").Trim().ToLowerInvariant();
            // Legacy HIGH statuses have a documented, conservative mapping.
            var isHigh = allowedStatuses.Contains("completed",
                StringComparer.OrdinalIgnoreCase) &&
                !allowedStatuses.Contains("in_progress",
                    StringComparer.OrdinalIgnoreCase);
            if (isHigh)
            {
                status = status switch
                {
                    "verified" or "modified" => "completed",
                    "incomplete" => "blocked",
                    _ => status
                };
            }
            var summary = summaryJson.GetString()?.Trim() ?? "";
            if (!allowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase) ||
                summary.Length == 0)
                return true;

            if (!TryReadJsonStrings(root, "changedPaths", out var changed) ||
                !TryReadJsonStrings(root, "issues", out var issues))
                return true;
            result = new(status, summary, ReadChangedPaths(changed),
                issues.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                Array.Empty<string>());
        }
        catch (JsonException)
        {
            // Malformed JSON cannot be promoted to a successful role report.
        }
        return true;
    }

    private static bool TryReadJsonStrings(
        JsonElement root,
        string name,
        out IReadOnlyList<string> values)
    {
        values = Array.Empty<string>();
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind == JsonValueKind.Null)
            return true;
        if (property.ValueKind == JsonValueKind.String)
        {
            values = new[] { property.GetString() ?? "" };
            return true;
        }
        if (property.ValueKind != JsonValueKind.Array ||
            property.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            return false;
        values = property.EnumerateArray()
            .Select(item => item.GetString() ?? "").ToArray();
        return true;
    }

    private static IReadOnlyList<string> ReadChangedPaths(
        IEnumerable<string> fields) =>
        fields.SelectMany(field => Regex.Split(field, @"[,;\r\n]+"))
            .Select(path => path.Trim())
            .Select(path => path.StartsWith("- ", StringComparison.Ordinal)
                ? path[2..].Trim() : path)
            .Select(path =>
            {
                // Model responses sometimes present local changed files as
                // clickable Markdown paths. Only the visible path is relevant.
                var link = Regex.Match(path, @"^\[([^\]]+)\]\([^)]+\)$");
                return link.Success ? link.Groups[1].Value.Trim() : path;
            })
            .Where(path => path.Length > 0 &&
                !string.Equals(path, "없음", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(path, "none", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
