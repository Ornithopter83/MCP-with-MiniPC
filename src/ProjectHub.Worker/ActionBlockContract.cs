using System.Collections.ObjectModel;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record ActionBlock(
    string Name,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Fields,
    string Body,
    string RawText,
    IReadOnlyList<string> Errors,
    string? JsonPayload = null,
    string? GotoTarget = null)
{
    public bool IsValid => Errors.Count == 0;

    public string? GetSingle(string fieldName) =>
        Fields.TryGetValue(fieldName, out var values) && values.Count > 0
            ? values[0]
            : null;

    public IReadOnlyList<string> GetMany(string fieldName) =>
        Fields.TryGetValue(fieldName, out var values)
            ? values
            : Array.Empty<string>();
}

public sealed record ActionBlockParseResult(
    IReadOnlyList<ActionBlock> Actions,
    IReadOnlyList<string> Errors)
{
    public IReadOnlyList<ActionBlock> ValidActions =>
        Actions.Where(action => action.IsValid).ToArray();

    public bool HasErrors =>
        Errors.Count > 0 || Actions.Any(action => !action.IsValid);
}

public static class ActionBlockContract
{
    private static readonly HashSet<string> HqActionNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "WORK", "PAUSE", "END"
        };

    private static readonly HashSet<string> ManagerActionNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "DISPATCH", "PAUSE", "REPORT"
        };

    private static readonly HashSet<string> ResultActionNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "RESULT"
        };

    public static ActionBlockParseResult ParseHq(string? rawMessage) =>
        ParseJsonEnvelope("HQ", rawMessage, HqActionNames);

    public static ActionBlockParseResult ParseManager(string? rawMessage) =>
        ParseJsonEnvelope("MANAGER", rawMessage, ManagerActionNames);

    public static ActionBlockParseResult ParseWork(string? rawMessage) =>
        ParseJsonEnvelope("WORK", rawMessage, ResultActionNames);

    public static ActionBlockParseResult ParseQa(string? rawMessage) =>
        ParseJsonEnvelope("QA", rawMessage, ResultActionNames);

    public static ActionBlockParseResult ParseHigh(string? rawMessage) =>
        ParseJsonEnvelope("HIGH", rawMessage, ResultActionNames);

    public static ActionBlockParseResult ParseRole(
        string role,
        string? rawMessage) =>
        role.Trim().ToUpperInvariant() switch
        {
            "HQ" => ParseHq(rawMessage),
            "MANAGER" => ParseManager(rawMessage),
            "WORK" => ParseWork(rawMessage),
            "QA" => ParseQa(rawMessage),
            "HIGH" => ParseHigh(rawMessage),
            _ => new(
                Array.Empty<ActionBlock>(),
                new[] { "ROLE_UNKNOWN" })
        };

    private static ActionBlockParseResult ParseJsonEnvelope(
        string role,
        string? rawMessage,
        IReadOnlySet<string> allowedActions)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return new(Array.Empty<ActionBlock>(), new[] { "ACTION_OUTPUT_EMPTY" });

        var normalized = rawMessage
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var significant = lines
            .Select((line, index) => new { line = line.Trim(), index })
            .Where(item => item.line.Length > 0)
            .ToArray();

        if (significant.Length == 0)
            return new(Array.Empty<ActionBlock>(), new[] { "ACTION_OUTPUT_EMPTY" });

        var cursor = 0;
        if (significant[cursor].line.StartsWith(
                "[KEY=",
                StringComparison.Ordinal))
        {
            cursor++;
        }

        string? gotoTarget = null;
        if (cursor < significant.Length &&
            TryReadGoto(significant[cursor].line, out var parsedGoto))
        {
            gotoTarget = parsedGoto;
            cursor++;
        }

        if (cursor >= significant.Length ||
            !TryReadActionStart(
                significant[cursor].line,
                out var actionName))
        {
            return new(
                Array.Empty<ActionBlock>(),
                new[] { "ACTION_BLOCK_NOT_FOUND" });
        }

        var actionLineIndex = significant[cursor].index;
        var responseOkIndex = Array.FindIndex(
            lines,
            actionLineIndex + 1,
            line => string.Equals(
                line.Trim(),
                "[RESPONSE=OK]",
                StringComparison.Ordinal));

        var jsonEnd = responseOkIndex >= 0
            ? responseOkIndex
            : lines.Length;

        var jsonPayload = string.Join(
                Environment.NewLine,
                lines[(actionLineIndex + 1)..jsonEnd])
            .Trim();

        var errors = new List<string>();
        if (!allowedActions.Contains(actionName))
            errors.Add("ACTION_UNKNOWN");

        if (responseOkIndex >= 0 &&
            lines[(responseOkIndex + 1)..]
                .Any(line => !string.IsNullOrWhiteSpace(line)))
        {
            errors.Add("CONTENT_AFTER_RESPONSE_OK");
        }

        if (string.IsNullOrWhiteSpace(jsonPayload))
            errors.Add("JSON_REQUIRED");

        var body = string.Empty;
        IReadOnlyDictionary<string, IReadOnlyList<string>> fields =
            EmptyFields();

        if (errors.Count == 0)
        {
            try
            {
                using var document = JsonDocument.Parse(jsonPayload);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("JSON_OBJECT_REQUIRED");
                }
                else
                {
                    if (root.TryGetProperty("action", out _))
                        errors.Add("JSON_ACTION_FIELD_FORBIDDEN");

                    fields = BuildFields(root);
                    ValidateRole(
                        role,
                        actionName,
                        root,
                        errors,
                        out body);
                }
            }
            catch (JsonException)
            {
                errors.Add("JSON_INVALID");
            }
        }

        var action = new ActionBlock(
            actionName.ToUpperInvariant(),
            fields,
            body,
            normalized.Trim(),
            errors.Distinct(StringComparer.Ordinal).ToArray(),
            jsonPayload,
            gotoTarget);

        return new(
            new[] { action },
            action.Errors.Select(error => $"{action.Name}:{error}").ToArray());
    }

    private static void ValidateRole(
        string role,
        string actionName,
        JsonElement root,
        List<string> errors,
        out string body)
    {
        body = string.Empty;

        switch (role)
        {
            case "HQ":
                ValidateHq(actionName, root, errors, out body);
                return;

            case "MANAGER":
                ValidateManager(actionName, root, errors, out body);
                return;

            case "WORK":
                ValidateResult(
                    root,
                    new[] { "completed", "blocked" },
                    errors,
                    out body);
                return;

            case "QA":
                ValidateResult(
                    root,
                    new[] { "completed", "blocked" },
                    errors,
                    out body);
                return;

            case "HIGH":
                ValidateResult(
                    root,
                    new[] { "verified", "modified", "incomplete" },
                    errors,
                    out body);
                if (TryGetString(root, "status", out var status) &&
                    string.Equals(
                        status,
                        "modified",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var paths = GetStringArray(root, "changedPaths");
                    if (paths.Count == 0)
                    {
                        errors.Add("CHANGED_PATHS_REQUIRED");
                    }
                    else if (paths.Any(path => !IsSafeRelativePath(path)))
                    {
                        errors.Add("CHANGED_PATH_INVALID");
                    }
                }
                return;
        }

        errors.Add("ROLE_UNKNOWN");
    }

    private static void ValidateHq(
        string actionName,
        JsonElement root,
        List<string> errors,
        out string body)
    {
        body = string.Empty;

        if (string.Equals(
                actionName,
                "WORK",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!root.TryGetProperty("milestone", out var milestone) ||
                milestone.ValueKind != JsonValueKind.Object)
            {
                errors.Add("MILESTONE_OBJECT_REQUIRED");
                return;
            }

            if (TryGetString(milestone, "goal", out var goal))
                body = goal;
            return;
        }

        if (string.Equals(
                actionName,
                "PAUSE",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                actionName,
                "END",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetString(root, "message", out body))
                errors.Add("MESSAGE_REQUIRED");
        }
    }

    private static void ValidateManager(
        string actionName,
        JsonElement root,
        List<string> errors,
        out string body)
    {
        body = string.Empty;

        if (string.Equals(
                actionName,
                "PAUSE",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetString(root, "message", out body))
                errors.Add("MESSAGE_REQUIRED");
            return;
        }

        if (string.Equals(
                actionName,
                "REPORT",
                StringComparison.OrdinalIgnoreCase))
        {
            ValidateStatus(
                root,
                new[] { "completed", "partial", "blocked" },
                errors);
            if (!TryGetString(root, "summary", out body))
                errors.Add("SUMMARY_REQUIRED");
            ValidateOptionalStringArray(root, "issues", errors);
            return;
        }

        if (!string.Equals(
                actionName,
                "DISPATCH",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ValidateIdArray(root, "workItemIds", errors);
        ValidateIdArray(root, "resourceIds", errors);

        if (!root.TryGetProperty("mechanical", out var mechanical) ||
            mechanical.ValueKind != JsonValueKind.Array)
        {
            errors.Add("MECHANICAL_ARRAY_REQUIRED");
            return;
        }

        foreach (var item in mechanical.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add("MECHANICAL_ITEM_INVALID");
                continue;
            }

            if (!TryGetString(item, "operation", out var operation) ||
                !new[] { "BUILD", "RUN", "PUBLISH" }.Contains(
                    operation,
                    StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("OPERATION_INVALID");
            }

            if (!TryGetString(item, "command", out _))
                errors.Add("COMMAND_REQUIRED");
        }
    }

    private static void ValidateResult(
        JsonElement root,
        IReadOnlyCollection<string> allowedStatuses,
        List<string> errors,
        out string body)
    {
        body = string.Empty;
        ValidateStatus(root, allowedStatuses, errors);

        if (!TryGetString(root, "summary", out body))
            errors.Add("SUMMARY_REQUIRED");

        ValidateOptionalStringArray(root, "changedPaths", errors);
        ValidateOptionalStringArray(root, "issues", errors);
    }

    private static void ValidateStatus(
        JsonElement root,
        IReadOnlyCollection<string> allowedStatuses,
        List<string> errors)
    {
        if (!TryGetString(root, "status", out var status))
        {
            errors.Add("STATUS_REQUIRED");
            return;
        }

        if (!allowedStatuses.Contains(
                status,
                StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("STATUS_INVALID");
        }
    }

    private static void ValidateIdArray(
        JsonElement root,
        string propertyName,
        List<string> errors)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            errors.Add(propertyName.ToUpperInvariant() + "_REQUIRED");
            return;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind is not
                (JsonValueKind.String or JsonValueKind.Number))
            {
                errors.Add(propertyName.ToUpperInvariant() + "_INVALID");
                return;
            }
        }
    }

    private static void ValidateOptionalStringArray(
        JsonElement root,
        string propertyName,
        List<string> errors)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return;

        if (value.ValueKind != JsonValueKind.Array ||
            value.EnumerateArray().Any(
                item => item.ValueKind != JsonValueKind.String))
        {
            errors.Add(propertyName.ToUpperInvariant() + "_INVALID");
        }
    }

    public static IReadOnlyList<string> GetStringArray(
        ActionBlock action,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(action.JsonPayload))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(action.JsonPayload);
            return GetStringArray(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<string> GetIdArray(
        ActionBlock action,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(action.JsonPayload))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(action.JsonPayload);
            if (!document.RootElement.TryGetProperty(
                    propertyName,
                    out var value) ||
                value.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return value.EnumerateArray()
                .Where(item => item.ValueKind is
                    JsonValueKind.String or JsonValueKind.Number)
                .Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString()?.Trim() ?? string.Empty
                    : item.GetRawText())
                .Where(item => item.Length > 0)
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public static string? GetJsonString(
        ActionBlock action,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(action.JsonPayload))
            return null;

        try
        {
            using var document = JsonDocument.Parse(action.JsonPayload);
            return TryGetString(
                    document.RootElement,
                    propertyName,
                    out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetStringArray(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>
        BuildFields(JsonElement root)
    {
        var values = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                values[property.Name] = new[]
                {
                    property.Value.GetString() ?? string.Empty
                };
            }
            else if (property.Value.ValueKind == JsonValueKind.Number)
            {
                values[property.Name] = new[]
                {
                    property.Value.GetRawText()
                };
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                var items = property.Value.EnumerateArray()
                    .Where(item => item.ValueKind is
                        JsonValueKind.String or JsonValueKind.Number)
                    .Select(item => item.ValueKind == JsonValueKind.String
                        ? item.GetString() ?? string.Empty
                        : item.GetRawText())
                    .ToArray();
                if (items.Length > 0)
                    values[property.Name] = items;
            }
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(values);
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return false;

        return path.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(part => part != "..");
    }

    private static bool TryReadActionStart(
        string? line,
        out string actionName)
    {
        actionName = string.Empty;
        var trimmed = line?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed) ||
            !trimmed.StartsWith(
                "[ACTION=",
                StringComparison.Ordinal) ||
            !trimmed.EndsWith(']'))
        {
            return false;
        }

        var value = trimmed[8..^1].Trim();
        if (value.Length == 0)
            return false;

        actionName = value.ToUpperInvariant();
        return true;
    }

    private static bool TryReadGoto(
        string? line,
        out string target)
    {
        target = string.Empty;
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            !trimmed.StartsWith(
                "[GOTO",
                StringComparison.OrdinalIgnoreCase) ||
            !trimmed.EndsWith(']'))
        {
            return false;
        }

        var colon = trimmed.IndexOf(':');
        if (colon < 0)
            return false;

        var value = trimmed[(colon + 1)..^1].Trim();
        if (value.Length == 0)
            return false;

        target = value.ToUpperInvariant();
        return true;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>
        EmptyFields() =>
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            new Dictionary<string, IReadOnlyList<string>>(
                StringComparer.OrdinalIgnoreCase));
}
