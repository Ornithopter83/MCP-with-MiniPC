using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorOperationGateTests
{
    [Fact]
    public async Task SameProjectUsesOneExclusiveEditorLease()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var first = await UnityEditorOperationGate.EnterAsync(root, CancellationToken.None);
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = Task.Run(async () =>
        {
            using var second = await UnityEditorOperationGate.EnterAsync(
                Path.Combine(root, "."), token.Token);
            entered.SetResult(true);
        });
        try
        {
            await Task.Delay(50);
            Assert.False(entered.Task.IsCompleted);
        }
        finally
        {
            first.Dispose();
        }
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(entered.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DifferentProjectsDoNotBlockOneAnother()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var secondRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var first = await UnityEditorOperationGate.EnterAsync(
            firstRoot, CancellationToken.None);
        using var second = await UnityEditorOperationGate.EnterAsync(
            secondRoot, CancellationToken.None);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task CancelledWaitNeverStealsLease()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var first = await UnityEditorOperationGate.EnterAsync(root, CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await UnityEditorOperationGate.EnterAsync(root, cancel.Token));
        first.Dispose();
        using var third = await UnityEditorOperationGate.EnterAsync(root, CancellationToken.None);
        Assert.NotNull(third);
    }
}
