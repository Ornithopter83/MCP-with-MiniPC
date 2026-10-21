using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class MilestoneGraphPersistenceTests
{
    [Fact]
    public void MilestoneGraphSnapshotRoundTripsUnderProjectHub()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubMilestoneGraphTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "SampleProject");
        Directory.CreateDirectory(workspace);

        try
        {
            var snapshot = new MilestoneExecutionGraphSnapshot(
                "job-graph",
                "M1",
                "RUNNING",
                ResourceReserved: true,
                Nodes: new[]
                {
                    new MilestoneGraphNodeSnapshot("HQ-DESIGN", "HQ", "COMPLETED"),
                    new MilestoneGraphNodeSnapshot("SV-1", "SV", "RUNNING", "1"),
                    new MilestoneGraphNodeSnapshot("RESOURCE-0", "RESOURCE", "PLANNED", "0"),
                    new MilestoneGraphNodeSnapshot("TEMP-WORK", "WORK", "PLANNED"),
                    new MilestoneGraphNodeSnapshot("HIGH", "HIGH", "PLANNED"),
                    new MilestoneGraphNodeSnapshot("GIT-FINALIZE", "WORKER", "PLANNED"),
                    new MilestoneGraphNodeSnapshot("HQ-FINAL", "HQ", "PLANNED")
                },
                Edges: new[]
                {
                    new MilestoneGraphEdgeSnapshot("HQ-DESIGN", "SV-1", "DISPATCH"),
                    new MilestoneGraphEdgeSnapshot("SV-1", "TEMP-WORK", "DELEGATE"),
                    new MilestoneGraphEdgeSnapshot("SV-1", "HIGH", "INTEGRATE_AND_REVIEW")
                },
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            Assert.True(ProjectWorkspacePersistence.SaveMilestoneGraph(
                workspace,
                snapshot));

            var path = ProjectWorkspacePersistence.WorkGraphPath(
                workspace,
                "job-graph");
            Assert.True(File.Exists(path));
            Assert.StartsWith(
                Path.Combine(Path.GetFullPath(workspace), ".projecthub", "work-graphs"),
                Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase);

            var loaded = ProjectWorkspacePersistence.TryLoadMilestoneGraph(
                workspace,
                "job-graph");
            Assert.NotNull(loaded);
            Assert.Equal("M1", loaded!.MilestoneId);
            Assert.Equal("RUNNING", loaded.State);
            Assert.Contains(loaded.Nodes, node =>
                node.Id == "SV-1" &&
                node.WorkItemId == "1");
            Assert.Contains(loaded.Edges, edge =>
                edge.From == "SV-1" &&
                edge.To == "HIGH");
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }
}
