using System.IO;

namespace ProjectHub.Worker;

public interface IParallelJudgeTransport
{
    Task<JudgeTransportResult> ReviewAsync(
        JudgeRequest request,
        CancellationToken cancellationToken);
}

public sealed class JevParallelJudgeTransport : IParallelJudgeTransport
{
    private readonly JevJudgeRunner _runner;
    private readonly JudgeSettings _settings;

    public JevParallelJudgeTransport(
        JevJudgeRunner runner,
        JudgeSettings settings)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public Task<JudgeTransportResult> ReviewAsync(
        JudgeRequest request,
        CancellationToken cancellationToken)
        => _runner.ReviewRawAsync(request, _settings, cancellationToken);
}

public sealed record ParallelJudgeRoutingEvent(
    string WorkItemId,
    string Stage,
    string Message,
    string? ErrorCode = null,
    JevCallTelemetry? Telemetry = null);

/// <summary>
/// Sends only explicit JUDGE_REQUEST blocks to JEV and returns the raw transport result
/// to the same WorkItem session. It never evaluates the judgment.
/// </summary>
public sealed class ParallelJudgeWorkItemRouter : IAsyncDisposable
{
    private readonly IParallelExternalBlockHost _host;
    private readonly IParallelJudgeTransport _transport;
    private readonly string _jobId;
    private readonly CancellationTokenSource _cts;
    private readonly System.Threading.Channels.Channel<ParallelWorkExternalBlock> _blocks;
    private readonly Task _pump;
    private bool _disposed;

    public ParallelJudgeWorkItemRouter(
        IParallelExternalBlockHost host,
        IParallelJudgeTransport transport,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID가 비어 있습니다.", nameof(jobId));
        _jobId = jobId.Trim();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _blocks = System.Threading.Channels.Channel.CreateUnbounded<ParallelWorkExternalBlock>(
            new System.Threading.Channels.UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        _host.ExternalBlockAvailable += OnExternalBlockAvailable;
        _pump = Task.Run(PumpAsync);
    }

    public event Action<ParallelJudgeRoutingEvent>? RoutingEvent;

    private void OnExternalBlockAvailable(ParallelWorkExternalBlock block)
    {
        if (!string.Equals(block.BlockCode, "JUDGE_REQUEST", StringComparison.Ordinal))
            return;

        _blocks.Writer.TryWrite(block);
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var block in _blocks.Reader.ReadAllAsync(_cts.Token))
                await HandleAsync(block, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    private async Task HandleAsync(
        ParallelWorkExternalBlock block,
        CancellationToken cancellationToken)
    {
        if (!JudgeTransportContract.TryParse(block.Body, out _, out var validationError))
        {
            var errorCode = "JUDGE_REQUEST_" + (validationError ?? "INVALID");
            RoutingEvent?.Invoke(new ParallelJudgeRoutingEvent(
                block.WorkItemId,
                "REQUEST_INVALID",
                "병렬 WorkItem JUDGE 요청 형식이 올바르지 않습니다.",
                errorCode));

            await ResumeFailureAsync(
                block.WorkItemId,
                errorCode,
                "JUDGE 요청 형식이 올바르지 않아 전송하지 않았습니다.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(block.WorktreePath) ||
            !Directory.Exists(block.WorktreePath))
        {
            const string errorCode = "JUDGE_WORKTREE_MISSING";
            RoutingEvent?.Invoke(new ParallelJudgeRoutingEvent(
                block.WorkItemId,
                "WORKTREE_MISSING",
                "JUDGE evidence 작업공간을 찾을 수 없습니다.",
                errorCode));

            await ResumeFailureAsync(
                block.WorkItemId,
                errorCode,
                "JUDGE evidence 작업공간을 찾을 수 없습니다.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var request = new JudgeRequest(
            string.IsNullOrWhiteSpace(block.Goal)
                ? $"WorkItem {block.WorkItemId}"
                : block.Goal!,
            1,
            block.WorktreePath!,
            block.Body,
            block.Body,
            Array.Empty<CodexCliFile>(),
            "GIT",
            block.ResultRef,
            _jobId,
            Array.Empty<CodexCommandExecution>());

        RoutingEvent?.Invoke(new ParallelJudgeRoutingEvent(
            block.WorkItemId,
            "SENDING",
            "JUDGE 요청을 전송합니다."));

        JudgeTransportResult result;
        try
        {
            result = await _transport.ReviewAsync(
                request,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            const string errorCode = "JUDGE_TRANSPORT_EXCEPTION";
            RoutingEvent?.Invoke(new ParallelJudgeRoutingEvent(
                block.WorkItemId,
                "FAILED",
                exception.Message,
                errorCode));

            await ResumeFailureAsync(
                block.WorkItemId,
                errorCode,
                exception.Message,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (result.ErrorCode is not null || string.IsNullOrWhiteSpace(result.RawResponse))
        {
            var errorCode = result.ErrorCode ?? "JEV_RESPONSE_MISSING";
            RoutingEvent?.Invoke(new ParallelJudgeRoutingEvent(
                block.WorkItemId,
                "FAILED",
                "JUDGE transport가 결과를 반환하지 못했습니다.",
                errorCode,
                result.Telemetry));

            await ResumeFailureAsync(
                block.WorkItemId,
                errorCode,
                "JUDGE transport가 결과를 반환하지 못했습니다.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        RoutingEvent?.Invoke(new ParallelJudgeRoutingEvent(
            block.WorkItemId,
            "COMPLETED",
            "JUDGE raw 응답을 같은 WorkItem으로 반환합니다.",
            Telemetry: result.Telemetry));

        var resumed = await _host.ResumeExternalWorkItemAsync(
            block.WorkItemId,
            "JUDGMENT",
            result.RawResponse,
            cancellationToken).ConfigureAwait(false);

        if (!resumed)
        {
            RoutingEvent?.Invoke(new ParallelJudgeRoutingEvent(
                block.WorkItemId,
                "RESUME_REJECTED",
                "JUDGE 결과를 반환할 차단된 WorkItem을 찾지 못했습니다.",
                "JUDGE_RESUME_REJECTED",
                result.Telemetry));
        }
    }

    private async Task ResumeFailureAsync(
        string workItemId,
        string errorCode,
        string message,
        CancellationToken cancellationToken)
    {
        var body =
            "JUDGE_RESULT\n" +
            $"workItemId: {workItemId}\n" +
            "status: FAILED\n" +
            $"errorCode: {errorCode}\n" +
            "message: " + SingleLine(message);

        var resumed = await _host.ResumeExternalWorkItemAsync(
            workItemId,
            "JUDGMENT",
            body,
            cancellationToken).ConfigureAwait(false);

        if (!resumed)
        {
            RoutingEvent?.Invoke(new ParallelJudgeRoutingEvent(
                workItemId,
                "RESUME_REJECTED",
                "JUDGE 실패 결과를 반환할 차단된 WorkItem을 찾지 못했습니다.",
                "JUDGE_RESUME_REJECTED"));
        }
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
        _blocks.Writer.TryComplete();
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
}
