using System.IO;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record ProjectMemorySnapshot(
    string JobId,
    string WorkingDirectory,
    WorkerAiRoleSettings Coordinator,
    WorkerAiRoleSettings Implementer,
    string? CoordinatorSessionId,
    string? WorkSessionId,
    string Status,
    string LastHqMessage,
    string EventLogPath,
    DateTimeOffset UpdatedAtUtc)
{
    public CoordinatorContinuationState ToContinuation()
        => new(
            JobId,
            WorkingDirectory,
            Coordinator,
            Implementer,
            CoordinatorSessionId,
            WorkSessionId,
            Status,
            LastHqMessage);
}

public sealed record ProjectEventLogEntry(
    string EventId,
    DateTimeOffset Timestamp,
    string Source,
    string FullMessage,
    string? Status,
    string? ReferenceId,
    long? SizeBytes,
    int? ItemCount,
    int? FileCount);

public static class ProjectWorkspacePersistence
{
    private static readonly object EventSync = new();
    private static readonly JsonSerializerOptions StateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string RootDirectory(string workingDirectory)
        => Path.Combine(Path.GetFullPath(workingDirectory), ".projecthub");

    public static string StatePath(string workingDirectory)
        => Path.Combine(RootDirectory(workingDirectory), "session-state.json");

    public static string HandoffPath(string workingDirectory)
        => Path.Combine(RootDirectory(workingDirectory), "last-handoff.md");

    public static string EventDirectory(string workingDirectory)
        => Path.Combine(RootDirectory(workingDirectory), "events");

    public static string TranscriptDirectory(string workingDirectory)
        => Path.Combine(RootDirectory(workingDirectory), "transcripts");

    public static string EventLogPath(string workingDirectory, string jobId)
        => Path.Combine(EventDirectory(workingDirectory), SanitizeId(jobId) + ".jsonl");

    public static string TranscriptPath(string workingDirectory, string jobId)
        => Path.Combine(TranscriptDirectory(workingDirectory), SanitizeId(jobId) + ".txt");

