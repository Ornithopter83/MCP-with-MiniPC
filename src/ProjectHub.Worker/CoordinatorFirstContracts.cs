using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record WorkAcceptanceCriterion(string AcId, string Claim, IReadOnlyList<string> EvidenceRequired);
public sealed record CoordinatorWorkCard(
    string WorkId,
    string Title,
    string Goal,
    IReadOnlyList<string> Scope,
    IReadOnlyList<WorkAcceptanceCriterion> AcceptanceCriteria,
    IReadOnlyList<string> ValidationCommands,
    IReadOnlyList<string> Prohibited);
public sealed record ImplementerValidationClaim(string Command, string Result, string? Note);
public sealed record ImplementerResult(
    string Status,
    string Summary,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<ImplementerValidationClaim> Validation,
    IReadOnlyList<string> Unverified);
public sealed record CoordinatorAcReview(string AcId, string Status, string Reason);
public sealed record CoordinatorReview(string Decision, string Summary, IReadOnlyList<CoordinatorAcReview> AcceptanceCriteria);

public static class CoordinatorFirstContracts
{
    public const string WorkCardSchema = """
        {"type":"object","additionalProperties":false,"required":["work_id","title","goal","scope","acceptance_criteria","validation_commands","prohibited"],"properties":{"work_id":{"type":"string","minLength":1,"maxLength":40},"title":{"type":"string","minLength":1,"maxLength":160},"goal":{"type":"string","minLength":1,"maxLength":1000},"scope":{"type":"array","minItems":1,"maxItems":30,"items":{"type":"string","minLength":1,"maxLength":500}},"acceptance_criteria":{"type":"array","minItems":1,"maxItems":30,"items":{"type":"object","additionalProperties":false,"required":["ac_id","claim","evidence_required"],"properties":{"ac_id":{"type":"string","minLength":1,"maxLength":40},"claim":{"type":"string","minLength":1,"maxLength":500},"evidence_required":{"type":"array","minItems":1,"maxItems":12,"items":{"type":"string","minLength":1,"maxLength":300}}}}},"validation_commands":{"type":"array","minItems":1,"maxItems":12,"items":{"type":"string","minLength":1,"maxLength":500}},"prohibited":{"type":"array","maxItems":30,"items":{"type":"string","minLength":1,"maxLength":300}}}}
        """;

    public const string ImplementerResultSchema = """
        {"type":"object","additionalProperties":false,"required":["status","summary","changed_paths","validation","unverified"],"properties":{"status":{"type":"string","enum":["IMPLEMENTED","PARTIAL","BLOCKED"]},"summary":{"type":"string","minLength":1,"maxLength":3000},"changed_paths":{"type":"array","maxItems":100,"items":{"type":"string","minLength":1,"maxLength":500}},"validation":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["command","result","note"],"properties":{"command":{"type":"string","minLength":1,"maxLength":500},"result":{"type":"string","enum":["PASS","FAIL","NOT_RUN"]},"note":{"type":["string","null"],"maxLength":1000}}}},"unverified":{"type":"array","items":{"type":"string","minLength":1,"maxLength":500}}}}
        """;

    public const string ReviewSchema = """
        {"type":"object","additionalProperties":false,"required":["decision","summary","acceptance_criteria"],"properties":{"decision":{"type":"string","enum":["ACCEPT","REVISE","COLLECT_EVIDENCE","BLOCKED"]},"summary":{"type":"string","minLength":1,"maxLength":3000},"acceptance_criteria":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["ac_id","status","reason"],"properties":{"ac_id":{"type":"string","minLength":1,"maxLength":40},"status":{"type":"string","enum":["PASS","FAIL","INSUFFICIENT"]},"reason":{"type":"string","minLength":1,"maxLength":1000}}}}}}
        """;

