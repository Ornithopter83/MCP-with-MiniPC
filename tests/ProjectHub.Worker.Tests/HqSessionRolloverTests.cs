using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class HqSessionRolloverTests
{
    [Fact]
    public void RolloverRequiresExistingSessionAndBudget()
    {
        Assert.False(HqSessionRollover.ShouldRollover(false, HqSessionRollover.MaxAccumulatedTextBytes));
        Assert.False(HqSessionRollover.ShouldRollover(true, HqSessionRollover.MaxAccumulatedTextBytes - 1));
        Assert.True(HqSessionRollover.ShouldRollover(true, HqSessionRollover.MaxAccumulatedTextBytes));
    }

    [Fact]
    public void HandoffKeepsOpenItemsAndCompactsOlderTerminalItems()
    {
        var graph = new WorkGraph("job-hq-rollover", 4);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                "10",
                "첫 작업",
                BaseRef: "base-a",
                Checklist: new[] { "첫 단계" })),
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                "11",
                "후속 작업",
                new[] { "10" },
                BaseRef: "base-a",
                Checklist: new[] { "후속 단계" }))
        })).Success);

        Assert.True(graph.TryMarkRunning("10"));
        Assert.True(graph.TryMarkCompleted(
            "10",
            "commit-10",
            "완료 결과",
            WorkItemResultType.CodeChange));

        var handoff = HqSessionRollover.BuildHandoff(
            "사용자 목표",
            graph.Snapshot(),
            "base-a");

        Assert.Contains("사용자 목표", handoff);
        Assert.Contains("#10", handoff);
        Assert.Contains("commit-10", handoff);
        Assert.Contains("#11", handoff);
        Assert.Contains("후속 작업", handoff);
        Assert.Contains("후속 단계", handoff);
        Assert.Contains("currentBaseRef: base-a", handoff);
    }

    [Fact]
    public void HandoffSnapshotIsStoredUnderProjectHubDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "projecthub-hq-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var path = ProjectWorkspacePersistence.SaveHqHandoff(
                directory,
                "job-a",
                2,
                "# handoff");

            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.True(path!.StartsWith(
                ProjectWorkspacePersistence.RootDirectory(directory),
                StringComparison.OrdinalIgnoreCase));
            Assert.True(path.Contains(
                Path.Combine(".projecthub", "hq-handoffs"),
                StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
