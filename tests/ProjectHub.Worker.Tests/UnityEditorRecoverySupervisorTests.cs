using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorRecoverySupervisorTests
{
    private static UnityToolchainPreflight.Result Missing() =>
        new(false, "UNITY_EDITOR_UNAVAILABLE",
            "Unity CLI exit 6: No Pipeline instance found for project");

    private static UnityToolchainPreflight.Result Connected() =>
        new(true, "UNITY_EDITOR_CONNECTED", "ready");

    private static UnityEditorRecoverySupervisor Create(
        Func<string, CancellationToken, Task<UnityToolchainPreflight.Result>> probe,
        Func<string, bool> locked,
        Func<string, CancellationToken,
            Task<UnityEditorCommandLifecycle.LaunchResult>> open,
        Action<string, string>? report = null,
        Func<TimeSpan, CancellationToken, Task>? wait = null) =>
        new(
            Path.Combine(Path.GetTempPath(), "projecthub-recovery-tests"),
            CancellationToken.None, probe, locked, open,
            wait ?? ((_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token)),
            report ?? ((_, _) => { }));

    [Fact]
    public async Task TwoConfirmedMissingChecks_RelaunchOnce_ThenConnectionRecovers()
    {
        var launches = 0;
        var states = new Queue<UnityToolchainPreflight.Result>(
            new[] { Connected(), Missing(), Missing(), Connected() });
        var events = new List<string>();
        await using var supervisor = Create(
            (_, _) => Task.FromResult(states.Dequeue()),
            _ => false,
            (_, _) =>
            {
                launches++;
                return Task.FromResult(
                    new UnityEditorCommandLifecycle.LaunchResult(true, "launched"));
            },
            (message, _) => events.Add(message));

        await supervisor.CheckOnceAsync(CancellationToken.None);
        await supervisor.CheckOnceAsync(CancellationToken.None);
        Assert.Equal(0, launches);
        await supervisor.CheckOnceAsync(CancellationToken.None);
        Assert.Equal(1, launches);
        await supervisor.CheckOnceAsync(CancellationToken.None);
        Assert.Contains(events, s => s.Contains("복구되었습니다"));
    }

    [Fact]
    public async Task LockedEditor_IsNeverOpenedTwice()
    {
        var launches = 0;
        await using var supervisor = Create(
            (_, _) => Task.FromResult(Missing()),
            _ => true,
            (_, _) =>
            {
                launches++;
                return Task.FromResult(
                    new UnityEditorCommandLifecycle.LaunchResult(true, "launched"));
            });
        for (var i = 0; i < 6; i++)
            await supervisor.CheckOnceAsync(CancellationToken.None);
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task SafeModeAndGenericCliFailures_NeverTriggerLaunch()
    {
        var launches = 0;
        await using var supervisor = Create(
            (_, _) => Task.FromResult(new UnityToolchainPreflight.Result(
                false, "UNITY_EDITOR_PREFLIGHT_TIMEOUT", "Editor busy")),
            _ => false,
            (_, _) =>
            {
                launches++;
                return Task.FromResult(
                    new UnityEditorCommandLifecycle.LaunchResult(true, "launched"));
            });
        for (var i = 0; i < 4; i++)
            await supervisor.CheckOnceAsync(CancellationToken.None);
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task RelaunchAttemptsAreBoundedAcrossRepeatedFailures()
    {
        var launches = 0;
        await using var supervisor = Create(
            (_, _) => Task.FromResult(Missing()),
            _ => false,
            (_, _) =>
            {
                launches++;
                return Task.FromResult(
                    new UnityEditorCommandLifecycle.LaunchResult(false, "failed"));
            });
        for (var i = 0; i < 10; i++)
            await supervisor.CheckOnceAsync(CancellationToken.None);
        Assert.Equal(2, launches);
    }

    [Fact]
    public async Task MonitorHasNoPollingUntilStarted_AndStopsAtJobEnd()
    {
        var probes = 0;
        var waitEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var supervisor = Create(
            (_, _) =>
            {
                Interlocked.Increment(ref probes);
                return Task.FromResult(Missing());
            },
            _ => false,
            (_, _) => Task.FromResult(
                new UnityEditorCommandLifecycle.LaunchResult(true, "launched")),
            wait: async (_, token) =>
            {
                waitEntered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }))
        {
            Assert.Equal(0, probes);
            supervisor.StartWatching();
            await waitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.Equal(0, probes);
    }
}
