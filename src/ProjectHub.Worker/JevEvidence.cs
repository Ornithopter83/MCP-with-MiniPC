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
    IReadOnlyDictionary<string, JevVerificationLayer> VerificationLayers)
{
    public static JevEvidenceEnvelope Create(JudgeRequest request, JudgeTransportRequest validation)
    {
        const int maxFileBytes = 64 * 1024;
        const int maxExcerptChars = 12_000;
        var now = DateTimeOffset.UtcNow;
        var revision = string.IsNullOrWhiteSpace(request.ReviewCommitSha) ? "WORKING_TREE" : request.ReviewCommitSha!;
        var evidence = new List<JevEvidence>();
        var evidencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = Path.GetFullPath(request.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var file in request.Files.Take(32)) evidencePaths.Add(file.Path);
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

        foreach (var (execution, index) in (request.CommandExecutions ?? Array.Empty<CodexCommandExecution>()).Take(32).Select((item, index) => (item, index)))
        {
            var excerpt = Sanitize(execution.Output ?? string.Empty);
            if (excerpt.Length > maxExcerptChars) excerpt = excerpt[..maxExcerptChars] + "\n[TRUNCATED]";
            var sourceRef = $"codex-command/{index + 1}";
            evidence.Add(new(EvidenceId(sourceRef + execution.Command), EvidenceKind.Runtime, sourceRef, revision,
                Digest(Encoding.UTF8.GetBytes(execution.Command + "\n" + execution.ExitCode + "\n" + excerpt)),
                "Codex CLI JSONL", execution.Command, execution.ExitCode,
                execution.ExitCode == 0 ? "AVAILABLE" : "FAILED", now, excerpt, execution.Command, EvidenceProvenance.Executed));
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
                 (e.Command is not null && q.Instructions.Contains(e.Command, StringComparison.OrdinalIgnoreCase)) ||
                 q.Instructions.Contains(e.EvidenceId, StringComparison.OrdinalIgnoreCase))).Select(e => e.EvidenceId).ToList();
            if (candidates.Count == 0) candidates.Add(evidence[^1].EvidenceId);
            return new JevQuestionEvidence(q.Id, candidates);
        }).ToArray();

        var engineRunners = new[] { FindOnPath("node.exe"), FindOnPath("dotnet.exe") }.Where(x => x is not null).ToArray();
        var hasJevKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));
        return new(string.IsNullOrWhiteSpace(request.JobId) ? Guid.NewGuid().ToString("N") : request.JobId!, revision, evidence, mappings,
            new Dictionary<string, JevVerificationLayer>(StringComparer.OrdinalIgnoreCase)
            {
                ["ENGINE_HEADLESS"] = new(engineRunners.Length > 0 ? "NODE_OR_DOTNET_AVAILABLE" : "NO_SUPPORTED_RUNNER_FOUND", "NOT_RUN", string.Join(",", engineRunners!), "Executable availability only; no test command or fixture was inferred."),
                ["UI_BROWSER"] = new("NO_BROWSER_AUTOMATION_ADAPTER", "BLOCKED_BY_TOOL", null, "Worker does not automate browser UI validation."),
                ["HUMAN_UX"] = new("USER_OBSERVATION", "NOT_RECORDED", null, "Requires an explicit user report or attached observation."),
                ["JEV"] = new("TYPESAFE_API", hasJevKey ? "PENDING" : "UNAVAILABLE", null, "Semantic judgment only; does not substitute for engine or UI execution.")
            });
    }

    public string ToProviderState() => JsonSerializer.Serialize(this);

    public string EvidenceFor(string questionId) => string.Join(",", QuestionEvidence.FirstOrDefault(x => x.QuestionId.Equals(questionId, StringComparison.OrdinalIgnoreCase))?.EvidenceIds ?? Array.Empty<string>());

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

}
