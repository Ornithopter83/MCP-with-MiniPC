using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record TargetWorkspaceFileFingerprint(
    long Size,
    long LastWriteUtcTicks);

public sealed record TargetWorkspaceMaterializationSnapshot(
    IReadOnlyDictionary<string, TargetWorkspaceFileFingerprint> Files);

public sealed record TargetWorkspaceMaterializationSnapshotResult(
    bool Success,
    string? ErrorCode,
    TargetWorkspaceMaterializationSnapshot? Snapshot,
    string? ErrorDetail = null);

public sealed record MaterializationFileRecord(
    string Path,
    string Operation,
    string? SourceWorkItemId,
    string? SourceResultRef,
    string? ExpectedSha256,
    string? ActualSha256,
    long? Size,
    bool Changed);

public sealed record MaterializationLedgerEntry(
    string JobId,
    string WorkItemId,
    long Invocation,
    DateTimeOffset RecordedAtUtc,
    bool Success,
    string? ErrorCode,
    IReadOnlyList<string> SourceResultRefs,
    IReadOnlyList<MaterializationFileRecord> Files,
    IReadOnlyList<string> UnexpectedChangedPaths,
    string? Detail);

public sealed record MaterializationVerificationResult(
    bool Success,
    string? ErrorCode,
    string LedgerPath,
    string Message);

