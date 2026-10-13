using System.Collections.Concurrent;
using System.Windows.Threading;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerUiDispatchTests
{
    [Fact]
    public async Task ConcurrentProducersDeliverEveryEventToOneDispatcher()
    {
        var started = new TaskCompletionSource<Dispatcher>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            started.TrySetResult(dispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WorkerUiDispatchTests UI Thread"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var dispatcher = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var received = new List<(int Worker, int Sequence)>();
        var wrongThreadCount = 0;
        try
        {
            var tasks = Enumerable.Range(0, 4).Select(worker =>
                Task.Run(() =>
                {
                    for (var sequence = 0; sequence < 25; sequence++)
                    {
                        var current = sequence;
                        WorkerUiDispatch.Invoke(dispatcher, () =>
                        {
                            if (!dispatcher.CheckAccess())
                                wrongThreadCount++;
                            received.Add((worker, current));
                        });
                    }
                })).ToArray();
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, wrongThreadCount);
            Assert.Equal(100, received.Count);
            Assert.Equal(100, received.Distinct().Count());
            foreach (var worker in Enumerable.Range(0, 4))
                Assert.Equal(Enumerable.Range(0, 25),
                    received.Where(item => item.Worker == worker)
                        .Select(item => item.Sequence));
            dispatcher.Invoke(() =>
                WorkerUiDispatch.Invoke(dispatcher, () =>
                    Assert.True(dispatcher.CheckAccess())));
        }
        finally
        {
            dispatcher.Invoke(() => dispatcher.InvokeShutdown());
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        }
    }
}
