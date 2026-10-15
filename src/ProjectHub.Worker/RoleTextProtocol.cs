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
    public static RoleTextResult ParseWork(string? raw)
    {
        var parsed = Parse(raw, new[] { "completed", "in_progress", "blocked" });
        if (!parsed.IsValid || !RoleGotoContract.TryReadLeading(raw,
                out var destination, out _) ||
            destination is null or "WORKER")
            return parsed;
        return parsed with { Errors = new[] { "WORK_GOTO_TARGET_INVALID" } };
    }

    public static RoleTextResult ParseHigh(string? raw) =>
        Parse(raw, new[] { "completed", "blocked", "in_progress" });

    public static string BuildResult(
        string status,
        string summary,
        IReadOnlyCollection<string>? changedPaths = null,
        IReadOnlyCollection<string>? issues = null,
        string? gotoTarget = null)
    {
        // All machine-readable response fields are middle fields inside one
        // major @@REPORT section; issue detail remains opaque free text.
        var b = new System.Text.StringBuilder();
        b.AppendLine(RoleGotoContract.Format(
            string.IsNullOrWhiteSpace(gotoTarget) ? "WORKER" : gotoTarget));
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
        var normalized = RoleGotoContract.NormalizeResultHeader(raw).Trim();
        var errors = new List<string>();
        if (normalized.Length == 0)
            return new("", "", Array.Empty<string>(), Array.Empty<string>(), new[] { "RESULT_EMPTY" });

        if (!RoleGotoContract.TryReadLeading(normalized, out _, out var gotoError))
            return new("", "", Array.Empty<string>(), Array.Empty<string>(),
                new[] { gotoError });
        var lines = normalized.Split('\n');
        var reportIndex = Array.FindIndex(lines,
            line => line.Trim() == "@@REPORT");
        if (reportIndex >= 0)
            return ParseLooseReport(lines, reportIndex, allowedStatuses);

        return new("", "", Array.Empty<string>(), Array.Empty<string>(),
            new[] { "REPORT_REQUIRED" });
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
                line != WebCorrelationContract.ResponseOkMarker &&
                !RoleGotoContract.IsGotoLine(line))
            .ToArray();
        if (header.Length > 0)
            errors.Add("REPORT_HEADER_INVALID");

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
        // Untagged prose is preserved, but it cannot replace the explicit
        // SUMMARY field when deciding whether a role report is complete.
        if (summaries.Count == 0)
            errors.Add("SUMMARY");
        if (outside.Count > 0)
            summaries.Insert(0, string.Join("\n", outside));
        var summary = string.Join("\n", summaries);

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
