using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityWorkerDelegationSemanticsTests
{
    [Fact]
    public async Task DelegateWork_WaitsForActualChildResult_InSameParentToolCall()
    {
        using var bridge = new BridgeServer();
        var session = new WorkerMcpSession(
            "SV", "job", Path.GetTempPath(), CancellationToken.None);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.McpToolExecutor = async (_, tool, _, token) =>
        {
            Assert.Equal("delegate_work", tool);
            started.SetResult();
            return await release.Task.WaitAsync(token);
        };
        using var doc = JsonDocument.Parse(
            """{"task":"inspect source","paths":["docs"]}""");

        var pending = bridge.ExecuteDelegatedWorkAsync(
            session, doc.RootElement, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.IsCompleted); // no DELEGATION_QUEUED reply
        release.SetResult("INVOCATION: child-one\nRESULT: completed");

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("RESULT: completed", result);
        Assert.Equal(1, Volatile.Read(ref session.SuccessfulDelegations));
        var evidence = Assert.Single(session.Evidence);
        Assert.Equal("COMPLETED", evidence.Status);
        Assert.Equal("child-one", evidence.Invocation);
    }

    [Fact]
    public async Task SameParent_ConcurrentCallsAreSerialized()
    {
        using var bridge = new BridgeServer();
        var session = new WorkerMcpSession(
            "HIGH", "job", Path.GetTempPath(), CancellationToken.None);
        var active = 0;
        var peak = 0;
        bridge.McpToolExecutor = async (_, _, _, token) =>
        {
            var running = Interlocked.Increment(ref active);
            Interlocked.Exchange(ref peak, Math.Max(peak, running));
            try
            {
                await Task.Delay(40, token);
                return "INVOCATION: child\nRESULT: completed";
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        };
        using var doc = JsonDocument.Parse(
            """{"task":"review","paths":["docs"]}""");
        // Read-only HIGH scopes must exist, so inspect an actual directory
        // when using the same validation rules as real MCP calls.
        using var realDoc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            task = "review",
            paths = new[] { Path.GetFileName(Path.GetTempPath().TrimEnd(
                Path.DirectorySeparatorChar)) }
        }));
        // A write-capable SV session permits narrow future file scopes.
        var sv = new WorkerMcpSession(
            "SV", "job", Path.GetTempPath(), CancellationToken.None);
        var first = bridge.ExecuteDelegatedWorkAsync(
            sv, doc.RootElement, CancellationToken.None);
        var second = bridge.ExecuteDelegatedWorkAsync(
            sv, doc.RootElement, CancellationToken.None);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, peak);
        Assert.Equal(2, Volatile.Read(ref sv.SuccessfulDelegations));
    }

    [Fact]
    public async Task DelegatedWorkCannotDelegateFurther()
    {
        using var bridge = new BridgeServer();
        var child = new WorkerMcpSession(
            "WORK", "job", Path.GetTempPath(), CancellationToken.None);
        using var doc = JsonDocument.Parse(
            """{"task":"nested","paths":["docs"]}""");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bridge.ExecuteDelegatedWorkAsync(
                child, doc.RootElement, CancellationToken.None));
    }

    [Fact]
    public void UnityStyleTools_ExposeNoSubdelegationToWork()
    {
        Assert.Single(BridgeServer.ToolDefinitions(workOnly: false));
        Assert.Empty(BridgeServer.ToolDefinitions(workOnly: true));
    }
}
