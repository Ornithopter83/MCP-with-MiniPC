using System.Text;
using System.Threading.Channels;

namespace ProjectHub.Worker;

public interface IParallelExternalBlockHost
{
    event Action<ParallelWorkExternalBlock>? ExternalBlockAvailable;

    Task<bool> ResumeExternalWorkItemAsync(
        string workItemId,
        string inputType,
        string body,
        CancellationToken cancellationToken = default);
}

public sealed record ParallelResourceRoutingEvent(
    string WorkItemId,
    string Stage,
    string Message,
    string? RequestId = null,
    string? ErrorCode = null);

/// <summary>
/// Routes only mechanical RESOURCE sidecar facts for blocked parallel WorkItems.
/// It never decides whether a resource is semantically good or whether another resource is needed.
/// </summary>
public sealed class ParallelResourceWorkItemRouter : IAsyncDisposable
{
    private readonly IParallelExternalBlockHost _host;
    private readonly ResourceSidecarQueue _resourceQueue;
    private readonly CancellationTokenSource _cts;
    private readonly Channel<RouterMessage> _messages;
    private readonly Task _pump;
    private bool _disposed;

    public ParallelResourceWorkItemRouter(
        IParallelExternalBlockHost host,
        ResourceSidecarQueue resourceQueue,
        CancellationToken cancellationToken = default)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _resourceQueue = resourceQueue ?? throw new ArgumentNullException(nameof(resourceQueue));
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _messages = Channel.CreateUnbounded<RouterMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _host.ExternalBlockAvailable += OnExternalBlockAvailable;
        _resourceQueue.CompletionAvailable += OnResourceCompletionAvailable;
        _pump = Task.Run(PumpAsync);
    }

    public event Action<ParallelResourceRoutingEvent>? RoutingEvent;

    private void OnExternalBlockAvailable(ParallelWorkExternalBlock block)
    {
        if (!string.Equals(block.BlockCode, "RESOURCE_REQUEST", StringComparison.Ordinal))
            return;

        _messages.Writer.TryWrite(new ResourceBlockMessage(block));
    }

    private void OnResourceCompletionAvailable(ResourceSidecarCompletion completion)
    {
        if (string.IsNullOrWhiteSpace(completion.WorkItemId))
            return;

        _messages.Writer.TryWrite(new ResourceCompletionMessage(completion));
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(_cts.Token))
            {
                switch (message)
                {
                    case ResourceBlockMessage block:
                        await HandleBlockAsync(block.Block, _cts.Token).ConfigureAwait(false);
                        break;
                    case ResourceCompletionMessage completion:
                        await HandleCompletionAsync(completion.Completion, _cts.Token).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    private async Task HandleBlockAsync(
        ParallelWorkExternalBlock block,
        CancellationToken cancellationToken)
    {
        if (!ResourceTransportContract.TryParse(
                block.Body,
                out var resource,
                out var error))
        {
            var errorCode = error ?? "RESOURCE_REQUEST_INVALID";
            RoutingEvent?.Invoke(new ParallelResourceRoutingEvent(
                block.WorkItemId,
                "REQUEST_INVALID",
                "병렬 WorkItem RESOURCE 요청 형식이 올바르지 않습니다.",
                ErrorCode: errorCode));

            await ResumeAsync(
                block.WorkItemId,
                BuildFailureResult(
                    block.WorkItemId,
                    null,
                    null,
                    errorCode,
                    "RESOURCE 요청 형식이 올바르지 않아 실행하지 않았습니다."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        ResourceSidecarRequest queued;
        try
        {
            queued = _resourceQueue.Enqueue(
                resource!.Type,
                resource.Prompt,
                block.WorkItemId);
        }
        catch (Exception exception)
        {
            const string errorCode = "RESOURCE_QUEUE_ENQUEUE_FAILED";
            RoutingEvent?.Invoke(new ParallelResourceRoutingEvent(
                block.WorkItemId,
                "QUEUE_FAILED",
                exception.Message,
                ErrorCode: errorCode));

            await ResumeAsync(
                block.WorkItemId,
                BuildFailureResult(
                    block.WorkItemId,
                    null,
                    resource!.Type,
                    errorCode,
                    exception.Message),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        RoutingEvent?.Invoke(new ParallelResourceRoutingEvent(
            block.WorkItemId,
            "QUEUED",
            $"RESOURCE 요청을 FIFO 대기열에 접수했습니다. type={queued.Type}",
            queued.Id));
    }

    private async Task HandleCompletionAsync(
        ResourceSidecarCompletion completion,
        CancellationToken cancellationToken)
    {
        var workItemId = completion.WorkItemId!;
        var body = FormatResult(workItemId, completion);

        RoutingEvent?.Invoke(new ParallelResourceRoutingEvent(
            workItemId,
            completion.Success ? "COMPLETED" : "FAILED",
            completion.Message,
            completion.RequestId,
            completion.ErrorCode));

        await ResumeAsync(workItemId, body, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResumeAsync(
        string workItemId,
        string body,
        CancellationToken cancellationToken)
    {
        var resumed = await _host.ResumeExternalWorkItemAsync(
            workItemId,
            "RESOURCE_RESULT",
            body,
            cancellationToken).ConfigureAwait(false);

        if (!resumed)
        {
            RoutingEvent?.Invoke(new ParallelResourceRoutingEvent(
                workItemId,
                "RESUME_REJECTED",
                "RESOURCE 결과를 반환할 차단된 WorkItem을 찾지 못했습니다.",
                ErrorCode: "RESOURCE_RESUME_REJECTED"));
        }
    }

    public static string FormatResult(
        string workItemId,
        ResourceSidecarCompletion completion)
    {
        var builder = new StringBuilder();
        builder.AppendLine("RESOURCE_RESULT");
        builder.AppendLine("workItemId: " + workItemId);
        builder.AppendLine("requestId: " + completion.RequestId);
        builder.AppendLine("type: " + completion.Type);
        builder.AppendLine("status: " + (completion.Success ? "SAVED" : "FAILED"));
        if (!string.IsNullOrWhiteSpace(completion.ErrorCode))
            builder.AppendLine("errorCode: " + completion.ErrorCode);
        builder.AppendLine("message: " + SingleLine(completion.Message));
        if (completion.SavedPaths.Count > 0)
        {
            builder.AppendLine("savedPaths:");
            foreach (var path in completion.SavedPaths)
                builder.AppendLine("- " + path);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildFailureResult(
        string workItemId,
        string? requestId,
        string? type,
        string errorCode,
        string message)
    {
        var builder = new StringBuilder();
        builder.AppendLine("RESOURCE_RESULT");
        builder.AppendLine("workItemId: " + workItemId);
        if (!string.IsNullOrWhiteSpace(requestId))
            builder.AppendLine("requestId: " + requestId);
        if (!string.IsNullOrWhiteSpace(type))
            builder.AppendLine("type: " + type);
        builder.AppendLine("status: FAILED");
        builder.AppendLine("errorCode: " + errorCode);
        builder.AppendLine("message: " + SingleLine(message));
        return builder.ToString().TrimEnd();
    }

    private static string SingleLine(string value)
        => (value ?? string.Empty)
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _host.ExternalBlockAvailable -= OnExternalBlockAvailable;
        _resourceQueue.CompletionAvailable -= OnResourceCompletionAvailable;
        _messages.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }

        _cts.Dispose();
    }

    private abstract record RouterMessage;
    private sealed record ResourceBlockMessage(ParallelWorkExternalBlock Block) : RouterMessage;
    private sealed record ResourceCompletionMessage(ResourceSidecarCompletion Completion) : RouterMessage;
}
