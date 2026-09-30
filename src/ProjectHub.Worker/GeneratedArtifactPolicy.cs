using System.IO;

namespace ProjectHub.Worker;

public static class GeneratedArtifactPolicy
{
    private static readonly string[] RegenerableDirectoryNames =
    {
        "dist-temp",
        ".nuget",
        ".dotnet",
        ".dotnet-cli",
        "TestResults",
        "coverage",
        "verification-output",
        "visual-captures"
    };

    private static readonly HashSet<string> TraversalSkipNames = new(
        new[]
        {
            ".git",
            ".projecthub",
            ".projecthub-worktrees",
            "node_modules",
            "bin",
            "obj",
            "dist-temp",
            ".nuget",
            ".dotnet",
            ".dotnet-cli",
            "TestResults",
            "coverage",
            "verification-output",
            "visual-captures"
        },
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> EnumerateGeneratedDirectories(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
            return Array.Empty<string>();

        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var result = new HashSet<string>(comparer);

        foreach (var name in RegenerableDirectoryNames)
        {
            var candidate = Path.Combine(root, name);
            if (Directory.Exists(candidate))
                result.Add(Path.GetFullPath(candidate));
        }

        foreach (var projectFile in EnumerateProjectFiles(root))
        {
            var projectDirectory = Path.GetDirectoryName(projectFile);
            if (string.IsNullOrWhiteSpace(projectDirectory))
                continue;

            foreach (var name in new[] { "bin", "obj" })
            {
                var candidate = Path.Combine(projectDirectory, name);
                if (Directory.Exists(candidate))
                    result.Add(Path.GetFullPath(candidate));
            }
        }

        return result
            .OrderBy(path => path, comparer)
            .ToArray();
    }

    public static bool IsGeneratedArtifactPath(string repositoryRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || string.IsNullOrWhiteSpace(relativePath))
            return false;

        var normalized = relativePath
            .Replace('\\', '/')
            .Trim('/');
        if (normalized.Length == 0)
            return false;

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment =>
                RegenerableDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase) ||
                segment.Equals(".projecthub", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(".projecthub-worktrees", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(".verification-appdata", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            if (!segments[index].Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                !segments[index].Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var projectDirectory = index == 0
                ? Path.GetFullPath(repositoryRoot)
                : Path.GetFullPath(Path.Combine(
                    repositoryRoot,
                    Path.Combine(segments.Take(index).ToArray())));

            try
            {
                if (Directory.Exists(projectDirectory) &&
                    Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                {
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }

    public static string ToRepositoryRelativePath(string repositoryRoot, string fullPath)
        => Path.GetRelativePath(
                Path.GetFullPath(repositoryRoot),
                Path.GetFullPath(fullPath))
            .Replace('\\', '/')
            .Trim('/');

    private static IEnumerable<string> EnumerateProjectFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> projectFiles;
            try
            {
                projectFiles = Directory.EnumerateFiles(
                    current,
                    "*.csproj",
                    SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var projectFile in projectFiles)
                yield return projectFile;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(
                    current,
                    "*",
                    SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var info = new DirectoryInfo(child);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    TraversalSkipNames.Contains(info.Name))
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }
}
