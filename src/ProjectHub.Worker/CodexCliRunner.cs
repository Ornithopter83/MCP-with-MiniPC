using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record CodexCliResult(
    string ExecutablePath,
    string Model,
    string Reasoning,
    string ConversationTitle,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string FinalMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

public sealed class CodexCliRunner
{
    public string? FindExecutable()
    {
        var candidates = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, "codex.exe")));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var bundledRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (Directory.Exists(bundledRoot))
        {
            candidates.AddRange(Directory.EnumerateFiles(bundledRoot, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc));
        }

        return candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    public async Task<CodexCliResult> RunAsync(string prompt, string model, string reasoning, string workingDirectory, CancellationToken cancellationToken)
    {
        var executable = FindExecutable() ?? throw new FileNotFoundException("codex.exe를 찾을 수 없습니다.");
        var outputFile = Path.Combine(Path.GetTempPath(), $"projecthub-codex-{Guid.NewGuid():N}.txt");
        var startedAt = DateTimeOffset.UtcNow;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.ArgumentList.Add("exec");
        process.StartInfo.ArgumentList.Add("--json");
        process.StartInfo.ArgumentList.Add("--model");
        process.StartInfo.ArgumentList.Add(model);
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add($"model_reasoning_effort=\"{reasoning}\"");
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(workingDirectory);
        process.StartInfo.ArgumentList.Add("--output-last-message");
        process.StartInfo.ArgumentList.Add(outputFile);
        process.StartInfo.ArgumentList.Add(prompt);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Codex CLI 프로세스를 시작하지 못했습니다.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var finalMessage = File.Exists(outputFile) ? await File.ReadAllTextAsync(outputFile) : ExtractFinalMessage(stdout);
            var finishedAt = DateTimeOffset.UtcNow;
            return new CodexCliResult(executable, model, reasoning, CreateConversationTitle(prompt), process.ExitCode, stdout, stderr, finalMessage.Trim(), startedAt, finishedAt);
        }
        finally
        {
            try { if (File.Exists(outputFile)) File.Delete(outputFile); } catch (IOException) { }
        }
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
                {
                    if (document.RootElement.TryGetProperty(propertyName, out var property))
                    {
                        var value = property.ToString();
                        if (!string.IsNullOrWhiteSpace(value)) messages.Add(value);
                    }
                }
            }
            catch (JsonException)
            {
            }
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

file static class StringExtensions
{
    public static IEnumerable<string> SplitLines(this string value) => value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
}



