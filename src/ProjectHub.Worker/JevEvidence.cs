using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace ProjectHub.Worker;

public enum EvidenceKind { Source, Diff, Test, Runtime, User }
public enum EvidenceProvenance { Executed, SourceExcerpt, SummaryOnly, UserVerified }

public sealed record JevEvidence(
    string EvidenceId,
    EvidenceKind Kind,
    string SourceRef,
    string SourceRevision,
    string ContentDigest,
    string Runner,
    string? Command,
    int? ExitCode,
    string Status,
    DateTimeOffset CheckedAt,
    string Excerpt,
    string Scope,
    EvidenceProvenance Provenance);

public sealed record JevQuestionEvidence(string QuestionId, IReadOnlyList<string> EvidenceIds);
public sealed record JevVerificationLayer(string Capability, string Status, string? Runner, string Scope);

public sealed record JevEvidenceEnvelope(
    string JobId,
    string SourceRevision,
    IReadOnlyList<JevEvidence> Evidence,
    IReadOnlyList<JevQuestionEvidence> QuestionEvidence,
    IReadOnlyDictionary<string, string> QuestionDigests,
    IReadOnlyDictionary<string, JevVerificationLayer> VerificationLayers)
{
    public static JevEvidenceEnvelope Create(JudgeRequest request, JevValidationRequest validation)
    {
        const int maxFileBytes = 64 * 1024;
        const int maxExcerptChars = 12_000;
        var now = DateTimeOffset.UtcNow;
        var revision = string.IsNullOrWhiteSpace(request.ReviewCommitSha) ? "WORKING_TREE" : request.ReviewCommitSha!;
        var evidence = new List<JevEvidence>();
        var evidencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = Path.GetFullPath(request.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var file in request.Files.Take(32)) evidencePaths.Add(file.Path);
        foreach (var priorPath in LoadPriorSourcePaths(request.JobId, root)) evidencePaths.Add(priorPath);
        foreach (var question in validation.Questions)
        foreach (Match match in Regex.Matches(question.Instructions, @"(?im)^\s*EVIDENCE\s*:\s*(.+)$"))
        foreach (Match pathMatch in Regex.Matches(match.Groups[1].Value.Split(new[] { " — ", " – ", ";", "," }, StringSplitOptions.None)[0], @"(?:[A-Za-z]:[\\/])?[\w .\\/-]+\.(?:cs|xaml|js|ts|tsx|jsx|json|md|log|txt|csproj|sln|xml|yml|yaml|toml|ps1)", RegexOptions.IgnoreCase))
        {
            try
            {
                var candidate = Path.GetFullPath(Path.IsPathRooted(pathMatch.Value) ? pathMatch.Value : Path.Combine(root, pathMatch.Value));
                if (candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || candidate.Equals(root, StringComparison.OrdinalIgnoreCase)) evidencePaths.Add(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }

        foreach (var path in evidencePaths.Take(32))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var sourceRef = MakeSourceRef(fullPath, root);
                if (!File.Exists(fullPath))
                {
                    evidence.Add(new(EvidenceId(fullPath), EvidenceKind.Source, sourceRef, revision, "missing", "Worker local file reader", null,
                        null, "MISSING", now, string.Empty, Path.GetFileName(fullPath), EvidenceProvenance.SourceExcerpt));
                    continue;
                }
                var info = new FileInfo(fullPath);
                if (info.Length <= 0 || info.Length > maxFileBytes) continue;
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || Regex.IsMatch(info.Name, @"(?i)(secret|credential|token|\.env|private[-_]?key)")) continue;
                var bytes = File.ReadAllBytes(fullPath);
                var content = new UTF8Encoding(false, true).GetString(bytes);
                if (content.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t')) continue;
                var excerpt = Sanitize(content);
                if (excerpt.Length > maxExcerptChars) excerpt = excerpt[..maxExcerptChars] + "\n[TRUNCATED]";
                var kind = IsTestFile(info.Name) ? EvidenceKind.Test : EvidenceKind.Source;
                evidence.Add(new(
                    EvidenceId(fullPath), kind, sourceRef, revision, Digest(bytes), "Worker local file reader", null,
                    null, "AVAILABLE", now, excerpt, info.Name, EvidenceProvenance.SourceExcerpt));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException or NotSupportedException) { }
        }

        var summary = Sanitize(request.CodexResult);
        if (summary.Length > maxExcerptChars) summary = summary[..maxExcerptChars] + "\n[TRUNCATED]";
        var summaryBytes = Encoding.UTF8.GetBytes(summary);
        evidence.Add(new(EvidenceId("codex-summary"), EvidenceKind.Runtime, "codex-summary", revision, Digest(summaryBytes),
            "Codex CLI", null, null, "REPORTED", now, summary, "Codex-reported result; execution details are not independently confirmed",
            EvidenceProvenance.SummaryOnly));

        var mappings = validation.Questions.Select(q =>
        {
            var candidates = evidence.Where(e => e.Provenance != EvidenceProvenance.SummaryOnly &&
                (q.Instructions.Contains(Path.GetFileName(e.Scope), StringComparison.OrdinalIgnoreCase) ||
                 q.Instructions.Contains(e.EvidenceId, StringComparison.OrdinalIgnoreCase))).Select(e => e.EvidenceId).ToList();
            if (candidates.Count == 0) candidates.Add(evidence[^1].EvidenceId);
            return new JevQuestionEvidence(q.Id, candidates);
        }).ToArray();

        var questionDigests = validation.Questions.ToDictionary(q => q.Id, q => Digest(Encoding.UTF8.GetBytes($"{q.Type}|{q.Instructions}|{q.Rule.Operator}|{q.Rule.Number}|{string.Join(";", q.Rule.Allowed.OrderBy(x => x))}|{string.Join(";", q.Criteria)}|{string.Join(";", q.ChoiceCriteria.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value))}")), StringComparer.OrdinalIgnoreCase);
        var engineRunners = new[] { FindOnPath("node.exe"), FindOnPath("dotnet.exe") }.Where(x => x is not null).ToArray();
        var hasJevKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));
        return new(string.IsNullOrWhiteSpace(request.JobId) ? Guid.NewGuid().ToString("N") : request.JobId!, revision, evidence, mappings, questionDigests,
            new Dictionary<string, JevVerificationLayer>(StringComparer.OrdinalIgnoreCase)
            {
                ["ENGINE_HEADLESS"] = new(engineRunners.Length > 0 ? "NODE_OR_DOTNET_AVAILABLE" : "NO_SUPPORTED_RUNNER_FOUND", "NOT_RUN", string.Join(",", engineRunners!), "Executable availability only; no test command or fixture was inferred."),
                ["UI_BROWSER"] = new("NO_BROWSER_AUTOMATION_ADAPTER", "BLOCKED_BY_TOOL", null, "Worker does not automate browser UI validation."),
                ["HUMAN_UX"] = new("USER_OBSERVATION", "NOT_RECORDED", null, "Requires an explicit user report or attached observation."),
                ["JEV"] = new("TYPESAFE_API", hasJevKey ? "PENDING" : "UNAVAILABLE", null, "Semantic judgment only; does not substitute for engine or UI execution.")
            });
    }

    public string ToProviderState() => JsonSerializer.Serialize(this);

    public bool HasDirectEvidence(string questionId) => QuestionEvidence.FirstOrDefault(x => x.QuestionId.Equals(questionId, StringComparison.OrdinalIgnoreCase))?.EvidenceIds
        .Any(id => Evidence.Any(e => e.EvidenceId == id && e.Status == "AVAILABLE" && e.Provenance != EvidenceProvenance.SummaryOnly)) == true;

    public string EvidenceFor(string questionId) => string.Join(",", QuestionEvidence.FirstOrDefault(x => x.QuestionId.Equals(questionId, StringComparison.OrdinalIgnoreCase))?.EvidenceIds ?? Array.Empty<string>());

    public JevEvidenceEnvelope WithLayer(string layer, string status) => this with
    {
        VerificationLayers = new Dictionary<string, JevVerificationLayer>(VerificationLayers, StringComparer.OrdinalIgnoreCase)
        {
            [layer] = VerificationLayers.TryGetValue(layer, out var previous) ? previous with { Status = status } : new("UNKNOWN", status, null, "")
        }
    };

    private static bool IsTestFile(string name) => name.Contains("test", StringComparison.OrdinalIgnoreCase) || name.Contains("spec", StringComparison.OrdinalIgnoreCase);
    private static string? FindOnPath(string executable)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var candidate = Path.Combine(directory.Trim('"'), executable); if (File.Exists(candidate)) return executable; }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return null;
    }
    private static string EvidenceId(string key) => "E-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
    private static string Digest(byte[] bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Sanitize(string value)
    {
        value = Regex.Replace(value, @"(?i)(api[_-]?key|access[_-]?token|client[_-]?secret|token|password|secret)(\s*['""]?\s*[=:]\s*['""]?)[^'""\s,;]+", "$1$2[REDACTED]");
        return Regex.Replace(value, @"\b(?:sk-[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|xox[baprs]-[A-Za-z0-9-]{20,})\b", "[REDACTED]");
    }

    private static string MakeSourceRef(string path, string root) => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        ? Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')
        : "artifact:" + Path.GetFileName(path);

    private static IReadOnlyList<string> LoadPriorSourcePaths(string? jobId, string root)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(jobId)) return paths;
        var latest = Path.Combine(WorkerPaths.State, "jev-evidence", Regex.Replace(jobId, @"[^A-Za-z0-9_-]", "_"), "latest.json");
        if (!File.Exists(latest)) return paths;
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(latest));
            foreach (var item in document.RootElement.GetProperty("envelope").GetProperty("Evidence").EnumerateArray())
            {
                if (!item.TryGetProperty("SourceRef", out var value) || value.ValueKind != JsonValueKind.String) continue;
                var sourceRef = value.GetString();
                if (string.IsNullOrWhiteSpace(sourceRef) || Path.IsPathRooted(sourceRef) || sourceRef.StartsWith("artifact:", StringComparison.OrdinalIgnoreCase)) continue;
                var path = Path.GetFullPath(Path.Combine(root, sourceRef.Replace('/', Path.DirectorySeparatorChar)));
                if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) paths.Add(path);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or NotSupportedException) { }
        finally { document?.Dispose(); }
        return paths;
    }
}

