using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record CodexUsage(long InputTokens, long CachedInputTokens, long OutputTokens, long ReasoningOutputTokens, long TotalTokens, long? ProviderTotalTokens = null, bool UsageKnown = false)
{
    public static CodexUsage Empty => new(0, 0, 0, 0, 0, null, false);
    public CodexUsage Add(CodexUsage other)
    {
        var providerTotal = !UsageKnown ? other.ProviderTotalTokens : !other.UsageKnown ? ProviderTotalTokens : ProviderTotalTokens is not null && other.ProviderTotalTokens is not null ? ProviderTotalTokens + other.ProviderTotalTokens : null;
        return new(InputTokens + other.InputTokens, CachedInputTokens + other.CachedInputTokens, OutputTokens + other.OutputTokens, ReasoningOutputTokens + other.ReasoningOutputTokens, TotalTokens + other.TotalTokens, providerTotal, UsageKnown || other.UsageKnown);
    }
}

public sealed record CodexCliResult(
    string ExecutablePath,
    string Model,
    string Reasoning,
    string ConversationTitle,
    string? SessionId,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string FinalMessage,
    IReadOnlyList<CodexCliFile> Files,
    CodexUsage Usage,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<CodexCommandExecution>? CommandExecutions = null,
    string? SessionDiagnostic = null);

public enum CodexSandboxMode
{
    ReadOnly,
    WorkspaceWrite,
    DangerFullAccess
}

public sealed record CodexCliFile(string Path, string FileName, string MimeType, long Size);

