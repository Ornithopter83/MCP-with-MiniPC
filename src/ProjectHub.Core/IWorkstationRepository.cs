namespace ProjectHub.Core;

public interface IWorkstationRepository
{
    Task<Workstation> UpsertAsync(
        Workstation workstation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Workstation>> ListAsync(
        CancellationToken cancellationToken = default);
}