public static class JevEvidenceArchive
{
    public sealed record SaveResult(JudgeResult Result, IReadOnlyList<string> InvalidatedQuestionIds);

    public static SaveResult Save(JevEvidenceEnvelope envelope, JudgeResult result, int round)
    {
        var evaluatedQuestionIds = envelope.QuestionDigests.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directory = Path.Combine(WorkerPaths.State, "jev-evidence", SafeName(envelope.JobId));
        Directory.CreateDirectory(directory);
        var latestPath = Path.Combine(directory, "latest.json");
        var invalidated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previousResults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(latestPath))
            {
                using var previousDoc = JsonDocument.Parse(File.ReadAllText(latestPath));
                var previousRoot = previousDoc.RootElement;
                var priorEnvelope = JsonSerializer.Deserialize<JevEvidenceEnvelope>(previousRoot.GetProperty("envelope").GetRawText());
                if (priorEnvelope is not null)
                {
                    var mergedEvidence = priorEnvelope.Evidence.Concat(envelope.Evidence).GroupBy(x => x.EvidenceId, StringComparer.OrdinalIgnoreCase).Select(group => group.Last()).ToArray();
                    var mergedMappings = priorEnvelope.QuestionEvidence.Concat(envelope.QuestionEvidence).GroupBy(x => x.QuestionId, StringComparer.OrdinalIgnoreCase).Select(group => group.Last()).ToArray();
                    var mergedDigests = priorEnvelope.QuestionDigests.Concat(envelope.QuestionDigests).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
                    envelope = envelope with { Evidence = mergedEvidence, QuestionEvidence = mergedMappings, QuestionDigests = mergedDigests };
                }
                var oldEvidence = (priorEnvelope?.Evidence ?? Array.Empty<JevEvidence>())
                    .ToDictionary(x => x.EvidenceId, x => (Digest: x.ContentDigest, Revision: x.SourceRevision), StringComparer.OrdinalIgnoreCase);
                var changed = envelope.Evidence.Where(e => oldEvidence.TryGetValue(e.EvidenceId, out var previous) && (previous.Digest != e.ContentDigest || previous.Revision != e.SourceRevision)).Select(e => e.EvidenceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var oldMappings = priorEnvelope?.QuestionEvidence ?? Array.Empty<JevQuestionEvidence>();
                foreach (var map in oldMappings.Concat(envelope.QuestionEvidence))
                    if (map.EvidenceIds.Any(changed.Contains)) invalidated.Add(map.QuestionId);
                if (priorEnvelope is not null)
                    foreach (var pair in envelope.QuestionDigests)
                        if (priorEnvelope.QuestionDigests.TryGetValue(pair.Key, out var oldDigest) && oldDigest != pair.Value) invalidated.Add(pair.Key);
                if (previousRoot.TryGetProperty("questionResults", out var oldResults) && oldResults.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in oldResults.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String) continue;
                        previousResults[property.Name] = property.Value.GetString() ?? "UNKNOWN";
                        if (previousResults[property.Name] == "NEEDS_RECHECK") invalidated.Add(property.Name);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }

        var pendingRecheck = invalidated.Where(id => !evaluatedQuestionIds.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (pendingRecheck.Count > 0 && result.Decision != JudgeDecision.Error)
        {
            var invalidationMessage = string.Join("\n", pendingRecheck.OrderBy(x => x).Select(id => $"{id} RESULT: EVIDENCE_CHANGED; prior PASS invalidated and this atomic question must be re-evaluated"));
            result = new(JudgeDecision.Partial, (result.Decision == JudgeDecision.Pass ? "[JEV PARTIAL]\n" : result.Message + "\n") + invalidationMessage, result.Provider, result.ExecutableOrEndpoint, result.Telemetry);
        }

        var questionResults = new Dictionary<string, string>(previousResults, StringComparer.OrdinalIgnoreCase);
        foreach (var id in pendingRecheck) questionResults[id] = "NEEDS_RECHECK";
        var failedIds = Regex.Matches(result.Message, @"(?im)^\s*([A-Z][A-Z0-9_-]{0,31})\s+(?:TYPE:|RESULT:)")
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in evaluatedQuestionIds)
            questionResults[id] = result.Decision switch
            {
                JudgeDecision.Pass => "PASS",
                JudgeDecision.Error => "ERROR",
                _ => failedIds.Contains(id) ? "PARTIAL" : "PASS"
            };

        var saved = new { envelope = envelope.WithLayer("JEV", result.Decision.ToString().ToUpperInvariant()), decision = result.Decision.ToString().ToUpperInvariant(), result = result.Message, telemetry = result.Telemetry, questionResults, round, checkedAt = DateTimeOffset.UtcNow, invalidatedQuestionIds = pendingRecheck.OrderBy(x => x).ToArray() };
        var json = JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true });
        var temporary = latestPath + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, latestPath, true);
        var roundPath = Path.Combine(directory, $"round-{round:D2}.json");
        if (!File.Exists(roundPath)) File.WriteAllText(roundPath, json, new UTF8Encoding(false));
        if (result.Telemetry is { } telemetry)
            UsageTelemetryStore.Append(new ModelCallTelemetry(envelope.JobId, round, "JEV", telemetry.Model, null, "VALIDATION", telemetry.InputTokens, telemetry.CachedInputTokens, telemetry.OutputTokens, telemetry.ReasoningTokens, telemetry.ProviderTotalTokens, telemetry.RequestBytes, 0, 0, telemetry.EvidenceBytes, telemetry.ResponseBytes, telemetry.LatencyMs, telemetry.ErrorCode, telemetry.UsageKnown, telemetry.QuestionCount, telemetry.QuestionCount, DateTimeOffset.UtcNow, null, null, telemetry.PayloadDigest));
        return new SaveResult(result, pendingRecheck.OrderBy(x => x).ToArray());
    }

    private static string SafeName(string value) => Regex.Replace(value, @"[^A-Za-z0-9_-]", "_");
}
