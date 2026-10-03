using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProjectHub.Worker;

public sealed record RepositoryRuntimePaths(
    string Root,
    string Worktrees,
    string IntegrationClones,
    string NuGetRoot,
    string NuGetPackages,
    string NuGetHttpCache,
    string NuGetPluginsCache,
    string NuGetScratch,
    string DotNetHome,
    string TempRoot);

public static class WorkerPaths
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Worker");

    public static string State => Path.Combine(Root, "state");
    public static string Config => Path.Combine(Root, "config");
    public static string Task => Path.Combine(Root, "Task");
    public static string Attachments => Path.Combine(Root, "attachments");
    public static string WebResults => Path.Combine(Root, "web-results");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Extension => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectHub", "GPTWeb-Hub", "extension");
    public static string ManagedWebRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectHub", "ManagedWeb");
    public static string ManagedWebBrowserRuntime => Path.Combine(ManagedWebRoot, "BrowserRuntime");
    public static string ManagedWebProfiles => Path.Combine(ManagedWebRoot, "Profiles");
    public static string ManagedWebHqProfile => Path.Combine(ManagedWebProfiles, "HQ");
    public static string ManagedWebResourceProfile => Path.Combine(ManagedWebProfiles, "RESOURCE");
    public static string GetPublishedArtifactDirectory(
        string repositoryRoot,
        string jobId,
        long invocation)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("저장소 경로가 비어 있습니다.", nameof(repositoryRoot));
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID가 비어 있습니다.", nameof(jobId));

        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return Path.Combine(
            root,
            ".projecthub",
            "artifacts",
            StableRuntimeSegment(jobId),
            "run-" + invocation.ToString("D12", System.Globalization.CultureInfo.InvariantCulture));
    }

    public static RepositoryRuntimePaths GetRepositoryRuntimePaths(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("저장소 경로가 비어 있습니다.", nameof(repositoryRoot));

        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(Path.GetFileName(root)))
            throw new InvalidOperationException("저장소 이름을 계산할 수 없습니다.");

        var runtimeRoot = Path.Combine(root, ".projecthub", "runtime");
        var nugetRoot = Path.Combine(runtimeRoot, "nuget");
        return new RepositoryRuntimePaths(
            runtimeRoot,
            Path.Combine(runtimeRoot, "worktrees"),
            Path.Combine(runtimeRoot, "integration-clones"),
            nugetRoot,
            Path.Combine(nugetRoot, "packages"),
            Path.Combine(nugetRoot, "http-cache"),
            Path.Combine(nugetRoot, "plugins-cache"),
            Path.Combine(nugetRoot, "scratch"),
            Path.Combine(runtimeRoot, "dotnet"),
            Path.Combine(runtimeRoot, "temp"));
    }

    public static void EnsureProjectHubGitIgnore(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
            throw new ArgumentException("저장소 경로가 존재하지 않습니다.", nameof(repositoryRoot));

        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var gitIgnorePath = Path.Combine(root, ".gitignore");
        const string entry = ".projecthub/";

        if (!File.Exists(gitIgnorePath))
        {
            File.WriteAllText(
                gitIgnorePath,
                entry + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        var lines = File.ReadAllLines(gitIgnorePath);
        if (lines.Any(line =>
                string.Equals(line.Trim(), entry, StringComparison.Ordinal) ||
                string.Equals(line.Trim(), "/.projecthub/", StringComparison.Ordinal) ||
                string.Equals(line.Trim(), ".projecthub", StringComparison.Ordinal) ||
                string.Equals(line.Trim(), "/.projecthub", StringComparison.Ordinal)))
        {
            return;
        }

        var existing = File.ReadAllText(gitIgnorePath);
        var separator = existing.Length == 0 ||
                        existing.EndsWith("\n", StringComparison.Ordinal) ||
                        existing.EndsWith("\r", StringComparison.Ordinal)
            ? string.Empty
            : Environment.NewLine;
        File.AppendAllText(
            gitIgnorePath,
            separator + entry + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string BuildWorkTempPath(
        RepositoryRuntimePaths runtime,
        string jobId,
        string workItemId)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return Path.Combine(
            runtime.TempRoot,
            StableRuntimeSegment(jobId),
            StableRuntimeSegment(workItemId));
    }

    public static string BuildResourceStagingRoot(
        RepositoryRuntimePaths runtime,
        string resourceType)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var segment = (resourceType ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "IMAGE" => "image",
            _ => throw new ArgumentException("지원되지 않는 RESOURCE 타입입니다.", nameof(resourceType))
        };
        return Path.Combine(runtime.TempRoot, segment);
    }

    public static string BuildResourceStagingDirectory(
        RepositoryRuntimePaths runtime,
        string resourceType,
        string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) ||
            requestId.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("RESOURCE request ID가 안전한 형식이 아닙니다.", nameof(requestId));

        return Path.Combine(
            BuildResourceStagingRoot(runtime, resourceType),
            requestId.Trim());
    }


    public static void EnsureWorkToolDirectories(
        RepositoryRuntimePaths runtime,
        string workTempPath)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var buildRoot = Path.Combine(workTempPath, "build");
        var appData = Path.Combine(workTempPath, "appdata");
        var localAppData = Path.Combine(workTempPath, "localappdata");
        var nuGetConfigDirectory = Path.Combine(appData, "NuGet");
        foreach (var directory in new[]
        {
            runtime.NuGetPackages,
            runtime.NuGetHttpCache,
            runtime.NuGetPluginsCache,
            runtime.NuGetScratch,
            runtime.DotNetHome,
            workTempPath,
            buildRoot,
            Path.Combine(buildRoot, "bin"),
            Path.Combine(buildRoot, "obj"),
            appData,
            localAppData,
            nuGetConfigDirectory
        })
        {
            Directory.CreateDirectory(directory);
        }

        var isolatedNuGetConfig = Path.Combine(nuGetConfigDirectory, "NuGet.Config");
        if (!File.Exists(isolatedNuGetConfig))
        {
            File.WriteAllText(
                isolatedNuGetConfig,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration></configuration>");
        }
    }

    public static IReadOnlyDictionary<string, string> BuildWorkToolEnvironment(
        RepositoryRuntimePaths runtime,
        string workTempPath)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var buildRoot = Path.Combine(workTempPath, "build");
        var appData = Path.Combine(workTempPath, "appdata");
        var localAppData = Path.Combine(workTempPath, "localappdata");
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PROJECTHUB_BUILD_ROOT"] = buildRoot,
            ["PROJECTHUB_BUILD_BIN"] = Path.Combine(buildRoot, "bin"),
            ["PROJECTHUB_BUILD_OBJ"] = Path.Combine(buildRoot, "obj"),
            ["NUGET_PACKAGES"] = runtime.NuGetPackages,
            ["RestorePackagesPath"] = runtime.NuGetPackages,
            ["NUGET_HTTP_CACHE_PATH"] = runtime.NuGetHttpCache,
            ["NUGET_PLUGINS_CACHE_PATH"] = runtime.NuGetPluginsCache,
            ["NUGET_SCRATCH"] = runtime.NuGetScratch,
            ["DOTNET_CLI_HOME"] = runtime.DotNetHome,
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["APPDATA"] = appData,
            ["LOCALAPPDATA"] = localAppData,
            ["TEMP"] = workTempPath,
            ["TMP"] = workTempPath
        };
    }

    public static bool TryResetEphemeralDirectories(out string? errorDetail)
    {
        var errors = new List<string>();
        foreach (var directory in new[]
        {
            Task,
            Attachments,
            WebResults
        })
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    ClearDeleteBlockingAttributes(new DirectoryInfo(directory));
                    Directory.Delete(directory, recursive: true);
                }

                Directory.CreateDirectory(directory);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                errors.Add(
                    Path.GetFileName(directory) +
                    ": " +
                    exception.GetType().Name +
                    ": " +
                    exception.Message);
            }
        }

        errorDetail = errors.Count == 0
            ? null
            : string.Join(Environment.NewLine, errors);
        return errors.Count == 0;
    }

    private static void ClearDeleteBlockingAttributes(DirectoryInfo directory)
    {
        if (!directory.Exists)
            return;

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child &&
                !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                ClearDeleteBlockingAttributes(child);
            }

            entry.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.System);
        }

        directory.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.System);
    }

    public static void EnsureCreated()
    {
        foreach (var directory in new[]
        {
            Root,
            State,
            Config,
            Task,
            Attachments,
            WebResults,
            Logs,
            Extension,
            ManagedWebRoot,
            ManagedWebBrowserRuntime,
            ManagedWebProfiles,
            ManagedWebHqProfile,
            ManagedWebResourceProfile
        })
            Directory.CreateDirectory(directory);
    }

    private static string StableRuntimeSegment(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "empty" : value.Trim();
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant()[..12];
    }
}
