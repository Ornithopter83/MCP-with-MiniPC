using System.IO;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record ModelCallTelemetry(
    string JobId,
    int? Round,
    string Role,
    string Model,
    string? Reasoning,
    string Purpose,
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? ProviderTotalTokens,
    long RequestBytes,
    long PromptBytes,
    long FooterBytes,
    long? EvidenceBytes,
    long ResponseBytes,
    long LatencyMs,
    string? RetryReason,
    bool UsageKnown,
    int? QuestionCount,
    int? BatchSize,
    DateTimeOffset RecordedAt,
    string? PromptDigest = null,
    string? FooterDigest = null,
    string? PayloadDigest = null);

public static class UsageTelemetryStore
{
    private static readonly object Gate = new();

    /// <summary>
    /// Mechanical handoff only: all known model calls, without leaking
    /// specialist-owned temporary WORK task identities to HQ.
    /// Missing provider usage is marked UNKNOWN, never silently recorded as 0.
    /// </summary>
    public static string FormatForNextRole(string jobId)
    {
        var job = string.Concat(jobId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        var file = Path.Combine(WorkerPaths.State, "usage", job, "calls.jsonl");
        var rows = new List<ModelCallTelemetry>();
        lock (Gate)
        {
            try
            {
                if (!File.Exists(file)) return "MODEL_USAGE_HANDOFF: NO_PREVIOUS_CALLS";
                foreach (var line in File.ReadLines(file))
                {
                    try
                    {
                        var item = JsonSerializer.Deserialize<ModelCallTelemetry>(
                            line, ProjectHubJson.WebIndentedOptions);
                        if (item is not null && item.JobId == jobId) rows.Add(item);
                    }
                    catch (JsonException) { }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return "MODEL_USAGE_HANDOFF: READ_UNAVAILABLE";
            }
        }
        if (rows.Count == 0) return "MODEL_USAGE_HANDOFF: NO_PREVIOUS_CALLS";
        var output = new StringBuilder();
        output.AppendLine("MODEL_USAGE_HANDOFF: recorded model calls, provider usage only");
        foreach (var group in rows.GroupBy(x => (x.Role, x.Model, x.Reasoning))
                     .OrderBy(x => x.Key.Role).ThenBy(x => x.Key.Model))
        {
            var known = group.Where(x => x.UsageKnown).ToArray();
            output.Append("ROLE=").Append(group.Key.Role)
                .Append("; MODEL=").Append(group.Key.Model)
                .Append("; REASONING=").Append(group.Key.Reasoning ?? "none")
                .Append("; CALLS=").Append(group.Count())
                .Append("; USAGE_KNOWN_CALLS=").Append(known.Length)
                .Append("; INPUT=").Append(known.Sum(x => x.InputTokens ?? 0))
                .Append("; CACHED_INPUT=").Append(known.Sum(x => x.CachedInputTokens ?? 0))
                .Append("; OUTPUT=").Append(known.Sum(x => x.OutputTokens ?? 0))
                .Append("; PROVIDER_TOTAL=").Append(known.Sum(x => x.ProviderTotalTokens ??
                    (x.InputTokens ?? 0) + (x.OutputTokens ?? 0)))
                .AppendLine(known.Length == group.Count() ? "" : "; UNKNOWN_CALLS=" +
                    (group.Count() - known.Length));
        }
        // This section is factual telemetry, not an assignment or instruction.
        return output.ToString().TrimEnd();
    }

    public static void RecordCall(string jobId, string role, string model,
        string? reasoning, string purpose, CodexUsage usage)
    {
        Append(new ModelCallTelemetry(jobId, null, role, model, reasoning,
            purpose, usage.UsageKnown ? usage.InputTokens : null,
            usage.UsageKnown ? usage.CachedInputTokens : null,
            usage.UsageKnown ? usage.OutputTokens : null,
            usage.UsageKnown ? usage.ReasoningOutputTokens : null,
            usage.ProviderTotalTokens, 0, 0, 0, null, 0, 0, null,
            usage.UsageKnown, null, null, DateTimeOffset.UtcNow));
    }

    public static void Append(ModelCallTelemetry telemetry)
    {
        var job = string.Concat(telemetry.JobId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        var directory = Path.Combine(WorkerPaths.State, "usage", job);
        Directory.CreateDirectory(directory);
        var line = ProjectHubJson.Serialize(telemetry) + Environment.NewLine;
        lock (Gate)
            File.AppendAllText(
                Path.Combine(directory, "calls.jsonl"),
                line,
                ProjectHubJson.Utf8NoBom);
    }
}

public static class ProviderUsageParser
{
    public static CodexUsage Extract(string stdout)
    {
        var cumulative = new List<CodexUsage>();
        var incremental = new List<CodexUsage>();
        var genericSnapshots = new List<CodexUsage>();
        var turnUsage = new List<CodexUsage>();
        foreach (var line in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                cumulative.AddRange(FindNamed(document.RootElement, "total_token_usage").Select(ReadUsage));
                incremental.AddRange(FindNamed(document.RootElement, "last_token_usage").Select(ReadUsage));
                if (TryString(document.RootElement, "type", out var type) && type.Equals("turn.completed", StringComparison.OrdinalIgnoreCase))
                    turnUsage.AddRange(FindNamed(document.RootElement, "usage").Select(ReadUsage));
                else
                    genericSnapshots.AddRange(FindNamed(document.RootElement, "usage").Concat(FindNamed(document.RootElement, "token_usage")).Select(ReadUsage));
            }
            catch (JsonException) { }
        }

        if (cumulative.Count > 0) return cumulative[^1];
        if (incremental.Count > 0) return incremental.Aggregate(CodexUsage.Empty, (sum, item) => sum.Add(item));
        if (turnUsage.Count > 0) return turnUsage.Aggregate(CodexUsage.Empty, (sum, item) => sum.Add(item));
        return genericSnapshots.Count == 0 ? CodexUsage.Empty : genericSnapshots[^1];
    }

    private static IEnumerable<JsonElement> FindNamed(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object)
                    yield return property.Value;
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    foreach (var nested in FindNamed(property.Value, name)) yield return nested;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
            foreach (var item in root.EnumerateArray())
                foreach (var nested in FindNamed(item, name)) yield return nested;
    }

    private static CodexUsage ReadUsage(JsonElement usage)
    {
        var input = ReadLong(usage, "input_tokens", "inputTokens");
        var cached = ReadLong(usage, "cached_input_tokens", "cachedInputTokens");
        var output = ReadLong(usage, "output_tokens", "outputTokens");
        var reasoning = ReadLong(usage, "reasoning_output_tokens", "reasoningOutputTokens");
        var total = ReadLong(usage, "total_tokens", "totalTokens");
        if (total == 0) total = input + output + reasoning;
        var providerTotal = TryReadLong(usage, "total_tokens", "totalTokens", out var reported) ? reported : (long?)null;
        var known = new[] { "input_tokens", "inputTokens", "cached_input_tokens", "cachedInputTokens", "output_tokens", "outputTokens", "reasoning_output_tokens", "reasoningOutputTokens", "total_tokens", "totalTokens" }.Any(name => usage.TryGetProperty(name, out _));
        return new(input, cached, output, reasoning, total, providerTotal, known);
    }

    private static long ReadLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            if (value.TryGetInt64(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number)) return number;
        }
        return 0;
    }

    private static bool TryReadLong(JsonElement element, string first, string second, out long number)
    {
        number = 0;
        foreach (var name in new[] { first, second })
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            if (value.TryGetInt64(out number)) return true;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number)) return true;
        }
        return false;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && (value = property.GetString() ?? string.Empty).Length > 0;
    }
}