public sealed class CodexCliRunner
{
    public string? FindExecutable()
    {
        var candidates = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
            candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(directory => Path.Combine(directory, "codex.exe")));
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var bundledRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (Directory.Exists(bundledRoot))
            candidates.AddRange(Directory.EnumerateFiles(bundledRoot, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc));
        return candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    public async Task<CodexCliResult> RunAsync(string prompt, string model, string reasoning, string workingDirectory, string? sessionId, bool readOnly, CancellationToken cancellationToken, string? outputSchemaJson = null, CodexSandboxMode? sandboxMode = null, Action<string>? progress = null, Action<string>? sessionStarted = null)
    {
        sessionId = NormalizeSessionId(sessionId);
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Codex 작업 폴더를 찾을 수 없습니다: {workingDirectory}");
        if (!CodexModelRequest.TryCreate(model, reasoning, out var modelRequest))
            throw new ArgumentException($"현재 서비스 enum에 없는 모델/reasoning 조합입니다: {model} / {reasoning}");
        var executable = FindExecutable() ?? throw new FileNotFoundException("codex.exe를 찾을 수 없습니다.");
        var outputFile = Path.Combine(Path.GetTempPath(), $"projecthub-codex-{Guid.NewGuid():N}.txt");
        var outputSchemaFile = string.IsNullOrWhiteSpace(outputSchemaJson) ? null : Path.Combine(Path.GetTempPath(), $"projecthub-schema-{Guid.NewGuid():N}.json");
        if (outputSchemaFile is not null) await File.WriteAllTextAsync(outputSchemaFile, outputSchemaJson!, new UTF8Encoding(false), cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        var sessionSnapshot = string.IsNullOrWhiteSpace(sessionId)
            ? CodexSessionLocator.CaptureSnapshot(workingDirectory, startedAt)
            : null;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable, WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.ArgumentList.Add("exec");
        process.StartInfo.ArgumentList.Add("--sandbox");
        process.StartInfo.ArgumentList.Add((sandboxMode ?? (readOnly ? CodexSandboxMode.ReadOnly : CodexSandboxMode.DangerFullAccess)) switch
        {
            CodexSandboxMode.ReadOnly => "read-only",
            CodexSandboxMode.WorkspaceWrite => "workspace-write",
            _ => "danger-full-access"
        });
        if (!string.IsNullOrWhiteSpace(sessionId)) process.StartInfo.ArgumentList.Add("resume");
        process.StartInfo.ArgumentList.Add("--json");
        foreach (var argument in modelRequest.ToCliArguments())
            process.StartInfo.ArgumentList.Add(argument);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(workingDirectory);
        }
        if (!IsGitRepository(workingDirectory))
            process.StartInfo.ArgumentList.Add("--skip-git-repo-check");
        process.StartInfo.ArgumentList.Add("--output-last-message");
        process.StartInfo.ArgumentList.Add(outputFile);
        if (outputSchemaFile is not null)
        {
            process.StartInfo.ArgumentList.Add("--output-schema");
            process.StartInfo.ArgumentList.Add(outputSchemaFile);
        }
        if (!string.IsNullOrWhiteSpace(sessionId)) process.StartInfo.ArgumentList.Add(sessionId);
        process.StartInfo.ArgumentList.Add(prompt);
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Codex CLI 프로세스를 시작하지 못했습니다.");
            var stdoutBuilder = new StringBuilder();
            var stdoutTask = Task.Run(async () =>
            {
                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                    if (line is null) break;
                    stdoutBuilder.AppendLine(line);
                    if (sessionStarted is not null && TryExtractThreadStarted(line, out var startedSessionId))
                    {
                        try { sessionStarted(startedSessionId); }
                        catch { }
                    }
                    if (progress is not null && TryExtractAgentMessage(line, out var progressText))
                    {
                        try { progress(progressText); }
                        catch { }
                    }
                }
            }, CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
            await stdoutTask;
            var stdout = stdoutBuilder.ToString();
            var stderr = await stderrTask;
            var finalMessage = File.Exists(outputFile) ? await File.ReadAllTextAsync(outputFile) : ExtractFinalMessage(stdout);
            var finishedAt = DateTimeOffset.UtcNow;
            var resolvedSessionId = sessionId ?? ExtractSessionId(stdout);
            string? sessionDiagnostic = null;
            if (resolvedSessionId is null && sessionSnapshot is not null)
            {
                resolvedSessionId = CodexSessionLocator.FindNewSessionId(sessionSnapshot, finishedAt, out var searchDiagnostic);
                if (resolvedSessionId is null)
                    sessionDiagnostic = $"stdoutBytes={Encoding.UTF8.GetByteCount(stdout)}; {searchDiagnostic}";
            }
            return new CodexCliResult(executable, model, reasoning, CreateConversationTitle(prompt), resolvedSessionId, process.ExitCode, stdout, stderr, finalMessage.Trim(), ExtractFiles(stdout, workingDirectory), ExtractUsage(stdout), startedAt, finishedAt, ExtractCommandExecutions(stdout), sessionDiagnostic);
        }
        finally
        {
            try { if (File.Exists(outputFile)) File.Delete(outputFile); } catch (IOException) { }
            try { if (outputSchemaFile is not null && File.Exists(outputSchemaFile)) File.Delete(outputSchemaFile); } catch (IOException) { }
        }
    }

    public static bool TryExtractThreadStarted(string jsonLine, out string sessionId)
    {
        sessionId = string.Empty;
        if (string.IsNullOrWhiteSpace(jsonLine)) return false;
        try
        {
            using var document = JsonDocument.Parse(jsonLine.TrimStart('\uFEFF', ' ', '\t'));
            var root = document.RootElement;
            if (!TryGetString(root, "type", out var eventType) ||
                !string.Equals(eventType, "thread.started", StringComparison.OrdinalIgnoreCase) ||
                !TryGetString(root, "thread_id", out var id) ||
                string.IsNullOrWhiteSpace(id))
                return false;

            sessionId = id.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryExtractAgentMessage(string jsonLine, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(jsonLine)) return false;
        try
        {
            using var document = JsonDocument.Parse(jsonLine.TrimStart('\uFEFF'));
            var root = document.RootElement;
            if (!TryGetString(root, "type", out var eventType) ||
                !string.Equals(eventType, "item.completed", StringComparison.OrdinalIgnoreCase) ||
                !TryGetPropertyIgnoreCase(root, "item", out var item) ||
                !TryGetString(item, "type", out var itemType) ||
                !string.Equals(itemType, "agent_message", StringComparison.OrdinalIgnoreCase) ||
                !TryGetString(item, "text", out var message) ||
                string.IsNullOrWhiteSpace(message))
                return false;

            text = message.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? NormalizeSessionId(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();

    public static IReadOnlyList<CodexCommandExecution> ExtractCommandExecutions(string stdout)
    {
        var executions = new List<CodexCommandExecution>();
        foreach (var line in stdout.SplitLines())
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                CollectCommandExecutions(document.RootElement, executions);
            }
            catch (JsonException) { }
        }
        return executions
            .DistinctBy(item => (item.Command, item.ExitCode))
            .Take(250)
            .ToList();
    }

    private static void CollectCommandExecutions(JsonElement element, List<CodexCommandExecution> executions)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var hasCommand = element.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String;
            var hasExitCode = element.TryGetProperty("exit_code", out var exitCode) || element.TryGetProperty("exitCode", out exitCode);
            var parsedExitCode = 0;
            hasExitCode = hasExitCode && exitCode.ValueKind == JsonValueKind.Number && exitCode.TryGetInt32(out parsedExitCode);
            var type = element.TryGetProperty("type", out var typeValue) && typeValue.ValueKind == JsonValueKind.String ? typeValue.GetString() : null;
            if (hasCommand && hasExitCode && type is not null && (type.Contains("command", StringComparison.OrdinalIgnoreCase) || type.Contains("exec", StringComparison.OrdinalIgnoreCase)))
            {
                var text = command.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text) && text.Length <= 2000)
                {
                    var output = element.TryGetProperty("aggregated_output", out var aggregatedOutput) && aggregatedOutput.ValueKind == JsonValueKind.String
                        ? aggregatedOutput.GetString()
                        : null;
                    executions.Add(new(text, parsedExitCode, output is { Length: > 4000 } ? output[..4000] : output));
                }
            }
            foreach (var property in element.EnumerateObject())
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    CollectCommandExecutions(property.Value, executions);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectCommandExecutions(item, executions);
        }
    }

    private static bool IsGitRepository(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                return true;
            directory = directory.Parent;
        }
        return false;
    }
    public static string? ExtractSessionId(string stdout)
    {
        foreach (var line in stdout.SplitLines())
        {
            try
            {
                using var document = JsonDocument.Parse(line.TrimStart('\uFEFF', ' ', '\t'));
                var root = document.RootElement;
                if (TryGetPropertyIgnoreCase(root, "type", out var type) && type.ValueKind == JsonValueKind.String &&
                    string.Equals(type.GetString(), "thread.started", StringComparison.OrdinalIgnoreCase) &&
                    TryGetPropertyIgnoreCase(root, "thread_id", out var id) &&
                    id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
                    return id.GetString();
            }
            catch (JsonException) { }
        }
        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
    public static CodexUsage ExtractUsage(string stdout) => ProviderUsageParser.Extract(stdout);

    private static string ExtractFinalMessage(string stdout)
    {
        var messages = new List<string>();
        foreach (var line in stdout.SplitLines())
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                foreach (var propertyName in new[] { "message", "text", "content" })
                    if (document.RootElement.TryGetProperty(propertyName, out var property) && !string.IsNullOrWhiteSpace(property.ToString()))
                        messages.Add(property.ToString());
            }
            catch (JsonException) { }
        }
        return string.Join(Environment.NewLine, messages);
    }

    private static IReadOnlyList<CodexCliFile> ExtractFiles(string stdout, string workingDirectory)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in stdout.SplitLines())
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                CollectFileCandidates(document.RootElement, candidates);
            }
            catch (JsonException) { }
        }

        return candidates
            .Select(path => ResolveFilePath(path, workingDirectory))
            .Where(path => path is not null && File.Exists(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .Where(file => file.Length > 0)
            .Select(file => new CodexCliFile(file.FullName, file.Name, GetMimeType(file.Extension), file.Length))
            .ToList();
    }

    private static void CollectFileCandidates(JsonElement element, HashSet<string> candidates)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && IsFileProperty(property.Name))
                    candidates.Add(property.Value.GetString() ?? string.Empty);
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    CollectFileCandidates(property.Value, candidates);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectFileCandidates(item, candidates);
        }
    }

    private static bool IsFileProperty(string name) => name.Contains("path", StringComparison.OrdinalIgnoreCase)
        || name.Contains("file", StringComparison.OrdinalIgnoreCase)
        || name.Contains("artifact", StringComparison.OrdinalIgnoreCase)
        || name.Contains("attachment", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveFilePath(string value, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().Trim('"');
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && uri.IsFile)
            normalized = uri.LocalPath;
        if (!Path.IsPathRooted(normalized)) normalized = Path.Combine(workingDirectory, normalized);
        try { return Path.GetFullPath(normalized); } catch (ArgumentException) { return null; }
    }

    private static string GetMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        ".json" => "application/json",
        _ => "application/octet-stream"
    };
    private static string CreateConversationTitle(string prompt)
    {
        var title = prompt.SplitLines().FirstOrDefault()?.Trim() ?? string.Empty;
        if (title.Length > 80) title = title[..80].TrimEnd() + "...";
        return string.IsNullOrWhiteSpace(title) ? "Codex 작업" : title;
    }
}

