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
    private string? _webConversationId;
    private string? _webConversationTitle;
    private string? _webProjectId;
    public bool WebConnected
    {
        get { lock (_gate) return _lastWebHeartbeat is not null && DateTimeOffset.UtcNow - _lastWebHeartbeat < TimeSpan.FromSeconds(10); }
    }

    public string? WebConversationTitle
    {
        get { lock (_gate) return _webConversationTitle; }
    }

    public event Action<BridgeTask>? TaskChanged;

    public BridgeTask? CreateTaskForLatestBinding(string prompt, List<BridgeAttachment>? attachments = null)
    {
        lock (_gate)
        {
            BindingState? binding = null;
            if (!string.IsNullOrWhiteSpace(_webConversationId))
                _state.Bindings.TryGetValue(_webConversationId, out binding);
            binding ??= _state.Bindings.Values.OrderByDescending(item => item.UpdatedAt).FirstOrDefault();
            if (binding is null) return null;
            var active = _state.Tasks.FirstOrDefault(item => item.ConversationId.Equals(binding.ConversationId, StringComparison.OrdinalIgnoreCase) && (item.Status is "PENDING" or "CLAIMED"));
            if (active is not null) return active;
            var task = new BridgeTask(Guid.NewGuid().ToString("N"), binding.ConversationId, binding.ProjectId, prompt, "PENDING", null, null, DateTimeOffset.UtcNow, null, "WEB", null, null, null, attachments ?? new());
            _state.Tasks.Add(task);
            SaveState();
            TaskChanged?.Invoke(task);
            return task;
        }
    }

    public bool CancelActiveTask()
    {
        lock (_gate)
        {
            var task = _state.Tasks.FirstOrDefault(item => item.Status is "PENDING" or "CLAIMED");
            if (task is null) return false;
            var canceled = task with { Status = "FAILED", Result = "작업이 취소되었습니다.", FinishReason = "canceled", CompletedAt = DateTimeOffset.UtcNow };
            ReplaceTask(canceled);
            SaveState();
            TaskChanged?.Invoke(canceled);
            return true;
        }
    }
    public BridgeAttachment CreateTextImageAttachment(string text)
    {
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectHub", "Worker", "attachments");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, id + ".png");
        using var bitmap = new Bitmap(720, 240);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 28, FontStyle.Bold))
        using (var brush = new SolidBrush(Color.FromArgb(18, 36, 76)))
        {
            graphics.Clear(Color.White);
            graphics.DrawString(text, font, brush, new PointF(34, 92));
        }
        bitmap.Save(path, ImageFormat.Png);
        return new BridgeAttachment(id, id + ".png", "image/png", new FileInfo(path).Length, "http://127.0.0.1:43821/bridge/attachment/" + id);
    }

    public BridgeServer()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectHub", "Worker");
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
        _listener.Stop();
        if (_loop is not null)
        {
            try { await _loop; } catch (HttpListenerException) { }
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
                payload = Claim(path["/bridge/task/".Length..^"/claim".Length]);
            else if (method == "POST" && path.StartsWith("/bridge/task/", StringComparison.Ordinal) && path.EndsWith("/result", StringComparison.Ordinal))
                payload = SubmitResult(path["/bridge/task/".Length..^"/result".Length], await ReadJsonAsync<ResultRequest>(context.Request));
            else if (method == "POST" && path == "/bridge/heartbeat") payload = Heartbeat(await ReadJsonAsync<HeartbeatRequest>(context.Request));
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
                webProjectId = _webProjectId
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
            return new BridgeResponse(true, new { conversationId, bound = binding is not null, projectId = binding?.ProjectId, updatedAt = binding?.UpdatedAt });
        }
    }

    private BridgeResponse Bind(BindRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId)) return new(false, new { error = "conversation_id_required" });
        lock (_gate)
        {
            _state.Bindings[request.ConversationId] = new BindingState(request.ConversationId, request.ProjectId ?? RepositoryName, DateTimeOffset.UtcNow);
            SaveState();
            return new BridgeResponse(true, _state.Bindings[request.ConversationId]);
        }
    }

    private BridgeResponse PendingTask(string? conversationId)
    {
        lock (_gate)
        {
            return new BridgeResponse(true, new { task = _state.Tasks.Where(task => string.IsNullOrWhiteSpace(conversationId) || task.ConversationId.Equals(conversationId, StringComparison.OrdinalIgnoreCase)).OrderByDescending(task => task.CompletedAt ?? task.ClaimedAt ?? task.CreatedAt).FirstOrDefault() });
        }
    }

    private BridgeResponse CreateTask(CreateTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId) || string.IsNullOrWhiteSpace(request.Prompt))
            return new(false, new { error = "conversation_id_and_prompt_required" });
        lock (_gate)
        {
            var active = _state.Tasks.FirstOrDefault(item => item.ConversationId.Equals(request.ConversationId, StringComparison.OrdinalIgnoreCase) && (item.Status is "PENDING" or "CLAIMED"));
            if (active is not null) return new(false, new { error = "task_conflict", task = active });
            var task = new BridgeTask(Guid.NewGuid().ToString("N"), request.ConversationId, request.ProjectId ?? RepositoryName, request.Prompt, "PENDING", null, null, DateTimeOffset.UtcNow, null, "WEB", null, null, null, request.Attachments ?? new());
            _state.Tasks.Add(task);
            SaveState();
            TaskChanged?.Invoke(task);
            return new BridgeResponse(true, task);
        }
    }

    private BridgeResponse Claim(string id)
    {
        lock (_gate)
        {
            var task = _state.Tasks.FirstOrDefault(item => item.Id == id);
            if (task is null) return new(false, new { error = "task_not_found" });
            if (task.Status != "PENDING") return new(false, new { error = "task_not_pending", task });
            var active = _state.Tasks.FirstOrDefault(item => item.Id != id && item.ConversationId.Equals(task.ConversationId, StringComparison.OrdinalIgnoreCase) && (item.Status is "PENDING" or "CLAIMED"));
            if (active is not null) return new(false, new { error = "task_conflict", task = active });
            var now = DateTimeOffset.UtcNow;
            var claimed = task with { Status = "CLAIMED", ClaimedAt = now, StartedAt = now, Owner = "WEB", LeaseId = Guid.NewGuid().ToString("N") };
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
            if (!string.IsNullOrWhiteSpace(request.ConversationId) && !task.ConversationId.Equals(request.ConversationId, StringComparison.OrdinalIgnoreCase)) return new(false, new { error = "conversation_mismatch" });
            if (!string.IsNullOrWhiteSpace(request.LeaseId) && !string.Equals(task.LeaseId, request.LeaseId, StringComparison.Ordinal)) return new(false, new { error = "lease_mismatch" });
            var completed = task with { Status = request.Success ? "COMPLETED" : "FAILED", Result = request.ResponseText ?? request.Result, FinishReason = request.FinishReason ?? (request.Success ? "completed" : "failed"), CompletedAt = DateTimeOffset.UtcNow };
            ReplaceTask(completed);
            SaveState();
            TaskChanged?.Invoke(completed);
            return new BridgeResponse(true, completed);
        }
    }

    private BridgeResponse Heartbeat(HeartbeatRequest request)
    {
        lock (_gate)
        {
            _lastWebHeartbeat = DateTimeOffset.UtcNow;
            _webConversationId = request.ConversationId;
            _webConversationTitle = request.ConversationTitle;
            _webProjectId = request.ProjectId;
        }
        return new BridgeResponse(true, new { worker = "ProjectHub Worker", repository = RepositoryName, client = request.Client ?? "extension", conversationId = request.ConversationId, conversationTitle = request.ConversationTitle, projectId = request.ProjectId, timestamp = _lastWebHeartbeat, status = "ready" });
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
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectHub", "Worker", "attachments", id + ".png");
        if (!File.Exists(path)) { response.StatusCode = 404; return; }
        var bytes = await File.ReadAllBytesAsync(path);
        response.ContentType = "image/png";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }
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
    public List<BridgeTask> Tasks { get; set; } = new();
}

public sealed record BindingState(string ConversationId, string ProjectId, DateTimeOffset UpdatedAt);
public sealed record BridgeTask(string Id, string ConversationId, string ProjectId, string Prompt, string Status, string? Result, DateTimeOffset? ClaimedAt, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string Owner = "WEB", string? LeaseId = null, DateTimeOffset? StartedAt = null, string? FinishReason = null, List<BridgeAttachment>? Attachments = null);
public sealed record BridgeAttachment(string Id, string FileName, string MimeType, long Size, string? DownloadUrl = null);
public sealed record BridgeResponse(bool Ok, object Data);
public sealed record BindRequest(string ConversationId, string? ProjectId);
public sealed record CreateTaskRequest(string ConversationId, string Prompt, string? ProjectId, List<BridgeAttachment>? Attachments = null);
public sealed record ResultRequest(bool Success = true, string? Result = null, string? TaskId = null, string? ConversationId = null, string? ResponseText = null, string? ResultType = "TEXT_RESULT", DateTimeOffset? CompletedAt = null, string? LeaseId = null, string? FinishReason = null);
public sealed record HeartbeatRequest(string? Client, string? ConversationId = null, string? ProjectId = null, string? ConversationTitle = null);
