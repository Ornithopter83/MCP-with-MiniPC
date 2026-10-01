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
        => ParseAuthorizationOrFullFallback(body, out _);

    public static BuildAuthorization ParseAuthorizationOrFullFallback(
        string? body,
        out string? fallbackReason)
    {
        fallbackReason = null;
        try
        {
            var json = ExtractJson(body);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return FullFallback("BUILD_AUTHORIZATION_ROOT_NOT_OBJECT", out fallbackReason);

            if (!root.TryGetProperty("scope", out var scopeElement) ||
                scopeElement.ValueKind != JsonValueKind.String)
            {
                return FullFallback("BUILD_AUTHORIZATION_SCOPE_MISSING", out fallbackReason);
            }

            var scope = (scopeElement.GetString() ?? string.Empty).Trim().ToUpperInvariant();
            if (scope is not ("TARGET" or "FULL"))
                return FullFallback("BUILD_AUTHORIZATION_SCOPE_INVALID", out fallbackReason);

            string? target = null;
            if (root.TryGetProperty("target", out var targetElement) &&
                targetElement.ValueKind != JsonValueKind.Null)
            {
                if (targetElement.ValueKind != JsonValueKind.String)
                    return FullFallback("BUILD_AUTHORIZATION_TARGET_INVALID", out fallbackReason);
                target = targetElement.GetString();
            }
            if (scope == "TARGET" && string.IsNullOrWhiteSpace(target))
                return FullFallback("BUILD_AUTHORIZATION_TARGET_MISSING", out fallbackReason);

            var configuration = "Debug";
            if (root.TryGetProperty("configuration", out var configurationElement) &&
                configurationElement.ValueKind != JsonValueKind.Null)
            {
                if (configurationElement.ValueKind != JsonValueKind.String)
                    return FullFallback("BUILD_AUTHORIZATION_CONFIGURATION_INVALID", out fallbackReason);
                configuration = configurationElement.GetString() ?? string.Empty;
            }
            configuration = string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration.Trim();

            var noRestore = false;
            if (root.TryGetProperty("noRestore", out var noRestoreElement) &&
                noRestoreElement.ValueKind != JsonValueKind.Null)
            {
                if (noRestoreElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return FullFallback("BUILD_AUTHORIZATION_NO_RESTORE_INVALID", out fallbackReason);
                noRestore = noRestoreElement.GetBoolean();
            }

            return new BuildAuthorization(scope, target?.Trim(), configuration, noRestore, false);
        }
        catch (JsonException)
        {
            return FullFallback("BUILD_AUTHORIZATION_JSON_INVALID", out fallbackReason);
        }
        catch (InvalidOperationException)
        {
            return FullFallback("BUILD_AUTHORIZATION_VALUE_INVALID", out fallbackReason);
        }
    }

    private static BuildAuthorization FullFallback(
        string reason,
        out string? fallbackReason)
    {
        fallbackReason = reason;
        return new("FULL", null, "Debug", false, true);
    }

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
        using var launched = processJob.Start(startInfo, cancellationToken);
        var process = launched.Process;
        var outputTask = launched.StandardOutput!.ReadToEndAsync(cancellationToken);
        var errorTask = launched.StandardError!.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            processJob.Dispose();
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

        var exitCode = process.ExitCode;
        processJob.Dispose();
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        var combined = output + (!string.IsNullOrEmpty(error) ? Environment.NewLine + error : string.Empty);
        await File.WriteAllTextAsync(logPath, combined, cancellationToken).ConfigureAwait(false);
        var summaryText = Limit(combined, 12000);
        return new MechanicalBuildResult(
            exitCode == 0,
            exitCode,
            target,
            logPath,
            summaryText,
            effectiveAuthorization.FallbackToFull);
    }

    internal static (string? Target, bool Fallback) ResolveTarget(
        string worktreePath,
        BuildAuthorization authorization)
    {
        var normalizedWorktree = Path.GetFullPath(worktreePath);

        if (authorization.Scope == "TARGET" && !string.IsNullOrWhiteSpace(authorization.Target))
        {
            var candidate = Path.GetFullPath(Path.Combine(normalizedWorktree, authorization.Target));
            if (IsInside(normalizedWorktree, candidate) &&
                !IsManagedIntegrationInput(normalizedWorktree, candidate) &&
                File.Exists(candidate) &&
                IsSupported(candidate))
            {
                return (candidate, false);
            }
        }

        var solution = Directory.EnumerateFiles(normalizedWorktree, "*.slnx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(normalizedWorktree, "*.sln", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (solution is not null)
            return (solution, authorization.Scope == "TARGET");

        var project = Directory.EnumerateFiles(normalizedWorktree, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsManagedIntegrationInput(normalizedWorktree, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return (project, authorization.Scope == "TARGET");
    }

    private static bool IsManagedIntegrationInput(string worktreePath, string path)
    {
        var relative = Path.GetRelativePath(worktreePath, path);
        if (Path.IsPathRooted(relative))
            return false;

        return relative
            .Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(
                segment,
                ".projecthub-integration-inputs",
                StringComparison.OrdinalIgnoreCase));
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
