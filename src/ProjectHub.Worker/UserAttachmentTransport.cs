using System.Security.Cryptography;
using System.Text;

namespace ProjectHub.Worker;

public sealed record UserAttachmentInput(
    string Id,
    string FileName,
    string StoredPath,
    string MimeType,
    long Size,
    string Sha256,
    string SourceKind)
{
    public string DisplaySize => UserAttachmentTransport.FormatSize(Size);
    public bool IsImage => MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

public sealed record AiInputAttachment(
    string FileName,
    string Path,
    string RelativePath,
    string MimeType,
    long Size,
    string Sha256)
{
    public bool IsImage => MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

public static class UserAttachmentTransport
{
    public const int MaxFilesPerMessage = 20;
    public const long MaxFileBytes = 50L * 1024L * 1024L;

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".msi", ".msp", ".cpl", ".lnk"
    };

    public static UserAttachmentInput CacheFile(
        string sourcePath,
        string sourceKind,
        string? preferredFileName = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("첨부할 파일을 찾을 수 없습니다.", sourcePath);
        if (Directory.Exists(sourcePath))
            throw new InvalidOperationException("폴더는 첨부할 수 없습니다.");

        var info = new FileInfo(sourcePath);
        if (info.Length <= 0)
            throw new InvalidOperationException("빈 파일은 첨부할 수 없습니다.");
        if (info.Length > MaxFileBytes)
            throw new InvalidOperationException("첨부 파일은 50MB를 초과할 수 없습니다.");

        var fileName = SanitizeFileName(
            string.IsNullOrWhiteSpace(preferredFileName)
                ? info.Name
                : preferredFileName);
        var extension = Path.GetExtension(fileName);
        if (BlockedExtensions.Contains(extension))
            throw new InvalidOperationException($"실행 가능한 파일 형식은 첨부할 수 없습니다: {extension}");

        WorkerPaths.EnsureCreated();
        var id = Guid.NewGuid().ToString("N");
        var storedExtension = SanitizeExtension(extension);
        var storedPath = Path.Combine(
            WorkerPaths.Attachments,
            id + storedExtension);

        File.Copy(sourcePath, storedPath, overwrite: false);
        var size = new FileInfo(storedPath).Length;
        var sha256 = ComputeSha256(storedPath);
        return new UserAttachmentInput(
            id,
            fileName,
            storedPath,
            GetMimeType(extension),
            size,
            sha256,
            string.IsNullOrWhiteSpace(sourceKind) ? "FILE" : sourceKind.Trim().ToUpperInvariant());
    }

    public static UserAttachmentInput RegisterCachedFile(
        string id,
        string fileName,
        string storedPath,
        string mimeType,
        string sourceKind)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id.Any(character => !char.IsLetterOrDigit(character)))
            throw new ArgumentException("첨부 ID가 올바르지 않습니다.", nameof(id));
        if (!File.Exists(storedPath))
            throw new FileNotFoundException("캐시된 첨부 파일을 찾을 수 없습니다.", storedPath);

        var info = new FileInfo(storedPath);
        if (info.Length <= 0 || info.Length > MaxFileBytes)
            throw new InvalidOperationException("첨부 파일 크기가 허용 범위를 벗어났습니다.");

        return new UserAttachmentInput(
            id,
            SanitizeFileName(fileName),
            Path.GetFullPath(storedPath),
            string.IsNullOrWhiteSpace(mimeType) ? GetMimeType(Path.GetExtension(fileName)) : mimeType,
            info.Length,
            ComputeSha256(storedPath),
            string.IsNullOrWhiteSpace(sourceKind) ? "FILE" : sourceKind.Trim().ToUpperInvariant());
    }

    public static IReadOnlyList<AiInputAttachment> StageForWorkspace(
        IReadOnlyList<UserAttachmentInput>? inputs,
        string workingDirectory,
        string batchId)
    {
        if (inputs is null || inputs.Count == 0)
            return Array.Empty<AiInputAttachment>();
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"첨부 작업 폴더를 찾을 수 없습니다: {workingDirectory}");

        var safeBatch = new string((batchId ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Take(40)
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeBatch))
            safeBatch = Guid.NewGuid().ToString("N");

        EnsureGitInfoExclude(workingDirectory);

        var relativeRoot = Path.Combine(".projecthub", "attachments", safeBatch);
        var targetRoot = Path.Combine(workingDirectory, relativeRoot);
        Directory.CreateDirectory(targetRoot);

        var result = new List<AiInputAttachment>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs.Take(MaxFilesPerMessage))
        {
            if (!File.Exists(input.StoredPath))
                throw new FileNotFoundException("캐시된 첨부 파일을 찾을 수 없습니다.", input.StoredPath);

            var fileName = MakeUniqueFileName(
                SanitizeFileName(input.FileName),
                usedNames);
            var destination = Path.Combine(targetRoot, fileName);
            File.Copy(input.StoredPath, destination, overwrite: true);
            var actualHash = ComputeSha256(destination);
            if (!string.Equals(actualHash, input.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"ATTACHMENT_HASH_MISMATCH: {input.FileName}");

            result.Add(new AiInputAttachment(
                input.FileName,
                destination,
                Path.GetRelativePath(workingDirectory, destination),
                input.MimeType,
                input.Size,
                input.Sha256));
        }

        return result;
    }

    public static string AppendPrompt(
        string prompt,
        IReadOnlyList<AiInputAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return prompt;

        var builder = new StringBuilder(prompt.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("[USER_ATTACHMENTS]");
        builder.AppendLine("The following files are user-provided inputs for this request.");
        builder.AppendLine("Read the files before answering or modifying anything that depends on them.");
        builder.AppendLine("For image files, use the local image viewing capability on the listed path before drawing visual conclusions.");
        foreach (var attachment in attachments)
        {
            builder.Append("- ")
                .Append(attachment.FileName)
                .Append(" | path=")
                .Append(attachment.Path)
                .Append(" | mime=")
                .Append(attachment.MimeType)
                .Append(" | bytes=")
                .Append(attachment.Size)
                .Append(" | sha256=")
                .AppendLine(attachment.Sha256);
        }
        builder.AppendLine("[/USER_ATTACHMENTS]");
        return builder.ToString();
    }

    public static string AppendWebPrompt(
        string prompt,
        IReadOnlyList<AiInputAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return prompt;

        var builder = new StringBuilder(prompt.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("[USER_ATTACHMENTS]");
        builder.AppendLine("The files listed below are attached to this ChatGPT turn and are also staged at the workspace paths shown for downstream WORK tasks.");
        foreach (var attachment in attachments)
        {
            builder.Append("- ")
                .Append(attachment.FileName)
                .Append(" | workspacePath=")
                .Append(attachment.RelativePath)
                .Append(" | mime=")
                .Append(attachment.MimeType)
                .Append(" | sha256=")
                .AppendLine(attachment.Sha256);
        }
        builder.AppendLine("[/USER_ATTACHMENTS]");
        return builder.ToString();
    }

    public static BridgeAttachment CreateBridgeAttachment(UserAttachmentInput input)
    {
        if (!File.Exists(input.StoredPath))
            throw new FileNotFoundException("Web 첨부 파일을 찾을 수 없습니다.", input.StoredPath);

        var root = Path.GetFullPath(WorkerPaths.Attachments)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(input.StoredPath);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Web 첨부는 Worker attachment 캐시 안의 파일만 사용할 수 있습니다.");

        var stem = Path.GetFileNameWithoutExtension(fullPath);
        if (!string.Equals(stem, input.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Web 첨부 ID와 캐시 파일명이 일치하지 않습니다.");

        var info = new FileInfo(fullPath);
        if (info.Length != input.Size)
            throw new InvalidOperationException($"ATTACHMENT_SIZE_MISMATCH: {input.FileName}");
        var actualHash = ComputeSha256(fullPath);
        if (!string.Equals(actualHash, input.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"ATTACHMENT_HASH_MISMATCH: {input.FileName}");

        return new BridgeAttachment(
            input.Id,
            input.FileName,
            input.MimeType,
            input.Size,
            "http://127.0.0.1:43821/bridge/attachment/" + input.Id,
            input.Sha256);
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024L) return $"{bytes / 1024d:0.#} KB";
        return $"{bytes / (1024d * 1024d):0.#} MB";
    }

    public static string GetMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".flac" => "audio/flac",
        ".m4a" => "audio/mp4",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        ".json" => "application/json",
        ".csv" => "text/csv",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/octet-stream"
    };

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string MakeUniqueFileName(string fileName, ISet<string> usedNames)
    {
        if (usedNames.Add(fileName))
            return fileName;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{stem}-{index}{extension}";
            if (usedNames.Add(candidate))
                return candidate;
        }

        var fallback = $"{stem}-{Guid.NewGuid():N}{extension}";
        usedNames.Add(fallback);
        return fallback;
    }

    private static string SanitizeFileName(string value)
    {
        var fileName = Path.GetFileName(value);
        foreach (var character in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(character, '_');
        fileName = fileName.Trim();
        return string.IsNullOrWhiteSpace(fileName) ? "attachment.bin" : fileName;
    }

    private static string SanitizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) ||
            extension.Length > 16 ||
            extension.Any(character => character != '.' && !char.IsLetterOrDigit(character)))
            return ".bin";
        return extension.ToLowerInvariant();
    }

    private static void EnsureGitInfoExclude(string workingDirectory)
    {
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(workingDirectory));
            while (current is not null)
            {
                var dotGit = Path.Combine(current.FullName, ".git");
                string? gitCommonDirectory = null;
                if (Directory.Exists(dotGit))
                {
                    gitCommonDirectory = dotGit;
                }
                else if (File.Exists(dotGit))
                {
                    var pointer = File.ReadAllText(dotGit).Trim();
                    const string prefix = "gitdir:";
                    if (pointer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var gitDirText = pointer[prefix.Length..].Trim();
                        var gitDirectory = Path.GetFullPath(Path.IsPathRooted(gitDirText)
                            ? gitDirText
                            : Path.Combine(current.FullName, gitDirText));
                        var commonFile = Path.Combine(gitDirectory, "commondir");
                        if (File.Exists(commonFile))
                        {
                            var commonText = File.ReadAllText(commonFile).Trim();
                            gitCommonDirectory = Path.GetFullPath(Path.IsPathRooted(commonText)
                                ? commonText
                                : Path.Combine(gitDirectory, commonText));
                        }
                        else
                        {
                            gitCommonDirectory = gitDirectory;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(gitCommonDirectory))
                {
                    var infoDirectory = Path.Combine(gitCommonDirectory, "info");
                    Directory.CreateDirectory(infoDirectory);
                    var excludePath = Path.Combine(infoDirectory, "exclude");
                    const string pattern = ".projecthub/attachments/";
                    var existing = File.Exists(excludePath)
                        ? File.ReadAllLines(excludePath)
                        : Array.Empty<string>();
                    if (!existing.Any(line => string.Equals(line.Trim(), pattern, StringComparison.OrdinalIgnoreCase)))
                        File.AppendAllText(excludePath, Environment.NewLine + pattern + Environment.NewLine, new UTF8Encoding(false));
                    return;
                }

                current = current.Parent;
            }
        }
        catch
        {
            // Attachment staging still works outside Git or when local Git metadata is unavailable.
        }
    }
}
