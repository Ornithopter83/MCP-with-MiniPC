using System.IO;
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
    /// Put verbatim user input at the START of the next AI's actual input,
    /// not after a role contract's closing [RESPONSE=OK] example. The existing
    /// @@MESSAGE or HQ input-body header is kept; no new labels, identifiers or
    /// interpretation instructions are added to user text.
    /// </summary>
    public static string AppendToHqBody(
        string body, IReadOnlyList<HqUserIntervention> pending)
    {
        if (pending.Count == 0) return body;
        var userText = string.Join("\n\n", pending.Select(item => item.Message));
        if (body.Length == 0) return userText;

        // Specialist prompts must retain exactly one generated @@MESSAGE.
        // Compose the role's prompt before invoking this method so that
        // user-controlled lines are never normalized or rewritten.
        const string specialistHeader = "@@MESSAGE\n";
        if (body.StartsWith(specialistHeader, StringComparison.Ordinal))
            return specialistHeader + userText + "\n\n" +
                body[specialistHeader.Length..];

        const string specialistHeaderCrLf = "@@MESSAGE\r\n";
        if (body.StartsWith(specialistHeaderCrLf, StringComparison.Ordinal))
            return specialistHeaderCrLf + userText + "\r\n\r\n" +
                body[specialistHeaderCrLf.Length..];

        // HQ prompts already have a natural input-body slot. Insert BEFORE
        // the original input and its contract, not after the contract footer.
        foreach (var inputHeader in new[] { "입력 본문:\r\n", "입력 본문:\n" })
        {
            var index = body.IndexOf(inputHeader, StringComparison.Ordinal);
            if (index >= 0)
            {
                var insertAt = index + inputHeader.Length;
                return body[..insertAt] + userText + "\n\n" + body[insertAt..];
            }
        }

        return userText + "\n\n" + body;
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
