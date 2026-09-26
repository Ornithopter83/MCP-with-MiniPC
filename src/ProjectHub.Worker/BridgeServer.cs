using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectHub.Worker;

public sealed class BridgeServer : IDisposable
{
    private const string Prefix = "http://127.0.0.1:43821/";
    private const string RepositoryName = "MCP-with-MiniPC";
    private const string ExpectedExtensionVersion = "0.2.0";
    private const string ExpectedExtensionBuild = "2026-09-26.3";
    private readonly HttpListener _listener = new();
    private readonly object _gate = new();
    private readonly string _statePath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private BridgeState _state;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTimeOffset? _lastWebHeartbeat;
    private ExtensionProgress? _extensionProgress;
    private string? _webConversationId;
    private string? _webConversationTitle;
    private string? _webProjectId;
    private string? _webExtensionVersion;
    private string? _webExtensionBuild;
    private readonly Dictionary<string, DateTimeOffset> _webHeartbeats = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _webConversationTitles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _webExtensionVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _webExtensionBuilds = new(StringComparer.OrdinalIgnoreCase);
    public bool WebConnected
    {
        get { lock (_gate) return _lastWebHeartbeat is not null && DateTimeOffset.UtcNow - _lastWebHeartbeat < TimeSpan.FromSeconds(10); }
    }

    public bool WebExtensionSynchronized
    {
        get { lock (_gate) return WebConnected && string.Equals(_webExtensionVersion, ExpectedExtensionVersion, StringComparison.Ordinal) && string.Equals(_webExtensionBuild, ExpectedExtensionBuild, StringComparison.Ordinal); }
    }

    public string? WebExtensionVersion
    {
        get { lock (_gate) return _webExtensionVersion; }
    }

    public string? WebExtensionBuild
    {
        get { lock (_gate) return _webExtensionBuild; }
    }

    public string? WebConversationTitle
    {
        get { lock (_gate) return _webConversationTitle; }
    }

    public bool WebConversationBound
    {
        get { lock (_gate) return !string.IsNullOrWhiteSpace(_webConversationId) && _state.Bindings.ContainsKey(_webConversationId); }
    }

    public event Action<BridgeTask>? TaskChanged;
    public event Action<ExtensionProgress>? ExtensionProgressChanged;

    public bool IsRoleBound(string role)
    {
        lock (_gate) return _state.RoleBindings.ContainsKey(NormalizeRole(role));
    }

    public bool IsRoleConnected(string role)
    {
        lock (_gate)
        {
            return _state.RoleBindings.TryGetValue(NormalizeRole(role), out var conversationId) &&
                   _webHeartbeats.TryGetValue(conversationId, out var seen) &&
                   DateTimeOffset.UtcNow - seen < TimeSpan.FromSeconds(10);
        }
    }

    public string? GetRoleConversationId(string role)
    {
        lock (_gate) return _state.RoleBindings.TryGetValue(NormalizeRole(role), out var value) ? value : null;
    }

    public WebRoleBindingStatus GetRoleBindingStatus(string role)
    {
        lock (_gate)
        {
            var normalizedRole = NormalizeRole(role);
            if (!_state.RoleBindings.TryGetValue(normalizedRole, out var conversationId))
                return new(normalizedRole, false, false, false, null, null);

            var connected = _webHeartbeats.TryGetValue(conversationId, out var seen) &&
                            DateTimeOffset.UtcNow - seen < TimeSpan.FromSeconds(10);
            _webConversationTitles.TryGetValue(conversationId, out var title);
            var synchronized = connected &&
                _webExtensionVersions.TryGetValue(conversationId, out var version) &&
                _webExtensionBuilds.TryGetValue(conversationId, out var build) &&
                string.Equals(version, ExpectedExtensionVersion, StringComparison.Ordinal) &&
                string.Equals(build, ExpectedExtensionBuild, StringComparison.Ordinal);
            return new(normalizedRole, true, connected, synchronized, conversationId, title);
        }
    }

