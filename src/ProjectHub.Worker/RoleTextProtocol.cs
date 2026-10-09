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

        var lines = normalized.Split('\n');
        var reportIndex = Array.FindIndex(lines,
            line => line.Trim() == "@@REPORT");
        if (reportIndex >= 0)
            return ParseLooseReport(lines, reportIndex, allowedStatuses);

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

    // Only role RESULTS use the tolerant scanner. HQ assignments, WORKITEM
    // scopes and other machine directives retain their strict parsers.
    // Explicit known opening tags delimit fields; closing tags are optional.
    private static RoleTextResult ParseLooseReport(
        string[] lines,
        int reportIndex,
        IReadOnlyCollection<string> allowedStatuses)
    {
        var errors = new List<string>();
        var header = lines.Take(reportIndex)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 &&
                line != WebCorrelationContract.ResponseOkMarker)
            .ToArray();
        if (header.Any(line => !string.Equals(
                line, "[ACTION=RESULT]", StringComparison.OrdinalIgnoreCase)))
            errors.Add("ACTION_RESULT_REQUIRED");

        var fields = new List<(string Name, string Value)>();
        var outside = new List<string>();
        var buffer = new System.Text.StringBuilder();
        string? current = null;
        string? previousClosed = null;
        var inFence = false;

        void Append(string part)
        {
            if (current is not null)
                buffer.Append(part);
            else if (!string.IsNullOrWhiteSpace(part))
                outside.Add(part.Trim());
        }

        void Finish()
        {
            if (current is not null)
            {
                fields.Add((current, buffer.ToString().Trim()));
                previousClosed = current;
                current = null;
                buffer.Clear();
            }
        }

        for (var i = reportIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed == WebCorrelationContract.ResponseOkMarker && !inFence)
            {
                if (lines.Skip(i + 1).Any(after =>
                    !string.IsNullOrWhiteSpace(after) &&
                    after.Trim() != WebCorrelationContract.ResponseOkMarker))
                    errors.Add("CONTENT_AFTER_RESPONSE_OK");
                break;
            }

            if (trimmed.StartsWith(new string((char)96, 3), StringComparison.Ordinal))
            {
                inFence = !inFence;
                Append(line);
                if (current is not null)
                    buffer.Append('\n');
                continue;
            }
            if (inFence)
            {
                Append(line);
                if (current is not null)
                    buffer.Append('\n');
                continue;
            }

            var matches = Regex.Matches(line,
                @"</>|</?(?:STATUS|SUMMARY|CHANGED_PATHS?|ISSUES?)>",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var position = 0;
            var lastWasClosing = false;
            foreach (Match match in matches)
            {
                var between = line[position..match.Index];
                var tag = match.Value;
                var isClose = tag.StartsWith("</", StringComparison.Ordinal);
                var namedClose = isClose && tag != "</>";
                var name = isClose
                    ? (namedClose ? tag[2..^1] : current ?? "")
                    : tag[1..^1];
                name = name.ToUpperInvariant() switch
                {
                    "CHANGED_PATHS" => "CHANGED_PATH",
                    "ISSUE" => "ISSUES",
                    var field => field
                };

                // Keep inline quotations as prose. A tag at a line boundary
                // or adjoining a recognized close/open tag is a delimiter.
                var atLineStart = string.IsNullOrWhiteSpace(line[..match.Index]);
                var atLineEnd = string.IsNullOrWhiteSpace(
                    line[(match.Index + match.Length)..]);
                var adjacentToClosedTag = lastWasClosing &&
                    string.IsNullOrWhiteSpace(between);
                var explicitInlinePair = !isClose &&
                    line[(match.Index + match.Length)..].Contains(
                        "</" + name + ">", StringComparison.OrdinalIgnoreCase);
                var structural = isClose || atLineStart || atLineEnd ||
                    adjacentToClosedTag || explicitInlinePair ||
                    (position > 0 && string.IsNullOrWhiteSpace(between));
                if (!structural)
                {
                    Append(between + tag);
                    position = match.Index + match.Length;
                    lastWasClosing = false;
                    continue;
                }

                Append(between);
                position = match.Index + match.Length;
                if (isClose)
                {
                    if (current is null)
                    {
                        if (namedClose && !string.Equals(name,
                            previousClosed, StringComparison.OrdinalIgnoreCase))
                            errors.Add("MIDDLE_FIELD_UNEXPECTED_CLOSE: " + name);
                    }
                    else if (namedClose &&
                        !string.Equals(current, name,
                            StringComparison.OrdinalIgnoreCase))
                        errors.Add("MIDDLE_FIELD_MISMATCH: " +
                            current + "/" + name);
                    else
                        Finish();
                    lastWasClosing = true;
                }
                else
                {
                    Finish(); // omitted close: next opening ends this field
                    current = name;
                    previousClosed = null;
                    lastWasClosing = false;
                }
            }
            Append(line[position..]);
            if (current is not null)
                buffer.Append('\n');
        }

        Finish();
        var statuses = fields.Where(field => field.Name == "STATUS")
            .Select(field => field.Value.Trim().ToLowerInvariant())
            .ToArray();
        var status = statuses.FirstOrDefault() ?? string.Empty;
        if (statuses.Length == 0 ||
            statuses.Any(value => !string.Equals(value, status,
                StringComparison.OrdinalIgnoreCase)) ||
            !allowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            errors.Add("STATUS");

        var summaries = fields.Where(field => field.Name == "SUMMARY")
            .Select(field => field.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (outside.Count > 0)
            summaries.Insert(0, string.Join("\n", outside));
        var summary = string.Join("\n", summaries);
        if (string.IsNullOrWhiteSpace(summary))
            errors.Add("SUMMARY");

        var changedPaths = ReadChangedPaths(fields
            .Where(field => field.Name == "CHANGED_PATH")
            .Select(field => field.Value));
        var issues = fields.Where(field => field.Name == "ISSUES")
            .SelectMany(field => field.Value.Split('\n'))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0 &&
                !string.Equals(value, "없음", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new(status, summary, changedPaths, issues,
            errors.Distinct(StringComparer.Ordinal).ToArray());
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
            var changedPaths = ReadChangedPaths(changed);
            if (isHigh &&
                string.Equals(statusJson.GetString(), "modified",
                    StringComparison.OrdinalIgnoreCase) &&
                changedPaths.Count == 0)
                return true; // Preserve the legacy modified-path requirement.
            result = new(status, summary, changedPaths,
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
