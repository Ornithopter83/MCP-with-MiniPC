using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>
/// Best-effort @@ body recovery. Only bracket control directives are strict.
/// Never infer writable paths; malformed or absent scopes become read-only WORK.
/// Preserve the original HQ body for HIGH rather than aborting a milestone.
/// </summary>
internal static class HqBodyFallbackContract
{
    private sealed record Section(string Name, string Argument, string Body);

    public static bool TryParse(string raw, bool completionValidatedByTransport,
        out HqTextProtocolResult result, bool reviewOnly = false)
    {
        result = Invalid("HQ_CONTROL_INVALID");
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n');
        var significant = lines.Select((line, index) => (Text: line.Trim(), Index: index))
            .Where(line => line.Text.Length != 0).ToArray();
        if (significant.Length == 0) return false;

        if (!RoleGotoContract.TryReadLeading(raw, out var destination, out _) ||
            (destination is not null && destination != "WORKER"))
            return false;

        var cursor = 0;
        if (Regex.IsMatch(significant[cursor].Text, @"^\[KEY=[^\]\r\n]+\]$"))
            cursor++;
        if (cursor < significant.Length && significant[cursor].Text == "[GOTO=WORKER]")
            cursor++;
        if (cursor >= significant.Length || significant[cursor].Text != "[ACTION=WORK]")
            return false;
        var actionLine = significant[cursor].Index;
        var terminator = Array.FindLastIndex(lines, line => line.Trim() == "[RESPONSE=OK]");
        if (!completionValidatedByTransport && terminator < 0)
            return false;
        if (terminator >= 0 && lines.Skip(terminator + 1)
            .Any(line => !string.IsNullOrWhiteSpace(line)))
            return false;
        var end = terminator < 0 ? lines.Length : terminator;
        if (lines.Skip(actionLine + 1).Take(end - actionLine - 1).Any(line =>
            Regex.IsMatch(line.Trim(), @"^\[(?:KEY|GOTO|ACTION|RESPONSE)\b",
                RegexOptions.IgnoreCase)))
            return false;

        var sections = new List<Section>();
        string? name = null;
        var argument = "";
        var body = new List<string>();
        var orphan = new List<string>();
        void Flush()
        {
            if (name is not null)
                sections.Add(new Section(name, argument, string.Join("\n", body).Trim()));
            body.Clear();
        }
        foreach (var line in lines.Skip(actionLine + 1).Take(end - actionLine - 1))
        {
            var header = Regex.Match(line.Trim(),
                @"^@@(?<name>[A-Z_]+)(?:[= ](?<id>\d+))?$",
                RegexOptions.IgnoreCase);
            if (header.Success)
            {
                Flush();
                name = header.Groups["name"].Value.ToUpperInvariant();
                argument = header.Groups["id"].Value;
            }
            else if (name is null)
                orphan.Add(line);
            else
                body.Add(line);
        }
        Flush();

        static string[] Fields(Section section, string field)
        {
            var escaped = Regex.Escape(field);
            // Both </> and conventional </FIELD> endings are accepted.
            var pattern = @"(?ms)^[ \t]*<" + escaped +
                @">(?<value>.*?)(?:</>|</" + escaped + @">)[ \t]*(?=\r?$)";
            var values = Regex.Matches(section.Body, pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(match => match.Groups["value"].Value.Trim()).ToArray();
            if (values.Length > 0) return values;
            // Legacy colon fields are also data; scope them to exact names.
            if (field is "PATH" or "WRITE_PATH")
            {
                var legacy = Regex.Matches(section.Body,
                        @"(?m)^[ \t]*" + escaped + @":[ \t]*(?<value>[^\r\n]+)$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    .Select(match => match.Groups["value"].Value.Trim()).ToArray();
                if (legacy.Length > 0) return legacy;
            }
            // Preserve a field with an incorrect or missing closing tag as text.
            return Regex.Matches(section.Body,
                    @"(?m)^[ \t]*<" + escaped + @">(?<value>[^\r\n<>]*)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(match => match.Groups["value"].Value.Trim())
                .Where(value => value.Length > 0).ToArray();
        }
        static string Field(Section section, string field) =>
            Fields(section, field).LastOrDefault() ?? "";
        static bool Safe(string path) =>
            path.Length <= 250 && MilestoneDefinitionContract.IsSafeRelativePath(path);
        static string Limit(string value, int count) =>
            value.Length <= count ? value : value[..count];
        static string BodyOrInstructions(Section section) =>
            string.IsNullOrWhiteSpace(section.Body) ? "원본 작업 지시를 확인하라." : section.Body;

        var notes = new List<string>();
        var milestoneSection = sections.FirstOrDefault(section => section.Name == "MILESTONE");
        var milestoneId = milestoneSection is null ? "" : Field(milestoneSection, "ID");
        if (milestoneId.Length == 0)
            milestoneId = Regex.Match(raw, @"(?m)^MILESTONE:[ \t]*(?<id>[A-Za-z0-9_-]+)")
                .Groups["id"].Value;
        if (!Regex.IsMatch(milestoneId, @"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$"))
        {
            milestoneId = "M_UNSPECIFIED";
            notes.Add("마일스톤 ID를 해석할 수 없어 기본값 M_UNSPECIFIED 사용");
        }
        var works = new List<Dictionary<string, object?>>();
        var ids = new HashSet<int>();
        foreach (var section in sections.Where(section => section.Name == "WORK"))
        {
            if (!int.TryParse(section.Argument, out var id) || id < 10 || !ids.Add(id))
            {
                notes.Add("WORK ID 오류·중복: " + section.Argument + " (HIGH에 원문 전달)");
                continue;
            }
            var paths = Fields(section, "PATH").Concat(Fields(section, "WRITE_PATH"))
                .Where(Safe).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
            var mode = Field(section, "MODE");
            if (mode is not ("NEW" or "CONTINUE")) mode = "NEW";
            var readOnly = paths.Length == 0;
            if (readOnly)
                notes.Add("WORK " + id + ": 안전한 쓰기 경로가 없어 읽기 전용으로 전달");
            var depends = Field(section, "DEPENDS_ON_DESIGN");
            works.Add(new Dictionary<string, object?>
            {
                ["id"] = id,
                ["executionMode"] = mode,
                ["readOnly"] = readOnly,
                ["testRequired"] = true,
                ["dependsOnDesign"] = depends.Equals("true", StringComparison.OrdinalIgnoreCase),
                ["writePaths"] = paths,
                ["goal"] = BodyOrInstructions(section),
                ["instructions"] = BodyOrInstructions(section),
                ["completionCriteria"] = new[] { BodyOrInstructions(section) }
            });
        }

        Dictionary<string, object?>? design = null;
        var designSection = sections.FirstOrDefault(section =>
            section.Name == "DESIGN" && section.Argument == "0");
        if (designSection is not null)
        {
            var designPath = Field(designSection, "PATH");
            if (designPath.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) &&
                designPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                Safe(designPath))
            {
                design = new Dictionary<string, object?>
                {
                    ["id"] = 0,
                    ["path"] = designPath,
                    ["writePaths"] = Fields(designSection, "WRITE_PATH")
                        .Where(Safe).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
                    ["instructions"] = Limit(BodyOrInstructions(designSection), 1200)
                };
            }
            else
                notes.Add("DESIGN 문서 경로가 불명확하여 자동 실행하지 않음");
        }

        Dictionary<string, object?>? asset = null;
        var assetSection = sections.FirstOrDefault(section =>
            section.Name == "ASSET_MANAGER" && section.Argument == "1");
        if (assetSection is not null)
        {
            var scene = Field(assetSection, "SCENE");
            var target = Field(assetSection, "TARGET_PATH");
            if (scene.StartsWith("Assets/", StringComparison.Ordinal) &&
                scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
                Safe(scene) && target.StartsWith("Assets/", StringComparison.Ordinal) &&
                !target.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && Safe(target))
            {
                var requestId = Field(assetSection, "REQUEST_ID");
                if (!Regex.IsMatch(requestId, @"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$"))
                    requestId = "HQ-RECOVERED-ASSET";
                var source = Field(assetSection, "SOURCE_POLICY");
                if (source is not ("PROJECT_ONLY" or "CC0_AUTO"))
                    source = "PROJECT_ONLY";
                asset = new Dictionary<string, object?>
                {
                    ["id"] = 1,
                    ["requestId"] = requestId,
                    ["goal"] = Limit(Field(assetSection, "GOAL") is { Length: > 0 } goal
                        ? goal : BodyOrInstructions(assetSection), 600),
                    ["scene"] = scene,
                    ["targetPath"] = target,
                    ["sourcePolicy"] = source,
                    ["instructions"] = Limit(BodyOrInstructions(assetSection), 1200)
                };
            }
            else
                notes.Add("ASSET_MANAGER 경로가 불명확하여 자동 획득·수정하지 않음");
        }

        // Avoid making a formerly invalid HQ response authorize overlapping writes.
        bool Overlaps(string path, string scope) =>
            MilestoneMechanicalExecutor.IsPathWithinScopes(path, new[] { scope }) ||
            MilestoneMechanicalExecutor.IsPathWithinScopes(scope, new[] { path });
        if (asset is not null && design is not null)
        {
            var designScopes = new[] { (string)design["path"]! }
                .Concat((string[])design["writePaths"]!).ToArray();
            if (designScopes.Any(scope =>
                Overlaps((string)asset["scene"]!, scope) ||
                Overlaps((string)asset["targetPath"]!, scope)))
            {
                notes.Add("ASSET_MANAGER와 DESIGN의 경로가 충돌하여 자산 역할을 이번 단계에서 보류");
                asset = null;
            }
        }
        var reserved = new List<string>();
        if (design is not null)
        {
            reserved.Add((string)design["path"]!);
            reserved.AddRange((string[])design["writePaths"]!);
        }
        if (asset is not null)
        {
            reserved.Add((string)asset["scene"]!);
            reserved.Add((string)asset["targetPath"]!);
        }
        foreach (var work in works)
        {
            var paths = (string[])work["writePaths"]!;
            if (paths.Any(path => reserved.Any(scope => Overlaps(path, scope))))
            {
                work["readOnly"] = true;
                work["writePaths"] = Array.Empty<string>();
                notes.Add("WORK " + work["id"] + ": 다른 역할의 쓰기 경로와 충돌하여 읽기 전용 전환");
            }
            else if (!(bool)work["readOnly"]!)
                reserved.AddRange(paths);
            if (design is null) work["dependsOnDesign"] = false;
        }

        if (reviewOnly)
        {
            // Last-resort recovery for a downstream milestone-contract mismatch:
            // run HIGH with the unmodified HQ text, never authorize file writes.
            works.Clear();
            design = null;
            asset = null;
            notes.Add("마일스톤 구성 오류: 역할 실행을 건너뛰고 HIGH에 원문만 전달");
        }

        var highSection = sections.FirstOrDefault(section => section.Name == "HIGH");
        var high = highSection is null ? "" : Field(highSection, "INSTRUCTIONS");
        if (string.IsNullOrWhiteSpace(high))
            high = "유효한 기존 결과를 재사용하고 미검증 사항을 분리해 보고하라.";
        // The AI, not the Worker, interprets non-control material.
        high += "\nHQ_BEST_EFFORT_BODY_RECOVERY: " + string.Join("; ", notes) +
            "\n다음 HQ 원문에서 복구하지 못한 지시를 검토하고 안전하게 보고하라. " +
            "원문을 성공한 실행으로 간주하지 않는다.\n" + raw;
        var planSection = sections.FirstOrDefault(section => section.Name == "PLAN");
        var plan = planSection is null ? "" : Field(planSection, "TEXT");
        if (string.IsNullOrWhiteSpace(plan) && planSection is not null)
            plan = planSection.Body;
        var milestone = new Dictionary<string, object?>
        {
            ["id"] = milestoneId, ["branch"] = "main", ["goal"] = milestoneId,
            ["highInstructions"] = high,
            ["planDocument"] = Limit(plan, 8000), ["entrypoint"] = null,
            ["initializeGitIfMissing"] = false, ["projectPolicy"] = reviewOnly
                ? "READ_ONLY_NO_FILE_CHANGES" : "DEFAULT",
            ["resource"] = null, ["design"] = design, ["assetManager"] = asset,
            ["workItems"] = works
        };
        var compatibility = "[ACTION=WORK]\n" + ProjectHubJson.SerializeIndented(
            new Dictionary<string, object?> { ["milestone"] = milestone });
        var parse = ActionBlockContract.ParseHq(compatibility);
        if (parse.HasErrors) return false;
        result = new HqTextProtocolResult("WORK", compatibility, parse,
            sections.Where(section => section.Name is not
                ("MILESTONE" or "PLAN" or "WORK" or "DESIGN" or "ASSET_MANAGER" or "HIGH"))
                .Select(section => "@@" + section.Name + "\n" + section.Body)
                .Concat(orphan.Where(line => !string.IsNullOrWhiteSpace(line)))
                .ToArray(), Array.Empty<string>());
        return true;
    }

    private static HqTextProtocolResult Invalid(string error) =>
        new("", "", new ActionBlockParseResult(Array.Empty<ActionBlock>(), new[] { error }),
            Array.Empty<string>(), new[] { error });
}
