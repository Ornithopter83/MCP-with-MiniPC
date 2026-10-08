using System.Text;
using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record MilestoneWorkDefinition(
    string Id,
    IReadOnlyList<string> WritePaths,
    bool ReadOnly,
    bool TestRequired,
    string Body,
    string RawText);

internal sealed record MilestoneResourceDefinition(
    string Id,
    string Type,
    string TargetPath,
    string Body,
    string RawText)
{
    public string Instructions { get; init; } = string.Empty;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Columns { get; init; }
    public int? Rows { get; init; }
    public bool RequireAlpha { get; init; }
}

internal sealed record MilestoneDefinition(
    string Id,
    string TargetBranch,
    bool QaReserved,
    string? Entrypoint,
    bool InitializeGitIfMissing,
    bool ReadOnlyNoFileChanges,
    string Body,
    string RawHqMessage,
    IReadOnlyDictionary<string, MilestoneWorkDefinition> WorkItems,
    IReadOnlyDictionary<string, MilestoneResourceDefinition> Resources,
    IReadOnlyList<string> ParseErrors)
{
    public string QaInstructions { get; init; } = string.Empty;
    public string HighInstructions { get; init; } = string.Empty;
    public string PlanDocument { get; init; } = string.Empty;
}

internal sealed record MilestoneMechanicalResult(
    string Operation,
    bool Success,
    int ExitCode,
    string Command,
    string LogPath,
    string Summary);

internal sealed record MilestoneGitResult(
    bool Success,
    bool PauseRequired,
    string TargetBranch,
    string? CommitSha,
    string Summary)
{
    public static MilestoneGitResult NotStarted(string targetBranch) =>
        new(false, false, targetBranch, null, "Git finalize 미수행");

    public static MilestoneGitResult SkippedReadOnly(string targetBranch) =>
        new(
            true,
            false,
            targetBranch,
            null,
            "GIT_FINALIZE" + Environment.NewLine + "status=SKIPPED_READ_ONLY");
}

