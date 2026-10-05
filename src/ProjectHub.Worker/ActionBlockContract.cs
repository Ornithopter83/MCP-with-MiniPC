using System.Collections.ObjectModel;

namespace ProjectHub.Worker;

public sealed record ActionBlock(
    string Name,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Fields,
    string Body,
    string RawText,
    IReadOnlyList<string> Errors)
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
    private static readonly HashSet<string> HqActionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "MILESTONE", "WORK", "RESOURCE", "PAUSE", "END"
    };

    private static readonly HashSet<string> ManagerActionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RUN_WORK", "RUN_RESOURCE", "MECHANICAL",
        "READY_FOR_VALIDATION", "GIT_FINALIZE", "PAUSE"
    };

    public static ActionBlockParseResult ParseHq(string? rawMessage) =>
        Parse(rawMessage, HqActionNames, ValidateHqAction);

    public static ActionBlockParseResult ParseManager(string? rawMessage) =>
        Parse(rawMessage, ManagerActionNames, ValidateManagerAction);

    private static ActionBlockParseResult Parse(
        string? rawMessage,
        IReadOnlySet<string> allowedActions,
        Action<ActionBlock, List<string>> validator)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return new(Array.Empty<ActionBlock>(), new[] { "ACTION_OUTPUT_EMPTY" });

        var lines = rawMessage
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var actions = new List<ActionBlock>();
        var parseErrors = new List<string>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (!TryReadActionStart(lines[index], out var actionName))
                continue;

            var start = index;
            var end = -1;
            var nextStart = -1;

            for (var cursor = index + 1; cursor < lines.Length; cursor++)
            {
                if (string.Equals(
                        lines[cursor].Trim(),
                        "[END_ACTION]",
                        StringComparison.Ordinal))
                {
                    end = cursor;
                    break;
                }

                if (TryReadActionStart(lines[cursor], out _))
                {
                    nextStart = cursor;
                    break;
                }
            }

            if (end < 0)
            {
                var malformedEnd =
                    nextStart >= 0 ? nextStart - 1 : lines.Length - 1;
                var rawBlock = string.Join(
                    Environment.NewLine,
                    lines[start..(malformedEnd + 1)]);

                actions.Add(new ActionBlock(
                    actionName,
                    EmptyFields(),
                    string.Empty,
                    rawBlock,
                    new[] { "ACTION_END_MISSING" }));
                parseErrors.Add($"{actionName}:ACTION_END_MISSING");

                if (nextStart >= 0)
                {
                    index = nextStart - 1;
                    continue;
                }

                break;
            }

            var blockLines = lines[start..(end + 1)];
            var action = ParseBlock(
                actionName,
                blockLines,
                allowedActions,
                validator);
            actions.Add(action);

            foreach (var error in action.Errors)
                parseErrors.Add($"{action.Name}:{error}");

            index = end;
        }

        if (actions.Count == 0)
            parseErrors.Add("ACTION_BLOCK_NOT_FOUND");

        return new(actions, parseErrors);
    }

    private static ActionBlock ParseBlock(
        string actionName,
        IReadOnlyList<string> blockLines,
        IReadOnlySet<string> allowedActions,
        Action<ActionBlock, List<string>> validator)
    {
        var fields =
            new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
        var bodyLines = new List<string>();
        var errors = new List<string>();
        var inBody = false;
        var bodyStarted = false;
        var bodyEnded = false;

        for (var index = 1; index < blockLines.Count - 1; index++)
        {
            var line = blockLines[index];
            var trimmed = line.Trim();

            if (string.Equals(
                    trimmed,
                    "BODY_BEGIN",
                    StringComparison.Ordinal))
            {
                if (inBody || bodyStarted)
                    errors.Add("BODY_BEGIN_DUPLICATED");

                inBody = true;
                bodyStarted = true;
                continue;
            }

            if (string.Equals(
                    trimmed,
                    "BODY_END",
                    StringComparison.Ordinal))
            {
                if (!inBody)
                    errors.Add("BODY_END_WITHOUT_BEGIN");

                inBody = false;
                bodyEnded = true;
                continue;
            }

            if (inBody)
            {
                bodyLines.Add(line);
                continue;
            }

            if (trimmed.Length == 0)
                continue;

            var separator = trimmed.IndexOf(':');
            if (separator <= 0)
            {
                errors.Add("FIELD_LINE_INVALID");
                continue;
            }

            var key = trimmed[..separator].Trim().ToUpperInvariant();
            var value = trimmed[(separator + 1)..].Trim();

            if (!fields.TryGetValue(key, out var values))
            {
                values = new List<string>();
                fields[key] = values;
            }

            values.Add(value);
        }

        if (inBody || (bodyStarted && !bodyEnded))
            errors.Add("BODY_END_MISSING");

        if (!allowedActions.Contains(actionName))
            errors.Add("ACTION_UNKNOWN");

        var readonlyFields = fields.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)
                new ReadOnlyCollection<string>(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        var rawBlock = string.Join(Environment.NewLine, blockLines);
        var action = new ActionBlock(
            actionName.ToUpperInvariant(),
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(
                readonlyFields),
            string.Join(Environment.NewLine, bodyLines).Trim(),
            rawBlock,
            Array.Empty<string>());

        validator(action, errors);
        return action with
        {
            Errors = errors
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ValidateHqAction(
        ActionBlock action,
        List<string> errors)
    {
        switch (action.Name)
        {
            case "MILESTONE":
                Require(action, errors, "MILESTONE_ID");
                Require(action, errors, "TARGET_BRANCH");
                var qa = Require(action, errors, "QA");
                if (qa is not null &&
                    !string.Equals(
                        qa,
                        "YES",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        qa,
                        "NO",
                        StringComparison.OrdinalIgnoreCase))
                    errors.Add("QA_INVALID");
                RequireBody(action, errors);
                break;

            case "WORK":
                var workId = Require(
                    action,
                    errors,
                    "WORK_ITEM_ID");
                if (workId is not null &&
                    (!int.TryParse(workId, out var workNumber) ||
                     workNumber < 10))
                    errors.Add("WORK_ITEM_ID_INVALID");

                if (action.GetMany("WRITE_PATH").Count == 0 ||
                    action.GetMany("WRITE_PATH")
                        .Any(string.IsNullOrWhiteSpace))
                    errors.Add("WRITE_PATH_REQUIRED");

                RequireBody(action, errors);
                break;

            case "RESOURCE":
                var resourceId = Require(
                    action,
                    errors,
                    "RESOURCE_ID");
                if (resourceId is not null &&
                    !string.Equals(
                        resourceId,
                        "0",
                        StringComparison.Ordinal))
                    errors.Add("RESOURCE_ID_INVALID");

                var resourceType = Require(
                    action,
                    errors,
                    "RESOURCE_TYPE");
                if (resourceType is not null &&
                    !string.Equals(
                        resourceType,
                        "IMAGE",
                        StringComparison.OrdinalIgnoreCase))
                    errors.Add("RESOURCE_TYPE_INVALID");

                Require(action, errors, "TARGET_PATH");
                RequireBody(action, errors);
                break;

            case "PAUSE":
            case "END":
                RequireBody(action, errors);
                break;
        }
    }

    private static void ValidateManagerAction(
        ActionBlock action,
        List<string> errors)
    {
        switch (action.Name)
        {
            case "RUN_WORK":
                Require(action, errors, "WORK_ITEM_ID");
                break;

            case "RUN_RESOURCE":
                Require(action, errors, "RESOURCE_ID");
                break;

            case "MECHANICAL":
                var operation = Require(
                    action,
                    errors,
                    "OPERATION");
                if (operation is not null &&
                    !string.Equals(
                        operation,
                        "BUILD",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        operation,
                        "RUN",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        operation,
                        "PUBLISH",
                        StringComparison.OrdinalIgnoreCase))
                    errors.Add("OPERATION_INVALID");

                RequireBody(action, errors);
                break;

            case "PAUSE":
                RequireBody(action, errors);
                break;
        }
    }

    private static string? Require(
        ActionBlock action,
        List<string> errors,
        string fieldName)
    {
        var value = action.GetSingle(fieldName);
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(fieldName + "_REQUIRED");
            return null;
        }

        return value;
    }

    private static void RequireBody(
        ActionBlock action,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(action.Body))
            errors.Add("BODY_REQUIRED");
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
            return false;

        var value = trimmed[8..^1].Trim();
        if (value.Length == 0)
            return false;

        actionName = value.ToUpperInvariant();
        return true;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>
        EmptyFields() =>
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            new Dictionary<string, IReadOnlyList<string>>(
                StringComparer.OrdinalIgnoreCase));
}
