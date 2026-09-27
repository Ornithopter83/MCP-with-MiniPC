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

    public static RepositoryRuntimePaths GetRepositoryRuntimePaths(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("저장소 경로가 비어 있습니다.", nameof(repositoryRoot));

        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Directory.GetParent(root)?.FullName
            ?? throw new InvalidOperationException("저장소 상위 경로를 계산할 수 없습니다.");
        var repositoryName = Path.GetFileName(root);
        if (string.IsNullOrWhiteSpace(repositoryName))
            throw new InvalidOperationException("저장소 이름을 계산할 수 없습니다.");

        var runtimeRoot = Path.Combine(parent, repositoryName + ".projecthub");
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

    public static void EnsureWorkToolDirectories(
        RepositoryRuntimePaths runtime,
        string workTempPath)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        foreach (var directory in new[]
        {
            runtime.NuGetPackages,
            runtime.NuGetHttpCache,
            runtime.NuGetPluginsCache,
            runtime.NuGetScratch,
            runtime.DotNetHome,
            workTempPath
        })
        {
            Directory.CreateDirectory(directory);
        }
    }

    public static IReadOnlyDictionary<string, string> BuildWorkToolEnvironment(
        RepositoryRuntimePaths runtime,
        string workTempPath)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NUGET_PACKAGES"] = runtime.NuGetPackages,
            ["RestorePackagesPath"] = runtime.NuGetPackages,
            ["NUGET_HTTP_CACHE_PATH"] = runtime.NuGetHttpCache,
            ["NUGET_PLUGINS_CACHE_PATH"] = runtime.NuGetPluginsCache,
            ["NUGET_SCRATCH"] = runtime.NuGetScratch,
            ["DOTNET_CLI_HOME"] = runtime.DotNetHome,
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["TEMP"] = workTempPath,
            ["TMP"] = workTempPath
        };
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
