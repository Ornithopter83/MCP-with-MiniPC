using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProjectHub.Worker;

public sealed record GitMetadataSnapshot(
    string GitDirectory,
    string? HeadSignature,
    string? ConfigHash,
    string? IndexHash,
    string? PackedRefsHash,
    string RefsHash);

public sealed record GitMetadataValidationResult(
    bool Success,
    string? ErrorCode = null,
    string? ErrorDetail = null);

public static class GitMetadataGuard
{
    public static IReadOnlyDictionary<string, string> BuildGitNonInteractiveEnvironment()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never",
            ["GIT_PROTOCOL_FROM_USER"] = "0",
            ["GIT_OPTIONAL_LOCKS"] = "0"
        };

    public static GitMetadataSnapshot Capture(string workingDirectory)
    {
        var gitDirectory = ResolveGitDirectory(workingDirectory);
        return new GitMetadataSnapshot(
            gitDirectory,
            ReadTextSignature(Path.Combine(gitDirectory, "HEAD")),
            HashFile(Path.Combine(gitDirectory, "config")),
            HashFile(Path.Combine(gitDirectory, "index")),
            HashFile(Path.Combine(gitDirectory, "packed-refs")),
            HashDirectory(Path.Combine(gitDirectory, "refs")));
    }

    public static GitMetadataValidationResult Validate(
        string workingDirectory,
        GitMetadataSnapshot expected)
    {
        GitMetadataSnapshot current;
        try
        {
            current = Capture(workingDirectory);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(
                false,
                "WORK_GIT_METADATA_UNAVAILABLE",
                exception.GetType().Name + ": " + exception.Message);
        }

        if (!PathsEqual(current.GitDirectory, expected.GitDirectory))
            return new(false, "WORK_GIT_DIRECTORY_CHANGED", "WORK 실행 중 .git 위치가 변경되었습니다.");
        if (!string.Equals(current.HeadSignature, expected.HeadSignature, StringComparison.Ordinal))
            return new(false, "WORK_GIT_HEAD_CHANGED", "WORK 실행 중 HEAD가 변경되었습니다.");
        if (!string.Equals(current.ConfigHash, expected.ConfigHash, StringComparison.Ordinal))
            return new(false, "WORK_GIT_CONFIG_CHANGED", "WORK 실행 중 Git config가 변경되었습니다.");
        if (!string.Equals(current.IndexHash, expected.IndexHash, StringComparison.Ordinal))
            return new(false, "WORK_GIT_INDEX_CHANGED", "WORK 실행 중 Git index가 변경되었습니다.");
        if (!string.Equals(current.PackedRefsHash, expected.PackedRefsHash, StringComparison.Ordinal) ||
            !string.Equals(current.RefsHash, expected.RefsHash, StringComparison.Ordinal))
        {
            return new(false, "WORK_GIT_REFS_CHANGED", "WORK 실행 중 Git refs가 변경되었습니다.");
        }

        return new(true);
    }

    private static string ResolveGitDirectory(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            throw new InvalidOperationException("작업공간이 존재하지 않습니다.");

        var root = Path.GetFullPath(workingDirectory);
        var gitPath = Path.Combine(root, ".git");
        if (Directory.Exists(gitPath))
            return Path.GetFullPath(gitPath);

        if (!File.Exists(gitPath))
            throw new InvalidOperationException(".git을 찾을 수 없습니다.");

        var pointer = File.ReadLines(gitPath).FirstOrDefault()?.Trim() ?? string.Empty;
        const string prefix = "gitdir:";
        if (!pointer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(".git 포인터 형식이 올바르지 않습니다.");

        var value = pointer[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(".git 포인터 대상이 비어 있습니다.");

        var resolved = Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(root, value));
        if (!Directory.Exists(resolved))
            throw new InvalidOperationException(".git 포인터 대상이 존재하지 않습니다.");
        return resolved;
    }

    private static string? ReadTextSignature(string path)
        => File.Exists(path)
            ? File.ReadAllText(path, Encoding.UTF8).Trim()
            : null;

    private static string? HashFile(string path)
    {
        if (!File.Exists(path))
            return null;
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            builder.Append(relative).Append('\n').Append(HashFile(path)).Append('\n');
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
