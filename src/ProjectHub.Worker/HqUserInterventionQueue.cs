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
/// is acknowledged only after the next AI role returns a valid response.
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

    public static string AppendToHqBody(
        string body, IReadOnlyList<HqUserIntervention> pending)
    {
        if (pending.Count == 0) return body;
        var result = new StringBuilder(body);
        result.AppendLine().AppendLine();
        result.AppendLine("실행 중 추가된 사용자 메시지 (원문 및 입력 순서 유지):");
        result.AppendLine("다음 역할 AI가 의미를 판단한다. Worker는 자연어를 PAUSE로 해석하지 않는다. 역할이 직접 처리할 수 없다면 보고를 통해 HQ에 인계하라.");
        foreach (var item in pending)
        {
            result.Append("USER_INTERVENTION_ID: ").AppendLine(item.Id);
            result.AppendLine("USER_MESSAGE_BEGIN");
            result.AppendLine(item.Message);
            result.AppendLine("USER_MESSAGE_END");
        }
        return result.ToString();
    }

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
