using System.Diagnostics;

namespace ProjectHub.Worker;

internal sealed class ManagedBrowserProcessJob : IDisposable
{
    internal const uint KillOnJobCloseLimitFlag = WorkerChildProcessJob.KillOnJobCloseLimitFlag;
    private readonly WorkerChildProcessJob _inner = new("관리형 Chromium");

    public bool Enabled => _inner.Enabled;

    public void Assign(Process process)
        => _inner.Assign(process);

    public void Dispose()
        => _inner.Dispose();
}
