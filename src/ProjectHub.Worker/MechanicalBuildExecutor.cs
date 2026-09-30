using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record BuildAuthorization(
    string Scope,
    string? Target,
    string Configuration,
    bool NoRestore,
    bool FallbackToFull);

internal sealed record MechanicalBuildResult(
    bool Success,
    int ExitCode,
    string Target,
    string LogPath,
    string Summary,
    bool FallbackToFull);

internal static class BuildRequestContract
{
    private const string Marker = "BUILD_REQUEST";

    public static bool ContainsRequest(string? body)
        => (body ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Any(line => line.Trim().StartsWith(Marker, StringComparison.Ordinal));

    public static BuildAuthorization ParseAuthorizationOrFullFallback(string? body)
    {
        try
        {
            var json = ExtractJson(body);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return FullFallback();

            var scope = root.TryGetProperty("scope", out var scopeElement)
                ? (scopeElement.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
            if (scope is not ("TARGET" or "FULL"))
                return FullFallback();

            var target = root.TryGetProperty("target", out var targetElement)
                ? targetElement.GetString()
                : null;
            if (scope == "TARGET" && string.IsNullOrWhiteSpace(target))
                return FullFallback();

            var configuration = root.TryGetProperty("configuration", out var configurationElement)
                ? configurationElement.GetString()
                : "Debug";
            configuration = string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration.Trim();

            var noRestore = root.TryGetProperty("noRestore", out var noRestoreElement) &&
                            noRestoreElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                            noRestoreElement.GetBoolean();

            return new BuildAuthorization(scope, target?.Trim(), configuration, noRestore, false);
        }
        catch (JsonException)
        {
            return FullFallback();
        }
        catch (InvalidOperationException)
        {
            return FullFallback();
        }
    }

    private static BuildAuthorization FullFallback()
        => new("FULL", null, "Debug", false, true);

    private static string ExtractJson(string? body)
    {
        var normalized = (body ?? string.Empty).Trim();
        var firstBrace = normalized.IndexOf('{');
        var lastBrace = normalized.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace < firstBrace)
            throw new JsonException("BUILD authorization JSON missing.");
        return normalized[firstBrace..(lastBrace + 1)];
    }
}

internal static class MechanicalBuildExecutor
{
    public static async Task<MechanicalBuildResult> ExecuteAsync(
        string worktreePath,
        string workTempPath,
        IReadOnlyDictionary<string, string> environment,
        BuildAuthorization authorization,
        CancellationToken cancellationToken)
    {
        var buildRoot = Path.Combine(workTempPath, "build");
        Directory.CreateDirectory(buildRoot);
        var logPath = Path.Combine(buildRoot, "build.log");

        var resolved = ResolveTarget(worktreePath, authorization);
        var effectiveAuthorization = resolved.Fallback
            ? authorization with { Scope = "FULL", Target = null, FallbackToFull = true }
            : authorization;

        var target = resolved.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            var summary = "BUILD_TARGET_NOT_FOUND: 빌드 가능한 .sln/.slnx/.csproj 대상을 찾지 못했습니다.";
            await File.WriteAllTextAsync(logPath, summary, cancellationToken).ConfigureAwait(false);
            return new MechanicalBuildResult(false, -1, "없음", logPath, summary, effectiveAuthorization.FallbackToFull);
        }

        var arguments = new List<string>
        {
            "build",
            target,
            "--configuration",
            effectiveAuthorization.Configuration,
            "--nologo",
            "--property:UseArtifactsOutput=true",
            "--property:ArtifactsPath=" + Path.Combine(buildRoot, "artifacts"),
            "--property:UseSharedCompilation=false"
        };
        if (effectiveAuthorization.NoRestore)
            arguments.Add("--no-restore");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = worktreePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var pair in environment)
            startInfo.Environment[pair.Key] = pair.Value;
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var processJob = new WorkerChildProcessJob("dotnet build");
        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException("BUILD_PROCESS_START_FAILED");

        processJob.Assign(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            throw;
        }

        var combined = output.ToString() + (error.Length > 0 ? Environment.NewLine + error : string.Empty);
        await File.WriteAllTextAsync(logPath, combined, cancellationToken).ConfigureAwait(false);
        var summaryText = Limit(combined, 12000);
        return new MechanicalBuildResult(
            process.ExitCode == 0,
            process.ExitCode,
            target,
            logPath,
            summaryText,
            effectiveAuthorization.FallbackToFull);
    }

    private static (string? Target, bool Fallback) ResolveTarget(
        string worktreePath,
        BuildAuthorization authorization)
    {
        if (authorization.Scope == "TARGET" && !string.IsNullOrWhiteSpace(authorization.Target))
        {
            var candidate = Path.GetFullPath(Path.Combine(worktreePath, authorization.Target));
            if (IsInside(worktreePath, candidate) &&
                File.Exists(candidate) &&
                IsSupported(candidate))
                return (candidate, false);
        }

        var solution = Directory.EnumerateFiles(worktreePath, "*.slnx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(worktreePath, "*.sln", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (solution is not null)
            return (solution, authorization.Scope == "TARGET");

        var project = Directory.EnumerateFiles(worktreePath, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return (project, authorization.Scope == "TARGET");
    }

    private static bool IsSupported(string path)
        => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string root, string path)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var pathFull = Path.GetFullPath(path);
        return pathFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
    }

    private static string Limit(string value, int max)
        => value.Length <= max ? value : value[..max] + Environment.NewLine + "[truncated]";
}
