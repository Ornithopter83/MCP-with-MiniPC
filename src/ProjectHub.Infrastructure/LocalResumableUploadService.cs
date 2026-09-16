using System.Collections.Concurrent;
using System.Security.Cryptography;
using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

public sealed class LocalResumableUploadService(LargeDataOptions options) : IResumableUploadService
{
    private readonly ConcurrentDictionary<string, string> sessions = new(StringComparer.Ordinal);

    public async Task<UploadChunkResult> WriteChunkAsync(LargeDataAssertionScope scope, int chunkIndex, Stream content, CancellationToken cancellationToken)
    {
        if (chunkIndex < 0) throw new InvalidOperationException("Chunk index must be non-negative.");
        var directory = SessionDirectory(scope.UploadSessionId);
        Directory.CreateDirectory(directory);
        sessions[scope.UploadSessionId] = directory;
        var path = Path.Combine(directory, $"{chunkIndex:D8}.part");
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await content.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        var bytes = Directory.EnumerateFiles(directory, "*.part").Sum(file => new FileInfo(file).Length);
        return new UploadChunkResult(scope.UploadSessionId, chunkIndex, bytes);
    }

    public Task<UploadStatus> GetStatusAsync(LargeDataAssertionScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = SessionDirectory(scope.UploadSessionId);
        var files = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.part").ToArray() : [];
        var chunks = files.Select(file => int.Parse(Path.GetFileNameWithoutExtension(file))).Order().ToArray();
        return Task.FromResult(new UploadStatus(scope.UploadSessionId, files.Sum(file => new FileInfo(file).Length), chunks, LargeDataLifecycle.Uploading));
    }

    public async Task<LargeDataLifecycle> FinalizeAsync(LargeDataAssertionScope scope, CancellationToken cancellationToken)
    {
        var directory = SessionDirectory(scope.UploadSessionId);
        if (!Directory.Exists(directory)) throw new InvalidOperationException("Upload session does not exist.");
        var parts = Directory.EnumerateFiles(directory, "*.part").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var staging = Path.Combine(directory, "assembled.tmp");
        await using (var output = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            foreach (var part in parts) await using (var input = File.OpenRead(part)) await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        var info = new FileInfo(staging);
        if (info.Length != scope.Object.SizeBytes) throw new InvalidOperationException("Final size does not match assertion.");
        await using var hashInput = File.OpenRead(staging);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(hashInput, cancellationToken)).ToLowerInvariant();
        if (!hash.Equals(scope.Object.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Final hash does not match assertion.");
        var destination = Path.Combine(options.StorageRoot, "objects", "sha256", hash[..2], hash);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(staging, destination, true);
        Directory.Delete(directory, true);
        return LargeDataLifecycle.Staged;
    }

    private string SessionDirectory(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Any(character => !char.IsLetterOrDigit(character) && character != '-' && character != '_')) throw new InvalidOperationException("Invalid upload session id.");
        return Path.Combine(options.StorageRoot, "staging", sessionId);
    }
}