    public BridgeTask? CreateTaskForRole(string role, string prompt, List<BridgeAttachment>? attachments = null, ResourceRequest? resource = null)
    {
        lock (_gate)
        {
            var normalizedRole = NormalizeRole(role);
            if (!_state.RoleBindings.TryGetValue(normalizedRole, out var conversationId)) return null;
            return CreateTaskLocked(conversationId, prompt, attachments, normalizedRole, resource);
        }
    }

    public BridgeTask? GetTaskSnapshot(string taskId)
    {
        lock (_gate)
            return _state.Tasks.FirstOrDefault(item => item.Id == taskId);
    }

    public async Task<BridgeTask?> WaitForTaskCompletionAsync(string taskId, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var task = _state.Tasks.FirstOrDefault(item => item.Id == taskId);
                if (task is null || task.Status is "COMPLETED" or "FAILED") return task;
            }
            await Task.Delay(250, cancellationToken);
        }
    }

    public BridgeTask? FailTask(string taskId, string result, string finishReason)
    {
        lock (_gate)
        {
            var task = _state.Tasks.FirstOrDefault(item => item.Id == taskId);
            if (task is null) return null;
            if (task.Status is "COMPLETED" or "FAILED") return task;

            var resource = task.Resource is null ? null : task.Resource with { Status = "FAILED" };
            var failed = task with
            {
                Status = "FAILED",
                Result = result,
                FinishReason = finishReason,
                CompletedAt = DateTimeOffset.UtcNow,
                Resource = resource
            };
            ReplaceTask(failed);
            SaveState();
            TaskChanged?.Invoke(failed);
            return failed;
        }
    }

    public bool CancelActiveTask()
        => CancelActiveTask(out _);

    public bool CancelActiveTask(out string? canceledTaskId)
    {
        canceledTaskId = null;
        lock (_gate)
        {
            var task = _state.Tasks.FirstOrDefault(item => item.Status is "PENDING" or "CLAIMED");
            if (task is null) return false;
            canceledTaskId = task.Id;
            var canceled = task with { Status = "FAILED", Result = "작업이 취소되었습니다.", FinishReason = "canceled", CompletedAt = DateTimeOffset.UtcNow };
            ReplaceTask(canceled);
            SaveState();
            TaskChanged?.Invoke(canceled);
            return true;
        }
    }
    public BridgeAttachment CreateFileAttachment(CodexCliFile file)
    {
        if (!File.Exists(file.Path)) throw new FileNotFoundException("CLI 파일을 찾을 수 없습니다.", file.Path);
        if (file.Size <= 0 || file.Size > 50 * 1024 * 1024) throw new InvalidOperationException("CLI 파일 크기가 허용 범위를 벗어났습니다.");

        var id = Guid.NewGuid().ToString("N");
        var directory = WorkerPaths.Attachments;
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Any(character => !char.IsLetterOrDigit(character) && character != '.'))
            extension = ".bin";
        var path = Path.Combine(directory, id + extension.ToLowerInvariant());
        File.Copy(file.Path, path, false);
        return new BridgeAttachment(id, Path.GetFileName(file.FileName), file.MimeType, new FileInfo(path).Length, "http://127.0.0.1:43821/bridge/attachment/" + id);
    }

    public BridgeServer()
    {
        var directory = WorkerPaths.Root;
        Directory.CreateDirectory(directory);
        _statePath = Path.Combine(directory, "bridge-state.json");
        _state = LoadState();
        _listener.Prefixes.Add(Prefix);
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _listener.Start();
        _loop = RunAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        // Start() can fail before HttpListener enters the listening state (for
        // example, when the URL reservation is unavailable). Cleanup must not
        // replace that startup exception with ObjectDisposedException.
        if (_listener.IsListening)
            _listener.Stop();
        if (_loop is not null)
        {
            try { await _loop; } catch (HttpListenerException) { } catch (ObjectDisposedException) { }
        }
        _loop = null;
        _cts.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context), cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var response = context.Response;
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

        try
        {
            if (context.Request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                return;
            }

            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            var method = context.Request.HttpMethod;
            if (method == "GET" && path.StartsWith("/bridge/attachment/", StringComparison.Ordinal))
            {
                await WriteAttachmentAsync(response, path["/bridge/attachment/".Length..]);
                return;
            }
            object payload;

            if (method == "GET" && path == "/bridge/status") payload = Status();
            else if (method == "GET" && path == "/bridge/projects") payload = Projects();
            else if (method == "GET" && path.StartsWith("/bridge/bindings/", StringComparison.Ordinal))
                payload = Binding(Uri.UnescapeDataString(path["/bridge/bindings/".Length..]));
            else if (method == "POST" && path == "/bridge/bind") payload = Bind(await ReadJsonAsync<BindRequest>(context.Request));
            else if (method == "GET" && path == "/bridge/task") payload = PendingTask(context.Request.QueryString["conversationId"]);
            else if (method == "POST" && path == "/bridge/task") payload = CreateTask(await ReadJsonAsync<CreateTaskRequest>(context.Request));
            else if (method == "POST" && path.StartsWith("/bridge/task/", StringComparison.Ordinal) && path.EndsWith("/claim", StringComparison.Ordinal))
                payload = Claim(path["/bridge/task/".Length..^"/claim".Length], await ReadJsonAsync<ClaimRequest>(context.Request));
            else if (method == "POST" && path.StartsWith("/bridge/task/", StringComparison.Ordinal) && path.EndsWith("/result", StringComparison.Ordinal))
                payload = SubmitResult(path["/bridge/task/".Length..^"/result".Length], await ReadJsonAsync<ResultRequest>(context.Request));
            else if (method == "POST" && path == "/bridge/heartbeat") payload = Heartbeat(await ReadJsonAsync<HeartbeatRequest>(context.Request));
            else if (method == "POST" && path == "/bridge/progress") payload = Progress(await ReadJsonAsync<ProgressRequest>(context.Request));
            else if (method == "POST" && path == "/bridge/reset") payload = Reset(await ReadJsonAsync<ResetRequest>(context.Request));
            else
            {
                response.StatusCode = 404;
                payload = new { error = "not_found" };
            }

            await WriteJsonAsync(response, payload);
        }
        catch (JsonException)
        {
            response.StatusCode = 400;
            await WriteJsonAsync(response, new { error = "invalid_json" });
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = "bridge_error", message = ex.Message });
        }
        finally
        {
            response.Close();
        }
    }

    private BridgeResponse Status()
    {
        lock (_gate)
        {
            return new BridgeResponse(true, new
            {
                worker = "ProjectHub Worker",
                repository = RepositoryName,
                version = "0.1.0",
                bridge = "ready",
                loopback = true,
                activeTask = _state.Tasks.FirstOrDefault(task => task.Status is "PENDING" or "CLAIMED"),
                webConnected = WebConnected,
                webLastSeen = _lastWebHeartbeat,
                webConversationId = _webConversationId,
                webConversationTitle = _webConversationTitle,
                webProjectId = _webProjectId,
                webConversationBound = WebConversationBound,
                roleBindings = _state.RoleBindings,
                webExtensionVersion = _webExtensionVersion,
                webExtensionBuild = _webExtensionBuild,
                expectedExtensionVersion = ExpectedExtensionVersion,
                expectedExtensionBuild = ExpectedExtensionBuild,
                webExtensionSynchronized = WebExtensionSynchronized,
                extensionProgress = _extensionProgress
            });
        }
    }

    private BridgeResponse Projects() => new(true, new[]
    {
        new { id = RepositoryName, name = RepositoryName, path = Environment.CurrentDirectory }
    });

    private BridgeResponse Binding(string conversationId)
    {
        lock (_gate)
        {
            _state.Bindings.TryGetValue(conversationId, out var binding);
            var role = _state.RoleBindings.FirstOrDefault(pair => pair.Value.Equals(conversationId, StringComparison.OrdinalIgnoreCase)).Key;
            return new BridgeResponse(true, new { conversationId, bound = binding is not null, projectId = binding?.ProjectId, role, updatedAt = binding?.UpdatedAt });
        }
    }

    private BridgeResponse Bind(BindRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId)) return new(false, new { error = "conversation_id_required" });
        lock (_gate)
        {
            var normalizedRole = string.IsNullOrWhiteSpace(request.Role) ? null : NormalizeRole(request.Role);
            if (normalizedRole is not null && normalizedRole is not ("HQ" or "RESOURCE"))
                return new(false, new { error = "binding_role_invalid" });
            if (normalizedRole is not null)
            {
                var conflict = _state.RoleBindings.FirstOrDefault(pair =>
                    !pair.Key.Equals(normalizedRole, StringComparison.OrdinalIgnoreCase) &&
                    pair.Value.Equals(request.ConversationId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(conflict.Key))
                    return new(false, new { error = "role_requires_distinct_conversation", boundRole = conflict.Key });
                _state.RoleBindings[normalizedRole] = request.ConversationId;
            }
            _state.Bindings[request.ConversationId] = new BindingState(request.ConversationId, request.ProjectId ?? RepositoryName, DateTimeOffset.UtcNow);
            SaveState();
            return new BridgeResponse(true, new { binding = _state.Bindings[request.ConversationId], role = normalizedRole });
        }
    }

    private BridgeResponse PendingTask(string? conversationId)
    {
        lock (_gate)
        {
            var tasks = _state.Tasks.Where(task =>
                string.IsNullOrWhiteSpace(conversationId) ||
                task.ConversationId.Equals(conversationId, StringComparison.OrdinalIgnoreCase));
            var task = tasks
                .OrderByDescending(item => item.Status is "PENDING" or "CLAIMED")
                .ThenByDescending(item => item.CompletedAt ?? item.ClaimedAt ?? item.CreatedAt)
                .FirstOrDefault();
            return new BridgeResponse(true, new { task });
        }
    }
    private BridgeResponse CreateTask(CreateTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId) || string.IsNullOrWhiteSpace(request.Prompt))
            return new(false, new { error = "conversation_id_and_prompt_required" });
        lock (_gate)
        {
            var task = CreateTaskLocked(request.ConversationId, request.Prompt, request.Attachments, request.Role ?? "WEB", request.Resource);
            return task is null
                ? new(false, new { error = "conversation_not_bound_or_task_conflict" })
                : new(true, task);
        }
    }

    private BridgeTask? CreateTaskLocked(string conversationId, string prompt, List<BridgeAttachment>? attachments, string role, ResourceRequest? resource)
    {
        if (!_state.Bindings.TryGetValue(conversationId, out var binding)) return null;
        var active = _state.Tasks.FirstOrDefault(item => item.ConversationId.Equals(conversationId, StringComparison.OrdinalIgnoreCase) && (item.Status is "PENDING" or "CLAIMED"));
        if (active is not null) return null;
        var task = new BridgeTask(Guid.NewGuid().ToString("N"), conversationId, binding.ProjectId, prompt, "PENDING", null, null, DateTimeOffset.UtcNow, null, role, null, null, null, attachments ?? new(), resource);
        _state.Tasks.Add(task);
        SaveState();
        TaskChanged?.Invoke(task);
        return task;
    }

    private BridgeResponse Claim(string id, ClaimRequest request)
    {
        lock (_gate)
        {
            var task = _state.Tasks.FirstOrDefault(item => item.Id == id);
            if (task is null) return new(false, new { error = "task_not_found" });
            if (!string.Equals(task.ConversationId, request.ConversationId, StringComparison.OrdinalIgnoreCase))
                return new(false, new { error = "conversation_mismatch" });
            if (task.Status != "PENDING") return new(false, new { error = "task_not_pending", task });
            var active = _state.Tasks.FirstOrDefault(item => item.Id != id && item.ConversationId.Equals(task.ConversationId, StringComparison.OrdinalIgnoreCase) && (item.Status is "PENDING" or "CLAIMED"));
            if (active is not null) return new(false, new { error = "task_conflict", task = active });
            var now = DateTimeOffset.UtcNow;
            var claimed = task with
            {
                Status = "CLAIMED",
                ClaimedAt = now,
                StartedAt = now,
                ClaimedBy = "WEB",
                LeaseId = Guid.NewGuid().ToString("N"),
                Resource = task.Resource is null ? null : task.Resource with { Status = "GENERATING" }
            };
            ReplaceTask(claimed);
            SaveState();
            TaskChanged?.Invoke(claimed);
            return new BridgeResponse(true, claimed);
        }
    }
    private BridgeResponse SubmitResult(string id, ResultRequest request)
    {
        lock (_gate)
        {
            var task = _state.Tasks.FirstOrDefault(item => item.Id == id);
            if (task is null) return new(false, new { error = "task_not_found" });
            if (!string.IsNullOrWhiteSpace(request.TaskId) && !string.Equals(task.Id, request.TaskId, StringComparison.Ordinal))
                return new(false, new { error = "task_mismatch" });
            if (!string.Equals(task.ConversationId, request.ConversationId, StringComparison.OrdinalIgnoreCase))
                return new(false, new { error = "conversation_mismatch" });
            if (task.Status is "COMPLETED" or "FAILED")
                return new BridgeResponse(true, task);
            if (task.Status != "CLAIMED")
                return new(false, new { error = "task_not_claimed" });
            if (string.IsNullOrWhiteSpace(request.LeaseId) || !string.Equals(task.LeaseId, request.LeaseId, StringComparison.Ordinal))
                return new(false, new { error = "lease_mismatch" });

            var resource = task.Resource;
            string? savedPath = resource?.SavedPath;
            List<string>? savedPaths = task.SavedPaths;
            if (request.Success && resource is not null)
            {
                try
                {
                    savedPaths = SaveResourceResults(resource, request);
                    savedPath = savedPaths.FirstOrDefault();
                    resource = resource with { Status = "SAVED", SavedPath = savedPath };
                }
                catch (Exception exception)
                {
                    resource = resource with { Status = "FAILED" };
                    var failed = task with
                    {
                        Status = "FAILED",
                        Result = "RESOURCE 저장 실패: " + exception.Message,
                        FinishReason = "resource_save_failed",
                        CompletedAt = DateTimeOffset.UtcNow,
                        Resource = resource,
                        SavedPath = null,
                        SavedPaths = null
                    };
                    ReplaceTask(failed);
                    SaveState();
                    TaskChanged?.Invoke(failed);
                    return new BridgeResponse(true, failed);
                }
            }
            else if (!request.Success && resource is not null)
            {
                resource = resource with { Status = "FAILED" };
            }

            var completed = task with
            {
                Status = request.Success ? "COMPLETED" : "FAILED",
                Result = request.ResponseText ?? request.Result,
                FinishReason = request.FinishReason ?? (request.Success ? "completed" : "failed"),
                CompletedAt = DateTimeOffset.UtcNow,
                Resource = resource,
                SavedPath = savedPath,
                SavedPaths = savedPaths
            };
            ReplaceTask(completed);
            SaveState();
            TaskChanged?.Invoke(completed);
            return new BridgeResponse(true, completed);
        }
    }
    private BridgeResponse Reset(ResetRequest request)
    {
        lock (_gate)
        {
            var task = _state.Tasks
                .Where(item => item.Status is "PENDING" or "CLAIMED")
                .Where(item => string.IsNullOrWhiteSpace(request.ConversationId) ||
                               item.ConversationId.Equals(request.ConversationId, StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(request.TaskId) || item.Id == request.TaskId)
                .OrderByDescending(item => item.ClaimedAt ?? item.CreatedAt)
                .FirstOrDefault();

            if (task is null)
                return new BridgeResponse(true, new { reset = false });

            var canceled = task with
            {
                Status = "FAILED",
                Result = "확장 업데이트로 작업 상태가 초기화되었습니다.",
                FinishReason = "extension_reset",
                CompletedAt = DateTimeOffset.UtcNow
            };
            ReplaceTask(canceled);
            SaveState();
            TaskChanged?.Invoke(canceled);
            return new BridgeResponse(true, new { reset = true, taskId = canceled.Id, status = canceled.Status });
        }
    }
    private BridgeResponse Heartbeat(HeartbeatRequest request)
    {
        lock (_gate)
        {
            _lastWebHeartbeat = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.ConversationId))
            {
                _webHeartbeats[request.ConversationId] = _lastWebHeartbeat.Value;
                if (!string.IsNullOrWhiteSpace(request.ConversationTitle))
                    _webConversationTitles[request.ConversationId] = request.ConversationTitle;
                if (!string.IsNullOrWhiteSpace(request.ExtensionVersion))
                    _webExtensionVersions[request.ConversationId] = request.ExtensionVersion;
                if (!string.IsNullOrWhiteSpace(request.ExtensionBuild))
                    _webExtensionBuilds[request.ConversationId] = request.ExtensionBuild;
            }
            _webConversationId = request.ConversationId;
            _webConversationTitle = request.ConversationTitle;
            _webProjectId = request.ProjectId;
            _webExtensionVersion = request.ExtensionVersion;
            _webExtensionBuild = request.ExtensionBuild;
        }
        return new BridgeResponse(true, new { worker = "ProjectHub Worker", repository = RepositoryName, client = request.Client ?? "extension", conversationId = request.ConversationId, conversationTitle = request.ConversationTitle, projectId = request.ProjectId, extensionVersion = request.ExtensionVersion, extensionBuild = request.ExtensionBuild, expectedExtensionVersion = ExpectedExtensionVersion, expectedExtensionBuild = ExpectedExtensionBuild, extensionSynchronized = WebExtensionSynchronized, timestamp = _lastWebHeartbeat, status = "ready" });
    }

    private BridgeResponse Progress(ProgressRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId) || string.IsNullOrWhiteSpace(request.ConversationId) || string.IsNullOrWhiteSpace(request.Stage))
            return new(false, new { error = "progress_fields_required" });
        lock (_gate)
        {
            var task = _state.Tasks.FirstOrDefault(item => item.Id == request.TaskId);
            if (task is null) return new(false, new { error = "task_not_found" });
            if (!task.ConversationId.Equals(request.ConversationId, StringComparison.OrdinalIgnoreCase))
                return new(false, new { error = "conversation_mismatch" });
            if (task.Status != "CLAIMED") return new(false, new { error = "task_not_claimed" });
            if (string.IsNullOrWhiteSpace(request.LeaseId) || !string.Equals(task.LeaseId, request.LeaseId, StringComparison.Ordinal))
                return new(false, new { error = "lease_mismatch" });
            var progress = new ExtensionProgress(task.Id, task.ConversationId, request.Stage.Trim(), request.Detail?.Trim(), request.Attempt, DateTimeOffset.UtcNow);
            _extensionProgress = progress;
            ExtensionProgressChanged?.Invoke(progress);
            return new BridgeResponse(true, progress);
        }
    }

    private static string NormalizeRole(string role) => role.Trim().ToUpperInvariant();

    private static List<string> SaveResourceResults(ResourceRequest resource, ResultRequest request)
    {
        if (!resource.Type.Equals("RESOURCE", StringComparison.OrdinalIgnoreCase) &&
            !ResourceTransportContract.IsSupportedType(resource.Type))
            throw new InvalidOperationException("RESOURCE_TYPE_UNSUPPORTED");

        var payloads = request.ResultFiles?.Where(file => !string.IsNullOrWhiteSpace(file.Base64)).ToList()
            ?? new List<ResourceResultFile>();
        if (payloads.Count == 0 && !string.IsNullOrWhiteSpace(request.ResultFileBase64))
            payloads.Add(new ResourceResultFile(
                request.ResultFileBase64,
                request.ResultFileMimeType ?? "application/octet-stream",
                request.ResultFileName));
        if (payloads.Count == 0)
            throw new InvalidOperationException("RESOURCE_DATA_MISSING");
        if (payloads.Count > 32)
            throw new InvalidOperationException("RESOURCE_FILE_COUNT_INVALID");

        var root = Path.GetFullPath(resource.WorkspaceRoot);
        var relativeDirectory = resource.TargetDirectory.Replace('/', Path.DirectorySeparatorChar);
        var targetDirectory = Path.GetFullPath(Path.Combine(root, relativeDirectory));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!targetDirectory.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !targetDirectory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("RESOURCE_TARGET_OUTSIDE_WORKSPACE");

        var decoded = new List<(byte[] Bytes, string FileName)>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        for (var index = 0; index < payloads.Count; index++)
        {
            var payload = payloads[index];
            var bytes = Convert.FromBase64String(payload.Base64);
            if (bytes.Length == 0 || bytes.Length > 25 * 1024 * 1024)
                throw new InvalidOperationException("RESOURCE_FILE_SIZE_INVALID");
            totalBytes += bytes.Length;
            if (totalBytes > 128L * 1024 * 1024)
                throw new InvalidOperationException("RESOURCE_TOTAL_SIZE_INVALID");

            var extension = ResourceFileExtension(payload.MimeType, payload.FileName);
            var fileName = ResourceFileName(payload.FileName, extension, index + 1, usedNames);
            decoded.Add((bytes, fileName));
        }

        Directory.CreateDirectory(targetDirectory);
        var paths = decoded
            .Select(item => Path.Combine(targetDirectory, item.FileName))
            .ToList();
        var tempPaths = paths.Select(path => path + ".tmp").ToList();
        try
        {
            for (var index = 0; index < decoded.Count; index++)
                File.WriteAllBytes(tempPaths[index], decoded[index].Bytes);
            for (var index = 0; index < paths.Count; index++)
                File.Move(tempPaths[index], paths[index], true);
            return paths;
        }
        catch
        {
            foreach (var path in tempPaths.Concat(paths))
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch { }
            }
            throw;
        }
    }

    private static string ResourceFileName(string? candidate, string extension, int index, ISet<string> usedNames)
    {
        var original = Path.GetFileName(candidate ?? string.Empty);
        var stem = Path.GetFileNameWithoutExtension(original);
        if (string.IsNullOrWhiteSpace(stem))
            stem = $"resource-{index:D2}";

        var invalid = Path.GetInvalidFileNameChars();
        stem = new string(stem
            .Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character)
            .ToArray())
            .Trim()
            .Trim('.');
        if (string.IsNullOrWhiteSpace(stem))
            stem = $"resource-{index:D2}";

        if (stem.Length > 120)
            stem = stem[..120];

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        if (reserved.Contains(stem))
            stem = "_" + stem;

        var fileName = stem + extension;
        var suffix = 2;
        while (!usedNames.Add(fileName))
            fileName = $"{stem}-{suffix++}{extension}";
        return fileName;
    }

    private static string ResourceFileExtension(string? mimeType, string? fileName)
    {
        var normalized = mimeType?.Trim();
        var separator = normalized?.IndexOf(';') ?? -1;
        if (separator >= 0)
            normalized = normalized![..separator].Trim();
        normalized = normalized?.ToLowerInvariant();
        var mapped = normalized switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/svg+xml" => ".svg",
            "audio/mpeg" => ".mp3",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/ogg" => ".ogg",
            "audio/flac" => ".flac",
            "audio/mp4" => ".m4a",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "application/pdf" => ".pdf",
            "application/zip" => ".zip",
            "application/json" => ".json",
            "text/plain" => ".txt",
            "text/markdown" => ".md",
            "text/csv" => ".csv",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
            _ => null
        };
        if (mapped is not null)
            return mapped;

        var candidate = Path.GetExtension(Path.GetFileName(fileName ?? string.Empty));
        if (candidate.Length is > 1 and <= 16 &&
            candidate.Skip(1).All(character => char.IsLetterOrDigit(character)))
            return candidate.ToLowerInvariant();

        return ".bin";
    }

    private void ReplaceTask(BridgeTask task)
    {
        var index = _state.Tasks.FindIndex(item => item.Id == task.Id);
        _state.Tasks[index] = task;
    }

    private BridgeState LoadState()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                return JsonSerializer.Deserialize<BridgeState>(File.ReadAllText(_statePath), _jsonOptions) ?? new BridgeState();
            }
        }
        catch (JsonException) { }
        return new BridgeState();
    }

    private void SaveState()
    {
        var temp = _statePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_state, _jsonOptions), Encoding.UTF8);
        File.Move(temp, _statePath, true);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException();
    }

    private static async Task WriteAttachmentAsync(HttpListenerResponse response, string id)
    {
        if (id.Any(character => !char.IsLetterOrDigit(character))) { response.StatusCode = 404; return; }
        var directory = WorkerPaths.Attachments;
        var path = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, id + ".*").SingleOrDefault()
            : null;
        if (path is null || !File.Exists(path)) { response.StatusCode = 404; return; }
        var bytes = await File.ReadAllBytesAsync(path);
        response.ContentType = GetMimeType(Path.GetExtension(path));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }
    private static string GetMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".flac" => "audio/flac",
        ".m4a" => "audio/mp4",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        ".json" => "application/json",
        ".csv" => "text/csv",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/octet-stream"
    };
    private async Task WriteJsonAsync(HttpListenerResponse response, object payload)
    {
        response.ContentType = "application/json; charset=utf-8";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _listener.Close();
    }
}

