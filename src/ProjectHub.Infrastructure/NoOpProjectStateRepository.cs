using ProjectHub.Core;

namespace ProjectHub.Infrastructure;

internal sealed class NoOpProjectStateRepository : IProjectStateRepository
{
    public Task UpsertProjectStateAsync(ProjectState state, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<ProjectState?> GetProjectStateAsync(
        string projectId,
        string workstationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ProjectState?>(null);

    public Task<IReadOnlyList<ProjectState>> GetProjectStatesAsync(
        string projectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectState>>(Array.Empty<ProjectState>());

    public Task AddEventAsync(ProjectEvent projectEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
