using System.Globalization;
using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

public sealed class LocalNasGatewayProvisioner(LargeDataOptions options) : INasGatewayProvisioner
{
    public Task<ProvisionResult> ProvisionAsync(LargeDataAssertionScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(scope);
        var relative = Path.Combine("objects", "sha256", scope.Object.Sha256[..2], scope.Object.Sha256);
        var full = Path.GetFullPath(Path.Combine(options.StorageRoot, relative));
        var root = EnsureRoot(options.StorageRoot);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Storage path escapes configured root.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var existing = File.Exists(full);
        return Task.FromResult(new ProvisionResult(scope.UploadSessionId, relative.Replace(Path.DirectorySeparatorChar, '/'), existing, existing ? LargeDataLifecycle.Staged : LargeDataLifecycle.Uploading));
    }

    private void Validate(LargeDataAssertionScope scope)
    {
        if (scope.Object.SizeBytes < 0 || scope.Object.SizeBytes > options.MaxObjectSizeBytes) throw new InvalidOperationException("Object size is outside the allowed range.");
        if (scope.Object.Sha256.Length != 64 || !scope.Object.Sha256.All(Uri.IsHexDigit)) throw new InvalidOperationException("Object hash must be a SHA-256 hex value.");
        if (string.IsNullOrWhiteSpace(scope.StorageScope) || Path.IsPathRooted(scope.StorageScope) || scope.StorageScope.Contains("..", StringComparison.Ordinal) || scope.StorageScope.Contains('\\')) throw new InvalidOperationException("Storage scope must be a safe relative path.");
    }

    private static string EnsureRoot(string value)
    {
        var root = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        return root;
    }
}
