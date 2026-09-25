using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkGraphPersistenceTests
{
    [Fact]
    public void WorkGraphSnapshotRoundTripsAndRunningWorkRestoresAsRecoveryBlocked()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "projecthub-workgraph-persistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var graph = new WorkGraph("job-graph", 4);
            Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
            {
                WorkGraphPatchOperation.Add(new WorkItemSpec("A", "한글 기반 작업", BaseRef: "base123")),
                WorkGraphPatchOperation.Add(new WorkItemSpec("B", "후속 작업", new[] { "A" }, BaseRef: "base123"))
            })).Success);

            Assert.True(graph.TryMarkRunning("A", "branch-a", "worktree-a", "session-a"));
            Assert.True(graph.TryMarkCompleted("A", "commit-a", "A 완료"));
            Assert.True(graph.TryMarkRunning("B", "branch-b", "worktree-b", "session-b"));

            Assert.True(ProjectWorkspacePersistence.SaveWorkGraph(directory, graph.Snapshot()));
            Assert.True(File.Exists(ProjectWorkspacePersistence.WorkGraphPath(directory, "job-graph")));

            var loaded = ProjectWorkspacePersistence.TryLoadWorkGraph(directory, "job-graph");
            Assert.NotNull(loaded);
            Assert.Equal(4, loaded!.MaxConcurrentWork);
            Assert.Contains(loaded.Items, item => item.Goal == "한글 기반 작업");

            var restored = WorkGraph.Restore(loaded);
            Assert.Equal(loaded.Revision, restored.Revision);
            Assert.Equal(WorkItemState.Completed, restored.Find("A")!.State);

            var interrupted = restored.Find("B")!;
            Assert.Equal(WorkItemState.Blocked, interrupted.State);
            Assert.Equal("RECOVERY_REQUIRED", interrupted.BlockCode);
            Assert.Equal("session-b", interrupted.SessionId);
            Assert.Equal("branch-b", interrupted.Branch);
            Assert.Equal("worktree-b", interrupted.WorktreePath);
            Assert.Empty(restored.GetReadyItems());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void EventLogKeepsWorkItemMechanicalContext()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "projecthub-workgraph-event-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            const string jobId = "job-event";
            var eventId = ProjectWorkspacePersistence.AppendEvent(
                directory,
                jobId,
                DateTimeOffset.UtcNow,
                "WORK",
                "작업 진행 원문",
                status: "RUNNING",
                referenceId: "ref-1",
                workItemId: "W7",
                graphRevision: 3,
                slot: 2,
                branch: "projecthub/job/W7",
                worktreePath: "C:/worktrees/W7");

            Assert.False(string.IsNullOrWhiteSpace(eventId));
            var entry = Assert.Single(ProjectWorkspacePersistence.ReadAllEvents(directory, jobId));
            Assert.Equal("W7", entry.WorkItemId);
            Assert.Equal(3, entry.GraphRevision);
            Assert.Equal(2, entry.Slot);
            Assert.Equal("projecthub/job/W7", entry.Branch);
            Assert.Equal("C:/worktrees/W7", entry.WorktreePath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ClearWorkGraphRemovesOnlyActiveGraphSnapshot()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "projecthub-workgraph-clear-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var graph = new WorkGraph("job-clear");
            Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
            {
                WorkGraphPatchOperation.Add(new WorkItemSpec("A", "작업"))
            })).Success);
            Assert.True(ProjectWorkspacePersistence.SaveWorkGraph(directory, graph.Snapshot()));

            ProjectWorkspacePersistence.AppendEvent(
                directory,
                "job-clear",
                DateTimeOffset.UtcNow,
                "WORK",
                "기록 보존");

            ProjectWorkspacePersistence.ClearWorkGraph(directory, "job-clear");

            Assert.False(File.Exists(ProjectWorkspacePersistence.WorkGraphPath(directory, "job-clear")));
            Assert.True(File.Exists(ProjectWorkspacePersistence.EventLogPath(directory, "job-clear")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
