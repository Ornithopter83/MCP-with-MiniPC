using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>
/// Strict new protocol: [KEY/ACTION/GOTO/RESPONSE] control, @@ major sections,
/// &lt;FIELD&gt;value&lt;/&gt; middle fields. Prose within a middle field is opaque.
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
        if (significant.Length > 0 && significant[0].StartsWith("[KEY=", StringComparison.Ordinal))
            i++;
        if (i < significant.Length && significant[i].StartsWith("[GOTO", StringComparison.Ordinal))
            i++;
        if (i >= significant.Length || significant[i] != "[ACTION=WORK]")
            return Invalid("ACTION");
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
                body.Add(line);
                if (trimmed == "</>") inMiddleBody = false;
                continue;
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
        if (sections.Count is 0 or > 24 || sections.Count(s => s.Name == "MILESTONE") != 1)
            errors.Add("MILESTONE");
        if (sections.Count(s => s.Name == "QA") != 1 ||
            sections.Count(s => s.Name == "HIGH") != 1)
            errors.Add("COMPACT_QA_HIGH_REQUIRED");
        if (sections.Any(s => s.Name is not ("MILESTONE" or "WORK" or "RESOURCE" or "QA" or "HIGH" or "PLAN")))
            errors.Add("COMPACT_SECTION_FORBIDDEN");
        var parsed = new Dictionary<Section, StructuredRoleFields>();
        foreach (var section in sections)
        {
            if (StructuredRoleFields.TryParse(section.Body, out var fields, out var error))
                parsed[section] = fields;
            else
                errors.Add(section.Name + "." + error);
        }
        string Value(Section? s, string key) => s is not null && parsed.TryGetValue(s, out var fields)
            ? fields.Get(key) ?? "" : "";
        var milestoneSection = sections.FirstOrDefault(s => s.Name == "MILESTONE");
        var milestoneId = Value(milestoneSection, "ID");
        if (string.IsNullOrWhiteSpace(milestoneId))
            errors.Add("MILESTONE.ID");

        var works = new List<Dictionary<string, object?>>();
        foreach (var s in sections.Where(s => s.Name == "WORK"))
        {
            var instr = Value(s, "INSTRUCTIONS");
            var paths = parsed.TryGetValue(s, out var f) ? f.GetMany("PATH") : Array.Empty<string>();
            if (!int.TryParse(s.Argument, out var id) || id < 10)
                errors.Add("WORK.ID");
            if (paths.Count is < 1 or > 5 || paths.Any(p => p.Length == 0 || p.Length > 250) ||
                instr.Length is < 1 or > 600)
                errors.Add("WORK " + s.Argument + ".COMPACT_LIMIT");
            if (paths.Any(p => !MilestoneDefinitionContract.IsSafeRelativePath(p)))
                errors.Add("WORK " + s.Argument + ".WRITE_PATH");
            if (parsed.TryGetValue(s, out var wf) &&
                wf.Names.Any(n => n is not ("PATH" or "INSTRUCTIONS")))
                errors.Add("WORK " + s.Argument + ".UNKNOWN_FIELD");
            works.Add(new()
            {
                ["id"] = id,
                ["readOnly"] = false,
                ["testRequired"] = true,
                ["writePaths"] = paths,
                ["goal"] = instr,
                ["instructions"] = instr,
                ["completionCriteria"] = new[] { instr }
            });
        }
        if (works.Count == 0 || works.Select(w => w["id"]).Distinct().Count() != works.Count)
            errors.Add("COMPACT_WORK_REQUIRED");

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
                var raw = Value(s, field);
                if (raw.Length == 0) continue;
                if (!int.TryParse(raw, out var value) || value < 1 || value > 16384)
                    errors.Add("RESOURCE." + field);
                else optional[field] = value;
            }
            var alpha = Value(s, "ALPHA");
            if (alpha.Length != 0 && alpha != "required" && alpha != "optional")
                errors.Add("RESOURCE.ALPHA");
            if (parsed.TryGetValue(s, out var rf) && rf.Names.Any(n =>
                    n is not ("TYPE" or "TARGET_PATH" or "INSTRUCTIONS" or "WIDTH" or "HEIGHT" or "COLUMNS" or "ROWS" or "ALPHA")))
                errors.Add("RESOURCE.UNKNOWN_FIELD");
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
        var qa = Value(sections.FirstOrDefault(s => s.Name == "QA"), "INSTRUCTIONS");
        var high = Value(sections.FirstOrDefault(s => s.Name == "HIGH"), "INSTRUCTIONS");
        var plan = Value(sections.FirstOrDefault(s => s.Name == "PLAN"), "TEXT");
        if (qa.Length is < 1 or > 600 || high.Length is < 1 or > 600 || plan.Length > 8000)
            errors.Add("COMPACT_SECTION_LENGTH");
        foreach (var s in sections.Where(s => s.Name is "QA" or "HIGH" or "PLAN" or "MILESTONE"))
        {
            var allowed = s.Name == "MILESTONE" ? "ID" : s.Name == "PLAN" ? "TEXT" : "INSTRUCTIONS";
            if (parsed.TryGetValue(s, out var f) && f.Names.Any(n => n != allowed))
                errors.Add(s.Name + ".UNKNOWN_FIELD");
        }
        if (errors.Count > 0) return Invalid(errors.ToArray());
        var milestone = new Dictionary<string, object?>
        {
            ["id"] = milestoneId, ["branch"] = "main", ["goal"] = milestoneId,
            ["qaInstructions"] = qa, ["highInstructions"] = high,
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
