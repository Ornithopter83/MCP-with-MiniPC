using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

public sealed class LargeDataAssertionIssuer(LargeDataOptions options) : ILargeDataAssertionIssuer
{
    public Task<string> IssueAsync(LargeDataAssertionScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(options.PrivateKeyPem))
            throw new InvalidOperationException("PROJECTHUB_ASSERTION_PRIVATE_KEY_PEM is not configured.");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(options.PrivateKeyPem);
        var header = JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = scope.KeyId });
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            v = 1, iss = scope.Issuer, aud = scope.Audience, sub = scope.Subject,
            project_id = scope.ProjectId, workstation_id = scope.WorkstationId,
            operation = scope.Operation.ToString().ToLowerInvariant(),
            upload_session_id = scope.UploadSessionId, object_hash = scope.Object.Sha256,
            size_bytes = scope.Object.SizeBytes, storage_scope = scope.StorageScope,
            relative_path = scope.RelativePath,
            iat = scope.IssuedAt.ToUnixTimeSeconds(), exp = scope.ExpiresAt.ToUnixTimeSeconds(),
            jti = scope.Jti, gateway_id = scope.GatewayId, location_id = scope.LocationId
        });
        var signingInput = $"{Base64Url(header)}.{Base64Url(payload)}";
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Task.FromResult($"{signingInput}.{Base64Url(signature)}");
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class LargeDataAssertionVerifier(LargeDataOptions options)
{
    public LargeDataAssertionScope Verify(string token, LargeDataOperation expectedOperation)
    {
        if (string.IsNullOrWhiteSpace(options.GatewayPublicKeyPem)) throw new InvalidOperationException("PROJECTHUB_GATEWAY_PUBLIC_KEY_PEM is not configured.");
        var parts = token.Split('.');
        if (parts.Length != 3) throw new UnauthorizedAccessException("Invalid assertion format.");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(options.GatewayPublicKeyPem);
        if (!rsa.VerifyData(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new UnauthorizedAccessException("Invalid assertion signature.");
        using var document = JsonDocument.Parse(Decode(parts[1]));
        var root = document.RootElement;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (root.GetProperty("v").GetInt32() != 1 || root.GetProperty("iss").GetString() != options.Issuer || root.GetProperty("aud").GetString() != options.Audience || root.GetProperty("exp").GetInt64() <= now || root.GetProperty("iat").GetInt64() > now + 60) throw new UnauthorizedAccessException("Invalid assertion claims.");
        var operation = Enum.Parse<LargeDataOperation>(root.GetProperty("operation").GetString()!, true);
        if (operation != expectedOperation) throw new UnauthorizedAccessException("Assertion operation is not allowed.");
        var relativePath = root.TryGetProperty("relative_path", out var relativePathElement) && relativePathElement.ValueKind == JsonValueKind.String ? relativePathElement.GetString() : null;
        return new(root.GetProperty("iss").GetString()!, root.GetProperty("aud").GetString()!, root.GetProperty("sub").GetString()!, root.GetProperty("project_id").GetString()!, root.GetProperty("workstation_id").GetString()!, operation, root.GetProperty("upload_session_id").GetString()!, new(root.GetProperty("object_hash").GetString()!, root.GetProperty("size_bytes").GetInt64()), root.GetProperty("storage_scope").GetString()!, DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("iat").GetInt64()), DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("exp").GetInt64()), root.GetProperty("jti").GetString()!, RelativePath: relativePath);
    }

    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
}
