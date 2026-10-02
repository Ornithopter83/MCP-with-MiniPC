using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class TargetWorkspaceFinalizerPublishTests
{
    [Fact]
    public async Task FinalizerRequiresRepublishWhenVerifiedCodeIsNewerThanLastPublish()
    {
        var root = CreateRoot();
        try
        {
            var graph = CreateCompletedGraph("code-new");
            WriteVerifiedLedger(root, "job", "code-new");

            var publish = new WorkspacePublishState(root, "job");
            await publish.MarkCodeMaterializedAsync("code-old");
            await publish.MarkPublishedAsync(3);
            await publish.MarkCodeMaterializedAsync("code-new");

            var finalizer = new TargetWorkspaceFinalizer(root, null);
            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.False(result.Success);
            Assert.Equal("TARGET_PUBLISH_STALE", result.ErrorCode);
            Assert.Contains("#9 BUILD/PUBLISH", result.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FinalizerAllowsEndAfterRepublishOfLatestVerifiedCode()
    {
        var root = CreateRoot();
        try
        {
            var graph = CreateCompletedGraph("code-new");
            WriteVerifiedLedger(root, "job", "code-new");

            var publish = new WorkspacePublishState(root, "job");
            await publish.MarkCodeMaterializedAsync("code-old");
            await publish.MarkPublishedAsync(3);
            await publish.MarkCodeMaterializedAsync("code-new");
            await publish.MarkPublishedAsync(5);

            var finalizer = new TargetWorkspaceFinalizer(root, null);
            var result = await finalizer.FinalizeAsync(graph.Snapshot());

            Assert.True(result.Success);
            Assert.Null(result.ErrorCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static WorkGraph CreateCompletedGraph(string resultRef)
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(
                new WorkItemSpec("10", "code", Kind: WorkItemKind.Normal, BaseRef: "base"))
        })).Success);
        Assert.True(graph.TryMarkRunning("10"));
        Assert.True(graph.TryMarkCompleted(
            "10",
            resultRef,
            "done",
            WorkItemResultType.CodeChange,
            "manifest.json"));
        return graph;
    }

    private static void WriteVerifiedLedger(
        string root,
        string jobId,
        string resultRef)
    {
        var directory = Path.Combine(
            root,
            ".projecthub",
            "materialization-ledger",
            jobId);
        Directory.CreateDirectory(directory);
        var entry = new MaterializationLedgerEntry(
            jobId,
            FixedWorkItemSlots.Materialize,
            1,
            DateTimeOffset.UtcNow,
            true,
            null,
            new[] { resultRef },
            Array.Empty<MaterializationFileRecord>(),
            Array.Empty<string>(),
            "verified");
        File.WriteAllText(
            Path.Combine(directory, "000000000001-8.json"),
            JsonSerializer.Serialize(
                entry,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-finalizer-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
