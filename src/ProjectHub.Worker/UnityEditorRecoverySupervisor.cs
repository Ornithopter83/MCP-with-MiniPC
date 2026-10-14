using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// Job-scoped, independent Unity Editor connectivity monitoring.
/// It never takes the cooperative Editor-operation gate: DESIGN and HIGH
/// hold that gate while the AI is executing. It never kills an Editor,
/// reopens a project with an active lock, or retries indefinitely.
/// </summary>
internal sealed class UnityEditorRecoverySupervisor : IAsyncDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(20);
    private const int RequiredMissingChecks = 2;
    private const int MaxRestartsPerJob = 2;

    private readonly string _projectRoot;
    private readonly CancellationTokenSource _stop;
    private readonly Func<string, CancellationToken,
        Task<UnityToolchainPreflight.Result>> _probe;
    private readonly Func<string, bool> _editorLockHeld;
    private readonly Func<string, CancellationToken,
        Task<UnityEditorCommandLifecycle.LaunchResult>> _open;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly Action<string, string> _report;
    private Task _loop = Task.CompletedTask;
    private int _missingChecks;
    private int _restartAttempts;
    private bool _connectionLost;
    private bool _reportedLocked;
    private bool _reportedExhausted;

    internal UnityEditorRecoverySupervisor(
        string projectRoot,
        CancellationToken jobCancellationToken,
        Func<string, CancellationToken, Task<UnityToolchainPreflight.Result>> probe,
        Func<string, bool> editorLockHeld,
        Func<string, CancellationToken,
            Task<UnityEditorCommandLifecycle.LaunchResult>> open,
        Func<TimeSpan, CancellationToken, Task> wait,
        Action<string, string> report)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(jobCancellationToken);
        _probe = probe;
        _editorLockHeld = editorLockHeld;
        _open = open;
        _wait = wait;
        _report = report;
    }

    internal static UnityEditorRecoverySupervisor StartForJob(
        string projectRoot, CancellationToken jobCancellationToken,
        Action<string, string> report)
    {
        var supervisor = new UnityEditorRecoverySupervisor(
            projectRoot, jobCancellationToken,
            UnityToolchainPreflight.RunUncoordinatedAsync,
            UnityEditorCommandLifecycle.ProjectEditorLockHeld,
            UnityEditorCommandLifecycle.OpenForRecoveryAsync,
            (delay, token) => Task.Delay(delay, token),
            report);
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
                // First probe is delayed; the foreground command preflight has
                // already run. There is no polling before StartWatching().
                await _wait(CheckInterval, _stop.Token).ConfigureAwait(false);
                await CheckOnceAsync(_stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // User cancel, completed job or disposed supervisor.
        }
    }

    // Separate tick boundary allows deterministic tests without Unity or delays.
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
            // Monitoring itself must never abort the running AI job.
            _missingChecks = 0;
            ReportOnce("UNITY_MONITOR_PROBE_ERROR: " + ex.GetType().Name);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (state.Ready)
        {
            if (_connectionLost)
                _report("Unity Editor / Pipeline 연결이 복구되었습니다.", "RECOVERED");
            _connectionLost = false;
            _reportedLocked = false;
            _missingChecks = 0;
            return;
        }

        // A timeout or compilation/Safe Mode error is not evidence that the
        // Editor has exited. Do not mistake it for permission to open another.
        if (!UnityEditorCommandLifecycle.CanAutoOpen(state))
        {
            _missingChecks = 0;
            return;
        }

        _connectionLost = true;
        if (++_missingChecks < RequiredMissingChecks)
            return;

        if (_editorLockHeld(_projectRoot))
        {
            if (!_reportedLocked)
                _report("Editor 연결 상실 확인, 기존 프로젝트 잠금 감지. " +
                    "중복 Editor 실행 없이 연결 회복을 감시합니다.", "UNVERIFIED");
            _reportedLocked = true;
            return;
        }

        if (_restartAttempts >= MaxRestartsPerJob)
        {
            if (!_reportedExhausted)
                _report("Unity Editor 자동 재실행 한도(2회)에 도달했습니다. " +
                    "현재 작업은 유지하며 Editor 의존 검증은 별도 확인이 필요합니다.",
                    "UNVERIFIED");
            _reportedExhausted = true;
            return;
        }

        // Recheck just before launching: a user could have opened the Editor
        // while the disconnected CLI probe was running.
        cancellationToken.ThrowIfCancellationRequested();
        if (_editorLockHeld(_projectRoot))
            return;

        _missingChecks = 0;
        _restartAttempts++;
        _report("Unity Editor 연결 상실 및 프로젝트 잠금 해제 확인. " +
            "Worker가 Editor 재실행을 시도합니다 (" +
            _restartAttempts + "/" + MaxRestartsPerJob + ").", "RECOVERY");

        try
        {
            var launch = await _open(_projectRoot, cancellationToken)
                .ConfigureAwait(false);
            if (!launch.Success)
                _report("Unity Editor 재실행 실패: " + launch.Detail, "UNVERIFIED");
            // Do not claim recovery until a later live Editor probe succeeds.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _report("Unity Editor 재실행 오류: " + ex.GetType().Name +
                ": " + ex.Message, "UNVERIFIED");
        }
    }

    private bool _probeFailureReported;

    private void ReportOnce(string detail)
    {
        if (_probeFailureReported) return;
        _probeFailureReported = true;
        _report(detail, "UNVERIFIED");
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A cancelled job cannot retain a background recovery process.
        }
        finally
        {
            _stop.Dispose();
        }
    }
}
