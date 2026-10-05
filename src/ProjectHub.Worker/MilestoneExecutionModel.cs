using System.Text;
using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record MilestoneWorkDefinition(
    string Id,
    IReadOnlyList<string> WritePaths,
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

                if (!workJson.TryGetProperty("writePaths", out var writePathsJson) ||
                    !TryGetNonEmptyStringArray(writePathsJson, out var writePaths))
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
                    $"- #{work.Id} WRITE_PATH={string.Join(", ", work.WritePaths)}");
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
        var builder = new StringBuilder();
        builder.AppendLine("MILESTONE_RESULT");
        builder.AppendLine($"MILESTONE_ID: {milestone.Id}");
        AppendReports(builder, "WORK_RESULTS", workReports);
        AppendReports(builder, "RESOURCE_RESULTS", resourceReports);
        builder.AppendLine("MECHANICAL_RESULTS:");
        if (mechanicalReports.Count == 0)
            builder.AppendLine("- 없음");
        else
            foreach (var report in mechanicalReports)
                builder.AppendLine(report);
        builder.AppendLine("QA_REPORT:");
        builder.AppendLine(string.IsNullOrWhiteSpace(qaReport) ? "없음" : qaReport);
        builder.AppendLine("HIGH_REPORT:");
        builder.AppendLine(string.IsNullOrWhiteSpace(highReport) ? "없음" : highReport);
        builder.AppendLine("MILESTONE_CHANGESET:");
        AppendPaths(builder, milestoneChanges);
        builder.AppendLine("INITIAL_LOCAL_CHANGES:");
        AppendPaths(builder, initialLocalChanges);
        builder.AppendLine("CURRENT_LOCAL_CHANGES:");
        AppendPaths(builder, currentLocalChanges);
        builder.AppendLine("GIT_RESULT:");
        builder.AppendLine(gitResult.Summary);
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

    public static string NormalizeWorkReport(
        int exitCode,
        string? finalMessage,
        string? standardError)
    {
        if (exitCode != 0)
        {
            return
                "[GOTO : MANAGER]" +
                Environment.NewLine +
                "WORK_ITEM_STATUS: BLOCKED" +
                Environment.NewLine +
                (string.IsNullOrWhiteSpace(standardError)
                    ? "WORK 실행 프로세스 실패"
                    : standardError.Trim());
        }

        var raw = finalMessage?.Trim() ?? string.Empty;
        var statusCount =
            CountExactLine(raw, "WORK_ITEM_STATUS: COMPLETED") +
            CountExactLine(raw, "WORK_ITEM_STATUS: BLOCKED");

        if (!HasFirstGoto(raw, "MANAGER") || statusCount != 1)
        {
            return
                "[GOTO : MANAGER]" +
                Environment.NewLine +
                "WORK_ITEM_STATUS: BLOCKED" +
                Environment.NewLine +
                "WORK_REPORT_CONTRACT_INVALID" +
                Environment.NewLine +
                "원본 응답:" +
                Environment.NewLine +
                raw;
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
            return
                "QA_STATUS: BLOCKED" +
                Environment.NewLine +
                (string.IsNullOrWhiteSpace(standardError)
                    ? "QA 실행 프로세스 실패"
                    : standardError.Trim());
        }

        var raw = finalMessage?.Trim() ?? string.Empty;
        var statusCount =
            CountExactLine(raw, "QA_STATUS: COMPLETED") +
            CountExactLine(raw, "QA_STATUS: BLOCKED");

        if (statusCount != 1)
        {
            return
                "QA_STATUS: BLOCKED" +
                Environment.NewLine +
                "QA_REPORT_CONTRACT_INVALID" +
                Environment.NewLine +
                "원본 응답:" +
                Environment.NewLine +
                raw;
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
            return
                "[GOTO : MANAGER]" +
                Environment.NewLine +
                "HIGH_STATUS: INCOMPLETE" +
                Environment.NewLine +
                (string.IsNullOrWhiteSpace(standardError)
                    ? "HIGH 실행 프로세스 실패"
                    : standardError.Trim());
        }

        var raw = finalMessage?.Trim() ?? string.Empty;
        var statusCount =
            CountExactLine(raw, "HIGH_STATUS: VERIFIED") +
            CountExactLine(raw, "HIGH_STATUS: MODIFIED") +
            CountExactLine(raw, "HIGH_STATUS: INCOMPLETE");

        var modified =
            CountExactLine(raw, "HIGH_STATUS: MODIFIED") == 1;
        var changedPaths = ExtractReportPaths(raw, "CHANGED_PATH");
        var changedPathsValid =
            changedPaths.Count > 0 &&
            changedPaths.All(IsSafeRelativePath);

        if (!HasFirstGoto(raw, "MANAGER") ||
            statusCount != 1 ||
            (modified && !changedPathsValid))
        {
            return
                "[GOTO : MANAGER]" +
                Environment.NewLine +
                "HIGH_STATUS: INCOMPLETE" +
                Environment.NewLine +
                "HIGH_REPORT_CONTRACT_INVALID" +
                Environment.NewLine +
                "원본 응답:" +
                Environment.NewLine +
                raw;
        }

        return raw;
    }

    public static IReadOnlyList<string> ExtractHighChangedPaths(
        string report)
    {
        if (CountExactLine(report, "HIGH_STATUS: MODIFIED") != 1 ||
            CountExactLine(report, "HIGH_STATUS: VERIFIED") != 0 ||
            CountExactLine(report, "HIGH_STATUS: INCOMPLETE") != 0)
        {
            return Array.Empty<string>();
        }

        return ExtractReportPaths(report, "CHANGED_PATH")
            .Where(IsSafeRelativePath)
            .ToArray();
    }

    public static IReadOnlyList<string> ExtractReportPaths(
        string report,
        string fieldName)
    {
        var prefix = fieldName.Trim() + ":";
        return (report ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
            .Select(line => line[prefix.Length..].Trim())
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsTerminalWorkReport(string report) =>
        CountExactLine(report, "WORK_ITEM_STATUS: COMPLETED") == 1 ||
        CountExactLine(report, "WORK_ITEM_STATUS: BLOCKED") == 1;

    private static bool HasFirstGoto(string text, string role)
    {
        var first = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?.Trim();

        return string.Equals(
            first,
            "[GOTO : " + role + "]",
            StringComparison.OrdinalIgnoreCase);
    }

    private static int CountExactLine(string text, string expected) =>
        (text ?? string.Empty)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n')
        .Count(line => string.Equals(
            line.Trim(),
            expected,
            StringComparison.Ordinal));

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
