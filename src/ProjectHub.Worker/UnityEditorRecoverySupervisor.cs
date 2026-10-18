using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// Job-scoped, passive Unity Editor connectivity monitor. It cannot start,
/// stop, refocus or retry an Editor. Decisions belong to HQ/specialists.
/// </summary>
internal sealed class UnityEditorRecoverySupervisor : IAsyncDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(20);
    private readonly string _projectRoot;
    private readonly CancellationTokenSource _stop;
    private readonly Func<string, CancellationToken, Task<UnityToolchainPreflight.Result>> _probe;
    private readonly Func<string, bool> _editorLockHeld;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly Action<string, string> _report;
    private readonly Action<UnityToolchainPreflight.Result>? _stateObserved;
    private Task _loop = Task.CompletedTask;
    private bool _wasDisconnected;
    private string? _lastDiagnostic;

    internal UnityEditorRecoverySupervisor(
        string projectRoot,
        CancellationToken jobCancellationToken,
        Func<string, CancellationToken,
            Task<UnityToolchainPreflight.Result>> probe,
        Func<string, bool> editorLockHeld,
        Func<TimeSpan, CancellationToken, Task> wait,
        Action<string, string> report,
        Action<UnityToolchainPreflight.Result>? stateObserved = null)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(jobCancellationToken);
        _probe = probe;
        _editorLockHeld = editorLockHeld;
        _wait = wait;
        _report = report;
        _stateObserved = stateObserved;
    }

    internal static UnityEditorRecoverySupervisor StartForJob(
        string projectRoot, CancellationToken jobCancellationToken,
        Action<string, string> report,
        Action<UnityToolchainPreflight.Result>? stateObserved = null)
    {
        var supervisor = new UnityEditorRecoverySupervisor(
            projectRoot, jobCancellationToken,
            UnityToolchainPreflight.RunUncoordinatedAsync,
            UnityEditorCommandLifecycle.ProjectEditorLockHeld,
            (delay, token) => Task.Delay(delay, token),
            report, stateObserved);
        supervisor.StartWatching();
        return supervisor;
    }

    internal void StartWatching()
    {
        if (_loop != Task.CompletedTask)
            throw new InvalidOperationException("Unity monitor is already started.");
        _loop = Task.Run(WatchAsync);
    }

    private async Task WatchAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await _wait(CheckInterval, _stop.Token).ConfigureAwait(false);
                await CheckOnceAsync(_stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Completed/canceled job: no lingering background monitor.
        }
    }

    internal async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UnityToolchainPreflight.Result state;
        try
        {
            state = await _probe(_projectRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ReportOnce("UNITY_MONITOR_PROBE_ERROR:" + ex.GetType().Name);
            return;
        }

        _stateObserved?.Invoke(state);
        if (state.Ready)
        {
            if (_wasDisconnected)
                _report("Unity Editor/Pipeline 연결 재확인", "RECOVERED");
            _wasDisconnected = false;
            _lastDiagnostic = null;
            return;
        }

        _wasDisconnected = true;
        var lockHeld = _editorLockHeld(_projectRoot);
        var diagnostic = state.Code + (lockHeld
            ? ": 프로젝트 잠금 감지, 실행 중 Editor의 CLI 연결 상태 미확인"
            : ": 프로젝트 잠금 없음, Editor 종료 여부는 미확인");
        ReportOnce(diagnostic + "; 재실행·재연결은 자동 수행하지 않음");
    }

    private void ReportOnce(string detail)
    {
        if (string.Equals(_lastDiagnostic, detail, StringComparison.Ordinal))
            return;
        _lastDiagnostic = detail;
        _report(detail, "UNVERIFIED");
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _loop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally { _stop.Dispose(); }
    }
}