public sealed class TargetWorkspaceMaterializationLedger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _workspace;
    private readonly string _jobId;
    private readonly string _ledgerDirectory;
    private readonly string _manifestRoot;

    public TargetWorkspaceMaterializationLedger(
        string workspace,
        string jobId)
    {
        if (string.IsNullOrWhiteSpace(workspace))
            throw new ArgumentException("사용자 작업 폴더가 비어 있습니다.", nameof(workspace));
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID가 비어 있습니다.", nameof(jobId));

        _workspace = Path.GetFullPath(workspace)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _jobId = jobId.Trim();
        _ledgerDirectory = Path.Combine(
            _workspace,
            ".projecthub",
            "materialization-ledger",
            SafePathComponent(_jobId));
        _manifestRoot = Path.Combine(
            _workspace,
            ".projecthub",
            "commit-manifests");
    }

    public TargetWorkspaceMaterializationSnapshotResult CaptureSnapshot()
    {
        try
        {
            var files = new Dictionary<string, TargetWorkspaceFileFingerprint>(
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);

            if (!Directory.Exists(_workspace))
                return new(false, "MATERIALIZATION_TARGET_WORKSPACE_MISSING", null);

            var pending = new Stack<string>();
            pending.Push(_workspace);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    var relative = NormalizeRelativePath(Path.GetRelativePath(_workspace, child));
                    if (IsExcludedRoot(relative))
                        continue;

                    var attributes = File.GetAttributes(child);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                        continue;

                    pending.Push(child);
                }

                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var relative = NormalizeRelativePath(Path.GetRelativePath(_workspace, file));
                    if (IsExcludedRoot(relative))
                        continue;

                    var info = new FileInfo(file);
                    files[relative] = new TargetWorkspaceFileFingerprint(
                        info.Length,
                        info.LastWriteTimeUtc.Ticks);
                }
            }

            return new(
                true,
                null,
                new TargetWorkspaceMaterializationSnapshot(files));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new(
                false,
                "MATERIALIZATION_TARGET_SNAPSHOT_FAILED",
                null,
                exception.Message);
        }
    }

    public async Task<MaterializationVerificationResult> VerifyAndRecordAsync(
        WorkItemSnapshot item,
        IReadOnlyList<WorkItemDependencyResult> dependencies,
        TargetWorkspaceMaterializationSnapshot before,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(before);

        var afterResult = CaptureSnapshot();
        if (!afterResult.Success || afterResult.Snapshot is null)
        {
            return await RecordFailureAsync(
                item,
                dependencies,
                Array.Empty<MaterializationFileRecord>(),
                Array.Empty<string>(),
                afterResult.ErrorCode ?? "MATERIALIZATION_TARGET_SNAPSHOT_FAILED",
                afterResult.ErrorDetail ?? "대상 프로젝트 루트의 반영 후 상태를 읽지 못했습니다.",
                cancellationToken).ConfigureAwait(false);
        }

        var changedPaths = FindChangedPaths(before, afterResult.Snapshot);
        var codeDependencies = (dependencies ?? Array.Empty<WorkItemDependencyResult>())
            .Where(dependency => dependency.ResultType == WorkItemResultType.CodeChange)
            .ToArray();

        var records = new List<MaterializationFileRecord>();
        var allowedPaths = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var dependency in codeDependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.CommitManifestPath))
            {
                errors.Add($"WorkItem {dependency.WorkItemId}: commit manifest가 없습니다.");
                continue;
            }

            var manifestLoad = await LoadManifestAsync(
                dependency.CommitManifestPath,
                cancellationToken).ConfigureAwait(false);
            if (manifestLoad.Manifest is null)
            {
                errors.Add(
                    $"WorkItem {dependency.WorkItemId}: " +
                    (manifestLoad.Error ?? "commit manifest를 읽지 못했습니다."));
                continue;
            }

            var manifest = manifestLoad.Manifest;
            if (!string.IsNullOrWhiteSpace(dependency.ResultRef) &&
                !string.Equals(
                    manifest.Commit,
                    dependency.ResultRef.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"WorkItem {dependency.WorkItemId}: manifest commit과 resultRef가 다릅니다.");
                continue;
            }

            foreach (var file in manifest.ChangedFiles ?? Array.Empty<CommitManifestFile>())
            {
                var relative = NormalizeRelativePath(file.Path);
                if (!TryResolveTarget(relative, out var targetPath))
                {
                    errors.Add($"안전하지 않은 대상 경로입니다: {file.Path}");
                    continue;
                }

                allowedPaths.Add(relative);
                if (!string.IsNullOrWhiteSpace(file.PreviousPath))
                {
                    var previous = NormalizeRelativePath(file.PreviousPath);
                    allowedPaths.Add(previous);
                    if (!TryResolveTarget(previous, out var previousPath))
                    {
                        errors.Add($"안전하지 않은 이전 경로입니다: {file.PreviousPath}");
                    }
                    else if (File.Exists(previousPath) || Directory.Exists(previousPath))
                    {
                        errors.Add($"이전 경로가 대상 프로젝트에 남아 있습니다: {previous}");
                    }
                }

                var changed = changedPaths.Contains(relative);
                if (string.Equals(file.ChangeType, "DELETE", StringComparison.Ordinal))
                {
                    if (File.Exists(targetPath) || Directory.Exists(targetPath))
                        errors.Add($"삭제 대상이 여전히 존재합니다: {relative}");

                    records.Add(new MaterializationFileRecord(
                        relative,
                        file.ChangeType,
                        dependency.WorkItemId,
                        dependency.ResultRef,
                        null,
                        null,
                        null,
                        changed));
                    continue;
                }

                if (!File.Exists(targetPath))
                {
                    errors.Add($"반영 대상 파일이 없습니다: {relative}");
                    records.Add(new MaterializationFileRecord(
                        relative,
                        file.ChangeType,
                        dependency.WorkItemId,
                        dependency.ResultRef,
                        file.Sha256,
                        null,
                        null,
                        changed));
                    continue;
                }

                var actual = await HashFileAsync(targetPath, cancellationToken).ConfigureAwait(false);
                if (file.Size is not null && actual.Size != file.Size.Value)
                    errors.Add($"파일 크기가 다릅니다: {relative}");
                if (string.IsNullOrWhiteSpace(file.Sha256))
                    errors.Add($"manifest SHA-256이 없습니다: {relative}");
                else if (!string.Equals(file.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"SHA-256이 다릅니다: {relative}");

                records.Add(new MaterializationFileRecord(
                    relative,
                    file.ChangeType,
                    dependency.WorkItemId,
                    dependency.ResultRef,
                    file.Sha256,
                    actual.Sha256,
                    actual.Size,
                    changed));
            }
        }

        var unexpected = codeDependencies.Length == 0
            ? Array.Empty<string>()
            : changedPaths
                .Where(path => !allowedPaths.Contains(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        if (unexpected.Length > 0)
            errors.Add("manifest에 없는 대상 파일이 변경되었습니다.");

        if (codeDependencies.Length == 0)
        {
            if ((dependencies?.Count ?? 0) > 0 && changedPaths.Count == 0)
                errors.Add("대상 프로젝트 루트에서 실제 파일 변경을 확인할 수 없습니다.");

            foreach (var path in changedPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryResolveTarget(path, out var targetPath))
                    continue;

                if (File.Exists(targetPath))
                {
                    var actual = await HashFileAsync(targetPath, cancellationToken).ConfigureAwait(false);
                    records.Add(new MaterializationFileRecord(
                        path,
                        before.Files.ContainsKey(path) ? "MODIFY" : "ADD",
                        null,
                        null,
                        null,
                        actual.Sha256,
                        actual.Size,
                        true));
                }
                else
                {
                    records.Add(new MaterializationFileRecord(
                        path,
                        "DELETE",
                        null,
                        null,
                        null,
                        null,
                        null,
                        true));
                }
            }
        }

        var success = errors.Count == 0;
        var errorCode = success ? null : "MATERIALIZATION_VERIFICATION_FAILED";
        var detail = success
            ? $"대상 프로젝트 루트의 {records.Count}개 파일 상태를 기계적으로 확인했습니다."
            : string.Join(Environment.NewLine, errors);

        return await WriteEntryAsync(
            item,
            dependencies,
            records,
            unexpected,
            success,
            errorCode,
            detail,
            cancellationToken).ConfigureAwait(false);
    }

    public bool IsResultVerified(string? resultRef)
    {
        if (string.IsNullOrWhiteSpace(resultRef) ||
            !Directory.Exists(_ledgerDirectory))
            return false;

        foreach (var path in Directory.EnumerateFiles(
                     _ledgerDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<MaterializationLedgerEntry>(
                    File.ReadAllText(path),
                    JsonOptions);
                if (entry is null || !entry.Success)
                    continue;
                if (entry.SourceResultRefs.Any(value =>
                        string.Equals(
                            value,
                            resultRef.Trim(),
                            StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        return false;
    }

    private async Task<MaterializationVerificationResult> RecordFailureAsync(
        WorkItemSnapshot item,
        IReadOnlyList<WorkItemDependencyResult> dependencies,
        IReadOnlyList<MaterializationFileRecord> records,
        IReadOnlyList<string> unexpected,
        string errorCode,
        string detail,
        CancellationToken cancellationToken)
        => await WriteEntryAsync(
            item,
            dependencies,
            records,
            unexpected,
            false,
            errorCode,
            detail,
            cancellationToken).ConfigureAwait(false);

    private async Task<MaterializationVerificationResult> WriteEntryAsync(
        WorkItemSnapshot item,
        IReadOnlyList<WorkItemDependencyResult> dependencies,
        IReadOnlyList<MaterializationFileRecord> records,
        IReadOnlyList<string> unexpected,
        bool success,
        string? errorCode,
        string detail,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_ledgerDirectory);
        var fileName =
            item.CreatedOrder.ToString("D12") + "-" +
            SafePathComponent(item.Id) + ".json";
        var path = Path.Combine(_ledgerDirectory, fileName);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");

        var sourceRefs = (dependencies ?? Array.Empty<WorkItemDependencyResult>())
            .Where(value => value.ResultType == WorkItemResultType.CodeChange)
            .Select(value => value.ResultRef)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var entry = new MaterializationLedgerEntry(
            _jobId,
            item.Id,
            item.CreatedOrder,
            DateTimeOffset.UtcNow,
            success,
            errorCode,
            sourceRefs,
            records,
            unexpected,
            detail);

        try
        {
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            await File.WriteAllTextAsync(
                temporary,
                json,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (IOException)
            {
            }

            return new(
                false,
                "MATERIALIZATION_LEDGER_WRITE_FAILED",
                path,
                exception.Message);
        }

        return new(success, errorCode, path, detail);
    }

    private async Task<(CommitManifest? Manifest, string? Error)> LoadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!IsPathWithin(fullPath, _manifestRoot) || !File.Exists(fullPath))
                return (null, "허용된 commit manifest 경로가 아닙니다.");

            var json = await File.ReadAllTextAsync(
                fullPath,
                cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<CommitManifest>(json, JsonOptions);
            return manifest is null
                ? (null, "commit manifest 내용이 비어 있습니다.")
                : (manifest, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return (null, exception.Message);
        }
    }

    private HashSet<string> FindChangedPaths(
        TargetWorkspaceMaterializationSnapshot before,
        TargetWorkspaceMaterializationSnapshot after)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var changed = new HashSet<string>(comparison);

        foreach (var pair in before.Files)
        {
            if (!after.Files.TryGetValue(pair.Key, out var current) ||
                current != pair.Value)
                changed.Add(pair.Key);
        }

        foreach (var pair in after.Files)
        {
            if (!before.Files.TryGetValue(pair.Key, out var previous) ||
                previous != pair.Value)
                changed.Add(pair.Key);
        }

        return changed;
    }

    private bool TryResolveTarget(string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var normalizedRelative = NormalizeRelativePath(relativePath);
        if (IsExcludedRoot(normalizedRelative))
            return false;

        try
        {
            var candidate = Path.GetFullPath(
                Path.Combine(
                    _workspace,
                    normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsPathWithin(candidate, _workspace) ||
                string.Equals(candidate, _workspace, PathComparison) ||
                ContainsReparsePoint(candidate))
                return false;
            fullPath = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static async Task<(long Size, string Sha256)> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 64,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return (stream.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static string NormalizeRelativePath(string path)
        => (path ?? string.Empty)
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');

    private static bool IsExcludedRoot(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var first = normalized.Split('/', 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.Equals(first, ".git", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(first, ".projecthub", StringComparison.OrdinalIgnoreCase);
    }

    private bool ContainsReparsePoint(string path)
    {
        var relative = Path.GetRelativePath(_workspace, path);
        var current = _workspace;
        foreach (var segment in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
                continue;

            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPathWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, PathComparison) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison) ||
               fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, PathComparison);
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

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
