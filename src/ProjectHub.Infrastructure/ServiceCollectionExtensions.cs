using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProjectHubInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(SupabaseOptions.FromConfiguration(configuration));
        services.AddSingleton(LargeDataOptions.FromConfiguration(configuration));
        services.AddSingleton<ILargeDataAssertionIssuer, LargeDataAssertionIssuer>();
        services.AddSingleton<LargeDataAssertionVerifier>();
        services.AddSingleton<INasGatewayProvisioner, LocalNasGatewayProvisioner>();
        services.AddSingleton<INasGatewayObjectDeleter, NasGatewayObjectDeleter>();
        services.AddSingleton<IResumableUploadService, LocalResumableUploadService>();
        services.AddSingleton<ILargeDataMetadataRepository, SupabaseLargeDataMetadataRepository>();
        services.AddHttpClient("Supabase", (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<SupabaseOptions>();
            if (Uri.TryCreate(options.Url, UriKind.Absolute, out var url))
            {
                client.BaseAddress = new Uri($"{url.ToString().TrimEnd('/')}/rest/v1/");
            }

            if (!string.IsNullOrWhiteSpace(options.ServiceRoleKey))
            {
                client.DefaultRequestHeaders.Add("apikey", options.ServiceRoleKey);
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ServiceRoleKey}");
            }
        });
        services.AddHttpClient("NasGateway");
        services.AddSingleton<IProjectStateRepository, SupabaseProjectStateRepository>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IWorkstationRepository, SupabaseWorkstationRepository>();
        return services;
    }
}
