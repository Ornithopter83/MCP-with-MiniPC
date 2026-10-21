using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>
/// Bracket control markers are strict; @@ sections and middle fields are
/// best-effort data extraction. Unsafe or ambiguous execution targets still
/// fail semantic validation. Prose within a middle field stays opaque.
/// </summary>
internal static class StructuredHqProtocol
{
    private sealed record Section(string Name, string Argument, string Body);
    public static bool IsModern(string raw) =>
        Regex.IsMatch(raw ?? string.Empty, @"(?m)^@@MILESTONE\s*$");

    public static HqTextProtocolResult Parse(string raw, bool completionValidatedByTransport)
    {
        HqTextProtocolResult Invalid(params string[] errors) =>
            new("", "", new ActionBlockParseResult(Array.Empty<ActionBlock>(), errors),
                Array.Empty<string>(), errors);
        var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var significant = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        var i = 0;
        if (significant.Length > 0 && Regex.IsMatch(significant[0], @"^\[KEY=[^\]\r\n]+\]$"))
            i++;
        if (i < significant.Length && significant[i] == "[GOTO=WORKER]")
            i++;
        if (i >= significant.Length || significant[i] != "[ACTION=WORK]")
            return Invalid("ACTION");
        // An extra/malformed bracket directive is never treated as prose.
        var controls = significant.Where(line =>
            Regex.IsMatch(line, @"^\[[A-Z][A-Z_]*[=\]]", RegexOptions.CultureInvariant))
            .ToArray();
        if (significant.Skip(i + 1).Any(line =>
                line.StartsWith("[KEY=", StringComparison.Ordinal) ||
                line.StartsWith("[GOTO=", StringComparison.Ordinal)) ||
            controls.Any(line => line != "[ACTION=WORK]" &&
            line != "[GOTO=WORKER]" && line != "[RESPONSE=OK]" &&
            !Regex.IsMatch(line, @"^\[KEY=[^\]\r\n]+\]$")) ||
            controls.Count(line => line == "[ACTION=WORK]") != 1 ||
            controls.Count(line => line == "[GOTO=WORKER]") > 1 ||
            controls.Count(line => line == "[RESPONSE=OK]") > 1 ||
            controls.Count(line => line.StartsWith("[KEY=", StringComparison.Ordinal)) > 1)
            return Invalid("BRACKET_CONTROL_INVALID");
        var terminator = Array.FindLastIndex(lines, l => l.Trim() == "[RESPONSE=OK]");
        if (!completionValidatedByTransport && terminator < 0)
            return Invalid("COMPACT_RESPONSE_TERMINATOR_INVALID");
        if (terminator >= 0 && lines.Skip(terminator + 1).Any(l => !string.IsNullOrWhiteSpace(l)))
            return Invalid("COMPACT_RESPONSE_TERMINATOR_INVALID");

        var sections = new List<Section>();
        string? currentName = null;
        string argument = "";
        var body = new List<string>();
        void Flush()
        {
            if (currentName is null) return;
            sections.Add(new(currentName, argument, string.Join("\n", body).Trim()));
            body.Clear();
        }
        var actionLine = Array.FindIndex(lines, l => l.Trim() == "[ACTION=WORK]");
        var inMiddleBody = false;
        foreach (var line in lines.Skip(actionLine + 1).Take((terminator < 0 ? lines.Length : terminator) - actionLine - 1))
        {
            var trimmed = line.Trim();
            if (inMiddleBody)
            {
                // A missing closing field tag must not swallow the next
                // recognized major section.
                if (!Regex.IsMatch(trimmed, @"^@@(?:MILESTONE|SV|HIGH|PLAN|RESOURCE(?:=\d+)?)$"))
                {
                    body.Add(line);
                    if (trimmed == "</>" ||
                        Regex.IsMatch(trimmed, @"^</[A-Z][A-Z0-9_]*>$"))
                        inMiddleBody = false;
                    continue;
                }
                inMiddleBody = false;
            }
            if (!trimmed.StartsWith("@@", StringComparison.Ordinal))
            {
                if (currentName is null && trimmed.Length > 0)
                    return Invalid("COMPACT_HEADER_FORBIDDEN");
                if (currentName is not null)
                {
                    body.Add(line);
                    if (Regex.IsMatch(trimmed, @"^<[A-Z][A-Z0-9_]*>$"))
                        inMiddleBody = true;
                }
                continue;
            }
            Flush();
            var m = Regex.Match(trimmed, @"^@@([A-Z_]+)(?:=(\d+))?$");
            if (!m.Success) return Invalid("MAJOR_SECTION_INVALID");
            currentName = m.Groups[1].Value;
            argument = m.Groups[2].Value;
        }
        Flush();
        var errors = new List<string>();
        if (sections.Count(s => s.Name == "MILESTONE") != 1)
            errors.Add("MILESTONE");
        if (sections.Count(s => s.Name == "SV") != 1 ||
            sections.Count(s => s.Name == "HIGH") != 1)
            errors.Add("COMPACT_SV_HIGH_REQUIRED");
        if (sections.Any(s => s.Name is not ("MILESTONE" or "SV" or "RESOURCE" or "HIGH" or "PLAN")))
            errors.Add("COMPACT_SECTION_FORBIDDEN");
        var parsed = new Dictionary<Section, StructuredRoleFields>();
        foreach (var section in sections)
        {
            StructuredRoleFields.TryParse(section.Body, out var fields, out _);
            parsed[section] = fields;
        }
        string Value(Section? section, string key)
        {
            if (section is null || !parsed.TryGetValue(section, out var fields))
                return "";
            var value = fields.Get(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
            // Keep naturally worded instructions when their optional
            // middle-field wrapper is malformed or omitted.
            return (section.Name is "SV" or "HIGH" or "PLAN") &&
                   (key is "INSTRUCTIONS" or "TEXT")
                ? section.Body.Trim() : "";
        }
        var milestoneSection = sections.FirstOrDefault(s => s.Name == "MILESTONE");
        var milestoneId = Value(milestoneSection, "ID");
        if (string.IsNullOrWhiteSpace(milestoneId))
            errors.Add("MILESTONE.ID");

        // HQ delegates exactly one implementation owner, not numbered WORKITEMs.
        var sv = Value(sections.FirstOrDefault(s => s.Name == "SV"), "INSTRUCTIONS");
        if (string.IsNullOrWhiteSpace(sv)) errors.Add("SV.INSTRUCTIONS");
        var works = new List<Dictionary<string, object?>>();

                Dictionary<string, object?>? resource = null;
        var resources = sections.Where(s => s.Name == "RESOURCE").ToArray();
        if (resources.Length > 1) errors.Add("RESOURCE_COUNT");
        if (resources.Length > 0)
        {
            var s = resources[0];
            var type = Value(s, "TYPE");
            var target = Value(s, "TARGET_PATH");
            var instructions = Value(s, "INSTRUCTIONS");
            if (s.Argument != "0") errors.Add("RESOURCE.ID");
            if (!string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
                errors.Add("RESOURCE.TYPE");
            if (!MilestoneDefinitionContract.IsSafeRelativePath(target))
                errors.Add("RESOURCE.TARGET_PATH");
            if (instructions.Length == 0) errors.Add("RESOURCE.INSTRUCTIONS");
            var optional = new Dictionary<string, int>();
            foreach (var field in new[] { "WIDTH", "HEIGHT", "COLUMNS", "ROWS" })
            {
                var rawValue = Value(s, field);
                if (rawValue.Length == 0) continue;
                if (!int.TryParse(rawValue, out var value) || value < 1 || value > 16384)
                    errors.Add("RESOURCE." + field);
                else optional[field] = value;
            }
            var alpha = Value(s, "ALPHA");
            if (alpha.Length != 0 && alpha != "required" && alpha != "optional")
                errors.Add("RESOURCE.ALPHA");
            resource = new()
            {
                ["id"] = 0, ["type"] = "image", ["targetPath"] = target,
                ["instructions"] = instructions,
                ["width"] = optional.GetValueOrDefault("WIDTH"),
                ["height"] = optional.GetValueOrDefault("HEIGHT"),
                ["columns"] = optional.GetValueOrDefault("COLUMNS"),
                ["rows"] = optional.GetValueOrDefault("ROWS"),
                ["requireAlpha"] = alpha == "required"
            };
        }
        var high = Value(sections.FirstOrDefault(s => s.Name == "HIGH"), "INSTRUCTIONS");
        var plan = Value(sections.FirstOrDefault(s => s.Name == "PLAN"), "TEXT");
        if (string.IsNullOrWhiteSpace(high))
            errors.Add("HIGH.INSTRUCTIONS");
        if (errors.Count > 0) return Invalid(errors.ToArray());
        var milestone = new Dictionary<string, object?>
        {
            ["id"] = milestoneId, ["branch"] = "main", ["goal"] = milestoneId,
            ["svInstructions"] = sv, ["highInstructions"] = high,
            ["planDocument"] = plan, ["entrypoint"] = null,
            ["initializeGitIfMissing"] = false, ["projectPolicy"] = "DEFAULT",
            ["resource"] = resource, ["workItems"] = works
        };
        var compatibility = "[ACTION=WORK]\n" + ProjectHubJson.SerializeIndented(
            new Dictionary<string, object?> { ["milestone"] = milestone });
        var actionParse = ActionBlockContract.ParseHq(compatibility);
        return new("WORK", compatibility, actionParse,
            Array.Empty<string>(), actionParse.Errors);
    }
}
