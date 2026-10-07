using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record HqTextProtocolResult(
    string ActionName,
    string CompatibilityMessage,
    ActionBlockParseResult Parse,
    IReadOnlyList<string> UnknownSections,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0 && !Parse.HasErrors;
}

internal static class HqTextProtocol
{
    private static readonly HashSet<string> KnownSections =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "GOAL",
            "WORK",
            "WORK_GOAL",
            "WORK_INSTRUCTIONS",
            "WORK_COMPLETION",
            "QA",
            "QA_INSTRUCTIONS",
            "RESOURCE",
            "RESOURCE_INSTRUCTIONS",
            "MILESTONE_COMPLETION",
            "VALIDATION",
            "MECHANICAL",
            "HIGH",
            "MANAGER",
            "MESSAGE",
            "RESUME"
        };

    private sealed record Section(
        string Name,
        string Argument,
        string Content,
        string Raw);

    private sealed class WorkBuilder
    {
        public required string Id { get; init; }
        public bool? ReadOnly { get; set; }
        public List<string> WritePaths { get; } = new();
        public string Goal { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public List<string> Completion { get; } = new();
    }

    private sealed class ResourceBuilder
    {
        public string Id { get; set; } = "0";
        public string Type { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
    }

    public static HqTextProtocolResult Parse(string? rawMessage)
    {
        var raw = rawMessage ?? string.Empty;
        var normalized = raw
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length == 0)
            return Invalid("ACTION_OUTPUT_EMPTY");

        var lines = normalized.Split('\n');
        var significant = lines
            .Select((line, index) => new { Text = line.Trim(), Index = index })
            .Where(item => item.Text.Length > 0)
            .ToArray();

        if (significant.Length == 0)
            return Invalid("ACTION_OUTPUT_EMPTY");

        var cursor = 0;
        if (significant[cursor].Text.StartsWith("[KEY=", StringComparison.Ordinal))
            cursor++;

        if (cursor < significant.Length &&
            significant[cursor].Text.StartsWith("[GOTO", StringComparison.OrdinalIgnoreCase))
        {
            cursor++;
        }

        if (cursor >= significant.Length ||
            !TryReadAction(significant[cursor].Text, out var action))
        {
            return Invalid("ACTION");
        }

        if (action is not ("WORK" or "PAUSE" or "END"))
            return Invalid("ACTION");

        var actionLine = significant[cursor].Index;
        var responseOk = Array.FindIndex(
            lines,
            actionLine + 1,
            line => string.Equals(
                line.Trim(),
                WebCorrelationContract.ResponseOkMarker,
                StringComparison.Ordinal));

        var end = responseOk >= 0 ? responseOk : lines.Length;
        var payloadLines = lines[(actionLine + 1)..end];

        var firstSection = Array.FindIndex(
            payloadLines,
            line => line.TrimStart().StartsWith("@@", StringComparison.Ordinal));

        var headerLines = firstSection < 0
            ? payloadLines
            : payloadLines[..firstSection];
        var sectionLines = firstSection < 0
            ? Array.Empty<string>()
            : payloadLines[firstSection..];

        var headers = ReadFields(headerLines);
        var sections = ReadSections(sectionLines);
        var unknown = sections
            .Where(section => !KnownSections.Contains(section.Name))
            .Select(section => section.Raw)
            .ToArray();

        return action switch
        {
            "WORK" => ParseWork(headers, sections, unknown),
            "PAUSE" => ParseControl("PAUSE", sections, unknown),
            "END" => ParseControl("END", sections, unknown),
            _ => Invalid("ACTION")
        };
    }

    private static HqTextProtocolResult ParseWork(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        IReadOnlyList<Section> sections,
        IReadOnlyList<string> unknown)
    {
        var errors = new List<string>();

        var milestoneId = One(headers, "MILESTONE");
        if (string.IsNullOrWhiteSpace(milestoneId))
            errors.Add("MILESTONE");

        var branch = One(headers, "BRANCH");
        if (!string.Equals(branch, "main", StringComparison.Ordinal))
            errors.Add("BRANCH");

        var policy = One(headers, "POLICY");
        if (string.IsNullOrWhiteSpace(policy))
            policy = "DEFAULT";
        if (!string.Equals(policy, "DEFAULT", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(policy, "READ_ONLY_NO_FILE_CHANGES", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("POLICY");
        }

        var entrypoint = One(headers, "ENTRYPOINT");
        if (string.Equals(entrypoint, "NONE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entrypoint, "없음", StringComparison.OrdinalIgnoreCase))
        {
            entrypoint = null;
        }

        var initializeGit = false;
        var initializeGitRaw = One(headers, "GIT_INIT");
        if (!string.IsNullOrWhiteSpace(initializeGitRaw) &&
            !TryReadBool(initializeGitRaw, out initializeGit))
        {
            errors.Add("GIT_INIT");
        }

        var goal = sections
            .FirstOrDefault(section =>
                string.Equals(section.Name, "GOAL", StringComparison.OrdinalIgnoreCase))
            ?.Content.Trim() ?? string.Empty;
        if (goal.Length == 0)
            errors.Add("GOAL");

        var works = new List<WorkBuilder>();
        WorkBuilder? currentWork = null;
        var qaRequired = false;
        var qaInstructions = string.Empty;
        ResourceBuilder? resource = null;
        var milestoneCompletion = new List<string>();
        var validation = new List<string>();
        var mechanical = new List<string>();
        var highInstructions = string.Empty;
        var managerInstructions = string.Empty;

        foreach (var section in sections)
        {
            switch (section.Name.ToUpperInvariant())
            {
                case "WORK":
                {
                    currentWork = new WorkBuilder { Id = section.Argument.Trim() };
                    if (!int.TryParse(currentWork.Id, out var number) || number < 10)
                        errors.Add("WORK.ID");

                    var fields = ReadFields(section.Content.Split('\n'));
                    if (fields.ContainsKey("ORDER"))
                        errors.Add($"WORK {currentWork.Id}.ORDER_FORBIDDEN");

                    if (TryReadBool(One(fields, "READ_ONLY"), out var readOnly))
                        currentWork.ReadOnly = readOnly;
                    else
                        errors.Add($"WORK {currentWork.Id}.READ_ONLY");

                    if (fields.TryGetValue("WRITE_PATH", out var paths))
                    {
                        currentWork.WritePaths.AddRange(
                            paths.Where(path => !string.IsNullOrWhiteSpace(path))
                                .Select(path => path.Trim()));
                    }

                    works.Add(currentWork);
                    break;
                }

                case "WORK_GOAL":
                    if (currentWork is null)
                        errors.Add("WORK_GOAL");
                    else
                        currentWork.Goal = section.Content.Trim();
                    break;

                case "WORK_INSTRUCTIONS":
                    if (currentWork is null)
                        errors.Add("WORK_INSTRUCTIONS");
                    else
                        currentWork.Instructions = section.Content.Trim();
                    break;

                case "WORK_COMPLETION":
                    if (currentWork is null)
                        errors.Add("WORK_COMPLETION");
                    else
                        currentWork.Completion.AddRange(ReadTextItems(section.Content));
                    break;

                case "QA":
                {
                    currentWork = null;
                    var fields = ReadFields(section.Content.Split('\n'));
                    var required = One(fields, "REQUIRED");
                    if (!string.IsNullOrWhiteSpace(required) &&
                        !TryReadBool(required, out qaRequired))
                    {
                        errors.Add("QA.REQUIRED");
                    }
                    break;
                }

                case "QA_INSTRUCTIONS":
                    currentWork = null;
                    qaInstructions = section.Content.Trim();
                    break;

                case "RESOURCE":
                {
                    currentWork = null;
                    resource = new ResourceBuilder
                    {
                        Id = string.IsNullOrWhiteSpace(section.Argument)
                            ? "0"
                            : section.Argument.Trim()
                    };
                    var fields = ReadFields(section.Content.Split('\n'));
                    resource.Type = One(fields, "TYPE") ?? string.Empty;
                    resource.TargetPath = One(fields, "TARGET_PATH") ?? string.Empty;
                    break;
                }

                case "RESOURCE_INSTRUCTIONS":
                    currentWork = null;
                    if (resource is null)
                        errors.Add("RESOURCE");
                    else
                        resource.Instructions = section.Content.Trim();
                    break;

                case "MILESTONE_COMPLETION":
                    currentWork = null;
                    milestoneCompletion.AddRange(ReadTextItems(section.Content));
                    break;

                case "VALIDATION":
                    currentWork = null;
                    validation.AddRange(ReadTextItems(section.Content));
                    break;

                case "MECHANICAL":
                    currentWork = null;
                    mechanical.AddRange(ReadTextItems(section.Content));
                    break;

                case "HIGH":
                    currentWork = null;
                    highInstructions = section.Content.Trim();
                    break;

                case "MANAGER":
                    currentWork = null;
                    managerInstructions = section.Content.Trim();
                    break;
            }
        }

        foreach (var work in works)
        {
            if (work.Goal.Length == 0)
                errors.Add($"WORK {work.Id}.GOAL");
            if (work.ReadOnly != true && work.WritePaths.Count == 0)
                errors.Add($"WORK {work.Id}.WRITE_PATH");
        }

        if (works.GroupBy(work => work.Id, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            errors.Add("WORK.ID_DUPLICATE");
        }

        if (qaRequired && qaInstructions.Length == 0)
            errors.Add("QA_INSTRUCTIONS");

        if (resource is not null)
        {
            if (!string.Equals(resource.Id, "0", StringComparison.Ordinal))
                errors.Add("RESOURCE.ID");
            if (!string.Equals(resource.Type, "image", StringComparison.OrdinalIgnoreCase))
                errors.Add("RESOURCE.TYPE");
            if (resource.TargetPath.Length == 0)
                errors.Add("RESOURCE.TARGET_PATH");
            if (resource.Instructions.Length == 0)
                errors.Add("RESOURCE_INSTRUCTIONS");
        }

        if (errors.Count > 0)
            return Invalid("WORK", unknown, errors);

        var milestone = new Dictionary<string, object?>
        {
            ["id"] = milestoneId,
            ["branch"] = "main",
            ["goal"] = goal,
            ["entrypoint"] = entrypoint,
            ["initializeGitIfMissing"] = initializeGit,
            ["projectPolicy"] = policy,
            ["qa"] = new Dictionary<string, object?>
            {
                ["required"] = qaRequired,
                ["instructions"] = qaInstructions
            },
            ["resource"] = resource is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["id"] = 0,
                    ["type"] = "image",
                    ["targetPath"] = resource.TargetPath,
                    ["instructions"] = resource.Instructions
                },
            ["workItems"] = works.Select(work => new Dictionary<string, object?>
            {
                ["id"] = int.Parse(work.Id),
                ["readOnly"] = work.ReadOnly!.Value,
                ["writePaths"] = work.WritePaths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ["goal"] = work.Goal,
                ["instructions"] = work.Instructions,
                ["completionCriteria"] = work.Completion.ToArray()
            }).ToArray(),
            ["completionCriteria"] = milestoneCompletion.ToArray(),
            ["validation"] = validation.ToArray(),
            ["mechanicalInstructions"] = mechanical.ToArray(),
            ["highInstructions"] = highInstructions,
            ["managerInstructions"] = managerInstructions
        };

        if (unknown.Count > 0)
        {
            milestone["hqUnknownSections"] = string.Join(
                Environment.NewLine + Environment.NewLine,
                unknown);
        }

        var compatibility =
            "[ACTION=WORK]" + Environment.NewLine +
            ProjectHubJson.SerializeIndented(
                new Dictionary<string, object?> { ["milestone"] = milestone });
        var parse = ActionBlockContract.ParseHq(compatibility);
        var parseErrors = parse.HasErrors
            ? parse.Errors.ToArray()
            : Array.Empty<string>();

        return new(
            "WORK",
            compatibility,
            parse,
            unknown,
            parseErrors);
    }

    private static HqTextProtocolResult ParseControl(
        string action,
        IReadOnlyList<Section> sections,
        IReadOnlyList<string> unknown)
    {
        if (unknown.Count > 0)
        {
            return Invalid(
                action,
                unknown,
                new[] { "UNKNOWN_SECTION_REQUIRES_WORK" });
        }

        var message = sections
            .FirstOrDefault(section =>
                string.Equals(section.Name, "MESSAGE", StringComparison.OrdinalIgnoreCase))
            ?.Content.Trim() ?? string.Empty;
        if (message.Length == 0)
            return Invalid(action, unknown, new[] { "MESSAGE" });

        var payload = new Dictionary<string, object?>
        {
            ["message"] = message
        };

        var resume = sections
            .FirstOrDefault(section =>
                string.Equals(section.Name, "RESUME", StringComparison.OrdinalIgnoreCase))
            ?.Content.Trim();
        if (!string.IsNullOrWhiteSpace(resume))
            payload["resumeCondition"] = resume;

        var compatibility =
            $"[ACTION={action}]" + Environment.NewLine +
            ProjectHubJson.SerializeIndented(payload);
        var parse = ActionBlockContract.ParseHq(compatibility);
        var parseErrors = parse.HasErrors
            ? parse.Errors.ToArray()
            : Array.Empty<string>();

        return new(action, compatibility, parse, unknown, parseErrors);
    }

    private static HqTextProtocolResult Invalid(string error) =>
        Invalid(string.Empty, Array.Empty<string>(), new[] { error });

    private static HqTextProtocolResult Invalid(
        string action,
        IReadOnlyList<string> unknown,
        IEnumerable<string> errors)
    {
        var values = errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(
            action,
            string.Empty,
            new ActionBlockParseResult(
                Array.Empty<ActionBlock>(),
                values),
            unknown,
            values);
    }

    private static bool TryReadAction(string line, out string action)
    {
        action = string.Empty;
        const string prefix = "[ACTION=";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !line.EndsWith(']'))
        {
            return false;
        }

        action = line[prefix.Length..^1].Trim().ToUpperInvariant();
        return action.Length > 0;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadFields(
        IEnumerable<string> lines)
    {
        var result = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            var split = trimmed.IndexOf(':');
            if (split <= 0)
                continue;

            var key = trimmed[..split].Trim();
            var value = trimmed[(split + 1)..].Trim();
            if (key.Length == 0)
                continue;

            if (!result.TryGetValue(key, out var values))
            {
                values = new List<string>();
                result[key] = values;
            }
            values.Add(value);
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<Section> ReadSections(
        IReadOnlyList<string> lines)
    {
        var result = new List<Section>();
        string? name = null;
        var argument = string.Empty;
        var rawMarker = string.Empty;
        var content = new List<string>();

        void Flush()
        {
            if (name is null)
                return;

            var rawBody = string.Join(
                Environment.NewLine,
                content).TrimEnd();
            var body = rawBody.Trim();
            var raw = rawBody.Length == 0
                ? rawMarker
                : rawMarker + Environment.NewLine + rawBody;
            result.Add(new Section(name, argument, body, raw));
            content.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("@@", StringComparison.Ordinal))
            {
                if (name is not null)
                    content.Add(line);
                continue;
            }

            Flush();
            rawMarker = line.TrimEnd();
            var marker = trimmed[2..].Trim();
            var split = marker.IndexOfAny(new[] { ' ', '\t' });
            if (split < 0)
            {
                name = marker.ToUpperInvariant();
                argument = string.Empty;
            }
            else
            {
                name = marker[..split].Trim().ToUpperInvariant();
                argument = marker[(split + 1)..].Trim();
            }
        }

        Flush();
        return result;
    }

    private static string? One(
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        string name) =>
        values.TryGetValue(name, out var items)
            ? items.LastOrDefault()
            : null;

    private static bool TryReadBool(string? raw, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (raw.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (raw.Equals("NO", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ReadTextItems(string content) =>
        (content ?? string.Empty)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .Select(line => line.StartsWith("- ", StringComparison.Ordinal)
            ? line[2..].Trim()
            : line)
        .Where(line => line.Length > 0)
        .ToArray();
}
