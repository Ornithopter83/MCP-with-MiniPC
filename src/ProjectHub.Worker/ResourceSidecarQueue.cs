using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;

namespace ProjectHub.Worker;

public sealed record ResourceSidecarRequest(
    string Id,
    string Type,
    string Prompt,
    string? WorkItemId = null,
    string? TargetWorkingDirectory = null);
public sealed record ResourceSidecarCompletion(string RequestId, string Type, bool Success, string Message, string? ErrorCode, IReadOnlyList<string> SavedPaths, string? WorkItemId = null);
public sealed record ResourceSidecarQueueState(bool Running, int QueuedCount, int OutstandingCount, string Stage, string? RequestId);
public sealed record ResourceSidecarTransportEvent(string Source, string Content, string? Status = null);

/// <summary>
/// Single-reader FIFO RESOURCE sidecar. It performs transport only and never judges resource quality.
/// </summary>
public sealed class ResourceSidecarQueue : IAsyncDisposable
{
    private static readonly TimeSpan ResourceTransportTimeout = TimeSpan.FromMinutes(30);
    private readonly BridgeServer? _bridgeServer;
    private readonly string _workingDirectory;
    private readonly MechanicalWorkRegistry _mechanicalWork;
    private readonly Channel<ResourceSidecarRequest> _queue;
    private readonly ConcurrentQueue<ResourceSidecarCompletion> _completions = new();
    private readonly CancellationTokenSource _cts;
    private readonly Task _pump;
    private readonly object _gate = new();
    private int _queuedCount;
    private int _outstandingCount;
    private bool _running;
    private TaskCompletionSource<bool> _idle = CompletedIdle();

    public ResourceSidecarQueue(
        BridgeServer? bridgeServer,
        string workingDirectory,
        CancellationToken jobCancellation,
        MechanicalWorkRegistry? mechanicalWork = null)
    {
        _bridgeServer = bridgeServer;
        _workingDirectory = workingDirectory;
        _mechanicalWork = mechanicalWork ?? new MechanicalWorkRegistry();
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

    public ResourceSidecarRequest Enqueue(
        string type,
        string prompt,
        string? workItemId = null,
        string? targetWorkingDirectory = null)
    {
        if (!ResourceTransportContract.IsSupportedType(type))
            throw new InvalidOperationException("RESOURCE_TYPE_UNSUPPORTED");
        workItemId = string.IsNullOrWhiteSpace(workItemId) ? null : workItemId.Trim();
        targetWorkingDirectory = string.IsNullOrWhiteSpace(targetWorkingDirectory)
            ? null
            : Path.GetFullPath(targetWorkingDirectory.Trim());
        if (targetWorkingDirectory is not null && !Directory.Exists(targetWorkingDirectory))
            throw new DirectoryNotFoundException("RESOURCE_TARGET_WORKSPACE_MISSING");
        var request = new ResourceSidecarRequest(
            Guid.NewGuid().ToString("N"),
            type.Trim().ToUpperInvariant(),
            prompt,
            workItemId,
            targetWorkingDirectory);
        ResourceSidecarQueueState state;
        lock (_gate)
        {
            if (_outstandingCount == 0)
                _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _outstandingCount++;
            _queuedCount++;
            state = SnapshotLocked("QUEUED", request.Id);
        }

        _mechanicalWork.Register(
            request.Id,
            "RESOURCE",
            MechanicalWorkCompletionMode.FinalizeOnly,
            $"type={request.Type}",
            request.WorkItemId);

        if (!_queue.Writer.TryWrite(request))
        {
            _mechanicalWork.Complete(
                request.Id,
                false,
                "RESOURCE 대기열이 닫혀 요청을 접수하지 못했습니다.",
                "RESOURCE_QUEUE_CLOSED");
            lock (_gate)
            {
                _queuedCount = Math.Max(0, _queuedCount - 1);
                _outstandingCount = Math.Max(0, _outstandingCount - 1);
                if (_outstandingCount == 0) _idle.TrySetResult(true);
            }
            throw new InvalidOperationException("RESOURCE_QUEUE_CLOSED");
        }

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
                        request.Type,
                        false,
                        exception.Message,
                        "RESOURCE_TRANSPORT_ERROR",
                        Array.Empty<string>(),
                        request.WorkItemId);
                }

                _completions.Enqueue(completion);
                _mechanicalWork.Complete(
                    request.Id,
                    completion.Success,
                    completion.Message,
                    completion.ErrorCode,
                    completion.SavedPaths);
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

        var targetWorkspace = request.TargetWorkingDirectory ?? _workingDirectory;
        var targetDirectory = $"assets/resources/{request.Id}";
        var trackedResource = new ResourceRequest(
            request.Id,
            request.Type,
            request.Prompt,
            targetDirectory,
            "resource-01.bin",
            "WORK",
            "REQUESTED",
            null,
            targetWorkspace);

        TransportEvent?.Invoke(new ResourceSidecarTransportEvent("WORKER → RESOURCE WEB", request.Prompt, "SENDING"));
        var bridgeTask = _bridgeServer.CreateTaskForRole("RESOURCE", request.Prompt, resource: trackedResource);
        if (bridgeTask is null)
            return Failure(request, "RESOURCE_WEB_UNAVAILABLE", "RESOURCE Web task를 생성할 수 없습니다.");

        TransportEvent?.Invoke(new ResourceSidecarTransportEvent(
            "RESOURCE WEB TASK",
            $"task {bridgeTask.Id} · type {request.Type} · assets/resources/{request.Id}",
            "GENERATING"));

        BridgeTask? completed;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ResourceTransportTimeout);
            completed = await _bridgeServer.WaitForTaskCompletionAsync(bridgeTask.Id, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var detail = $"RESOURCE Web 작업이 {ResourceTransportTimeout.TotalMinutes:0}분 내 완료되지 않아 transport timeout으로 종료했습니다.";
            completed = _bridgeServer.FailTask(bridgeTask.Id, detail, "resource_timeout");
            if (completed is null)
                return Failure(request, "RESOURCE_TIMEOUT", detail);
        }

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
        var message = $"RESOURCE 저장 완료 · type={request.Type} · {relative.Length}개:\n" +
                      string.Join("\n", relative.Select(path => "- " + path)) +
                      "\n자동 코드 연결은 수행하지 않았습니다.";
        return new ResourceSidecarCompletion(request.Id, request.Type, true, message, null, paths, request.WorkItemId);
    }

    private static ResourceSidecarCompletion Failure(ResourceSidecarRequest request, string code, string message)
        => new(request.Id, request.Type, false, message, code, Array.Empty<string>(), request.WorkItemId);

    private static string FailureCode(BridgeTask task) => task.FinishReason switch
    {
        "resource_not_generated" => "RESOURCE_NOT_GENERATED",
        "resource_capture_failed" => "RESOURCE_CAPTURE_FAILED",
        "resource_download_failed" => "RESOURCE_DOWNLOAD_FAILED",
        "resource_image_not_generated" => "RESOURCE_NOT_GENERATED",
        "resource_image_capture_failed" => "RESOURCE_CAPTURE_FAILED",
        "resource_image_download_failed" => "RESOURCE_DOWNLOAD_FAILED",
        "resource_save_failed" => "RESOURCE_SAVE_FAILED",
        "send_failed" => "RESOURCE_WEB_DELIVERY_FAILED",
        "resource_timeout" => "RESOURCE_TIMEOUT",
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
