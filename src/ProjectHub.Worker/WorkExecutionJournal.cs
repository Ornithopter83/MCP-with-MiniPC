using System.IO;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

// One durable owner per WORKITEM. A milestone is a verification boundary,
// not the lifetime of a WORK session.
internal sealed record WorkExecutionCheckpoint(
    string JobId,
    string WorkItemId,
    string OriginMilestoneId,
    IReadOnlyList<string> WritePaths,
    bool ReadOnly,
    string Provider,
    string Model,
    string State,
    string? SessionId,
    int Attempts,
    string LastReport,
    DateTimeOffset UpdatedAtUtc,
    int ReResolutionAttempts = 0);

internal static class WorkExecutionJournal
{
    private static readonly object Sync = new();

    public static bool TryBegin(
        string workspace,
        string jobId,
        string milestoneId,
        MilestoneWorkDefinition work,
        WorkerAiRoleSettings role,
        out WorkExecutionCheckpoint? checkpoint,
        out string error)
    {
        checkpoint = null;
        error = string.Empty;
        if (!TryPath(workspace, jobId, work.Id, out var path))
        {
            error = "WORK_ID_OR_JOB_INVALID";
            return false;
        }

        lock (Sync)
        {
            try
            {
                var previous = ReadFile(path);
                var mode = work.ExecutionMode.ToUpperInvariant();
                if (mode == "NEW" && previous is not null)
                {
                    error = "WORK_ID_ALREADY_USED";
                    return false;
                }
                if (mode == "CONTINUE")
                {
                    if (previous is null)
                    {
                        error = "WORK_CONTINUATION_NOT_FOUND";
                        return false;
                    }
                    if (previous.State is not ("RUNNING" or "IN_PROGRESS"))
                    {
                        error = "WORK_CONTINUATION_TERMINAL";
                        return false;
                    }
                    if (!SameScope(previous.WritePaths, work.WritePaths) ||
                        previous.ReadOnly != work.ReadOnly ||
                        !string.Equals(previous.Provider, role.Provider, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(previous.Model, role.Model, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "WORK_CONTINUATION_SCOPE_OR_MODEL_CHANGED";
                        return false;
                    }
                }

                checkpoint = previous is null
                    ? new WorkExecutionCheckpoint(
                        jobId, work.Id, milestoneId, work.WritePaths.ToArray(),
                        work.ReadOnly, role.Provider, role.Model, "RUNNING",
                        null, 0, string.Empty, DateTimeOffset.UtcNow)
                    : previous with { State = "RUNNING", UpdatedAtUtc = DateTimeOffset.UtcNow };
                WriteFile(path, checkpoint);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                error = "WORK_CHECKPOINT_IO: " + exception.Message;
                return false;
            }
        }
    }

    public static WorkExecutionCheckpoint? Read(
        string workspace, string jobId, string workId)
    {
        if (!TryPath(workspace, jobId, workId, out var path))
            return null;
        lock (Sync)
        {
            try { return ReadFile(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }
    }

    public static IReadOnlyList<WorkExecutionCheckpoint> ReadAll(
        string workspace, string jobId)
    {
        if (!TryPath(workspace, jobId, "10", out var samplePath))
            return Array.Empty<WorkExecutionCheckpoint>();
        var directory = Path.GetDirectoryName(samplePath)!;
        lock (Sync)
        {
            if (!Directory.Exists(directory))
                return Array.Empty<WorkExecutionCheckpoint>();
            var result = new List<WorkExecutionCheckpoint>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    var checkpoint = ReadFile(path);
                    if (checkpoint is not null &&
                        string.Equals(checkpoint.JobId, jobId, StringComparison.Ordinal))
                        result.Add(checkpoint);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    // A corrupt checkpoint is not silently eligible for resumption.
                }
            }
            return result.OrderBy(entry => long.Parse(entry.WorkItemId)).ToArray();
        }
    }

    public static bool Record(
        string workspace,
        string jobId,
        string workId,
        string state,
        string? sessionId,
        string? report = null,
        bool invocationStarted = false)
    {
        if (!TryPath(workspace, jobId, workId, out var path) ||
            state is not ("RUNNING" or "IN_PROGRESS" or "COMPLETED" or "BLOCKED" or "CANCELED"))
            return false;
        lock (Sync)
        {
            try
            {
                var prior = ReadFile(path);
                if (prior is null)
                    return false;
                var updated = prior with
                {
                    State = state,
                    SessionId = string.IsNullOrWhiteSpace(sessionId) ? prior.SessionId : sessionId,
                    Attempts = prior.Attempts + (invocationStarted ? 1 : 0),
                    LastReport = report is null ? prior.LastReport : report[..Math.Min(report.Length, 4000)],
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                WriteFile(path, updated);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return false;
            }
        }
    }

    // Atomically claim the one self-review for this WORKITEM's entire
    // checkpoint lifetime (including later CONTINUE and application restarts).
    // Keep the first report as provisional, not a terminal COMPLETED/BLOCKED.
    public static bool TryClaimReResolution(
        string workspace,
        string jobId,
        string workId,
        string? sessionId,
        string firstReport)
    {
        if (string.IsNullOrWhiteSpace(sessionId) ||
            !TryPath(workspace, jobId, workId, out var path))
            return false;

        lock (Sync)
        {
            try
            {
                var previous = ReadFile(path);
                if (previous is null ||
                    previous.State is not ("RUNNING" or "IN_PROGRESS") ||
                    previous.ReResolutionAttempts >= 1)
                    return false;

                var updated = previous with
                {
                    State = "IN_PROGRESS",
                    SessionId = sessionId,
                    ReResolutionAttempts = previous.ReResolutionAttempts + 1,
                    LastReport = firstReport[..Math.Min(firstReport.Length, 4000)],
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                WriteFile(path, updated);
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return false;
            }
        }
    }

    private static bool TryPath(string workspace, string jobId, string workId, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(workspace) ||
            string.IsNullOrWhiteSpace(jobId) ||
            !jobId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ||
            !long.TryParse(workId, out var id) || id < 10)
            return false;
        path = Path.Combine(Path.GetFullPath(workspace), ".projecthub",
            "work-executions", jobId, id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json");
        return true;
    }

    private static WorkExecutionCheckpoint? ReadFile(string path)
        => File.Exists(path)
            ? JsonSerializer.Deserialize<WorkExecutionCheckpoint>(
                File.ReadAllText(path, Encoding.UTF8), ProjectHubJson.WebIndentedOptions)
            : null;

    private static void WriteFile(string path, WorkExecutionCheckpoint value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, ProjectHubJson.WebIndentedOptions),
                ProjectHubJson.Utf8NoBom);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static bool SameScope(
        IReadOnlyList<string> original, IReadOnlyList<string> current)
        => original.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(current.OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
}
