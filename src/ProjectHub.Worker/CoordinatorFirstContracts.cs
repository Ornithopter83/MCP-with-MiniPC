using System.Text.Json;
using System.Text.RegularExpressions;

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
public enum CoordinatorActionKind { Continue, Pause, End }
public sealed record CoordinatorAction(CoordinatorActionKind Kind, CoordinatorReview Review);

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
            if (!HasExactProperties(root, "decision", "summary", "acceptance_criteria"))
                return Fail("REVIEW_INVALID_FIELDS", out error);
            var criteria = root.GetProperty("acceptance_criteria").EnumerateArray().Select(item => new CoordinatorAcReview(
                RequiredString(item, "ac_id"), RequiredString(item, "status"), RequiredString(item, "reason"))).ToList();
            if (root.GetProperty("acceptance_criteria").EnumerateArray().Any(item => !HasExactProperties(item, "ac_id", "status", "reason")))
                return Fail("REVIEW_INVALID_FIELDS", out error);
            if (criteria.Select(ac => ac.AcId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != criteria.Count)
                return Fail("REVIEW_DUPLICATE_AC_ID", out error);
            var requiredIds = requiredCriteria.Select(ac => ac.AcId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (criteria.Count != requiredIds.Count || criteria.Any(ac => !requiredIds.Contains(ac.AcId)))
                return Fail("REVIEW_AC_SET_MISMATCH", out error);
            var decision = RequiredString(root, "decision");
            if (decision is not ("ACCEPT" or "REVISE" or "COLLECT_EVIDENCE" or "BLOCKED") ||
                criteria.Any(ac => ac.Status is not ("PASS" or "FAIL" or "INSUFFICIENT")))
                return Fail("REVIEW_INVALID_STATUS", out error);
            var summary = RequiredString(root, "summary");
            if (summary.Length > 3000 || criteria.Any(ac => ac.AcId.Length > 40 || ac.Reason.Length > 1000))
                return Fail("REVIEW_INVALID_LENGTH", out error);
            review = new CoordinatorReview(decision, summary, criteria);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Fail("REVIEW_INVALID", out error);
        }
    }

    public static bool TryParseReviewAction(string? response, IReadOnlyList<WorkAcceptanceCriterion> requiredCriteria, out CoordinatorAction? action, out string error)
    {
        action = null;
        var lines = (response ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0) return Fail("ACTION_MISSING", out error);
        var control = lines[first].Trim().TrimStart('\uFEFF');
        var match = Regex.Match(control, @"^\[ACTION=(CONTINUE|PAUSE|END)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return Fail("ACTION_INVALID_FIRST_LINE", out error);
        var body = string.Join('\n', lines.Skip(first + 1)).Trim();
        if (Regex.IsMatch(body, @"(?m)^\s*\[ACTION=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return Fail("ACTION_DUPLICATE", out error);
        if (!TryParseReview(body, requiredCriteria, out var review, out error) || review is null) return false;
        var kind = match.Groups[1].Value.ToUpperInvariant() switch
        {
            "CONTINUE" => CoordinatorActionKind.Continue,
            "PAUSE" => CoordinatorActionKind.Pause,
            _ => CoordinatorActionKind.End
        };
        if (kind == CoordinatorActionKind.End && review.Decision != "ACCEPT" ||
            kind == CoordinatorActionKind.Continue && review.Decision is not ("REVISE" or "COLLECT_EVIDENCE") ||
            kind == CoordinatorActionKind.Pause && review.Decision == "ACCEPT")
            return Fail("ACTION_REVIEW_CONFLICT", out error);
        action = new CoordinatorAction(kind, review);
        error = string.Empty;
        return true;
    }

    public static bool HasRequiredValidationEvidence(CoordinatorWorkCard card, IReadOnlyList<CodexCommandExecution> executions, out string detail)
    {
        var missing = new List<string>();
        foreach (var command in card.ValidationCommands)
        {
            var matches = executions.Where(item => CommandMatches(command, item.Command)).ToList();
            if (matches.Count == 0) missing.Add($"NOT_OBSERVED: {command}");
            else if (!matches.Any(item => item.ExitCode == 0)) missing.Add($"EXIT_{matches[^1].ExitCode}: {command}");
        }
        detail = string.Join(Environment.NewLine, missing);
        return missing.Count == 0;
    }

    public static bool CommandMatches(string requiredCommand, string observedCommand)
    {
        static string Unquote(string value)
        {
            value = value.Trim();
            return value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'' ? value[1..^1] : value;
        }
        static string Normalize(string value) => string.Join(' ', Unquote(value).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var observed = observedCommand.Trim();
        for (var depth = 0; depth <= 2; depth++)
        {
            if (string.Equals(Normalize(observed), Normalize(requiredCommand), StringComparison.OrdinalIgnoreCase)) return true;
            var wrapper = Regex.Match(observed,
                "^(?:\"[^\"]*(?:powershell|pwsh)\\.exe\"|(?:[^\\s\"']*[\\\\/])?(?:powershell|pwsh)(?:\\.exe)?)(?:\\s+-(?:NoProfile|NonInteractive))*\\s+-Command\\s+(.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!wrapper.Success)
                wrapper = Regex.Match(observed, @"^(?:cmd(?:\.exe)?)\s+/[cC]\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!wrapper.Success) return false;
            observed = Unquote(wrapper.Groups[1].Value);
        }
        return false;
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

    private static bool HasExactProperties(JsonElement element, params string[] names) =>
        element.ValueKind == JsonValueKind.Object &&
        element.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names);

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

public sealed record CodexCommandExecution(string Command, int ExitCode, string? Output = null);
