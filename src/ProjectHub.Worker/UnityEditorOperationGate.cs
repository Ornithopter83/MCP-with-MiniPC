using System.Collections.Concurrent;
using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// One cooperative Editor lane per physical Unity project shared by WORK,
/// asset integration, CLI recompilation, CLI imports and Editor probing.
/// The lease must cover the entire Editor mutation, not only process launch.
/// Commands issued outside this Worker (for example, arbitrary Codex shell
/// invocations or another application) are not intercepted by this semaphore.
/// </summary>
internal static class UnityEditorOperationGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    internal static async Task<IDisposable> EnterAsync(
        string projectRoot, CancellationToken cancellationToken)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}
