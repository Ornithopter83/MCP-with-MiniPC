using System.Text;

namespace ProjectHub.Worker;

public interface IWorkItemObservationGate
{
    string GetRequestDirectory(string workItemId);

    void RegisterWorkItemRoot(string workItemId, string worktreePath);

    Task<IReadOnlyList<MechanicalWorkCompletion>> CollectRequiredAsync(
        string workItemId,
        CancellationToken cancellationToken);
}

public sealed class WorkItemObservationGate : IWorkItemObservationGate
{
    private readonly ObservationSidecarQueue _queue;
    private readonly MechanicalWorkRegistry _registry;

    public WorkItemObservationGate(
        ObservationSidecarQueue queue,
        MechanicalWorkRegistry registry)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public string GetRequestDirectory(string workItemId)
        => _queue.GetRequestDirectory(workItemId);

    public void RegisterWorkItemRoot(string workItemId, string worktreePath)
        => _queue.RegisterWorkItemRoot(workItemId, worktreePath);

    public async Task<IReadOnlyList<MechanicalWorkCompletion>> CollectRequiredAsync(
        string workItemId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workItemId))
            throw new ArgumentException("WorkItem ID가 비어 있습니다.", nameof(workItemId));

        await _queue.ScanNowAsync(cancellationToken).ConfigureAwait(false);

        var result = new List<MechanicalWorkCompletion>();
        result.AddRange(_registry.DrainCompletions(
            "OBSERVATION",
            MechanicalWorkCompletionMode.WorkResultRequired,
            workItemId));

        if (_registry.WorkResultRequiredCountForOwner(workItemId) > 0)
        {
            await _registry.WaitForWorkResultRequiredAsync(
                workItemId,
                cancellationToken).ConfigureAwait(false);

            result.AddRange(_registry.DrainCompletions(
                "OBSERVATION",
                MechanicalWorkCompletionMode.WorkResultRequired,
                workItemId));
        }

        return result
            .OrderBy(value => value.FinishedAt)
            .ToArray();
    }

    public static string FormatResultBody(
        string workItemId,
        IReadOnlyList<MechanicalWorkCompletion> completions)
    {
        var builder = new StringBuilder();
        builder.AppendLine("OBSERVATION_RESULT");
        builder.AppendLine("workItemId: " + workItemId);

        foreach (var completion in completions)
        {
            builder.AppendLine($"observationId: {completion.Id}");
            builder.AppendLine($"status: {(completion.Success ? "COMPLETED" : "FAILED")}");
            if (completion.ExitCode is not null)
                builder.AppendLine($"exitCode: {completion.ExitCode}");
            if (!string.IsNullOrWhiteSpace(completion.ErrorCode))
                builder.AppendLine($"errorCode: {completion.ErrorCode}");
            builder.AppendLine("message: " + SingleLine(completion.Message));
            if (completion.ResultPaths.Count > 0)
            {
                builder.AppendLine("resultPaths:");
                foreach (var path in completion.ResultPaths)
                    builder.AppendLine("- " + path);
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string SingleLine(string value)
        => (value ?? string.Empty)
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
}
