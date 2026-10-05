using System.IO;
using System.Text;

namespace ProjectHub.Worker;

public sealed record RepositoryRuntimePaths(
    string Root,
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
    public static string Extension => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectHub",
        "GPTWeb-Hub",
        "extension");
    public static string ManagedWebRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectHub",
        "ManagedWeb");
    public static string ManagedWebBrowserRuntime =>
        Path.Combine(ManagedWebRoot, "BrowserRuntime");
    public static string ManagedWebProfiles =>
        Path.Combine(ManagedWebRoot, "Profiles");
    public static string ManagedWebHqProfile =>
        Path.Combine(ManagedWebProfiles, "HQ");
    public static string ManagedWebResourceProfile =>
        Path.Combine(ManagedWebProfiles, "RESOURCE");

    public static RepositoryRuntimePaths GetRepositoryRuntimePaths(
        string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException(
                "저장소 경로가 비어 있습니다.",
                nameof(repositoryRoot));

        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(Path.GetFileName(root)))
            throw new InvalidOperationException(
                "저장소 이름을 계산할 수 없습니다.");

        var tempRoot = Path.Combine(root, "temp");
        return new RepositoryRuntimePaths(
            Path.Combine(tempRoot, "ProjectHub"),
            tempRoot);
    }

    public static bool NeedsProjectHubGitIgnoreUpdate(
        string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) ||
            !Directory.Exists(repositoryRoot))
        {
            throw new ArgumentException(
                "저장소 경로가 존재하지 않습니다.",
                nameof(repositoryRoot));
        }

        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        var gitIgnorePath = Path.Combine(root, ".gitignore");
        var normalized = File.Exists(gitIgnorePath)
            ? File.ReadAllLines(gitIgnorePath)
                .Select(line => line.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new[] { "bin/", "temp/" }
            .Any(entry => !HasIgnoreEntry(normalized, entry));
    }

    public static bool EnsureProjectHubGitIgnore(
        string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) ||
            !Directory.Exists(repositoryRoot))
        {
            throw new ArgumentException(
                "저장소 경로가 존재하지 않습니다.",
                nameof(repositoryRoot));
        }

        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        var gitIgnorePath = Path.Combine(root, ".gitignore");
        var requiredEntries = new[] { "bin/", "temp/" };

        var existingLines = File.Exists(gitIgnorePath)
            ? File.ReadAllLines(gitIgnorePath).ToList()
            : new List<string>();
        var normalized = existingLines
            .Select(line => line.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = requiredEntries
            .Where(entry => !HasIgnoreEntry(normalized, entry))
            .ToArray();

        if (additions.Length == 0)
            return false;

        var existing = File.Exists(gitIgnorePath)
            ? File.ReadAllText(gitIgnorePath)
            : string.Empty;
        var separator = existing.Length == 0 ||
                        existing.EndsWith("\n", StringComparison.Ordinal) ||
                        existing.EndsWith("\r", StringComparison.Ordinal)
            ? string.Empty
            : Environment.NewLine;

        File.WriteAllText(
            gitIgnorePath,
            existing +
            separator +
            string.Join(Environment.NewLine, additions) +
            Environment.NewLine,
            new UTF8Encoding(false));
        return true;
    }

    private static bool HasIgnoreEntry(
        IReadOnlySet<string> normalized,
        string entry)
    {
        var bare = entry.TrimEnd('/');
        return normalized.Contains(entry) ||
               normalized.Contains("/" + entry) ||
               normalized.Contains("**/" + entry) ||
               normalized.Contains(bare) ||
               normalized.Contains("/" + bare) ||
               normalized.Contains("**/" + bare);
    }

    public static string BuildResourceStagingRoot(
        RepositoryRuntimePaths runtime,
        string resourceType)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        _ = (resourceType ?? string.Empty)
            .Trim()
            .ToUpperInvariant() switch
        {
            "IMAGE" => "IMAGE",
            _ => throw new ArgumentException(
                "지원되지 않는 RESOURCE 타입입니다.",
                nameof(resourceType))
        };

        return Path.Combine(runtime.TempRoot, "Resource");
    }

    public static string BuildResourceStagingDirectory(
        RepositoryRuntimePaths runtime,
        string resourceType,
        string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) ||
            requestId.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "RESOURCE request ID가 안전한 형식이 아닙니다.",
                nameof(requestId));
        }

        return Path.Combine(
            BuildResourceStagingRoot(runtime, resourceType),
            requestId.Trim());
    }

    public static bool TryResetProjectTemp(
        string? repositoryRoot,
        out string? errorDetail)
    {
        errorDetail = null;
        if (string.IsNullOrWhiteSpace(repositoryRoot) ||
            !Directory.Exists(repositoryRoot))
            return true;

        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        var tempRoot = Path.Combine(root, "temp");

        try
        {
            if (Directory.Exists(tempRoot))
            {
                ClearDeleteBlockingAttributes(
                    new DirectoryInfo(tempRoot));
                Directory.Delete(tempRoot, recursive: true);
            }

            Directory.CreateDirectory(tempRoot);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            errorDetail =
                exception.GetType().Name +
                ": " +
                exception.Message;
            return false;
        }
    }

    public static bool TryResetEphemeralDirectories(
        out string? errorDetail)
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
                    ClearDeleteBlockingAttributes(
                        new DirectoryInfo(directory));
                    Directory.Delete(directory, recursive: true);
                }

                Directory.CreateDirectory(directory);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
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

    private static void ClearDeleteBlockingAttributes(
        DirectoryInfo directory)
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

            entry.Attributes &=
                ~(FileAttributes.ReadOnly | FileAttributes.System);
        }

        directory.Attributes &=
            ~(FileAttributes.ReadOnly | FileAttributes.System);
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
        {
            Directory.CreateDirectory(directory);
        }
    }
}
