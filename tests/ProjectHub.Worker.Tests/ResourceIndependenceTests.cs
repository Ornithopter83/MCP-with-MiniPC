using System.IO;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ResourceIndependenceTests
{
    [Fact]
    public void CompletedResourcePaths_SurviveRestartAndPreserveLateUpdates()
    {
        var root = Path.Combine(Path.GetTempPath(), "ph-resource-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ledger = new ResourcePendingGitPaths();
            ledger.Register(root, new[] { "assets/image.png" });
            var committed = ledger.Snapshot(root);
            Assert.Single(committed);

            // A new RESOURCE result arriving during Git cannot be cleared by
            // the finalize that saw an earlier version of that path.
            ledger.Register(root, new[] { "assets/image.png", "assets/later.png" });
            ledger.Acknowledge(root, committed);
            Assert.Equal(2, ledger.Snapshot(root).Count);

            var afterRestart = new ResourcePendingGitPaths();
            var nextFinalize = afterRestart.Snapshot(root);
            Assert.Equal(2, nextFinalize.Count);
            afterRestart.Acknowledge(root, nextFinalize);
            Assert.Empty(new ResourcePendingGitPaths().Snapshot(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SameUserTask_ResourcesRunWithoutSerialWait()
    {
        var root = Path.Combine(Path.GetTempPath(), "ph-resource-concurrency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var lifecycle = new ResourceTaskLifecycle();
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var started = 0;
            async Task<int> Run(CancellationToken token)
            {
                Interlocked.Increment(ref started);
                await release.Task.WaitAsync(token);
                return 1;
            }

            var first = lifecycle.RunAsync("task-1", root, Run);
            var second = lifecycle.RunAsync("task-1", root, Run);
            Assert.Equal(2, Volatile.Read(ref started));
            release.SetResult(true);
            Assert.Equal(1, await first);
            Assert.Equal(1, await second);
            await lifecycle.CancelAllAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
