using Microsoft.Extensions.Configuration;

namespace ProjectHub.Infrastructure;

public sealed class SupabaseOptions
{
    public string Url { get; init; } = string.Empty;

    public string ServiceRoleKey { get; init; } = string.Empty;

    public bool IsConfigured =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        !string.IsNullOrWhiteSpace(ServiceRoleKey);

    public static SupabaseOptions FromConfiguration(IConfiguration configuration)
    {
        var urlVariable = configuration["Supabase:UrlEnvironmentVariable"] ?? "PROJECTHUB_SUPABASE_URL";
        var keyVariable = configuration["Supabase:ServiceRoleKeyEnvironmentVariable"] ?? "PROJECTHUB_SUPABASE_SERVICE_ROLE_KEY";

        return new SupabaseOptions
        {
            Url = Environment.GetEnvironmentVariable(urlVariable) ?? string.Empty,
            ServiceRoleKey = Environment.GetEnvironmentVariable(keyVariable) ?? string.Empty
        };
    }
}
