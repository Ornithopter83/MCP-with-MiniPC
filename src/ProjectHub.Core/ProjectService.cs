namespace ProjectHub.Core;

public sealed class ProjectService(IProjectStateRepository repository) : IProjectService
{
    public Task<ProjectState?> GetProjectStateAsync(
        string projectId,
        string workstationId,
        CancellationToken cancellationToken = default) =>
        repository.GetProjectStateAsync(projectId, workstationId, cancellationToken);

    public Task<IReadOnlyList<ProjectState>> GetProjectStatesAsync(
        string projectId,
        CancellationToken cancellationToken = default) =>
        repository.GetProjectStatesAsync(projectId, cancellationToken);

    public Task UpdateProjectStateAsync(
        ProjectState state,
        CancellationToken cancellationToken = default) =>
        repository.UpsertProjectStateAsync(state, cancellationToken);
}
