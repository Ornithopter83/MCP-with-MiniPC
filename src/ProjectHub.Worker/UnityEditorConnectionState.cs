using System.IO;
using System.Threading;

namespace ProjectHub.Worker;

/// <summary>
/// Current job's Editor CLI connectivity, shared by the independent recovery
/// watcher and operation-boundary checks. A successful CLI catalog probe is
/// connectivity evidence, not proof of Game View rendering or eval_file.
/// </summary>
internal sealed class UnityEditorConnectionState
{
    private readonly string _projectRoot;
    private readonly Func<string, CancellationToken,
        Task<UnityToolchainPreflight.Result>> _probe;
    private UnityToolchainPreflight.Result _latest;

    internal UnityEditorConnectionState(
        string projectRoot,
        UnityToolchainPreflight.Result initial,
        Func<string, CancellationToken,
            Task<UnityToolchainPreflight.Result>> probe)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _latest = initial;
        _probe = probe;
    }

    internal UnityToolchainPreflight.Result Current =>
        Volatile.Read(ref _latest);

    internal void Observe(UnityToolchainPreflight.Result result) =>
        Volatile.Write(ref _latest, result);

    /// <summary>
    /// Re-probe at a milestone/verification boundary rather than treating a
    /// failed task-start probe as permanently unavailable.
    /// This never launches an Editor or requests desktop focus.
    /// </summary>
    internal async Task<UnityToolchainPreflight.Result> RecheckAsync(
        CancellationToken cancellationToken)
    {
        var refreshed = await _probe(_projectRoot, cancellationToken)
            .ConfigureAwait(false);
        Observe(refreshed);
        return refreshed;
    }
}
