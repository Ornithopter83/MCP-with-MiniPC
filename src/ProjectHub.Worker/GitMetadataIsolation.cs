namespace ProjectHub.Worker;

public sealed record GitMetadataRestoreResult(
    bool Success,
    string? ErrorCode = null,
    string? ErrorDetail = null,
    string? QuarantinePath = null);

public sealed class GitMetadataIsolationLease
{
    private readonly string _gitPath;
    private readonly string? _detachedPath;
    private readonly string? _isolationDirectory;
    private bool _restored;

    private GitMetadataIsolationLease(
        string gitPath,
        string? detachedPath,
        string? isolationDirectory)
    {
        _gitPath = gitPath;
        _detachedPath = detachedPath;
        _isolationDirectory = isolationDirectory;
    }

    public static GitMetadataIsolationLease Detach(
        string workingDirectory,
        string jobId,
        string workItemId)
    {
        var root = string.IsNullOrWhiteSpace(workingDirectory)
            ? string.Empty
            : Path.GetFullPath(workingDirectory);
        var gitPath = string.IsNullOrWhiteSpace(root)
            ? string.Empty
            : Path.Combine(root, ".git");

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new GitMetadataIsolationLease(gitPath, null, null);

        var isFile = File.Exists(gitPath);
        var isDirectory = Directory.Exists(gitPath);

        if (!isFile && !isDirectory)
            return new GitMetadataIsolationLease(gitPath, null, null);

        var parent = Directory.GetParent(root)?.FullName
            ?? throw new InvalidOperationException("작업공간 상위 경로를 확인할 수 없습니다.");
        var isolationDirectory = Path.Combine(
            parent,
            ".projecthub-git-metadata",
            SafePathComponent(jobId) + "-" +
            SafePathComponent(workItemId) + "-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolationDirectory);

        var detachedPath = Path.Combine(isolationDirectory, "git-metadata");
        if (isFile)
            File.Move(gitPath, detachedPath);
        else
            Directory.Move(gitPath, detachedPath);

        return new GitMetadataIsolationLease(
            gitPath,
            detachedPath,
            isolationDirectory);
    }

    public static IReadOnlyDictionary<string, string> BuildGitNetworkDenyEnvironment()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never",
            ["GIT_ALLOW_PROTOCOL"] = "file",
            ["GIT_PROTOCOL_FROM_USER"] = "0",
            ["GIT_CONFIG_COUNT"] = "4",
            ["GIT_CONFIG_KEY_0"] = "protocol.http.allow",
            ["GIT_CONFIG_VALUE_0"] = "never",
            ["GIT_CONFIG_KEY_1"] = "protocol.https.allow",
            ["GIT_CONFIG_VALUE_1"] = "never",
            ["GIT_CONFIG_KEY_2"] = "protocol.ssh.allow",
            ["GIT_CONFIG_VALUE_2"] = "never",
            ["GIT_CONFIG_KEY_3"] = "protocol.git.allow",
            ["GIT_CONFIG_VALUE_3"] = "never"
        };

    public GitMetadataRestoreResult Restore()
    {
        if (_restored)
            return new(true);

        _restored = true;
        if (string.IsNullOrWhiteSpace(_detachedPath))
            return new(true);

        string? quarantinePath = null;
        try
        {
            if (File.Exists(_gitPath) || Directory.Exists(_gitPath))
            {
                quarantinePath = Path.Combine(
                    _isolationDirectory!,
                    "unexpected-git-metadata");

                if (File.Exists(_gitPath))
                    File.Move(_gitPath, quarantinePath);
                else
                    Directory.Move(_gitPath, quarantinePath);
            }

            if (File.Exists(_detachedPath))
                File.Move(_detachedPath, _gitPath);
            else if (Directory.Exists(_detachedPath))
                Directory.Move(_detachedPath, _gitPath);
            else
                return new(
                    false,
                    "GIT_METADATA_DETACHED_COPY_MISSING",
                    _detachedPath,
                    quarantinePath);

            if (quarantinePath is null)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(_isolationDirectory!).Any())
                        Directory.Delete(_isolationDirectory!);
                }
                catch (IOException)
                {
                }
            }

            return quarantinePath is null
                ? new(true)
                : new(
                    false,
                    "GIT_METADATA_RECREATED_BY_WORK",
                    "WORK 실행 중 .git이 새로 생성되어 격리 보관했습니다.",
                    quarantinePath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new(
                false,
                "GIT_METADATA_RESTORE_FAILED",
                exception.Message,
                quarantinePath);
        }
    }

    private static string SafePathComponent(string value)
    {
        var cleaned = new string(
            (value ?? string.Empty)
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray())
            .Trim('-');

        return string.IsNullOrWhiteSpace(cleaned) ? "item" : cleaned;
    }
}
