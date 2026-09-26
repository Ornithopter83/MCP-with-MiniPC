using System.Text.Json;

namespace ProjectHub.Worker;

public static class WorkGraphTransportContract
{
    private const string Marker = "WORK_GRAPH_PATCH:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryParse(string? body, out WorkGraphPatch? patch, out string? error)
    {
        patch = null;
        error = null;

        var normalized = (body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0 || !string.Equals(lines[first].Trim(), Marker, StringComparison.Ordinal))
        {
            error = "WORK_GRAPH_PATCH_MARKER_MISSING";
            return false;
        }

        var json = string.Join("\n", lines.Skip(first + 1)).Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "WORK_GRAPH_PATCH_JSON_MISSING";
            return false;
        }

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
        var id = operation.WorkItemId?.Trim() ?? string.Empty;

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

    private static IReadOnlyList<string> NormalizeDependencies(IReadOnlyList<string>? values)
        => (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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
        public string? WorkItemId { get; init; }
        public string? Goal { get; init; }
        public List<string>? Dependencies { get; init; }
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
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0)
        {
            error = "WORK_ITEM_REPORT_EMPTY";
            return false;
        }

        var line = lines[first].Trim();
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "WORK_ITEM_STATUS_MISSING";
            return false;
        }

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

        var remainder = string.Join("\n", lines.Skip(first + 1)).Trim();
        report = new WorkItemReport(status.Value, remainder);
        return true;
    }
}
