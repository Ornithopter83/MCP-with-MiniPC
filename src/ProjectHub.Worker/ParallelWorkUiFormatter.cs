namespace ProjectHub.Worker;

public sealed record ParallelWorkUiStatus(string Summary, string Detail);

public static class ParallelWorkUiFormatter
{
    public static ParallelWorkUiStatus Format(ParallelWorkSchedulerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var running = snapshot.Running
            .OrderBy(item => item.Slot)
            .Select(item => FormatRunning(snapshot, item))
            .ToArray();
        var ready = Items(snapshot, WorkItemState.Ready);
        var blocked = Items(snapshot, WorkItemState.Blocked);
        var completed = Items(snapshot, WorkItemState.Completed);
        var failed = Items(snapshot, WorkItemState.Failed);
        var canceled = Items(snapshot, WorkItemState.Canceled);

        var summary =
            $"RUN {snapshot.RunningCount}/{snapshot.Graph.MaxConcurrentWork} · " +
            $"R {snapshot.ReadyCount} · B {snapshot.BlockedCount} · " +
            $"C {snapshot.CompletedCount} · F {snapshot.FailedCount}";

        var detail = string.Join(
            Environment.NewLine,
            $"RUNNING {FormatItems(running)}",
            $"READY {FormatItems(ready)}",
            $"BLOCKED {FormatItems(blocked)}",
            $"COMPLETED {FormatItems(completed)}",
            $"FAILED {FormatItems(failed)}",
            $"CANCELED {FormatItems(canceled)}");

        return new(summary, detail);
    }

    private static string FormatRunning(
        ParallelWorkSchedulerSnapshot snapshot,
        RunningWorkItemSnapshot running)
    {
        var item = snapshot.Graph.Items.FirstOrDefault(value =>
            string.Equals(value.Id, running.WorkItemId, StringComparison.Ordinal));
        return item is null
            ? $"{running.WorkItemId}:S{running.Slot}"
            : FormatItem(item, $":S{running.Slot}");
    }

    private static string[] Items(
        ParallelWorkSchedulerSnapshot snapshot,
        WorkItemState state)
        => snapshot.Graph.Items
            .Where(item => item.State == state)
            .OrderBy(item => item.CreatedOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => FormatItem(item))
            .ToArray();

    private static string FormatItem(WorkItemSnapshot item, string suffix = "")
    {
        var kind = item.Kind == WorkItemKind.Integration ? "[I]" : string.Empty;
        var code =
            item.State == WorkItemState.Blocked && !string.IsNullOrWhiteSpace(item.BlockCode)
                ? $"({item.BlockCode})"
                : item.State == WorkItemState.Failed && !string.IsNullOrWhiteSpace(item.FailureCode)
                    ? $"({item.FailureCode})"
                    : string.Empty;

        return $"{item.Id}{kind}{suffix}{code}";
    }

    private static string FormatItems(IReadOnlyList<string> items)
        => items.Count == 0 ? "-" : string.Join(", ", items);
}