internal static class MilestoneDefinitionContract
{
    public static bool TryBuild(
        string rawMessage,
        ActionBlockParseResult parse,
        out MilestoneDefinition? milestone,
        out string error)
    {
        milestone = null;
        error = string.Empty;

        var workActions = parse.ValidActions
            .Where(action => string.Equals(
                action.Name,
                "WORK",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (workActions.Length != 1)
        {
            error =
                $"valid WORK count={workActions.Length}" +
                Environment.NewLine +
                string.Join(Environment.NewLine, parse.Errors);
            return false;
        }

        var action = workActions[0];
        if (string.IsNullOrWhiteSpace(action.JsonPayload))
        {
            error = "HQ_WORK_JSON_REQUIRED";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(action.JsonPayload);
            var root = document.RootElement;
            if (!root.TryGetProperty("milestone", out var milestoneJson) ||
                milestoneJson.ValueKind != JsonValueKind.Object)
            {
                error = "MILESTONE_OBJECT_REQUIRED";
                return false;
            }

            if (!TryGetRequiredJsonString(milestoneJson, "id", out var milestoneId))
            {
                error = "MILESTONE_ID_REQUIRED";
                return false;
            }

            if (!TryGetRequiredJsonString(milestoneJson, "branch", out var targetBranch))
            {
                error = "MILESTONE_BRANCH_REQUIRED";
                return false;
            }

            if (!string.Equals(
                    targetBranch,
                    "main",
                    StringComparison.Ordinal))
            {
                error = "MILESTONE_MAIN_BRANCH_REQUIRED";
                return false;
            }

            targetBranch = "main";

            if (!TryGetRequiredJsonString(milestoneJson, "goal", out _))
            {
                error = "MILESTONE_GOAL_REQUIRED";
                return false;
            }

            if (!milestoneJson.TryGetProperty("entrypoint", out var entrypointJson) ||
                entrypointJson.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                error = "MILESTONE_ENTRYPOINT_REQUIRED";
                return false;
            }

            var entrypoint = entrypointJson.ValueKind == JsonValueKind.String
                ? entrypointJson.GetString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(entrypoint))
                entrypoint = null;

            var initializeGitIfMissing = false;
            if (milestoneJson.TryGetProperty(
                    "initializeGitIfMissing",
                    out var initializeGitJson))
            {
                if (initializeGitJson.ValueKind is not
                    (JsonValueKind.True or JsonValueKind.False))
                {
                    error = "MILESTONE_GIT_INIT_INVALID";
                    return false;
                }

                initializeGitIfMissing = initializeGitJson.GetBoolean();
            }

            var projectPolicy = TryGetOptionalJsonString(
                milestoneJson,
                "projectPolicy");
            var readOnlyNoFileChanges = string.Equals(
                projectPolicy,
                "READ_ONLY_NO_FILE_CHANGES",
                StringComparison.OrdinalIgnoreCase);

            if (readOnlyNoFileChanges &&
                milestoneJson.TryGetProperty("resource", out var readOnlyResource) &&
                readOnlyResource.ValueKind != JsonValueKind.Null)
            {
                error = "READ_ONLY_RESOURCE_FORBIDDEN";
                return false;
            }

            if (!milestoneJson.TryGetProperty("workItems", out var workItemsJson) ||
                workItemsJson.ValueKind != JsonValueKind.Array)
            {
                error = "MILESTONE_WORK_ITEMS_REQUIRED";
                return false;
            }

            var workItems = new Dictionary<string, MilestoneWorkDefinition>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var workJson in workItemsJson.EnumerateArray())
            {
                if (workJson.ValueKind != JsonValueKind.Object ||
                    !TryGetJsonId(workJson, "id", out var workId) ||
                    !int.TryParse(workId, out var workNumber) ||
                    workNumber < 10)
                {
                    error = "WORK_ITEM_ID_INVALID";
                    return false;
                }

                if (workItems.ContainsKey(workId))
                {
                    error = $"WORK {workId}: WORK_ITEM_ID_DUPLICATED";
                    return false;
                }

                if (workJson.TryGetProperty("order", out _))
                {
                    error = $"WORK {workId}: ORDER_FORBIDDEN";
                    return false;
                }

                var workReadOnly = readOnlyNoFileChanges;
                if (workJson.TryGetProperty("readOnly", out var workReadOnlyJson))
                {
                    if (workReadOnlyJson.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        error = $"WORK {workId}: READ_ONLY_INVALID";
                        return false;
                    }
                    workReadOnly = workReadOnly || workReadOnlyJson.GetBoolean();
                }

                if (!workJson.TryGetProperty("testRequired", out var testRequiredJson) ||
                    testRequiredJson.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    error = $"WORK {workId}: TEST_REQUIRED_INVALID";
                    return false;
                }
                var testRequired = testRequiredJson.GetBoolean();

                string[] writePaths;
                if (!workJson.TryGetProperty("writePaths", out var writePathsJson) ||
                    writePathsJson.ValueKind != JsonValueKind.Array ||
                    !IsStringArray(writePathsJson))
                {
                    error = $"WORK {workId}: WRITE_PATH_REQUIRED";
                    return false;
                }

                writePaths = writePathsJson
                    .EnumerateArray()
                    .Select(item => item.GetString()?.Trim() ?? string.Empty)
                    .Where(item => item.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (!workReadOnly && writePaths.Length == 0)
                {
                    error = $"WORK {workId}: WRITE_PATH_REQUIRED";
                    return false;
                }

                if (writePaths.Any(path => !IsSafeRelativePath(path)))
                {
                    error = $"WORK {workId}: WRITE_PATH_OUTSIDE_PROJECT_ROOT";
                    return false;
                }

                if (!TryGetRequiredJsonString(workJson, "goal", out var workGoal))
                {
                    error = $"WORK {workId}: GOAL_REQUIRED";
                    return false;
                }

                if (!workJson.TryGetProperty("instructions", out var workInstructions) ||
                    workInstructions.ValueKind != JsonValueKind.String)
                {
                    error = $"WORK {workId}: INSTRUCTIONS_REQUIRED";
                    return false;
                }

                if (!workJson.TryGetProperty("completionCriteria", out var workCriteria) ||
                    !IsStringArray(workCriteria))
                {
                    error = $"WORK {workId}: COMPLETION_CRITERIA_REQUIRED";
                    return false;
                }

                var body = workJson.GetRawText();
                workItems[workId] = new(
                    workId,
                    writePaths,
                    workReadOnly,
                    testRequired,
                    body,
                    workJson.GetRawText());
            }

            var resources = new Dictionary<string, MilestoneResourceDefinition>(
                StringComparer.OrdinalIgnoreCase);
            if (!milestoneJson.TryGetProperty("resource", out var resourceJson))
            {
                error = "MILESTONE_RESOURCE_REQUIRED";
                return false;
            }

            if (resourceJson.ValueKind != JsonValueKind.Null)
            {
                if (resourceJson.ValueKind != JsonValueKind.Object)
                {
                    error = "RESOURCE_OBJECT_OR_NULL_REQUIRED";
                    return false;
                }

                var resourceId = TryGetJsonId(resourceJson, "id", out var explicitResourceId)
                    ? explicitResourceId
                    : "0";
                if (!string.Equals(resourceId, "0", StringComparison.Ordinal))
                {
                    error = "RESOURCE_ID_INVALID";
                    return false;
                }

                if (!TryGetRequiredJsonString(resourceJson, "type", out var resourceType) ||
                    !string.Equals(resourceType, "image", StringComparison.OrdinalIgnoreCase))
                {
                    error = "RESOURCE_TYPE_INVALID";
                    return false;
                }

                if (!TryGetRequiredJsonString(resourceJson, "targetPath", out var targetPath))
                {
                    error = "RESOURCE_TARGET_PATH_REQUIRED";
                    return false;
                }

                if (!IsSafeRelativePath(targetPath))
                {
                    error = "RESOURCE 0: TARGET_PATH_OUTSIDE_PROJECT_ROOT";
                    return false;
                }

                if (!TryGetRequiredJsonString(resourceJson, "instructions", out var resourceInstructions))
                {
                    error = "RESOURCE_INSTRUCTIONS_REQUIRED";
                    return false;
                }

                var resourceBody = resourceJson.GetRawText();

                static int? PositiveInt(JsonElement json, string name)
                {
                    if (!json.TryGetProperty(name, out var item) ||
                        item.ValueKind != JsonValueKind.Number)
                        return null;
                    return item.TryGetInt32(out var value) && value > 0 ? value : null;
                }

                resources["0"] = new(
                    "0",
                    resourceType.ToUpperInvariant(),
                    targetPath,
                    resourceBody,
                    resourceJson.GetRawText())
                {
                    Instructions = resourceInstructions,
                    Width = PositiveInt(resourceJson, "width"),
                    Height = PositiveInt(resourceJson, "height"),
                    Columns = PositiveInt(resourceJson, "columns"),
                    Rows = PositiveInt(resourceJson, "rows"),
                    RequireAlpha = resourceJson.TryGetProperty("requireAlpha", out var alpha) &&
                        alpha.ValueKind == JsonValueKind.True
                };
            }

            var qaInstructions = TryGetOptionalJsonString(milestoneJson, "qaInstructions") ?? string.Empty;
            var highInstructions = TryGetOptionalJsonString(milestoneJson, "highInstructions") ?? string.Empty;
            var planDocument = TryGetOptionalJsonString(milestoneJson, "planDocument") ?? string.Empty;
            var qaReserved = workItems.Values.Any(work => work.TestRequired) ||
                             qaInstructions.Length > 0;
            if (qaReserved && !string.IsNullOrWhiteSpace(entrypoint) &&
                !IsSafeRelativePath(entrypoint))
            {
                error = "MILESTONE_ENTRYPOINT_INVALID";
                return false;
            }

            milestone = new(
                milestoneId,
                targetBranch,
                qaReserved,
                entrypoint,
                initializeGitIfMissing,
                readOnlyNoFileChanges,
                milestoneJson.GetRawText(),
                rawMessage,
                workItems,
                resources,
                parse.Errors.ToArray())
            {
                QaInstructions = qaInstructions,
                HighInstructions = highInstructions,
                PlanDocument = planDocument
            };
            return true;
        }
        catch (JsonException exception)
        {
            error = "HQ_WORK_JSON_INVALID: " + exception.Message;
            return false;
        }
    }

    private static bool TryGetRequiredJsonString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static string? TryGetOptionalJsonString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
            return null;

        if (property.ValueKind != JsonValueKind.String)
            return null;

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryGetJsonId(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property))
            return false;

        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty
        };
        return value.Length > 0;
    }

    private static bool TryGetNonEmptyStringArray(
        JsonElement element,
        out string[] values)
    {
        values = Array.Empty<string>();
        if (!IsStringArray(element))
            return false;

        values = element
            .EnumerateArray()
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length > 0;
    }

    private static bool IsStringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return false;

        return element
            .EnumerateArray()
            .All(item => item.ValueKind == JsonValueKind.String);
    }

    public static string BuildWorkContext(
        MilestoneWorkDefinition work)
    {
        var builder = new StringBuilder();
        builder.AppendLine("@@GOAL");
        builder.AppendLine(ReadWorkField(work, "goal") ?? string.Empty);
        builder.AppendLine();
        builder.AppendLine("@@INSTRUCTIONS");
        builder.AppendLine(ReadWorkField(work, "instructions") ?? string.Empty);
        builder.AppendLine();
        builder.AppendLine("@@COMPLETION");
        foreach (var item in ReadWorkStringArray(work, "completionCriteria"))
            builder.AppendLine("- " + item);
        return builder.ToString().TrimEnd();
    }

    public static string BuildQaContext(
        MilestoneDefinition milestone,
        IReadOnlyDictionary<string, string> workReports,
        IReadOnlyList<string>? mechanicalReports = null)
    {
        var builder = new StringBuilder();
        AppendWorkGoals(builder, milestone.WorkItems.Values);
        if (!string.IsNullOrWhiteSpace(milestone.QaInstructions))
        {
            builder.AppendLine("@@QA_INSTRUCTIONS");
            builder.AppendLine(StripRoleFormatOverrides(milestone.QaInstructions));
        }
        if (!string.IsNullOrWhiteSpace(milestone.Entrypoint))
        {
            builder.AppendLine();
            builder.AppendLine("@@TARGET");
            builder.AppendLine(milestone.Entrypoint);
        }
        builder.AppendLine();
        builder.AppendLine("@@WORK_RESULTS");
        foreach (var pair in workReports.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var parsed = RoleTextProtocol.ParseWork(pair.Value);
            builder.AppendLine($"# {pair.Key}");
            builder.AppendLine("STATUS: " + (parsed.IsValid ? parsed.Status : "blocked"));
            builder.AppendLine(parsed.IsValid ? parsed.Summary : pair.Value);
            if (parsed.IsValid)
                foreach (var issue in parsed.Issues)
                    builder.AppendLine("ISSUE: " + issue);
        }
        if (mechanicalReports is not null && mechanicalReports.Count > 0)
        {
            builder.AppendLine("@@BUILD_RESULTS");
            foreach (var report in mechanicalReports)
                builder.AppendLine(Limit(report, 1200));
        }
        return builder.ToString().TrimEnd();
    }

    public static string BuildHighContext(
        MilestoneDefinition milestone,
        IReadOnlyDictionary<string, string> workReports,
        string? qaReport,
        IReadOnlyList<string>? mechanicalReports = null)
    {
        var builder = new StringBuilder();
        AppendWorkGoals(builder, milestone.WorkItems.Values);
        if (!string.IsNullOrWhiteSpace(milestone.HighInstructions))
        {
            builder.AppendLine();
            builder.AppendLine("@@HIGH_INSTRUCTIONS");
            builder.AppendLine(StripRoleFormatOverrides(milestone.HighInstructions));
        }
        builder.AppendLine();
        builder.AppendLine("@@WORK_RESULTS");
        foreach (var pair in workReports.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var parsed = RoleTextProtocol.ParseWork(pair.Value);
            builder.AppendLine($"# {pair.Key}");
            if (parsed.IsValid)
            {
                builder.AppendLine("STATUS: " + parsed.Status);
                builder.AppendLine(parsed.Summary);
                foreach (var issue in parsed.Issues)
                    builder.AppendLine("ISSUE: " + issue);
            }
            else
                builder.AppendLine(pair.Value);
        }

        if (mechanicalReports is not null && mechanicalReports.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("@@BUILD_RESULTS");
            foreach (var report in mechanicalReports)
                builder.AppendLine(Limit(report, 1200));
        }
        if (!string.IsNullOrWhiteSpace(qaReport))
        {
            builder.AppendLine();
            builder.AppendLine("@@QA_REPORT");
            // HIGH receives the complete QA report, including failures and
            // unparsed evidence. Never reduce it to an issues-only summary.
            builder.AppendLine(qaReport);
        }

        return builder.ToString().TrimEnd();
    }

    private static string StripRoleFormatOverrides(string instructions)
    {
        // HQ may describe tests, but it must not redefine the QA/HIGH
        // [ACTION=RESULT] + @@REPORT + <STATUS> output contract.
        return System.Text.RegularExpressions.Regex.Replace(
            instructions ?? string.Empty,
            @"첫\s*줄에\s*ACTION_RESULT:[^.!?\r\n]*[.!?]\s*",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    }

    private static void AppendWorkGoals(
        StringBuilder builder,
        IEnumerable<MilestoneWorkDefinition> works)
    {
        builder.AppendLine("@@WORK_GOALS");
        var any = false;
        foreach (var work in works.OrderBy(work => work.Id, StringComparer.OrdinalIgnoreCase))
        {
            var goal = ReadWorkField(work, "goal");
            if (string.IsNullOrWhiteSpace(goal))
                continue;

            any = true;
            builder.AppendLine($"- #{work.Id}: {goal}");
        }

        if (!any)
            builder.AppendLine("없음");
    }

    private static string? ReadWorkField(
        MilestoneWorkDefinition work,
        string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(work.Body);
            return document.RootElement.TryGetProperty(
                       propertyName,
                       out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadWorkStringArray(
        MilestoneWorkDefinition work,
        string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(work.Body);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()?.Trim() ?? string.Empty)
                    .Where(item => item.Length > 0)
                    .ToArray()
                : Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static void AppendMechanicalReports(
        StringBuilder builder,
        IReadOnlyList<string> mechanicalReports)
    {
        builder.AppendLine("MECHANICAL_RESULTS:");
        if (mechanicalReports.Count == 0)
            builder.AppendLine("- 없음");
        else
            foreach (var report in mechanicalReports)
                builder.AppendLine(report);
    }

    private static void AppendStringArray(
        StringBuilder builder,
        string label,
        IReadOnlyList<string> values)
    {
        builder.AppendLine(label + ":");
        if (values.Count == 0)
            builder.AppendLine("- 없음");
        else
            foreach (var value in values)
                builder.AppendLine("- " + value);
    }

    private static string? ReadQaInstructions(MilestoneDefinition milestone)
    {
        try
        {
            using var document = JsonDocument.Parse(milestone.Body);
            if (!document.RootElement.TryGetProperty("qa", out var qa) ||
                qa.ValueKind != JsonValueKind.Object ||
                !qa.TryGetProperty("instructions", out var instructions) ||
                instructions.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = instructions.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ReadMilestoneGoal(MilestoneDefinition milestone) =>
        ReadMilestoneString(milestone, "goal");

    private static string? ReadMilestoneString(
        MilestoneDefinition milestone,
        string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(milestone.Body);
            if (!document.RootElement.TryGetProperty(propertyName, out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = value.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadMilestonePropertyRaw(
        MilestoneDefinition milestone,
        string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(milestone.Body);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                ? value.GetRawText()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadMilestoneStringArray(
        MilestoneDefinition milestone,
        string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(milestone.Body);
            if (!document.RootElement.TryGetProperty(propertyName, out var value) ||
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
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public static string BuildHqReport(
        MilestoneDefinition milestone,
        string workerOutcome,
        IReadOnlyDictionary<string, string> workReports,
        IReadOnlyDictionary<string, string> resourceReports,
        IReadOnlyList<string> mechanicalReports,
        string qaReport,
        string highReport,
        MilestoneGitResult gitResult,
        IReadOnlyCollection<string> initialLocalChanges,
        IReadOnlyCollection<string> milestoneChanges,
        IReadOnlyCollection<string> currentLocalChanges,
        bool formatRecoveryOccurred = false,
        IReadOnlyCollection<string>? unreadRecoveryElements = null,
        string? workingDirectory = null,
        string? jobId = null)
    {
        _ = initialLocalChanges;
        _ = milestoneChanges;
        _ = currentLocalChanges;

        var archiveReference = string.IsNullOrWhiteSpace(workingDirectory)
            ? "not-written"
            : ArchiveMilestoneReports(
                workingDirectory,
                milestone,
                workerOutcome,
                workReports,
                resourceReports,
                mechanicalReports,
                qaReport,
                highReport,
                gitResult);

        var builder = new StringBuilder();
        builder.AppendLine("MILESTONE_REPORT");
        builder.AppendLine($"MILESTONE: {milestone.Id}");
        builder.AppendLine("OUTCOME: " + workerOutcome.Trim());

        var statuses = workReports
            .Select(pair => (pair.Key, Status: ReadWorkStatus(pair.Value)))
            .ToArray();
        var completedIds = statuses
            .Where(item => string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Key)
            .OrderBy(id => int.Parse(id))
            .ToArray();
        var blockedIds = statuses
            .Where(item => string.Equals(item.Status, "blocked", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Key)
            .OrderBy(id => int.Parse(id))
            .ToArray();
        builder.AppendLine("WORK_COMPLETED: " + (completedIds.Length == 0 ? "none" : string.Join(",", completedIds)));
        builder.AppendLine("WORK_BLOCKED: " + (blockedIds.Length == 0 ? "none" : string.Join(",", blockedIds)));
        builder.AppendLine("NEXT_WORKITEM_ID: " + NextWorkItemId(milestone, workingDirectory, jobId));

        foreach (var pair in workReports.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var work = RoleTextProtocol.ParseWork(pair.Value);
            if (work.IsValid && string.Equals(work.Status, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine("WORK_BLOCKED_DETAIL: " + pair.Key);
                builder.AppendLine("- summary=" + Limit(work.Summary, 800));
                foreach (var issue in work.Issues.Take(3))
                    builder.AppendLine("- issue=" + Limit(issue, 600));
            }
        }

        if (!string.IsNullOrWhiteSpace(qaReport))
        {
            var qa = RoleTextProtocol.ParseQa(qaReport);
            builder.AppendLine("QA_STATUS: " + (qa.IsValid ? qa.Status : "protocol_invalid"));
            if (!qa.IsValid)
                builder.AppendLine("- raw=" + Limit(qaReport.Replace("\n", " | "), 1400));
            else if (!string.Equals(qa.Status, "passed", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine("- summary=" + Limit(qa.Summary, 800));
                foreach (var issue in qa.Issues.Take(3))
                    builder.AppendLine("- issue=" + Limit(issue, 600));
            }
        }

        if (!string.IsNullOrWhiteSpace(highReport))
        {
            var high = RoleTextProtocol.ParseHigh(highReport);
            builder.AppendLine("HIGH_STATUS: " + (high.IsValid ? high.Status : "protocol_invalid"));
            if (!high.IsValid)
                builder.AppendLine("- raw=" + Limit(highReport.Replace("\n", " | "), 1400));
            else
            {
                builder.AppendLine("- summary=" + Limit(high.Summary, 1000));
                foreach (var issue in high.Issues.Take(3))
                    builder.AppendLine("- issue=" + Limit(issue, 600));
            }
        }

        if (resourceReports.Count > 0)
        {
            builder.AppendLine("RESOURCE_RESULTS:");
            foreach (var report in resourceReports.OrderBy(pair => pair.Key))
                builder.AppendLine("- RESOURCE #" + report.Key + ": " +
                    Limit(report.Value.Replace("\r", "").Replace("\n", " | "), 1500));
        }

        if (mechanicalReports.Count > 0)
        {
            builder.AppendLine("MECHANICAL_RESULTS:");
            foreach (var report in mechanicalReports.Take(4))
                builder.AppendLine("- " + Limit(report.Replace("\r", "", StringComparison.Ordinal)
                    .Replace("\n", " | ", StringComparison.Ordinal), 1000));
        }
        builder.AppendLine("GIT_RESULT:");
        builder.AppendLine("- success=" + (gitResult.Success ? "YES" : "NO"));
        builder.AppendLine("- commit=" + (gitResult.CommitSha ?? "none"));
        if (!gitResult.Success)
        {
            // Preserve the mechanical failure reason so HQ can distinguish a
            // rejected GitHub blob from a transient push or local index failure.
            var diagnostic = (gitResult.Summary ?? string.Empty)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("step=", StringComparison.Ordinal) ||
                               line.StartsWith("exit=", StringComparison.Ordinal) ||
                               line.StartsWith("push=", StringComparison.Ordinal) ||
                               line.StartsWith("detail=", StringComparison.Ordinal) ||
                               line.Contains("GH001", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("exceeds GitHub", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("remote rejected", StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToArray();
            builder.AppendLine("- failure=" + Limit(
                string.Join(" | ", diagnostic.Length > 0 ? diagnostic :
                    new[] { gitResult.Summary ?? "unknown" }), 2000));
        }

        if (formatRecoveryOccurred)
        {
            var unread = unreadRecoveryElements?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            builder.AppendLine(unread.Length == 0
                ? "FORMAT_RECOVERY_NOTICE: 역할 응답 전체 재요청 사용"
                : "FORMAT_RECOVERY_NOTICE: 복구 후 읽지 못한 항목: " +
                  string.Join(", ", unread));
        }

        builder.AppendLine("ARCHIVE: " + archiveReference);
        builder.AppendLine("DECISION_REQUIRED: 다음 WORK / PAUSE / END 중 하나를 판단");
        return builder.ToString();
    }

    private static long NextWorkItemId(
        MilestoneDefinition milestone,
        string? workingDirectory,
        string? jobId)
    {
        long highestId = 9;
        foreach (var id in milestone.WorkItems.Keys)
        {
            if (long.TryParse(id, out var workId) && workId >= 10)
                highestId = Math.Max(highestId, workId);
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory) &&
            !string.IsNullOrWhiteSpace(jobId))
        {
            foreach (var entry in ProjectWorkspacePersistence.ReadAllEvents(workingDirectory, jobId))
            {
                if (!string.Equals(entry.Source, "HQ RESPONSE", StringComparison.Ordinal))
                    continue;

                foreach (var line in entry.FullMessage.Split('\n'))
                {
                    var header = line.Trim();
                    if (header.StartsWith("@@WORK ", StringComparison.Ordinal) &&
                        long.TryParse(header["@@WORK ".Length..], out var workId) &&
                        workId >= 10)
                    {
                        highestId = Math.Max(highestId, workId);
                    }
                }
            }
        }

        return highestId + 1;
    }

    public static string? ReadWorkStatus(string message)
    {
        var parsed = RoleTextProtocol.ParseWork(message);
        return parsed.IsValid ? parsed.Status : null;
    }

    public static string? ReadQaStatus(string message)
    {
        var parsed = RoleTextProtocol.ParseQa(message);
        return parsed.IsValid ? parsed.Status : null;
    }

    public static string? ReadHighStatus(string message)
    {
        var parsed = RoleTextProtocol.ParseHigh(message);
        return parsed.IsValid ? parsed.Status : null;
    }

    private static string ArchiveMilestoneReports(
        string workingDirectory,
        MilestoneDefinition milestone,
        string workerOutcome,
        IReadOnlyDictionary<string, string> workReports,
        IReadOnlyDictionary<string, string> resourceReports,
        IReadOnlyList<string> mechanicalReports,
        string qaReport,
        string highReport,
        MilestoneGitResult gitResult)
    {
        try
        {
            WorkerPaths.EnsureProjectHubLocalExclude(workingDirectory);
            var runtime = WorkerPaths.GetRepositoryRuntimePaths(
                workingDirectory);
            var milestoneName = SafeArchiveName(milestone.Id);
            var relative =
                "temp/ProjectHub/reports/" + milestoneName;
            var directory = Path.Combine(
                runtime.Root,
                "reports",
                milestoneName);
            Directory.CreateDirectory(directory);

            File.WriteAllText(Path.Combine(directory, "hq.txt"), milestone.RawHqMessage ?? string.Empty);
            File.WriteAllText(Path.Combine(directory, "outcome.txt"), workerOutcome ?? string.Empty);
            File.WriteAllText(Path.Combine(directory, "qa.txt"), qaReport ?? string.Empty);
            File.WriteAllText(Path.Combine(directory, "high.txt"), highReport ?? string.Empty);
            File.WriteAllText(Path.Combine(directory, "git.txt"), gitResult.Summary ?? string.Empty);

            var workDirectory = Path.Combine(directory, "work");
            Directory.CreateDirectory(workDirectory);
            foreach (var pair in workReports)
                File.WriteAllText(Path.Combine(workDirectory, SafeArchiveName(pair.Key) + ".txt"), pair.Value ?? string.Empty);

            var resourceDirectory = Path.Combine(directory, "resource");
            Directory.CreateDirectory(resourceDirectory);
            foreach (var pair in resourceReports)
                File.WriteAllText(Path.Combine(resourceDirectory, SafeArchiveName(pair.Key) + ".txt"), pair.Value ?? string.Empty);

            return relative;
        }
        catch (Exception exception)
        {
            return "archive-failed:" + exception.GetType().Name;
        }
    }

    private static string SafeArchiveName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = (value ?? string.Empty)
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray();
        var safe = new string(chars).Trim();
        return safe.Length == 0 ? "unknown" : safe;
    }

    public static string FormatMechanicalResult(MilestoneMechanicalResult result) =>
        $"MECHANICAL {result.Operation}" +
        Environment.NewLine +
        $"status={(result.Success ? "COMPLETED" : "FAILED")}" +
        Environment.NewLine +
        $"exitCode={result.ExitCode}" +
        Environment.NewLine +
        $"command={result.Command}" +
        Environment.NewLine +
        $"log={result.LogPath}" +
        Environment.NewLine +
        result.Summary;

    public static string? ReadBodyDirective(string body, string name)
    {
        var prefix = name.Trim() + ":";
        foreach (var line in (body ?? string.Empty)
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed[prefix.Length..].Trim();
        }

        return null;
    }

    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return false;

        var parts = path.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.All(part => part != "..");
    }

    public static bool IsPathInsideRoot(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedRoot, normalizedPath, comparison) ||
               normalizedPath.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   comparison);
    }

    public static string BuildRoleResult(
        string? gotoTarget,
        string status,
        string summary,
        IReadOnlyCollection<string>? changedPaths = null,
        IReadOnlyCollection<string>? issues = null)
    {
        _ = gotoTarget;
        return RoleTextProtocol.BuildResult(
            status,
            summary,
            changedPaths,
            issues);
    }

    public static string NormalizeWorkReport(
        int exitCode,
        string? finalMessage,
        string? standardError)
    {
        if (exitCode != 0)
        {
            return RoleTextProtocol.BuildResult(
                "blocked",
                string.IsNullOrWhiteSpace(standardError)
                    ? "WORK 실행 프로세스 실패"
                    : standardError.Trim());
        }

        var raw = finalMessage?.Trim() ?? string.Empty;
        var parsed = RoleTextProtocol.ParseWork(raw);
        return parsed.IsValid
            ? raw
            : RoleTextProtocol.BuildResult(
                "blocked",
                "WORK_REPORT_CONTRACT_INVALID",
                issues: parsed.Errors);
    }

    public static string NormalizeQaReport(
        int exitCode,
        string? finalMessage,
        string? standardError)
    {
        if (exitCode != 0)
        {
            return RoleTextProtocol.BuildResult(
                "blocked",
                string.IsNullOrWhiteSpace(standardError)
                    ? "QA 실행 프로세스 실패"
                    : standardError.Trim());
        }

        var raw = finalMessage?.Trim() ?? string.Empty;
        var parsed = RoleTextProtocol.ParseQa(raw);
        // Report format errors are not technical QA failures. Keep the
        // original report, even when the structured status cannot be read.
        return raw.Length == 0
            ? RoleTextProtocol.BuildResult("blocked", "QA_REPORT_EMPTY")
            : raw;
    }

    public static string NormalizeHighReport(
        int exitCode,
        string? finalMessage,
        string? standardError)
    {
        if (exitCode != 0)
        {
            return RoleTextProtocol.BuildResult(
                "blocked",
                string.IsNullOrWhiteSpace(standardError)
                    ? "HIGH 실행 프로세스 실패"
                    : standardError.Trim());
        }

        var raw = finalMessage?.Trim() ?? string.Empty;
        var parsed = RoleTextProtocol.ParseHigh(raw);
        return raw.Length == 0
            ? RoleTextProtocol.BuildResult("blocked", "HIGH_REPORT_EMPTY")
            : raw;
    }

    public static IReadOnlyList<string> ExtractHighChangedPaths(string report) =>
        Array.Empty<string>();

    public static IReadOnlyList<string> ExtractReportPaths(
        string report,
        string fieldName)
    {
        if (!string.Equals(
                fieldName,
                "CHANGED_PATH",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                fieldName,
                "changedPaths",
                StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        foreach (var parser in new Func<string?, ActionBlockParseResult>[]
                 {
                     ActionBlockContract.ParseHigh,
                     ActionBlockContract.ParseWork
                 })
        {
            var parsed = parser(report);
            if (!parsed.HasErrors && parsed.ValidActions.Count == 1)
            {
                return ActionBlockContract.GetStringArray(
                        parsed.ValidActions[0],
                        "changedPaths")
                    .Where(IsSafeRelativePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        return Array.Empty<string>();
    }

    public static bool IsTerminalWorkReport(string report)
    {
        var parsed = ActionBlockContract.ParseWork(report);
        if (parsed.HasErrors || parsed.ValidActions.Count != 1)
            return false;

        var status = ActionBlockContract.GetJsonString(
            parsed.ValidActions[0],
            "status");

        return string.Equals(
                   status,
                   "completed",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   status,
                   "blocked",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string Limit(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        return value[..maxLength] + Environment.NewLine + "[truncated]";
    }

    private static void AppendPaths(
        StringBuilder builder,
        IEnumerable<string> paths)
    {
        var values = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (values.Length == 0)
        {
            builder.AppendLine("- 없음");
            return;
        }

        foreach (var path in values)
            builder.AppendLine("- " + path);
    }

    private static void AppendReports(
        StringBuilder builder,
        string title,
        IReadOnlyDictionary<string, string> reports)
    {
        builder.AppendLine(title + ":");
        if (reports.Count == 0)
        {
            builder.AppendLine("- 없음");
            return;
        }

        foreach (var pair in reports.OrderBy(
                     pair => pair.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"--- {pair.Key} ---");
            builder.AppendLine(pair.Value);
        }
    }
}
