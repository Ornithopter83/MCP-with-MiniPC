using Microsoft.Extensions.DependencyInjection;
using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProjectHubInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IProjectStateRepository, NoOpProjectStateRepository>();
        services.AddSingleton<IProjectService, ProjectService>();
        return services;
    }
}