    public static bool TryParseWorkCard(string? json, out CoordinatorWorkCard? card, out string error)
    {
        card = null;
        if (!TryGetRoot(json, out var root, out error)) return false;
        try
        {
            var acs = root.GetProperty("acceptance_criteria").EnumerateArray().Select(item => new WorkAcceptanceCriterion(
                RequiredString(item, "ac_id"), RequiredString(item, "claim"), RequiredStringArray(item, "evidence_required"))).ToList();
            if (acs.Select(ac => ac.AcId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != acs.Count)
                return Fail("WORK_CARD_DUPLICATE_AC_ID", out error);
            card = new CoordinatorWorkCard(
                RequiredString(root, "work_id"), RequiredString(root, "title"), RequiredString(root, "goal"),
                RequiredStringArray(root, "scope"), acs, RequiredStringArray(root, "validation_commands"), RequiredStringArray(root, "prohibited", required: false));
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Fail("WORK_CARD_INVALID", out error);
        }
    }

    public static bool TryParseImplementerResult(string? json, out ImplementerResult? result, out string error)
    {
        result = null;
        if (!TryGetRoot(json, out var root, out error)) return false;
        try
        {
            var validations = root.GetProperty("validation").EnumerateArray().Select(item => new ImplementerValidationClaim(
                RequiredString(item, "command"), RequiredString(item, "result"), ReadNullableString(item, "note"))).ToList();
            result = new ImplementerResult(RequiredString(root, "status"), RequiredString(root, "summary"),
                RequiredStringArray(root, "changed_paths", required: false), validations, RequiredStringArray(root, "unverified", required: false));
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Fail("IMPLEMENTER_RESULT_INVALID", out error);
        }
    }

    public static bool TryParseReview(string? json, IReadOnlyList<WorkAcceptanceCriterion> requiredCriteria, out CoordinatorReview? review, out string error)
    {
        review = null;
        if (!TryGetRoot(json, out var root, out error)) return false;
        try
        {
            var criteria = root.GetProperty("acceptance_criteria").EnumerateArray().Select(item => new CoordinatorAcReview(
                RequiredString(item, "ac_id"), RequiredString(item, "status"), RequiredString(item, "reason"))).ToList();
            if (criteria.Select(ac => ac.AcId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != criteria.Count)
                return Fail("REVIEW_DUPLICATE_AC_ID", out error);
            var requiredIds = requiredCriteria.Select(ac => ac.AcId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (criteria.Count != requiredIds.Count || criteria.Any(ac => !requiredIds.Contains(ac.AcId)))
                return Fail("REVIEW_AC_SET_MISMATCH", out error);
            review = new CoordinatorReview(RequiredString(root, "decision"), RequiredString(root, "summary"), criteria);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Fail("REVIEW_INVALID", out error);
        }
    }

    public static bool HasRequiredValidationEvidence(CoordinatorWorkCard card, IReadOnlyList<CodexCommandExecution> executions, out string detail)
    {
        var missing = new List<string>();
        foreach (var command in card.ValidationCommands)
        {
            var execution = executions.FirstOrDefault(item => CommandMatches(command, item.Command));
            if (execution is null) missing.Add($"NOT_OBSERVED: {command}");
            else if (execution.ExitCode != 0) missing.Add($"EXIT_{execution.ExitCode}: {command}");
        }
        detail = string.Join(Environment.NewLine, missing);
        return missing.Count == 0;
    }

    public static bool CommandMatches(string requiredCommand, string observedCommand)
    {
        static string Normalize(string value) => string.Join(' ', value.Trim().Trim('"', '\'').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Normalize(observedCommand).Contains(Normalize(requiredCommand), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetRoot(string? json, out JsonElement root, out string error)
    {
        root = default;
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            root = document.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object) return Fail("JSON_ROOT_NOT_OBJECT", out error);
            error = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return Fail("JSON_INVALID", out error);
        }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            throw new FormatException();
        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? throw new FormatException() : value;
    }

    private static string? ReadNullableString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : throw new FormatException();
    }

    private static IReadOnlyList<string> RequiredStringArray(JsonElement element, string name, bool required = true)
    {
        if (!element.TryGetProperty(name, out var property))
            return required ? throw new FormatException() : Array.Empty<string>();
        if (property.ValueKind != JsonValueKind.Array) throw new FormatException();
        var values = property.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null).ToList();
        if (values.Any(string.IsNullOrWhiteSpace) || (required && values.Count == 0)) throw new FormatException();
        return values.Select(value => value!).ToList();
    }

    private static bool Fail(string code, out string error)
    {
        error = code;
        return false;
    }
}

public sealed record CodexCommandExecution(string Command, int ExitCode);
