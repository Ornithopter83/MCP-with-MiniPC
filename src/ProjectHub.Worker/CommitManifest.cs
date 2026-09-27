using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record CommitManifestFile(
    string Path,
    string ChangeType,
    string? PreviousPath,
    long? Size,
    string? Sha256,
    bool IsText,
    string? Content);

public sealed record CommitManifest(
    string WorkItemId,
    string Commit,
    string? ParentCommit,
    string Tree,
    IReadOnlyList<CommitManifestFile> ChangedFiles);

public sealed record GitCommitManifestResult(
    bool Success,
    string? ErrorCode,
    string? ManifestPath,
    CommitManifest? Manifest,
    string? ErrorDetail = null);

public sealed class GitCommitManifestBuilder
{
    private const int MaxInlineTextBytes = 256 * 1024;
    private static readonly TimeSpan GitReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IGitWorktreeCommandRunner _runner;

    public GitCommitManifestBuilder(IGitWorktreeCommandRunner? runner = null)
    {
        _runner = runner ?? new ProcessGitWorktreeCommandRunner();
    }

    public async Task<GitCommitManifestResult> BuildAsync(
        string worktreePath,
        string workspace,
        string jobId,
        string workItemId,
        string commit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(worktreePath) || !Directory.Exists(worktreePath))
            return Failure("COMMIT_MANIFEST_WORKTREE_MISSING", "Worktree를 찾을 수 없습니다.");
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return Failure("COMMIT_MANIFEST_WORKSPACE_MISSING", "Workspace를 찾을 수 없습니다.");
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(workItemId))
            return Failure("COMMIT_MANIFEST_ID_MISSING", "jobId 또는 workItemId가 비어 있습니다.");
        if (string.IsNullOrWhiteSpace(commit))
            return Failure("COMMIT_MANIFEST_COMMIT_MISSING", "commit이 비어 있습니다.");

        var normalizedCommit = commit.Trim();

        var parentResult = await RunAsync(
            worktreePath,
            cancellationToken,
            "rev-list",
            "--parents",
            "-n",
            "1",
            normalizedCommit).ConfigureAwait(false);
        if (parentResult.ExitCode != 0 || string.IsNullOrWhiteSpace(parentResult.StandardOutput))
            return GitFailure("COMMIT_MANIFEST_PARENT_FAILED", "git rev-list --parents", parentResult);

        var parentParts = FirstLine(parentResult.StandardOutput)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parentCommit = parentParts.Length > 1 ? parentParts[1] : null;

        var treeResult = await RunAsync(
            worktreePath,
            cancellationToken,
            "rev-parse",
            "--verify",
            normalizedCommit + "^{tree}").ConfigureAwait(false);
        if (treeResult.ExitCode != 0 || string.IsNullOrWhiteSpace(treeResult.StandardOutput))
            return GitFailure("COMMIT_MANIFEST_TREE_FAILED", "git rev-parse commit^{tree}", treeResult);

        var tree = FirstLine(treeResult.StandardOutput);

        var changedResult = await RunAsync(
            worktreePath,
            cancellationToken,
            "-c",
            "core.quotepath=false",
            "diff-tree",
            "--root",
            "--no-commit-id",
            "--name-status",
            "-r",
            "-M",
            normalizedCommit).ConfigureAwait(false);
        if (changedResult.ExitCode != 0)
            return GitFailure("COMMIT_MANIFEST_DIFF_FAILED", "git diff-tree --name-status", changedResult);

        var files = new List<CommitManifestFile>();
        foreach (var line in NormalizeNewlines(changedResult.StandardOutput)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parsed = ParseChangedPath(line);
            if (parsed is null)
                return Failure("COMMIT_MANIFEST_DIFF_PARSE_FAILED", "변경 경로를 해석할 수 없습니다: " + line);

            long? size = null;
            string? sha256 = null;
            var isText = false;
            string? content = null;

            if (!string.Equals(parsed.Value.ChangeType, "DELETE", StringComparison.Ordinal))
            {
                var filePath = ResolveInside(worktreePath, parsed.Value.Path);
                if (filePath is null)
                    return Failure("COMMIT_MANIFEST_PATH_UNSAFE", parsed.Value.Path);

                if (File.Exists(filePath))
                {
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
                        size = bytes.LongLength;
                        sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

                        if (bytes.Length <= MaxInlineTextBytes &&
                            Array.IndexOf(bytes, (byte)0) < 0)
                        {
                            try
                            {
                                content = StrictUtf8.GetString(bytes);
                                isText = true;
                            }
                            catch (DecoderFallbackException)
                            {
                                content = null;
                                isText = false;
                            }
                        }
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        return Failure(
                            "COMMIT_MANIFEST_FILE_READ_FAILED",
                            parsed.Value.Path + ": " + exception.Message);
                    }
                }
            }

            files.Add(new CommitManifestFile(
                parsed.Value.Path,
                parsed.Value.ChangeType,
                parsed.Value.PreviousPath,
                size,
                sha256,
                isText,
                content));
        }

        var manifest = new CommitManifest(
            workItemId.Trim(),
            normalizedCommit,
            parentCommit,
            tree,
            files);

        var directory = Path.Combine(
            Path.GetFullPath(workspace),
            ".projecthub",
            "commit-manifests",
            SafePathComponent(jobId));
        var shortCommit = normalizedCommit[..Math.Min(12, normalizedCommit.Length)];
        var manifestPath = Path.Combine(
            directory,
            SafePathComponent(workItemId) + "-" + shortCommit + ".json");
        var temporaryPath = manifestPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, manifestPath, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }

            return Failure("COMMIT_MANIFEST_WRITE_FAILED", exception.Message);
        }

        return new(true, null, manifestPath, manifest);
    }

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
        => _runner.RunAsync(
            workingDirectory,
            arguments,
            GitReadTimeout,
            cancellationToken);

    private static (string ChangeType, string Path, string? PreviousPath)? ParseChangedPath(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length < 2)
            return null;

        var status = parts[0].Trim();
        if (status.Length == 0)
            return null;

        var code = char.ToUpperInvariant(status[0]);
        return code switch
        {
            'A' => ("ADD", parts[1], null),
            'M' => ("MODIFY", parts[1], null),
            'D' => ("DELETE", parts[1], null),
            'T' => ("TYPE_CHANGE", parts[1], null),
            'R' when parts.Length >= 3 => ("RENAME", parts[2], parts[1]),
            'C' when parts.Length >= 3 => ("COPY", parts[2], parts[1]),
            _ => ("OTHER", parts[^1], parts.Length >= 3 ? parts[^2] : null)
        };
    }

    private static string? ResolveInside(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, normalizedRelative));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return fullPath.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            comparison)
            ? fullPath
            : null;
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

    private static GitCommitManifestResult GitFailure(
        string errorCode,
        string operation,
        GitCommandResult result)
        => Failure(
            errorCode,
            operation +
            " 실패 · exitCode=" + result.ExitCode +
            (string.IsNullOrWhiteSpace(result.StandardError)
                ? string.Empty
                : " · " + result.StandardError.Trim()));

    private static GitCommitManifestResult Failure(string code, string detail)
        => new(false, code, null, null, detail);

    private static string NormalizeNewlines(string value)
        => (value ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

    private static string FirstLine(string value)
        => NormalizeNewlines(value)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? string.Empty;
}
