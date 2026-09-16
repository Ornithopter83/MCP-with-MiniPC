using Microsoft.Extensions.Configuration;

namespace ProjectHub.Infrastructure;

public sealed record LargeDataOptions(
    string Issuer,
    string Audience,
    string? PrivateKeyPem,
    string? GatewayPublicKeyPem,
    string StorageRoot,
    long MaxObjectSizeBytes,
    long ChunkSizeBytes)
{
    public static LargeDataOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("LargeData");
        return new(
            section["Issuer"] ?? "projecthub",
            section["Audience"] ?? "projecthub-gateway",
            Environment.GetEnvironmentVariable("PROJECTHUB_ASSERTION_PRIVATE_KEY_PEM"),
            Environment.GetEnvironmentVariable("PROJECTHUB_GATEWAY_PUBLIC_KEY_PEM"),
            section["StorageRoot"] ?? Path.Combine(AppContext.BaseDirectory, "objects"),
            long.TryParse(section["MaxObjectSizeBytes"], out var max) && max > 0 ? max : 1L << 40,
            long.TryParse(section["ChunkSizeBytes"], out var chunk) && chunk is >= (16L << 20) and <= (64L << 20) ? chunk : 32L << 20);
    }
}
