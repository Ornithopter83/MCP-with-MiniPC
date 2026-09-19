using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record CodexUsage(long InputTokens, long CachedInputTokens, long OutputTokens, long ReasoningOutputTokens, long TotalTokens)
{
    public static CodexUsage Empty => new(0, 0, 0, 0, 0);
    public CodexUsage Add(CodexUsage other) => new(InputTokens + other.InputTokens, CachedInputTokens + other.CachedInputTokens, OutputTokens + other.OutputTokens, ReasoningOutputTokens + other.ReasoningOutputTokens, TotalTokens + other.TotalTokens);
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
    CodexUsage Usage,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

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

    public async Task<CodexCliResult> RunAsync(string prompt, string model, string reasoning, string workingDirectory, string? sessionId, CancellationToken cancellationToken)
    {
        var executable = FindExecutable() ?? throw new FileNotFoundException("codex.exe를 찾을 수 없습니다.");
        var outputFile = Path.Combine(Path.GetTempPath(), $"projecthub-codex-{Guid.NewGuid():N}.txt");
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
        process.StartInfo.ArgumentList.Add("--output-last-message");
        process.StartInfo.ArgumentList.Add(outputFile);
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
            return new CodexCliResult(executable, model, reasoning, CreateConversationTitle(prompt), resolvedSessionId, process.ExitCode, stdout, stderr, finalMessage.Trim(), ExtractUsage(stdout), startedAt, DateTimeOffset.UtcNow);
        }
        finally
        {
            try { if (File.Exists(outputFile)) File.Delete(outputFile); } catch (IOException) { }
        }
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
    private static CodexUsage ExtractUsage(string stdout)
    {
        var total = CodexUsage.Empty;
        foreach (var line in stdout.SplitLines())
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                foreach (var usage in FindUsageObjects(document.RootElement))
                    total = total.Add(ReadUsage(usage));
            }
            catch (JsonException) { }
        }

        if (total.TotalTokens == 0)
            total = total with { TotalTokens = total.InputTokens + total.OutputTokens + total.ReasoningOutputTokens };
        return total;
    }

    private static IEnumerable<JsonElement> FindUsageObjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                yield return usage;
            if (root.TryGetProperty("token_usage", out var tokenUsage) && tokenUsage.ValueKind == JsonValueKind.Object)
                yield return tokenUsage;

            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    foreach (var nested in FindUsageObjects(property.Value))
                        yield return nested;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                foreach (var nested in FindUsageObjects(item))
                    yield return nested;
        }
    }

    private static CodexUsage ReadUsage(JsonElement usage)
    {
        var input = ReadLong(usage, "input_tokens", "inputTokens");
        var cached = ReadLong(usage, "cached_input_tokens", "cachedInputTokens");
        var output = ReadLong(usage, "output_tokens", "outputTokens");
        var reasoning = ReadLong(usage, "reasoning_output_tokens", "reasoningOutputTokens");
        var total = ReadLong(usage, "total_tokens", "totalTokens");
        if (total == 0)
            total = input + output + reasoning;
        return new CodexUsage(input, cached, output, reasoning, total);
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

    public static string RootPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectHub",
        "codex-threads");

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