    public static string? AppendEvent(
        string? workingDirectory,
        string? jobId,
        DateTimeOffset timestamp,
        string source,
        string fullMessage,
        string? status = null,
        string? referenceId = null,
        long? sizeBytes = null,
        int? itemCount = null,
        int? fileCount = null)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) ||
            string.IsNullOrWhiteSpace(jobId) ||
            string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(fullMessage) ||
            !Directory.Exists(workingDirectory))
            return null;

        try
        {
            var path = EventLogPath(workingDirectory, jobId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var eventId = Guid.NewGuid().ToString("N");
            var entry = new ProjectEventLogEntry(
                eventId,
                timestamp,
                source.Trim(),
                fullMessage.Trim(),
                status,
                referenceId,
                sizeBytes,
                itemCount,
                fileCount);
            var line = WorkerTranscriptJson.Serialize(entry) + Environment.NewLine;
            lock (EventSync)
            {
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
            return eventId;
        }
        catch
        {
            return null;
        }
    }

    public static bool SaveContinuation(CoordinatorContinuationState state)
    {
        if (string.IsNullOrWhiteSpace(state.WorkingDirectory) || !Directory.Exists(state.WorkingDirectory))
            return false;

        try
        {
            var root = RootDirectory(state.WorkingDirectory);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(EventDirectory(state.WorkingDirectory));
            Directory.CreateDirectory(TranscriptDirectory(state.WorkingDirectory));

            var eventLogPath = EventLogPath(state.WorkingDirectory, state.JobId);
            var snapshot = new ProjectMemorySnapshot(
                state.JobId,
                Path.GetFullPath(state.WorkingDirectory),
                state.Coordinator,
                state.Implementer,
                state.CoordinatorSessionId,
                state.WorkSessionId,
                state.Status,
                state.LastHqMessage ?? string.Empty,
                eventLogPath,
                DateTimeOffset.UtcNow);

            WriteAtomic(
                StatePath(state.WorkingDirectory),
                JsonSerializer.Serialize(snapshot, StateJsonOptions));

            var handoff = BuildHandoff(snapshot);
            WriteAtomic(HandoffPath(state.WorkingDirectory), handoff);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ClearContinuation(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return;
        try
        {
            var path = StatePath(workingDirectory);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    public static ProjectMemorySnapshot? TryLoad(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return null;

        try
        {
            var path = StatePath(workingDirectory);
            if (!File.Exists(path)) return null;
            var snapshot = JsonSerializer.Deserialize<ProjectMemorySnapshot>(
                File.ReadAllText(path, Encoding.UTF8),
                StateJsonOptions);
            if (snapshot is null) return null;
            if (!PathsEqual(snapshot.WorkingDirectory, workingDirectory)) return null;

            var coordinatorSession = IsCodexSessionAvailable(workingDirectory, snapshot.CoordinatorSessionId)
                ? snapshot.CoordinatorSessionId
                : null;
            var workSession = IsCodexSessionAvailable(workingDirectory, snapshot.WorkSessionId)
                ? snapshot.WorkSessionId
                : null;

            return snapshot with
            {
                WorkingDirectory = Path.GetFullPath(workingDirectory),
                CoordinatorSessionId = coordinatorSession,
                WorkSessionId = workSession
            };
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<ProjectEventLogEntry> ReadRecentEvents(string workingDirectory, string jobId, int maxCount = 200)
    {
        if (maxCount <= 0) return Array.Empty<ProjectEventLogEntry>();
        try
        {
            var path = EventLogPath(workingDirectory, jobId);
            if (!File.Exists(path)) return Array.Empty<ProjectEventLogEntry>();
            var queue = new Queue<ProjectEventLogEntry>(maxCount);
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<ProjectEventLogEntry>(line, StateJsonOptions);
                    if (entry is null) continue;
                    if (queue.Count == maxCount) queue.Dequeue();
                    queue.Enqueue(entry);
                }
                catch (JsonException)
                {
                }
            }
            return queue.ToArray();
        }
        catch
        {
            return Array.Empty<ProjectEventLogEntry>();
        }
    }

    private static string BuildHandoff(ProjectMemorySnapshot snapshot)
    {
        var coordinatorSession = string.IsNullOrWhiteSpace(snapshot.CoordinatorSessionId) ? "없음" : snapshot.CoordinatorSessionId;
        var workSession = string.IsNullOrWhiteSpace(snapshot.WorkSessionId) ? "없음" : snapshot.WorkSessionId;
        return $"# ProjectHub 프로젝트 기억{Environment.NewLine}{Environment.NewLine}" +
               $"상태: {snapshot.Status}{Environment.NewLine}" +
               $"작업 ID: {snapshot.JobId}{Environment.NewLine}" +
               $"갱신 시각(UTC): {snapshot.UpdatedAtUtc:O}{Environment.NewLine}" +
               $"HQ 세션: {coordinatorSession}{Environment.NewLine}" +
               $"WORK 세션: {workSession}{Environment.NewLine}" +
               $"이벤트 로그: {snapshot.EventLogPath}{Environment.NewLine}{Environment.NewLine}" +
               $"## 마지막 HQ 메시지{Environment.NewLine}{Environment.NewLine}" +
               (string.IsNullOrWhiteSpace(snapshot.LastHqMessage) ? "이전 HQ 메시지 없음" : snapshot.LastHqMessage.Trim()) +
               Environment.NewLine;
    }

    private static bool IsCodexSessionAvailable(string workingDirectory, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;
        try
        {
            var sessionsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex",
                "sessions");
            if (!Directory.Exists(sessionsRoot)) return false;

            foreach (var file in Directory.EnumerateFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories))
            {
                try
                {
                    var firstLine = File.ReadLines(file).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(firstLine)) continue;
                    using var document = JsonDocument.Parse(firstLine);
                    if (!document.RootElement.TryGetProperty("payload", out var payload)) continue;
                    var storedSession = payload.TryGetProperty("session_id", out var sessionElement)
                        ? sessionElement.GetString()
                        : null;
                    if (!string.Equals(storedSession, sessionId, StringComparison.OrdinalIgnoreCase)) continue;
                    var cwd = payload.TryGetProperty("cwd", out var cwdElement) ? cwdElement.GetString() : null;
                    return !string.IsNullOrWhiteSpace(cwd) && PathsEqual(cwd, workingDirectory);
                }
                catch (JsonException)
                {
                }
                catch (IOException)
                {
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    private static string SanitizeId(string value)
    {
        var filtered = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(filtered) ? "job" : filtered;
    }
}
