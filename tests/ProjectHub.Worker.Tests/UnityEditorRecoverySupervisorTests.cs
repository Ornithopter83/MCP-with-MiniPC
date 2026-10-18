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
        Action<string, string>? report = null,
        Func<TimeSpan, CancellationToken, Task>? wait = null,
        Action<UnityToolchainPreflight.Result>? observe = null) =>
        new(
            Path.Combine(Path.GetTempPath(), "projecthub-recovery-tests"),
            CancellationToken.None, probe, locked,
            wait ?? ((_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token)),
            report ?? ((_, _) => { }), observe);

    [Fact]
    public async Task PassiveProbeUpdatesSharedStateAndReportsRecovery()
    {
        var connection = new UnityEditorConnectionState(
            Path.GetTempPath(), Missing(), (_, _) => Task.FromResult(Connected()));
        var states = new Queue<UnityToolchainPreflight.Result>(
            new[] { Missing(), Connected() });
        var events = new List<string>();
        await using var supervisor = Create(
            (_, _) => Task.FromResult(states.Dequeue()),
            _ => true, (message, _) => events.Add(message),
            observe: connection.Observe);
        await supervisor.CheckOnceAsync(CancellationToken.None);
        Assert.False(connection.Current.Ready);
        await supervisor.CheckOnceAsync(CancellationToken.None);
        Assert.True(connection.Current.Ready);
        Assert.Contains(events, e => e.Contains("재실행·재연결은 자동 수행하지 않음"));
        Assert.Contains(events, e => e.Contains("연결 재확인"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PersistentMissingEditorNeverTriggersRecoveryOrRepeatedNotifications(
        bool lockHeld)
    {
        var events = new List<string>();
        var lockChecks = 0;
        await using var supervisor = Create(
            (_, _) => Task.FromResult(Missing()),
            _ => { lockChecks++; return lockHeld; },
            (message, _) => events.Add(message));
        for (var i = 0; i < 5; i++)
            await supervisor.CheckOnceAsync(CancellationToken.None);
        Assert.Equal(1, events.Count);
        Assert.Equal(5, lockChecks);
        Assert.Contains("자동 수행하지 않음", events[0]);
    }

    [Fact]
    public async Task MonitorDoesNotPollUntilStartedAndStopsAtJobEnd()
    {
        var probes = 0;
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var supervisor = Create(
            (_, _) =>
            {
                Interlocked.Increment(ref probes);
                return Task.FromResult(Missing());
            },
            _ => false,
            wait: async (_, token) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }))
        {
            Assert.Equal(0, probes);
            supervisor.StartWatching();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.Equal(0, probes);
    }
}