public sealed class BridgeState
{
    public Dictionary<string, BindingState> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RoleBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<BridgeTask> Tasks { get; set; } = new();
}

public sealed record BindingState(string ConversationId, string ProjectId, DateTimeOffset UpdatedAt);
public sealed record WebRoleBindingStatus(string Role, bool Bound, bool Connected, bool ExtensionSynchronized, string? ConversationId, string? ConversationTitle);
public sealed record ResourceRequest(string Id, string Type, string Prompt, string TargetDirectory, string TargetFileName, string RequestedBy, string Status, string? SavedPath, string WorkspaceRoot);
public sealed record BridgeTask(string Id, string ConversationId, string ProjectId, string Prompt, string Status, string? Result, DateTimeOffset? ClaimedAt, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string Owner = "WEB", string? LeaseId = null, DateTimeOffset? StartedAt = null, string? FinishReason = null, List<BridgeAttachment>? Attachments = null, ResourceRequest? Resource = null, string? SavedPath = null, string? ClaimedBy = null, List<string>? SavedPaths = null);
public sealed record BridgeAttachment(string Id, string FileName, string MimeType, long Size, string? DownloadUrl = null);
public sealed record BridgeResponse(bool Ok, object Data);
public sealed record BindRequest(string ConversationId, string? ProjectId, string? Role = null);
public sealed record ClaimRequest(string ConversationId);
public sealed record ResetRequest(string? ConversationId = null, string? TaskId = null);
public sealed record CreateTaskRequest(string ConversationId, string Prompt, string? ProjectId, List<BridgeAttachment>? Attachments = null, string? Role = null, ResourceRequest? Resource = null);
public sealed record ResourceResultFile(string Base64, string MimeType, string? FileName = null);
public sealed record ResultRequest(bool Success = true, string? Result = null, string? TaskId = null, string? ConversationId = null, string? ResponseText = null, string? ResultType = "TEXT_RESULT", DateTimeOffset? CompletedAt = null, string? LeaseId = null, string? FinishReason = null, string? ResultFileBase64 = null, string? ResultFileMimeType = null, string? ResultFileName = null, List<ResourceResultFile>? ResultFiles = null);
public sealed record HeartbeatRequest(string? Client, string? ConversationId = null, string? ProjectId = null, string? ConversationTitle = null, string? ExtensionVersion = null, string? ExtensionBuild = null);
public sealed record ProgressRequest(string TaskId, string ConversationId, string LeaseId, string Stage, string? Detail = null, int Attempt = 0);
public sealed record ExtensionProgress(string TaskId, string ConversationId, string Stage, string? Detail, int Attempt, DateTimeOffset UpdatedAt);
