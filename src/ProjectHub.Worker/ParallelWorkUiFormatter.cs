namespace ProjectHub.Worker;

public sealed record ParallelWorkUiStatus(string Summary, string Detail);

public static class ParallelWorkUiFormatter
{
    public static ParallelWorkUiStatus Format(ParallelWorkSchedulerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var running = snapshot.Running
            .OrderBy(item => item.Slot)
            .Select(item => $"{item.WorkItemId}:S{item.Slot}")
            .ToArray();
        var ready = Ids(snapshot, WorkItemState.Ready);
        var blocked = Ids(snapshot, WorkItemState.Blocked);
        var completed = Ids(snapshot, WorkItemState.Completed);
        var failed = Ids(snapshot, WorkItemState.Failed);

        var summary =
            $"RUN {snapshot.RunningCount}/{snapshot.Graph.MaxConcurrentWork} · " +
            $"R {snapshot.ReadyCount} · B {snapshot.BlockedCount} · " +
            $"C {snapshot.CompletedCount} · F {snapshot.FailedCount}";

        var detail = string.Join(
            Environment.NewLine,
            $"RUNNING {FormatIds(running)}",
            $"READY {FormatIds(ready)}",
            $"BLOCKED {FormatIds(blocked)}",
            $"COMPLETED {FormatIds(completed)}",
            $"FAILED {FormatIds(failed)}");

        return new(summary, detail);
    }

    private static string[] Ids(
        ParallelWorkSchedulerSnapshot snapshot,
        WorkItemState state)
        => snapshot.Graph.Items
            .Where(item => item.State == state)
            .OrderBy(item => item.CreatedOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => item.Id)
            .ToArray();

    private static string FormatIds(IReadOnlyList<string> ids)
        => ids.Count == 0 ? "-" : string.Join(", ", ids);
}
