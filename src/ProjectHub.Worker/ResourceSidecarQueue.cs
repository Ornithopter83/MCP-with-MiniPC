using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;

namespace ProjectHub.Worker;

public sealed record ResourceSidecarRequest(string Id, string Prompt);
public sealed record ResourceSidecarCompletion(string RequestId, bool Success, string Message, string? ErrorCode, IReadOnlyList<string> SavedPaths);
public sealed record ResourceSidecarQueueState(bool Running, int QueuedCount, int OutstandingCount, string Stage, string? RequestId);
public sealed record ResourceSidecarTransportEvent(string Source, string Content, string? Status = null);

/// <summary>
/// Single-reader FIFO RESOURCE sidecar. It performs transport only and never judges resource quality.
/// </summary>
public sealed class ResourceSidecarQueue : IAsyncDisposable
{
    private readonly BridgeServer? _bridgeServer;
    private readonly string _workingDirectory;
    private readonly Channel<ResourceSidecarRequest> _queue;
    private readonly ConcurrentQueue<ResourceSidecarCompletion> _completions = new();
    private readonly CancellationTokenSource _cts;
    private readonly Task _pump;
    private readonly object _gate = new();
    private int _queuedCount;
    private int _outstandingCount;
    private bool _running;
    private TaskCompletionSource<bool> _idle = CompletedIdle();

    public ResourceSidecarQueue(BridgeServer? bridgeServer, string workingDirectory, CancellationToken jobCancellation)
    {
        _bridgeServer = bridgeServer;
        _workingDirectory = workingDirectory;
        _queue = Channel.CreateUnbounded<ResourceSidecarRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _cts = CancellationTokenSource.CreateLinkedTokenSource(jobCancellation);
        _pump = Task.Run(PumpAsync);
    }

    public event Action<ResourceSidecarQueueState>? StateChanged;
    public event Action<ResourceSidecarCompletion>? CompletionAvailable;
    public event Action<ResourceSidecarTransportEvent>? TransportEvent;

    public bool IsIdle
    {
        get { lock (_gate) return _outstandingCount == 0; }
    }

    public int OutstandingCount
    {
        get { lock (_gate) return _outstandingCount; }
    }

    public int QueuedCount
    {
        get { lock (_gate) return _queuedCount; }
    }

    public ResourceSidecarRequest Enqueue(string prompt)
    {
        var request = new ResourceSidecarRequest(Guid.NewGuid().ToString("N"), prompt);
        ResourceSidecarQueueState state;
        lock (_gate)
        {
            if (_outstandingCount == 0)
                _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _outstandingCount++;
            _queuedCount++;
            state = SnapshotLocked("QUEUED", request.Id);
        }

        if (!_queue.Writer.TryWrite(request))
            throw new InvalidOperationException("RESOURCE_QUEUE_CLOSED");

        StateChanged?.Invoke(state);
        return request;
    }

    public bool TryDequeueCompletion(out ResourceSidecarCompletion completion)
    {
        if (_completions.TryDequeue(out var value))
        {
            completion = value;
            return true;
        }

        completion = null!;
        return false;
    }

    public Task WaitForIdleAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (_gate) task = _idle.Task;
        return task.WaitAsync(cancellationToken);
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                ResourceSidecarQueueState started;
                lock (_gate)
                {
                    _queuedCount = Math.Max(0, _queuedCount - 1);
                    _running = true;
                    started = SnapshotLocked("GENERATING", request.Id);
                }
                StateChanged?.Invoke(started);

