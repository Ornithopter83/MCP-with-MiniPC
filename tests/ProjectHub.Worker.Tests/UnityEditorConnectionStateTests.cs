using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorConnectionStateTests
{
    private static UnityToolchainPreflight.Result Disconnected() =>
        new(false, "UNITY_EDITOR_UNAVAILABLE", "No Pipeline instance");

    private static UnityToolchainPreflight.Result Connected() =>
        new(true, "UNITY_EDITOR_CONNECTED", "Live CLI command catalog");

    [Fact]
    public async Task StartFailure_DoesNotPreventLaterMilestoneFromSeeingRecovery()
    {
        var connected = Connected();
        var probes = 0;
        var tracker = new UnityEditorConnectionState(
            Path.GetTempPath(), Disconnected(), (_, _) =>
            {
                probes++;
                return Task.FromResult(connected);
            });

        Assert.False(tracker.Current.Ready);
        var nextMilestone = await tracker.RecheckAsync(CancellationToken.None);
        Assert.True(nextMilestone.Ready);
        Assert.True(tracker.Current.Ready);
        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task PreviouslyConnectedEditor_MustBeRecheckedAndCanBecomeUnavailable()
    {
        var lost = Disconnected();
        var tracker = new UnityEditorConnectionState(
            Path.GetTempPath(), Connected(),
            (_, _) => Task.FromResult(lost));

        Assert.True(tracker.Current.Ready);
        var now = await tracker.RecheckAsync(CancellationToken.None);
        Assert.False(now.Ready);
        Assert.False(tracker.Current.Ready);
    }

    [Fact]
    public async Task RecheckPreservesCancellationInsteadOfTreatingItAsOffline()
    {
        var tracker = new UnityEditorConnectionState(
            Path.GetTempPath(), Connected(), (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(Disconnected());
            });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tracker.RecheckAsync(cts.Token));
        Assert.True(tracker.Current.Ready);
    }
}
