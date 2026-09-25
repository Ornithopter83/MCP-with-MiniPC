using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkUiFormatterTests
{
    [Fact]
    public void FormatShowsOccupancyCountsAndMechanicalWorkItemIds()
    {
        var now = DateTimeOffset.UtcNow;
        var graph = new WorkGraphSnapshot(
            "job",
            4,
            4,
            new[]
            {
                Item("W1", WorkItemState.Running, 0, now),
                Item("W2", WorkItemState.Ready, 1, now),
                Item("W3", WorkItemState.Blocked, 2, now),
                Item("W4", WorkItemState.Completed, 3, now),
                Item("W5", WorkItemState.Failed, 4, now)
            });
        var snapshot = new ParallelWorkSchedulerSnapshot(
            graph,
            new[] { new RunningWorkItemSnapshot("W1", 2) });

        var formatted = ParallelWorkUiFormatter.Format(snapshot);

        Assert.Equal("RUN 1/4 · R 1 · B 1 · C 1 · F 1", formatted.Summary);
        Assert.Contains("RUNNING W1:S2", formatted.Detail);
        Assert.Contains("READY W2", formatted.Detail);
        Assert.Contains("BLOCKED W3", formatted.Detail);
        Assert.Contains("COMPLETED W4", formatted.Detail);
        Assert.Contains("FAILED W5", formatted.Detail);
    }

    private static WorkItemSnapshot Item(
        string id,
        WorkItemState state,
        long order,
        DateTimeOffset now)
        => new(
            id,
            id + " 목표",
            Array.Empty<string>(),
            WorkItemKind.Normal,
            state,
            order,
            "base",
            null,
            null,
            null,
            null,
            null,
            state == WorkItemState.Failed ? "TEST_FAILURE" : null,
            state == WorkItemState.Blocked ? "TEST_BLOCKED" : null,
            null,
            null,
            now,
            state == WorkItemState.Running ? now : null,
            state is WorkItemState.Completed or WorkItemState.Failed ? now : null);
}
