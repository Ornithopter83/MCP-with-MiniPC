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
    IReadOnlyList<CodexCommandExecution>? CommandExecutions = null);

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

    public async Task<CodexCliResult> RunAsync(string prompt, string model, string reasoning, string workingDirectory, string? sessionId, bool readOnly, CancellationToken cancellationToken, string? outputSchemaJson = null, CodexSandboxMode? sandboxMode = null)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Codex 작업 폴더를 찾을 수 없습니다: {workingDirectory}");
        var executable = FindExecutable() ?? throw new FileNotFoundException("codex.exe를 찾을 수 없습니다.");
        var outputFile = Path.Combine(Path.GetTempPath(), $"projecthub-codex-{Guid.NewGuid():N}.txt");
        var outputSchemaFile = string.IsNullOrWhiteSpace(outputSchemaJson) ? null : Path.Combine(Path.GetTempPath(), $"projecthub-schema-{Guid.NewGuid():N}.json");
        if (outputSchemaFile is not null) await File.WriteAllTextAsync(outputSchemaFile, outputSchemaJson!, new UTF8Encoding(false), cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
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
        process.StartInfo.ArgumentList.Add("--model");
        process.StartInfo.ArgumentList.Add(model);
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add($"model_reasoning_effort=\"{reasoning}\"");
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
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var finalMessage = File.Exists(outputFile) ? await File.ReadAllTextAsync(outputFile) : ExtractFinalMessage(stdout);
            var resolvedSessionId = sessionId ?? ExtractSessionId(stdout);
            return new CodexCliResult(executable, model, reasoning, CreateConversationTitle(prompt), resolvedSessionId, process.ExitCode, stdout, stderr, finalMessage.Trim(), ExtractFiles(stdout, workingDirectory), ExtractUsage(stdout), startedAt, DateTimeOffset.UtcNow, ExtractCommandExecutions(stdout));
        }
        finally
        {
            try { if (File.Exists(outputFile)) File.Delete(outputFile); } catch (IOException) { }
            try { if (outputSchemaFile is not null && File.Exists(outputSchemaFile)) File.Delete(outputSchemaFile); } catch (IOException) { }
        }
    }

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
            hasExitCode = hasExitCode && exitCode.TryGetInt32(out parsedExitCode);
            var type = element.TryGetProperty("type", out var typeValue) && typeValue.ValueKind == JsonValueKind.String ? typeValue.GetString() : null;
            if (hasCommand && hasExitCode && type is not null && (type.Contains("command", StringComparison.OrdinalIgnoreCase) || type.Contains("exec", StringComparison.OrdinalIgnoreCase)))
            {
                var text = command.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text) && text.Length <= 2000)
                    executions.Add(new(text, parsedExitCode));
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
    private static string? ExtractSessionId(string stdout)
    {
        foreach (var line in stdout.SplitLines())
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("type", out var type) &&
                    string.Equals(type.GetString(), "thread.started", StringComparison.OrdinalIgnoreCase) &&
                    root.TryGetProperty("thread_id", out var id))
                    return id.GetString();
            }
            catch (JsonException) { }
        }
        return null;
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
