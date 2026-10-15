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
        if (!RoleGotoContract.TryReadLeading(raw, out var destination,
                out var gotoError))
            return Invalid(gotoError);
        if (destination is not null && destination != "WORKER")
            return Invalid("HQ_GOTO_TARGET_INVALID");
        if (significant.Length > 0 && significant[0].StartsWith("[KEY=", StringComparison.Ordinal))
            i++;
        while (i < significant.Length && RoleGotoContract.IsGotoLine(significant[i]))
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
        if (sections.Count(s => s.Name == "MILESTONE") != 1)
            errors.Add("MILESTONE");
        if (sections.Count(s => s.Name == "HIGH") != 1)
            errors.Add("COMPACT_HIGH_REQUIRED");
        if (sections.Any(s => s.Name is not ("MILESTONE" or "WORK" or "DESIGN" or "ASSET_MANAGER" or "HIGH" or "PLAN")))
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
            var executionMode = Value(s, "MODE");
            if (executionMode.Length == 0) executionMode = "NEW";
            if (executionMode is not ("NEW" or "CONTINUE"))
                errors.Add("WORK " + s.Argument + ".MODE");
            var paths = parsed.TryGetValue(s, out var f) ? f.GetMany("PATH") : Array.Empty<string>();
            if (!int.TryParse(s.Argument, out var id) || id < 10)
                errors.Add("WORK.ID");
            if (paths.Count is < 1 or > 5 || paths.Any(p => p.Length == 0 || p.Length > 250) ||
                instr.Length is < 1 or > 600)
                errors.Add("WORK " + s.Argument + ".COMPACT_LIMIT");
            if (paths.Any(p => !MilestoneDefinitionContract.IsSafeRelativePath(p)))
                errors.Add("WORK " + s.Argument + ".WRITE_PATH");
            if (parsed.TryGetValue(s, out var wf) &&
                wf.Names.Any(n => n is not ("PATH" or "INSTRUCTIONS" or "MODE")))
                errors.Add("WORK " + s.Argument + ".UNKNOWN_FIELD");
            works.Add(new()
            {
                ["id"] = id,
                ["executionMode"] = executionMode,
                ["readOnly"] = false,
                ["testRequired"] = true,
                ["writePaths"] = paths,
                ["goal"] = instr,
                ["instructions"] = instr,
                ["completionCriteria"] = new[] { instr }
            });
        }
        if (works.Select(w => w["id"]).Distinct().Count() != works.Count)
            errors.Add("WORK.ID_DUPLICATE");

        Dictionary<string, object?>? assetManager = null;
        var assetSections = sections.Where(s => s.Name == "ASSET_MANAGER").ToArray();
        if (assetSections.Length > 1) errors.Add("ASSET_MANAGER_COUNT");
        if (assetSections.Length == 1)
        {
            var s = assetSections[0];
            var requestId = Value(s, "REQUEST_ID");
            var goal = Value(s, "GOAL");
            var scene = Value(s, "SCENE");
            var target = Value(s, "TARGET_PATH");
            var source = Value(s, "SOURCE_POLICY");
            var instructions = Value(s, "INSTRUCTIONS");
            if (s.Argument != "1") errors.Add("ASSET_MANAGER.ID");
            if (!Regex.IsMatch(requestId, @"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$"))
                errors.Add("ASSET_MANAGER.REQUEST_ID");
            if (goal.Length is < 1 or > 600 || instructions.Length is < 1 or > 1200)
                errors.Add("ASSET_MANAGER.INSTRUCTIONS");
            if (!scene.StartsWith("Assets/", StringComparison.Ordinal) ||
                !scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                !MilestoneDefinitionContract.IsSafeRelativePath(scene))
                errors.Add("ASSET_MANAGER.SCENE");
            if (!target.StartsWith("Assets/", StringComparison.Ordinal) ||
                target.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                !MilestoneDefinitionContract.IsSafeRelativePath(target))
                errors.Add("ASSET_MANAGER.TARGET_PATH");
            if (source is not ("PROJECT_ONLY" or "CC0_AUTO"))
                errors.Add("ASSET_MANAGER.SOURCE_POLICY");
            if (parsed.TryGetValue(s, out var af) && af.Names.Any(n =>
                    n is not ("REQUEST_ID" or "GOAL" or "SCENE" or "TARGET_PATH" or "SOURCE_POLICY" or "INSTRUCTIONS")))
                errors.Add("ASSET_MANAGER.UNKNOWN_FIELD");
            if (sections.FindIndex(section => section == s) >
                sections.FindIndex(section => section.Name == "WORK") &&
                sections.Any(section => section.Name == "WORK"))
                errors.Add("ASSET_MANAGER_MUST_PRECEDE_WORK");
            assetManager = new()
            {
                ["id"] = 1, ["requestId"] = requestId, ["goal"] = goal,
                ["scene"] = scene, ["targetPath"] = target,
                ["sourcePolicy"] = source, ["instructions"] = instructions
            };
        }
        if (works.Count == 0 && assetManager is null &&
            !sections.Any(s => s.Name == "DESIGN"))
            errors.Add("COMPACT_WORK_REQUIRED");

        // DESIGN #0 is a real, ordered design role, not a resource sidecar.
        // RESOURCE #0 image generation is intentionally unsupported in UnityWorker.
        Dictionary<string, object?>? design = null;
        var designs = sections.Where(section => section.Name == "DESIGN").ToArray();
        if (designs.Length > 1)
            errors.Add("DESIGN_COUNT");
        if (designs.Length == 1)
        {
            var section = designs[0];
            var path = Value(section, "PATH");
            var instructions = Value(section, "INSTRUCTIONS");
            var writePaths = parsed.TryGetValue(section, out var designFields)
                ? designFields.GetMany("WRITE_PATH") : Array.Empty<string>();
            if (writePaths.Count > 12 ||
                writePaths.Any(value => !MilestoneDefinitionContract.IsSafeRelativePath(value)))
                errors.Add("DESIGN.WRITE_PATH");
            if (section.Argument != "0") errors.Add("DESIGN.ID");
            if (!path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                !MilestoneDefinitionContract.IsSafeRelativePath(path))
                errors.Add("DESIGN.PATH");
            if (instructions.Length is < 1 or > 1200)
                errors.Add("DESIGN.INSTRUCTIONS");
            if (parsed.TryGetValue(section, out var fields) && fields.Names.Any(name =>
                    name is not ("PATH" or "INSTRUCTIONS" or "WRITE_PATH")))
                errors.Add("DESIGN.UNKNOWN_FIELD");
            if (sections.Any(item => item.Name == "WORK") &&
                sections.FindIndex(item => item == section) >
                sections.FindIndex(item => item.Name == "WORK"))
                errors.Add("DESIGN_MUST_PRECEDE_WORK");
            if (works.Any(work => ((IReadOnlyList<string>)work["writePaths"]!).Any(
                    workPath => (new[] { path }.Concat(writePaths)).Any(
                        designPath => MilestoneMechanicalExecutor.IsPathWithinScopes(
                            designPath, new[] { workPath }) ||
                            MilestoneMechanicalExecutor.IsPathWithinScopes(
                                workPath, new[] { designPath })))))
                errors.Add("DESIGN_WORK_PATH_OVERLAP");
            design = new()
            {
                ["id"] = 0, ["path"] = path, ["instructions"] = instructions,
                ["writePaths"] = writePaths
            };
        }
        var high = Value(sections.FirstOrDefault(s => s.Name == "HIGH"), "INSTRUCTIONS");
        var plan = Value(sections.FirstOrDefault(s => s.Name == "PLAN"), "TEXT");
        if (high.Length is < 1 or > 600 || plan.Length > 8000)
            errors.Add("COMPACT_SECTION_LENGTH");
        foreach (var s in sections.Where(s => s.Name is "HIGH" or "PLAN" or "MILESTONE"))
        {
            var allowed = s.Name == "MILESTONE" ? "ID" : s.Name == "PLAN" ? "TEXT" : "INSTRUCTIONS";
            if (parsed.TryGetValue(s, out var f) && f.Names.Any(n => n != allowed))
                errors.Add(s.Name + ".UNKNOWN_FIELD");
        }
        if (errors.Count > 0) return Invalid(errors.ToArray());
        var milestone = new Dictionary<string, object?>
        {
            ["id"] = milestoneId, ["branch"] = "main", ["goal"] = milestoneId,
            ["highInstructions"] = high,
            ["planDocument"] = plan, ["entrypoint"] = null,
            ["initializeGitIfMissing"] = false, ["projectPolicy"] = "DEFAULT",
            ["resource"] = null, ["design"] = design, ["assetManager"] = assetManager, ["workItems"] = works
        };
        var compatibility = "[ACTION=WORK]\n" + ProjectHubJson.SerializeIndented(
            new Dictionary<string, object?> { ["milestone"] = milestone });
        var actionParse = ActionBlockContract.ParseHq(compatibility);
        return new("WORK", compatibility, actionParse,
            Array.Empty<string>(), actionParse.Errors);
    }
}
