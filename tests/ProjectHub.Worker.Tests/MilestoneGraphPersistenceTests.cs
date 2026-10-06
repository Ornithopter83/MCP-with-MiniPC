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
                QaReserved: true,
                ResourceReserved: true,
                Nodes: new[]
                {
                    new MilestoneGraphNodeSnapshot("HQ-DESIGN", "HQ", "COMPLETED"),
                    new MilestoneGraphNodeSnapshot("WORK-10", "WORK", "RUNNING", "10"),
                    new MilestoneGraphNodeSnapshot("RESOURCE-0", "RESOURCE", "PLANNED", "0"),
                    new MilestoneGraphNodeSnapshot("QA", "QA", "PLANNED"),
                    new MilestoneGraphNodeSnapshot("HIGH", "HIGH", "PLANNED"),
                    new MilestoneGraphNodeSnapshot("MANAGER-FINAL", "MANAGER", "PLANNED"),
                    new MilestoneGraphNodeSnapshot("HQ-FINAL", "HQ", "PLANNED")
                },
                Edges: new[]
                {
                    new MilestoneGraphEdgeSnapshot("HQ-DESIGN", "WORK-10", "DISPATCH"),
                    new MilestoneGraphEdgeSnapshot("WORK-10", "QA", "RESULT_TO_VALIDATION"),
                    new MilestoneGraphEdgeSnapshot("QA", "HIGH", "QA_TO_REVIEW")
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
                node.Id == "WORK-10" &&
                node.WorkItemId == "10");
            Assert.Contains(loaded.Edges, edge =>
                edge.From == "QA" &&
                edge.To == "HIGH");
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }
}