public sealed record CodexSessionSnapshot(IReadOnlyList<string> SessionsRoots, string WorkingDirectory, DateTimeOffset StartedAt, IReadOnlySet<string> ExistingRolloutFiles);

public static class CodexSessionLocator
{
    public static CodexSessionSnapshot CaptureSnapshot(string workingDirectory, DateTimeOffset startedAt)
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var specialFolderProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var sessionsRoots = ResolveSessionsRoots(codexHome, userProfile, specialFolderProfile, localAppData);
        var existing = sessionsRoots.SelectMany(root => EnumerateRolloutFiles(root, startedAt, startedAt)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(sessionsRoots, Path.GetFullPath(workingDirectory), startedAt, existing);
    }

    public static IReadOnlyList<string> ResolveSessionsRoots(string? codexHome, string? userProfile, string? specialFolderProfile, string? localAppData = null) =>
        new[]
        {
            string.IsNullOrWhiteSpace(codexHome) ? null : Path.Combine(codexHome, "sessions"),
            string.IsNullOrWhiteSpace(userProfile) ? null : Path.Combine(userProfile, ".codex", "sessions"),
            string.IsNullOrWhiteSpace(specialFolderProfile) ? null : Path.Combine(specialFolderProfile, ".codex", "sessions"),
            ProfileFromLocalAppData(localAppData) is { } localProfile ? Path.Combine(localProfile, ".codex", "sessions") : null
        }
        .Where(path => path is not null)
        .Select(path => Path.GetFullPath(path!))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string? ProfileFromLocalAppData(string? localAppData)
    {
        if (string.IsNullOrWhiteSpace(localAppData)) return null;
        var local = new DirectoryInfo(Path.GetFullPath(localAppData));
        return string.Equals(local.Name, "Local", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(local.Parent?.Name, "AppData", StringComparison.OrdinalIgnoreCase)
            ? local.Parent?.Parent?.FullName
            : null;
    }

    public static string ResolveSessionsRoot(string? codexHome, string? userProfile, string? specialFolderProfile)
    {
        var root = !string.IsNullOrWhiteSpace(codexHome)
            ? codexHome
            : !string.IsNullOrWhiteSpace(userProfile)
                ? Path.Combine(userProfile, ".codex")
                : Path.Combine(specialFolderProfile ?? string.Empty, ".codex");
        return Path.Combine(root, "sessions");
    }

    public static string? FindNewSessionId(CodexSessionSnapshot snapshot, DateTimeOffset finishedAt) =>
        FindNewSessionId(snapshot, finishedAt, out _);

    public static string? FindNewSessionId(CodexSessionSnapshot snapshot, DateTimeOffset finishedAt, out string diagnostic)
    {
        var matches = new List<string>();
        var scanned = 0;
        var newFiles = 0;
        var writeWindow = 0;
        var metadata = 0;
        var cliSource = 0;
        var cwdMatch = 0;
        var timestampMatch = 0;
        foreach (var path in snapshot.SessionsRoots.SelectMany(root => EnumerateRolloutFiles(root, snapshot.StartedAt, finishedAt)))
        {
            scanned++;
            if (snapshot.ExistingRolloutFiles.Contains(path)) continue;
            newFiles++;
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(path);
                if (lastWrite < snapshot.StartedAt.UtcDateTime.AddSeconds(-3) || lastWrite > finishedAt.UtcDateTime.AddSeconds(5)) continue;
                writeWindow++;
                using var stream = File.OpenRead(path);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var firstLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(firstLine)) continue;
                using var document = JsonDocument.Parse(firstLine);
                var root = document.RootElement;
                if (!TryGetString(root, "type", out var type) || !string.Equals(type, "session_meta", StringComparison.OrdinalIgnoreCase) ||
                    !TryGetPropertyIgnoreCase(root, "payload", out var payload)) continue;
                metadata++;
                if (!TryGetString(payload, "originator", out var originator) || !string.Equals(originator, "codex_exec", StringComparison.OrdinalIgnoreCase) ||
                    !TryGetString(payload, "source", out var source) || !string.Equals(source, "exec", StringComparison.OrdinalIgnoreCase)) continue;
                cliSource++;
                if (!TryGetString(payload, "cwd", out var cwd) || !PathsEqual(cwd, snapshot.WorkingDirectory)) continue;
                cwdMatch++;
                if (!TryGetString(payload, "timestamp", out var timestamp) ||
                    !DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var sessionStartedAt) ||
                    sessionStartedAt < snapshot.StartedAt.AddSeconds(-3) || sessionStartedAt > finishedAt.AddSeconds(5)) continue;
                timestampMatch++;
                if (!TryGetString(payload, "id", out var id) && !TryGetString(payload, "session_id", out id)) continue;
                if (!string.IsNullOrWhiteSpace(id)) matches.Add(id);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                // Session metadata is only a fallback to the CLI's JSONL event; unreadable files are ignored.
            }
        }
        diagnostic = $"roots={snapshot.SessionsRoots.Count}; availableRoots={snapshot.SessionsRoots.Count(Directory.Exists)}; scanned={scanned}; new={newFiles}; writeWindow={writeWindow}; metadata={metadata}; cliSource={cliSource}; cwd={cwdMatch}; timestamp={timestampMatch}; ids={matches.Count}";
        return matches.Count == 1 ? matches[0] : null;
    }

    private static IEnumerable<string> EnumerateRolloutFiles(string root, DateTimeOffset start, DateTimeOffset end)
    {
        if (!Directory.Exists(root)) yield break;
        for (var day = start.ToLocalTime().Date; day <= end.ToLocalTime().Date; day = day.AddDays(1))
        {
            var directory = Path.Combine(root, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
            if (!Directory.Exists(directory)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory, "rollout-*.jsonl", SearchOption.TopDirectoryOnly).ToArray(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files) yield return file;
        }
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (TryGetPropertyIgnoreCase(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}

public sealed record CachedCodexThread(
    string SessionId,
    string ProjectPath,
    string Title,
    DateTimeOffset UpdatedAt);

public static class CodexThreadArchive
{
    private static readonly object Sync = new();

    public static string RootPath => Path.Combine(WorkerPaths.Root, "codex-threads");

    public static void Save(CodexCliResult result, string prompt, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(result.SessionId)) return;
        var normalizedProject = Path.GetFullPath(projectPath);
        var directory = Path.Combine(RootPath, result.SessionId);
        Directory.CreateDirectory(directory);
        var now = DateTimeOffset.UtcNow;
        var metadata = new CachedCodexThread(result.SessionId, normalizedProject, result.ConversationTitle, now);
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        var metadataPath = Path.Combine(directory, "metadata.json");
        var tempPath = metadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        lock (Sync)
        {
            File.WriteAllText(tempPath, metadataJson, Encoding.UTF8);
            File.Move(tempPath, metadataPath, true);
            var transcriptPath = Path.Combine(directory, "transcript.jsonl");
            var entries = new[]
            {
                JsonSerializer.Serialize(new { timestamp = now, role = "user", content = prompt }),
                JsonSerializer.Serialize(new { timestamp = now, role = "assistant", content = result.FinalMessage })
            };
            File.AppendAllLines(transcriptPath, entries, Encoding.UTF8);
            var handoff = string.Join(Environment.NewLine, new[]
            {
                "# ProjectHub Codex Handoff",
                "",
                $"- Thread ID: {result.SessionId}",
                $"- Project: {normalizedProject}",
                $"- Updated: {now:O}",
                "",
                "## Latest request",
                prompt,
                "",
                "## Latest response",
                result.FinalMessage
            });
            File.WriteAllText(Path.Combine(directory, "handoff.md"), handoff, new UTF8Encoding(false));
        }
    }

    public static IEnumerable<CachedCodexThread> ReadForProject(string projectPath)
    {
        var normalizedProject = Path.GetFullPath(projectPath);
        if (!Directory.Exists(RootPath)) yield break;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(RootPath, "metadata.json", SearchOption.AllDirectories).ToList(); }
        catch { yield break; }
        foreach (var file in files)
        {
            CachedCodexThread? item = null;
            try { item = JsonSerializer.Deserialize<CachedCodexThread>(File.ReadAllText(file)); }
            catch { }
            if (item is not null &&
                string.Equals(Path.GetFullPath(item.ProjectPath), normalizedProject, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.SessionId))
                yield return item;
        }
    }
}
file static class StringExtensions
{
    public static IEnumerable<string> SplitLines(this string value) => value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
}
