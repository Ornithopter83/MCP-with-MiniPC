namespace ProjectHub.Core;

public interface IProjectStateRepository
{
    Task UpsertProjectAsync(Project project, CancellationToken cancellationToken = default);

    Task UpsertProjectStateAsync(ProjectState state, CancellationToken cancellationToken = default);

    Task<ProjectState?> GetProjectStateAsync(
        string projectId,
        string workstationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectState>> GetProjectStatesAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task AddEventAsync(ProjectEvent projectEvent, CancellationToken cancellationToken = default);
}