                ResourceSidecarCompletion completion;
                try
                {
                    completion = await ExecuteAsync(request, _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    completion = new ResourceSidecarCompletion(
                        request.Id,
                        false,
                        exception.Message,
                        "RESOURCE_TRANSPORT_ERROR",
                        Array.Empty<string>());
                }

                _completions.Enqueue(completion);
                CompletionAvailable?.Invoke(completion);

                ResourceSidecarQueueState finished;
                TaskCompletionSource<bool>? idleToRelease = null;
                lock (_gate)
                {
                    _running = false;
                    _outstandingCount = Math.Max(0, _outstandingCount - 1);
                    if (_outstandingCount == 0) idleToRelease = _idle;
                    finished = SnapshotLocked(_outstandingCount == 0 ? "IDLE" : "QUEUED", null);
                }
                idleToRelease?.TrySetResult(true);
                StateChanged?.Invoke(finished);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    private async Task<ResourceSidecarCompletion> ExecuteAsync(ResourceSidecarRequest request, CancellationToken cancellationToken)
    {
        if (_bridgeServer is null)
            return Failure(request, "RESOURCE_WEB_UNAVAILABLE", "RESOURCE Web bridge를 사용할 수 없습니다.");

        var status = _bridgeServer.GetRoleBindingStatus("RESOURCE");
        if (!status.Bound || !status.Connected || !status.ExtensionSynchronized)
            return Failure(request, "RESOURCE_WEB_UNAVAILABLE", "RESOURCE 역할로 연결된 ChatGPT Web 대화가 활성 상태가 아니거나 확장 버전이 맞지 않습니다.");

        var targetDirectory = $"assets/resources/{request.Id}";
        var trackedResource = new ResourceRequest(
            request.Id,
            "IMAGE",
            request.Prompt,
            targetDirectory,
            "image-01.png",
            "WORK",
            "REQUESTED",
            null,
            _workingDirectory);

        TransportEvent?.Invoke(new ResourceSidecarTransportEvent("WORKER → RESOURCE WEB", request.Prompt, "SENDING"));
        var bridgeTask = _bridgeServer.CreateTaskForRole("RESOURCE", request.Prompt, resource: trackedResource);
        if (bridgeTask is null)
            return Failure(request, "RESOURCE_WEB_UNAVAILABLE", "RESOURCE Web task를 생성할 수 없습니다.");

        TransportEvent?.Invoke(new ResourceSidecarTransportEvent(
            "RESOURCE WEB TASK",
            $"task {bridgeTask.Id} · assets/resources/{request.Id}",
            "GENERATING"));

        var completed = await _bridgeServer.WaitForTaskCompletionAsync(bridgeTask.Id, cancellationToken);
        if (completed is null)
            return Failure(request, "RESOURCE_RESULT_MISSING", "RESOURCE result task가 사라졌습니다.");

        IEnumerable<string> savedCandidates = completed.SavedPaths ?? Enumerable.Empty<string>();
        var paths = savedCandidates
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0 && !string.IsNullOrWhiteSpace(completed.SavedPath) && File.Exists(completed.SavedPath))
            paths.Add(completed.SavedPath);

        if (completed.Status != "COMPLETED" || paths.Count == 0)
            return Failure(request, FailureCode(completed), completed.Result ?? "RESOURCE result file missing.");

        var relative = paths
            .Select(path => Path.GetRelativePath(_workingDirectory, path).Replace('\\', '/'))
            .ToArray();
        var message = $"리소스 저장 완료 ({relative.Length}개):\n" +
                      string.Join("\n", relative.Select(path => "- " + path)) +
                      "\n자동 코드 연결은 수행하지 않았습니다.";
        return new ResourceSidecarCompletion(request.Id, true, message, null, paths);
    }

    private static ResourceSidecarCompletion Failure(ResourceSidecarRequest request, string code, string message)
        => new(request.Id, false, message, code, Array.Empty<string>());

    private static string FailureCode(BridgeTask task) => task.FinishReason switch
    {
        "resource_image_not_generated" => "RESOURCE_IMAGE_NOT_GENERATED",
        "resource_image_capture_failed" => "RESOURCE_IMAGE_CAPTURE_FAILED",
        "resource_image_download_failed" => "RESOURCE_IMAGE_DOWNLOAD_FAILED",
        "resource_save_failed" => "RESOURCE_SAVE_FAILED",
        "send_failed" => "RESOURCE_WEB_DELIVERY_FAILED",
        _ => "RESOURCE_RESULT_MISSING"
    };

    private ResourceSidecarQueueState SnapshotLocked(string stage, string? requestId)
        => new(_running, _queuedCount, _outstandingCount, stage, requestId);

    private static TaskCompletionSource<bool> CompletedIdle()
    {
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.TrySetResult(true);
        return source;
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        try { await _pump; }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        _cts.Dispose();
    }
}
