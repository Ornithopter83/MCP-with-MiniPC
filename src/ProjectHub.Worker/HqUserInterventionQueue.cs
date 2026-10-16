using System.IO;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record HqUserIntervention(
    string Id,
    DateTimeOffset CreatedAt,
    string Message,
    bool RecordOnly,
    DateTimeOffset? DeliveredAt = null);

/// <summary>
/// Persists user messages independently of milestone checkpoints. A message
/// is acknowledged after the recipient AI returns a successful, nonempty
/// response. Report grammar validation is separate from actual delivery.
/// </summary>
public sealed class HqUserInterventionQueue
{
    private static readonly object Sync = new();
    private readonly string _path;
    // In-memory claims prevent duplicate deliveries across parallel roles.
    // Process restarts release all claims without losing queued messages.
    private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

    public HqUserInterventionQueue(string workingDirectory, string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        _path = Path.Combine(
            ProjectWorkspacePersistence.RootDirectory(workingDirectory),
            "hq-interventions",
            new string(jobId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray()) + ".json");
    }

    public HqUserIntervention Add(string message, bool recordOnly)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("An intervention message is required.", nameof(message));
        var entry = new HqUserIntervention(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            message,
            recordOnly);
        lock (Sync)
        {
            var all = ReadCore();
            all.Add(entry);
            SaveCore(all);
        }
        return entry;
    }

    public IReadOnlyList<HqUserIntervention> ReadAll()
    {
        lock (Sync)
            return ReadCore().ToArray();
    }

    public IReadOnlyList<HqUserIntervention> Pending()
    {
        lock (Sync)
            return ReadCore().Where(x => !x.RecordOnly && x.DeliveredAt is null).ToArray();
    }

    public IReadOnlyList<HqUserIntervention> ClaimPending()
    {
        lock (Sync)
        {
            var available = ReadCore().Where(x => !x.RecordOnly &&
                x.DeliveredAt is null && !_claimed.Contains(x.Id)).ToArray();
            foreach (var message in available) _claimed.Add(message.Id);
            return available;
        }
    }

    public void Release(IReadOnlyList<HqUserIntervention> sent)
    {
        lock (Sync)
            foreach (var message in sent) _claimed.Remove(message.Id);
    }

    public void Acknowledge(IReadOnlyList<HqUserIntervention> sent)
    {
        if (sent.Count == 0) return;
        lock (Sync)
        {
            var all = ReadCore();
            var ids = sent.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            if (all.Count(x => ids.Contains(x.Id) && !x.RecordOnly && x.DeliveredAt is null) != ids.Count)
                throw new IOException("HQ_USER_INTERVENTION_ACK_MISMATCH");
            var deliveredAt = DateTimeOffset.UtcNow;
            for (var i = 0; i < all.Count; i++)
                if (ids.Contains(all[i].Id))
                    all[i] = all[i] with { DeliveredAt = deliveredAt };
            SaveCore(all);
            foreach (var message in sent) _claimed.Remove(message.Id);
        }
    }

    /// <summary>
    /// Latest user input has the highest TASK-level priority. The verbatim
    /// payload is the first content in the recipient's prompt: specialist
    /// roles retain only their required leading @@MESSAGE envelope. Existing
    /// plans and role task instructions follow the user input, not precede it.
    /// Provider-level safeguards, executable permissions, and the machine
    /// response schema are not transport instructions and are not bypassed.
    /// </summary>
    internal const string LatestUserPriorityGuidance =
        "최신 사용자 지시 우선 적용: 위 사용자 메시지는 이번 AI 호출의 최우선 작업 지시다. " +
        "기존 HQ 계획, 이전 마일스톤 지시, 이전 사용자 요청, 역할 계약의 일반적인 작업 내용과 " +
        "충돌하면 위 원문을 우선하여 판단하고 실행하라. " +
        "Worker의 역할·쓰기 권한 경계 및 필수 응답 문법은 지키고, " +
        "현재 역할에 없는 권한이나 다른 역할의 재배정이 필요하면 " +
        "임의로 무시하지 말고 HQ에 변경 판단을 요청하라.";

    public static string AppendToHqBody(
        string body, IReadOnlyList<HqUserIntervention> pending)
    {
        if (pending.Count == 0) return body;
        var userText = string.Join("\n\n", pending.Select(item => item.Message));
        var priorityAndPrevious = "\n\n" + LatestUserPriorityGuidance +
            "\n\n" + body;

        // Preserve the protocol-only marker required by specialist prompts.
        // The raw user bytes must be the FIRST payload after this marker and
        // MUST NOT pass through UnityRoleMessageComposer.Compose().
        const string specialistHeader = "@@MESSAGE\n";
        if (body.StartsWith(specialistHeader, StringComparison.Ordinal))
            return specialistHeader + userText +
                "\n\n" + LatestUserPriorityGuidance + "\n\n" +
                body[specialistHeader.Length..];

        const string specialistHeaderCrLf = "@@MESSAGE\r\n";
        if (body.StartsWith(specialistHeaderCrLf, StringComparison.Ordinal))
            return specialistHeaderCrLf + userText +
                "\r\n\r\n" + LatestUserPriorityGuidance + "\r\n\r\n" +
                body[specialistHeaderCrLf.Length..];

        // HQ must see the latest user message BEFORE role metadata, project
        // summaries, original instructions and the routing contract.
        // An empty base prompt contains nothing to supersede: keep the raw
        // user message exactly as it was received.
        return body.Length == 0 ? userText : userText + priorityAndPrevious;
    }

    public static bool CanAcknowledgeAiDelivery(int exitCode, string? finalMessage) =>
        exitCode == 0 && !string.IsNullOrWhiteSpace(finalMessage);

    private List<HqUserIntervention> ReadCore()
    {
        if (!File.Exists(_path))
            return new List<HqUserIntervention>();
        return JsonSerializer.Deserialize<List<HqUserIntervention>>(
            File.ReadAllText(_path, Encoding.UTF8),
            ProjectHubJson.WebCompactOptions)
            ?? throw new IOException("HQ_USER_INTERVENTION_STORE_INVALID");
    }

    private void SaveCore(List<HqUserIntervention> all)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(
            all, ProjectHubJson.WebIndentedOptions), ProjectHubJson.Utf8NoBom);
        File.Move(tempPath, _path, true);
    }
}
