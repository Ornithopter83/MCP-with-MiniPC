namespace ProjectHub.Core;

public interface IProjectService
{
    Task<ProjectState?> GetProjectStateAsync(
        string projectId,
        string workstationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectState>> GetProjectStatesAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task UpdateProjectStateAsync(
        ProjectState state,
        CancellationToken cancellationToken = default);
}
