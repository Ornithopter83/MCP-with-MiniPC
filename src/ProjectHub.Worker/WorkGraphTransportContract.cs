using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectHub.Worker;

public static class WorkGraphTransportContract
{
    private const string Marker = "WORK_GRAPH_PATCH:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedHqOperationAliases = new(
        new[]
        {
            "ADD",
            "CANCEL",
            "SET_DEPENDENCIES",
            "SET_GOAL",
            "SET_BASE_REF",
            "RELEASE"
        },
        StringComparer.OrdinalIgnoreCase);

    public static bool TryParse(string? body, out WorkGraphPatch? patch, out string? error)
    {
        patch = null;
        error = null;

        var normalized = (body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var markerIndexes = lines
            .Select((line, index) => (Line: line.Trim(), Index: index))
            .Where(item => item.Line.StartsWith(Marker, StringComparison.Ordinal))
            .Select(item => item.Index)
            .ToArray();

        if (markerIndexes.Length == 0)
        {
            error = "WORK_GRAPH_PATCH_MARKER_MISSING";
            return false;
        }

        if (markerIndexes.Length > 1)
        {
            error = "WORK_GRAPH_PATCH_MARKER_DUPLICATE";
            return false;
        }

        var markerIndex = markerIndexes[0];
        var markerLine = lines[markerIndex].Trim();
        var inlinePayload = markerLine[Marker.Length..].Trim();
        var payload = string.Join("\n", new[]
        {
            inlinePayload,
            string.Join("\n", lines.Skip(markerIndex + 1))
        }.Where(value => value.Length > 0));

        return TryParseJsonPayload(payload, out patch, out error);
    }

    public static bool TryParseJsonPayload(
        string? payload,
        out WorkGraphPatch? patch,
        out string? error)
    {
        patch = null;
        error = null;

        if (!TryExtractFirstJsonObject(payload ?? string.Empty, out var json, out var jsonFound))
        {
            error = jsonFound
                ? "WORK_GRAPH_PATCH_JSON_INVALID"
                : "WORK_GRAPH_PATCH_JSON_MISSING";
            return false;
        }

        return TryParseJsonObject(json, out patch, out error);
    }

    public static bool TryRepairOperationTypeAliases(
        string rawPayload,
        out string repairedPayload,
        out string? repairSummary)
    {
        repairedPayload = rawPayload ?? string.Empty;
        repairSummary = null;

        if (!TryExtractFirstJsonObject(repairedPayload, out var json, out _))
            return false;

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsed is not JsonObject root ||
            root["operations"] is not JsonArray operations)
            return false;

        var repairs = new List<(JsonObject Operation, int Index, string Alias)>();
        for (var index = 0; index < operations.Count; index++)
        {
            if (operations[index] is not JsonObject operation)
                return false;

            if (TryGetNonBlankString(operation, "type", out _))
                continue;

            if (!TryGetNonBlankString(operation, "operation", out var alias) ||
                !SupportedHqOperationAliases.Contains(alias))
                return false;

            repairs.Add((operation, index, alias));
        }

        if (repairs.Count == 0)
            return false;

        foreach (var repair in repairs)
        {
            repair.Operation["type"] = repair.Alias;
            repair.Operation.Remove("operation");
        }

        repairedPayload = root.ToJsonString();
        repairSummary = string.Join(
            "; ",
            repairs.Select(repair =>
                $"operations[{repair.Index}].operation -> operations[{repair.Index}].type"));
        return true;
    }

    public static string? DescribeError(
        string? payload,
        string? errorCode)
    {
        if (!TryExtractFirstJsonObject(payload ?? string.Empty, out var json, out _))
        {
            return string.Equals(
                    errorCode,
                    "WORK_GRAPH_OPERATION_TYPE_MISSING",
                    StringComparison.Ordinal)
                ? "path=operations[*].type" + Environment.NewLine +
                  "message=Required field \"type\" is missing, but the WorkGraph JSON object could not be inspected."
                : null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("operations", out var operations) ||
                operations.ValueKind != JsonValueKind.Array)
                return null;

            var index = 0;
            foreach (var operation in operations.EnumerateArray())
            {
                if (operation.ValueKind != JsonValueKind.Object)
                {
                    index++;
                    continue;
                }

                var keys = operation
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .ToArray();

                var type = operation.TryGetProperty("type", out var typeElement) &&
                           typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()?.Trim().ToUpperInvariant()
                    : null;

                if (string.Equals(
                        errorCode,
                        "WORK_GRAPH_OPERATION_TYPE_MISSING",
                        StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(type))
                {
                    string hint;
                    if (operation.TryGetProperty("operation", out var aliasElement) &&
                        aliasElement.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(aliasElement.GetString()))
                    {
                        var alias = aliasElement.GetString()!.Trim();
                        hint = SupportedHqOperationAliases.Contains(alias)
                            ? $"Use \"type\":\"{alias}\". The received \"operation\" key is only a compatibility alias and should be normalized to \"type\"."
                            : $"Use the \"type\" field with a supported operation name. Received unsupported alias value \"{alias}\" in \"operation\".";
                    }
                    else
                    {
                        hint = "Use the \"type\" field with one of ADD, CANCEL, SET_DEPENDENCIES, SET_GOAL, SET_BASE_REF, RELEASE.";
                    }

                    return
                        $"path=operations[{index}].type" + Environment.NewLine +
                        "message=Required field \"type\" is missing." + Environment.NewLine +
                        "receivedKeys=" + JsonSerializer.Serialize(keys, JsonOptions) + Environment.NewLine +
                        "hint=" + hint;
                }

                if (string.Equals(
                        errorCode,
                        "WORK_GRAPH_SET_GOAL_SCHEMA_INVALID",
                        StringComparison.Ordinal) &&
                    string.Equals(type, "SET_GOAL", StringComparison.Ordinal))
                {
                    return
                        $"path=operations[{index}].value" + Environment.NewLine +
                        "message=SET_GOAL requires a nonblank \"value\" field." + Environment.NewLine +
                        "receivedKeys=" + JsonSerializer.Serialize(keys, JsonOptions) + Environment.NewLine +
                        "hint=For SET_GOAL put the new goal text in \"value\". The \"goal\" field is used by ADD, not SET_GOAL.";
                }

                if (string.Equals(
                        errorCode,
                        "WORK_GRAPH_ADD_SCHEMA_INVALID",
                        StringComparison.Ordinal) &&
                    string.Equals(type, "ADD", StringComparison.Ordinal))
                {
                    return
                        $"path=operations[{index}]" + Environment.NewLine +
                        "message=ADD requires a safe workItemId and a nonblank \"goal\" field." + Environment.NewLine +
                        "receivedKeys=" + JsonSerializer.Serialize(keys, JsonOptions) + Environment.NewLine +
                        "hint=For ADD use \"goal\" for the WorkItem goal; \"value\" is not the ADD goal field.";
                }

                if (string.Equals(
                        errorCode,
                        "WORK_GRAPH_KIND_INVALID",
                        StringComparison.Ordinal) &&
                    string.Equals(type, "ADD", StringComparison.Ordinal))
                {
                    return
                        $"path=operations[{index}].kind" + Environment.NewLine +
                        "message=ADD kind must be NORMAL or INTEGRATION." + Environment.NewLine +
                        "receivedKeys=" + JsonSerializer.Serialize(keys, JsonOptions) + Environment.NewLine +
                        "hint=Use \"kind\":\"NORMAL\" or \"kind\":\"INTEGRATION\".";
                }

                if (string.Equals(
                        errorCode,
                        "WORK_GRAPH_SET_BASE_REF_SCHEMA_INVALID",
                        StringComparison.Ordinal) &&
                    string.Equals(type, "SET_BASE_REF", StringComparison.Ordinal))
                {
                    return
                        $"path=operations[{index}].value" + Environment.NewLine +
                        "message=SET_BASE_REF uses the \"value\" field for the new base ref." + Environment.NewLine +
                        "receivedKeys=" + JsonSerializer.Serialize(keys, JsonOptions) + Environment.NewLine +
                        "hint=Do not use \"baseRef\" on SET_BASE_REF. Put that same ref in \"value\".";
                }

                if (string.Equals(
                        errorCode,
                        "WORK_GRAPH_RELEASE_SCHEMA_INVALID",
                        StringComparison.Ordinal) &&
                    string.Equals(type, "RELEASE", StringComparison.Ordinal))
                {
                    return
                        $"path=operations[{index}].value" + Environment.NewLine +
                        "message=RELEASE uses optional \"value\" for the resume body." + Environment.NewLine +
                        "receivedKeys=" + JsonSerializer.Serialize(keys, JsonOptions) + Environment.NewLine +
                        "hint=Do not use \"body\" on RELEASE. Put the same resume body in \"value\".";
                }

                if (string.Equals(
                        errorCode,
                        "WORK_GRAPH_OPERATION_UNSUPPORTED",
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(type))
                {
                    return
                        $"path=operations[{index}].type" + Environment.NewLine +
                        $"message=Unsupported WorkGraph operation \"{type}\"." + Environment.NewLine +
                        "receivedKeys=" + JsonSerializer.Serialize(keys, JsonOptions) + Environment.NewLine +
                        "hint=Use one of ADD, CANCEL, SET_DEPENDENCIES, SET_GOAL, SET_BASE_REF, RELEASE.";
                }

                index++;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool TryGetNonBlankString(
        JsonObject value,
        string propertyName,
        out string result)
    {
        result = string.Empty;
        if (!value.TryGetPropertyValue(propertyName, out var node) ||
            node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var text) ||
            string.IsNullOrWhiteSpace(text))
            return false;

        result = text.Trim();
        return true;
    }

    private static bool TryRejectMisleadingOperationFields(
        string json,
        out string? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("operations", out var operations) ||
                operations.ValueKind != JsonValueKind.Array)
                return true;

            foreach (var operation in operations.EnumerateArray())
            {
                if (operation.ValueKind != JsonValueKind.Object ||
                    !operation.TryGetProperty("type", out var typeElement) ||
                    typeElement.ValueKind != JsonValueKind.String)
                    continue;

                var type = typeElement.GetString()?.Trim().ToUpperInvariant();
                var hasValue = operation.TryGetProperty("value", out _);

                if (string.Equals(type, "SET_BASE_REF", StringComparison.Ordinal) &&
                    !hasValue &&
                    operation.TryGetProperty("baseRef", out _))
                {
                    error = "WORK_GRAPH_SET_BASE_REF_SCHEMA_INVALID";
                    return false;
                }

                if (string.Equals(type, "RELEASE", StringComparison.Ordinal) &&
                    !hasValue &&
                    operation.TryGetProperty("body", out _))
                {
                    error = "WORK_GRAPH_RELEASE_SCHEMA_INVALID";
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            error = "WORK_GRAPH_PATCH_JSON_INVALID";
            return false;
        }
    }

    private static bool TryParseJsonObject(
        string json,
        out WorkGraphPatch? patch,
        out string? error)
    {
        patch = null;
        error = null;

        if (!TryRejectMisleadingOperationFields(json, out error))
            return false;

        PatchDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PatchDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            error = "WORK_GRAPH_PATCH_JSON_INVALID";
            return false;
        }

        if (dto is null || dto.ExpectedRevision is null || dto.ExpectedRevision < 0 || dto.Operations is null)
        {
            error = "WORK_GRAPH_PATCH_SCHEMA_INVALID";
            return false;
        }

        var operations = new List<WorkGraphPatchOperation>();
        foreach (var operation in dto.Operations)
        {
            if (!TryMapOperation(operation, out var mapped, out error))
                return false;
            operations.Add(mapped!);
        }

        patch = new WorkGraphPatch(dto.ExpectedRevision.Value, operations);
        return true;
    }

    private static bool TryExtractFirstJsonObject(
        string payload,
        out string json,
        out bool jsonFound)
    {
        json = string.Empty;
        jsonFound = false;

        for (var searchStart = 0; searchStart < payload.Length;)
        {
            var objectStart = payload.IndexOf('{', searchStart);
            if (objectStart < 0)
                return false;

            jsonFound = true;
            var bytes = Encoding.UTF8.GetBytes(payload[objectStart..]);
            try
            {
                var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                {
                    searchStart = objectStart + 1;
                    continue;
                }

                using var document = JsonDocument.ParseValue(ref reader);
                json = document.RootElement.GetRawText();
                return true;
            }
            catch (JsonException)
            {
                searchStart = objectStart + 1;
            }
        }

        return false;
    }

    private static bool TryMapOperation(
        OperationDto? operation,
        out WorkGraphPatchOperation? mapped,
        out string? error)
    {
        mapped = null;
        error = null;

        if (operation is null || string.IsNullOrWhiteSpace(operation.Type))
        {
            error = "WORK_GRAPH_OPERATION_TYPE_MISSING";
            return false;
        }

        var type = operation.Type.Trim().ToUpperInvariant();
        var id = NormalizeId(operation.WorkItemId);

        switch (type)
        {
            case "ADD":
                if (!IsSafeId(id) || string.IsNullOrWhiteSpace(operation.Goal))
                {
                    error = "WORK_GRAPH_ADD_SCHEMA_INVALID";
                    return false;
                }
                if (!TryParseKind(operation.Kind, out var kind))
                {
                    error = "WORK_GRAPH_KIND_INVALID";
                    return false;
                }
                mapped = WorkGraphPatchOperation.Add(new WorkItemSpec(
                    id,
                    operation.Goal.Trim(),
                    NormalizeDependencies(operation.Dependencies),
                    kind,
                    NullIfWhiteSpace(operation.BaseRef)));
                return true;

            case "CANCEL":
                if (!IsSafeId(id)) return Fail("WORK_GRAPH_WORK_ITEM_ID_INVALID", out mapped, out error);
                mapped = WorkGraphPatchOperation.Cancel(id);
                return true;

            case "SET_DEPENDENCIES":
                if (!IsSafeId(id)) return Fail("WORK_GRAPH_WORK_ITEM_ID_INVALID", out mapped, out error);
                mapped = WorkGraphPatchOperation.SetDependencies(id, NormalizeDependencies(operation.Dependencies).ToArray());
                return true;

            case "SET_GOAL":
                if (!IsSafeId(id) || string.IsNullOrWhiteSpace(operation.Value))
                    return Fail("WORK_GRAPH_SET_GOAL_SCHEMA_INVALID", out mapped, out error);
                mapped = WorkGraphPatchOperation.SetGoal(id, operation.Value.Trim());
                return true;

            case "SET_BASE_REF":
                if (!IsSafeId(id))
                    return Fail("WORK_GRAPH_WORK_ITEM_ID_INVALID", out mapped, out error);
                mapped = WorkGraphPatchOperation.SetBaseRef(id, NullIfWhiteSpace(operation.Value));
                return true;

            case "RELEASE":
                if (!IsSafeId(id))
                    return Fail("WORK_GRAPH_WORK_ITEM_ID_INVALID", out mapped, out error);
                mapped = WorkGraphPatchOperation.Release(
                    id,
                    NullIfWhiteSpace(operation.InputType),
                    NullIfWhiteSpace(operation.Value));
                return true;

            case "SET_MAX_CONCURRENCY":
                error = "WORK_GRAPH_HQ_CONCURRENCY_CHANGE_NOT_ALLOWED";
                return false;

            default:
                error = "WORK_GRAPH_OPERATION_UNSUPPORTED";
                return false;
        }
    }

    private static bool TryParseKind(string? value, out WorkItemKind kind)
    {
        switch ((value ?? "NORMAL").Trim().ToUpperInvariant())
        {
            case "NORMAL":
                kind = WorkItemKind.Normal;
                return true;
            case "INTEGRATION":
                kind = WorkItemKind.Integration;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static IReadOnlyList<string> NormalizeDependencies(IReadOnlyList<JsonElement>? values)
        => (values ?? Array.Empty<JsonElement>())
            .Select(value => NormalizeId(value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string NormalizeId(JsonElement? value)
    {
        if (value is null)
            return string.Empty;

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.Value.GetRawText().Trim(),
            _ => string.Empty
        };
    }

    private static bool IsSafeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96)
            return false;
        return value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Fail(
        string errorCode,
        out WorkGraphPatchOperation? mapped,
        out string? error)
    {
        mapped = null;
        error = errorCode;
        return false;
    }

    private sealed class PatchDto
    {
        public PatchDto() { }

        public long? ExpectedRevision { get; init; }
        public List<OperationDto>? Operations { get; init; }
    }

    private sealed class OperationDto
    {
        public OperationDto() { }

        public string? Type { get; init; }
        public JsonElement? WorkItemId { get; init; }
        public string? Goal { get; init; }
        public List<JsonElement>? Dependencies { get; init; }
        public string? Kind { get; init; }
        public string? BaseRef { get; init; }
        public string? Value { get; init; }
        public string? InputType { get; init; }
    }
}

public enum WorkItemReportStatus
{
    Completed,
    Blocked,
    SplitRequest,
    Failed
}

public sealed record WorkItemReport(
    WorkItemReportStatus Status,
    string Body);

public static class WorkItemReportContract
{
    private const string Prefix = "WORK_ITEM_STATUS:";

    public static bool TryParse(string? body, out WorkItemReport? report, out string? error)
    {
        report = null;
        error = null;

        var lines = (body ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
        if (!lines.Any(line => !string.IsNullOrWhiteSpace(line)))
        {
            error = "WORK_ITEM_REPORT_EMPTY";
            return false;
        }

        var statusIndexes = lines
            .Select((line, index) => (Line: line.Trim(), Index: index))
            .Where(item => item.Line.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(item => item.Index)
            .ToArray();

        if (statusIndexes.Length == 0)
        {
            error = "WORK_ITEM_STATUS_MISSING";
            return false;
        }

        if (statusIndexes.Length > 1)
        {
            error = "WORK_ITEM_STATUS_DUPLICATE";
            return false;
        }

        var statusIndex = statusIndexes[0];
        var line = lines[statusIndex].Trim();
        var value = line[Prefix.Length..].Trim().ToUpperInvariant();
        var status = value switch
        {
            "COMPLETED" => WorkItemReportStatus.Completed,
            "BLOCKED" => WorkItemReportStatus.Blocked,
            "SPLIT_REQUEST" => WorkItemReportStatus.SplitRequest,
            "FAILED" => WorkItemReportStatus.Failed,
            _ => (WorkItemReportStatus?)null
        };

        if (status is null)
        {
            error = "WORK_ITEM_STATUS_INVALID";
            return false;
        }

        var remainder = string.Join(
            "\n",
            lines.Where((_, index) => index != statusIndex)).Trim();
        report = new WorkItemReport(status.Value, remainder);
        return true;
    }
}
