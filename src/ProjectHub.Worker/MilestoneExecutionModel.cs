using System.Text;
using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record MilestoneWorkDefinition(
    string Id,
    IReadOnlyList<string> WritePaths,
    bool ReadOnly,
    string Body,
    string RawText);

internal sealed record MilestoneResourceDefinition(
    string Id,
    string Type,
    string TargetPath,
    string Body,
    string RawText);

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
    IReadOnlyList<string> ParseErrors);

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

            if (!milestoneJson.TryGetProperty("qa", out var qaJson) ||
                qaJson.ValueKind != JsonValueKind.Object ||
                !qaJson.TryGetProperty("required", out var qaRequiredJson) ||
                qaRequiredJson.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                error = "MILESTONE_QA_REQUIRED";
                return false;
            }

            var qaReserved = qaRequiredJson.GetBoolean();
            if (!qaJson.TryGetProperty("instructions", out var qaInstructions) ||
                qaInstructions.ValueKind != JsonValueKind.String)
            {
                error = "MILESTONE_QA_INSTRUCTIONS_REQUIRED";
                return false;
            }

            if (qaReserved && string.IsNullOrWhiteSpace(qaInstructions.GetString()))
            {
                error = "MILESTONE_QA_INSTRUCTIONS_REQUIRED";
                return false;
            }

            if (!milestoneJson.TryGetProperty("completionCriteria", out var completionCriteria) ||
                !IsStringArray(completionCriteria))
            {
                error = "MILESTONE_COMPLETION_CRITERIA_REQUIRED";
                return false;
            }

            if (!milestoneJson.TryGetProperty("validation", out var validation) ||
                !IsStringArray(validation))
            {
                error = "MILESTONE_VALIDATION_REQUIRED";
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

                var body = BuildWorkBody(workJson, workGoal);
                workItems[workId] = new(
                    workId,
                    writePaths,
                    workReadOnly,
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

                var resourceBody =
                    resourceInstructions +
                    Environment.NewLine +
                    Environment.NewLine +
                    "HQ_RESOURCE_JSON:" +
                    Environment.NewLine +
                    resourceJson.GetRawText();

                resources["0"] = new(
                    "0",
                    resourceType.ToUpperInvariant(),
                    targetPath,
                    resourceBody,
                    resourceJson.GetRawText());
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
                parse.Errors.ToArray());
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

    private static string BuildWorkBody(
        JsonElement workJson,
        string goal)
    {
        var builder = new StringBuilder();
        builder.AppendLine(goal);

        var instructions = TryGetOptionalJsonString(
            workJson,
            "instructions");
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            builder.AppendLine();
            builder.AppendLine("세부 지시:");
            builder.AppendLine(instructions);
        }

        if (workJson.TryGetProperty("completionCriteria", out var criteria) &&
            criteria.ValueKind == JsonValueKind.Array)
        {
            var items = criteria
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            if (items.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("완료 기준:");
                foreach (var item in items)
                    builder.AppendLine("- " + item);
            }
        }

        builder.AppendLine();
        builder.AppendLine("HQ_WORK_ITEM_JSON:");
        builder.AppendLine(workJson.GetRawText());

        return builder.ToString().Trim();
    }

    public static string BuildManagerInput(
        MilestoneDefinition milestone,
        IReadOnlyDictionary<string, string> workReports,
        IReadOnlyDictionary<string, string> resourceReports,
        IReadOnlyList<string> mechanicalReports,
        string qaReport,
        string highReport,
        MilestoneGitResult gitResult,
        string eventBody)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"MILESTONE_ID: {milestone.Id}");
        builder.AppendLine($"TARGET_BRANCH: {milestone.TargetBranch}");
        builder.AppendLine($"QA_RESERVED: {(milestone.QaReserved ? "YES" : "NO")}");
        builder.AppendLine($"ENTRYPOINT: {milestone.Entrypoint ?? "없음"}");
        builder.AppendLine($"GIT_INIT_IF_MISSING: {(milestone.InitializeGitIfMissing ? "YES" : "NO")}");
        builder.AppendLine($"PROJECT_POLICY: {(milestone.ReadOnlyNoFileChanges ? "READ_ONLY_NO_FILE_CHANGES" : "DEFAULT")}");
        builder.AppendLine("HQ_MILESTONE_DESIGN:");
        builder.AppendLine(milestone.Body);

        if (milestone.ParseErrors.Count > 0)
        {
            builder.AppendLine("HQ_ACTION_PARSE_ERRORS:");
            foreach (var parseError in milestone.ParseErrors)
                builder.AppendLine("- " + parseError);
        }

        builder.AppendLine("PLANNED_WORK:");
        if (milestone.WorkItems.Count == 0)
        {
            builder.AppendLine("- 없음");
        }
        else
        {
            foreach (var work in milestone.WorkItems.Values)
            {
                builder.AppendLine(
                    $"- #{work.Id} MODE={(work.ReadOnly ? "READ_ONLY" : "WRITE")} WRITE_PATH={(work.WritePaths.Count == 0 ? "없음" : string.Join(", ", work.WritePaths))}");
                builder.AppendLine("  " + work.Body.Replace(
                    Environment.NewLine,
                    " "));
            }
        }

        builder.AppendLine("PLANNED_RESOURCE:");
        if (milestone.Resources.Count == 0)
        {
            builder.AppendLine("- 없음");
        }
        else
        {
            foreach (var resource in milestone.Resources.Values)
            {
                builder.AppendLine(
                    $"- #{resource.Id} {resource.Type} -> {resource.TargetPath}");
            }
        }

        AppendReports(builder, "WORK_RESULTS", workReports);
        AppendReports(builder, "RESOURCE_RESULTS", resourceReports);

        builder.AppendLine("MECHANICAL_RESULTS:");
        if (mechanicalReports.Count == 0)
        {
            builder.AppendLine("- 없음");
        }
        else
        {
            foreach (var report in mechanicalReports)
                builder.AppendLine(report);
        }

        builder.AppendLine("QA_REPORT:");
        builder.AppendLine(string.IsNullOrWhiteSpace(qaReport) ? "없음" : qaReport);
        builder.AppendLine("HIGH_REPORT:");
        builder.AppendLine(string.IsNullOrWhiteSpace(highReport) ? "없음" : highReport);
        builder.AppendLine("GIT_RESULT:");
        builder.AppendLine(gitResult.Summary);
        builder.AppendLine("CURRENT_EVENT:");
        builder.AppendLine(eventBody);
        return builder.ToString();
    }

    public static string BuildValidationContext(
        MilestoneDefinition milestone,
        IReadOnlyDictionary<string, string> workReports,
        IReadOnlyDictionary<string, string> resourceReports,
        IReadOnlyList<string> mechanicalReports,
        string? qaReport)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"MILESTONE_ID: {milestone.Id}");
        builder.AppendLine($"ENTRYPOINT: {milestone.Entrypoint ?? "없음"}");
        builder.AppendLine("HQ_DESIGN:");
        builder.AppendLine(milestone.Body);
        builder.AppendLine("WORK_ITEM_INSTRUCTIONS:");
        if (milestone.WorkItems.Count == 0)
        {
            builder.AppendLine("- 없음");
        }
        else
        {
            foreach (var work in milestone.WorkItems.Values.OrderBy(
                         work => work.Id,
                         StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"--- WORK {work.Id} ---");
                builder.AppendLine(work.Body);
            }
        }
        AppendReports(builder, "WORK_RESULTS", workReports);
        AppendReports(builder, "RESOURCE_RESULTS", resourceReports);
        builder.AppendLine("MECHANICAL_RESULTS:");
        if (mechanicalReports.Count == 0)
            builder.AppendLine("- 없음");
        else
            foreach (var report in mechanicalReports)
                builder.AppendLine(report);

        if (!string.IsNullOrWhiteSpace(qaReport))
        {
            builder.AppendLine("QA_REPORT:");
            builder.AppendLine(qaReport);
        }

        return builder.ToString();
    }

    public static string BuildHqReport(
        MilestoneDefinition milestone,
        string managerMessage,
        IReadOnlyDictionary<string, string> workReports,
        IReadOnlyDictionary<string, string> resourceReports,
        IReadOnlyList<string> mechanicalReports,
        string qaReport,
        string highReport,
        MilestoneGitResult gitResult,
        IReadOnlyCollection<string> initialLocalChanges,
        IReadOnlyCollection<string> milestoneChanges,
        IReadOnlyCollection<string> currentLocalChanges)
    {
        // Detailed WORK/HIGH changed paths and dirty snapshots remain Worker
        // mechanical state. HQ receives only the semantic integration result.
        _ = workReports;
        _ = mechanicalReports;
        _ = qaReport;
        _ = highReport;
        _ = initialLocalChanges;
        _ = milestoneChanges;
        _ = currentLocalChanges;

        var builder = new StringBuilder();
        builder.AppendLine("MILESTONE_RESULT");
        builder.AppendLine($"MILESTONE_ID: {milestone.Id}");
        builder.AppendLine("TARGET_BRANCH: main");
        builder.AppendLine("REMOTE_BRANCH: origin/main");
        builder.AppendLine("RESOURCE_STATE_AT_REPORT:");
        if (resourceReports.Count == 0)
        {
            builder.AppendLine("- 없음");
        }
        else
        {
            foreach (var pair in resourceReports.OrderBy(
                         pair => pair.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"--- {pair.Key} ---");
                builder.AppendLine(Limit(pair.Value, 2000));
            }
        }

        builder.AppendLine("GIT_RESULT:");
        builder.AppendLine(Limit(gitResult.Summary, 4000));
        builder.AppendLine("MANAGER_FINAL_REPORT:");
        builder.AppendLine(managerMessage);
        return builder.ToString();
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
        var payload = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["summary"] = summary,
            ["changedPaths"] = changedPaths?.ToArray() ?? Array.Empty<string>(),
            ["issues"] = issues?.ToArray() ?? Array.Empty<string>()
        };

        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        var prefix = string.IsNullOrWhiteSpace(gotoTarget)
            ? string.Empty
            : "[GOTO : " + gotoTarget.Trim().ToUpperInvariant() + "]" +
              Environment.NewLine;

        return prefix +
               "[ACTION=RESULT]" +
               Environment.NewLine +
               json;
    }

    public static string NormalizeWorkReport(
        int exitCode,
        string? finalMessage,
        string? standardError)
    {
        if (exitCode != 0)
        {
            return BuildRoleResult(
                null,
                "blocked",
                string.IsNullOrWhiteSpace(standardError)
                    ? "WORK 실행 프로세스 실패"
                    : standardError.Trim());
        }

        var raw = finalMessage?.Trim() ?? string.Empty;
        var parsed = ActionBlockContract.ParseWork(raw);
        if (parsed.HasErrors ||
            parsed.ValidActions.Count != 1 ||
            !string.Equals(
                parsed.ValidActions[0].Name,
                "RESULT",
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildRoleResult(
                null,
                "blocked",
                "WORK_REPORT_CONTRACT_INVALID",
                issues: parsed.Errors.Count == 0
                    ? new[] { "RESULT action required" }
                    : parsed.Errors);
        }

        return raw;
    }

    public static string NormalizeQaReport(
        int exitCode,
        string? finalMessage,
        string? standardError)
    {
        if (exitCode != 0)
        {
            return BuildRoleResult(
                "HIGH",
                "blocked",
                string.IsNullOrWhiteSpace(standardError)
                    ? "QA 실행 프로세스 실패"
                    : standardError.Trim());
        }

        var raw = finalMessage?.Trim() ?? string.Empty;
        var parsed = ActionBlockContract.ParseQa(raw);
        if (parsed.HasErrors ||
            parsed.ValidActions.Count != 1 ||
            !string.Equals(
                parsed.ValidActions[0].Name,
                "RESULT",
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildRoleResult(
                "HIGH",
                "blocked",
                "QA_REPORT_CONTRACT_INVALID",
                issues: parsed.Errors.Count == 0
                    ? new[] { "RESULT action required" }
                    : parsed.Errors);
        }

        return raw;
    }

    public static string NormalizeHighReport(
        int exitCode,
        string? finalMessage,
        string? standardError)
    {
        if (exitCode != 0)
        {
            return BuildRoleResult(
                "MANAGER",
                "incomplete",
                string.IsNullOrWhiteSpace(standardError)
                    ? "HIGH 실행 프로세스 실패"
                    : standardError.Trim());
        }

        var raw = finalMessage?.Trim() ?? string.Empty;
        var parsed = ActionBlockContract.ParseHigh(raw);
        if (parsed.HasErrors ||
            parsed.ValidActions.Count != 1 ||
            !string.Equals(
                parsed.ValidActions[0].Name,
                "RESULT",
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildRoleResult(
                "MANAGER",
                "incomplete",
                "HIGH_REPORT_CONTRACT_INVALID",
                issues: parsed.Errors.Count == 0
                    ? new[] { "RESULT action required" }
                    : parsed.Errors);
        }

        return raw;
    }

    public static IReadOnlyList<string> ExtractHighChangedPaths(
        string report)
    {
        var parsed = ActionBlockContract.ParseHigh(report);
        if (parsed.HasErrors || parsed.ValidActions.Count != 1)
            return Array.Empty<string>();

        var action = parsed.ValidActions[0];
        var status = ActionBlockContract.GetJsonString(action, "status");
        if (!string.Equals(
                status,
                "modified",
                StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        return ActionBlockContract.GetStringArray(action, "changedPaths")
            .Where(IsSafeRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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
