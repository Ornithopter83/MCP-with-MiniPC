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
        Project project,
        ProjectState state,
        CancellationToken cancellationToken = default) =>
        UpdateProjectAndStateAsync(project, state, cancellationToken);

    private async Task UpdateProjectAndStateAsync(
        Project project,
        ProjectState state,
        CancellationToken cancellationToken)
    {
        await repository.UpsertProjectAsync(project, cancellationToken);
        await repository.UpsertProjectStateAsync(state, cancellationToken);
    }
}
