using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectHub.Worker;

public sealed class BridgeServer : IDisposable
{
    private const string Prefix = "http://127.0.0.1:43821/";
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
            object payload;

            if (method == "GET" && path == "/bridge/status") payload = Status();
            else if (method == "GET" && path == "/bridge/projects") payload = Projects();
            else if (method == "GET" && path.StartsWith("/bridge/bindings/", StringComparison.Ordinal))
                payload = Binding(Uri.UnescapeDataString(path["/bridge/bindings/".Length..]));
            else if (method == "POST" && path == "/bridge/bind") payload = Bind(await ReadJsonAsync<BindRequest>(context.Request));
            else if (method == "GET" && path == "/bridge/task") payload = PendingTask();
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
                version = "0.1.0",
                bridge = "ready",
                loopback = true,
                activeTask = _state.Tasks.FirstOrDefault(task => task.Status is "PENDING" or "CLAIMED")
            });
        }
    }

    private BridgeResponse Projects() => new(true, new[]
    {
        new { id = "MCP-with-MiniPC", name = "MCP-with-MiniPC", path = Environment.CurrentDirectory }
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
            _state.Bindings[request.ConversationId] = new BindingState(request.ConversationId, request.ProjectId ?? "MCP-with-MiniPC", DateTimeOffset.UtcNow);
            SaveState();
            return new BridgeResponse(true, _state.Bindings[request.ConversationId]);
        }
    }

    private BridgeResponse PendingTask()
    {
        lock (_gate)
        {
            return new BridgeResponse(true, new { task = _state.Tasks.FirstOrDefault(task => task.Status == "PENDING") });
        }
    }

    private BridgeResponse CreateTask(CreateTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId) || string.IsNullOrWhiteSpace(request.Prompt))
            return new(false, new { error = "conversation_id_and_prompt_required" });
        lock (_gate)
        {
            var task = new BridgeTask(Guid.NewGuid().ToString("N"), request.ConversationId, request.ProjectId ?? "MCP-with-MiniPC", request.Prompt, "PENDING", null, null, DateTimeOffset.UtcNow, null);
            _state.Tasks.Add(task);
            SaveState();
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
            var claimed = task with { Status = "CLAIMED", ClaimedAt = DateTimeOffset.UtcNow };
            ReplaceTask(claimed);
            SaveState();
            return new BridgeResponse(true, claimed);
        }
    }

    private BridgeResponse SubmitResult(string id, ResultRequest request)
    {
        lock (_gate)
        {
            var task = _state.Tasks.FirstOrDefault(item => item.Id == id);
            if (task is null) return new(false, new { error = "task_not_found" });
            var completed = task with { Status = request.Success ? "COMPLETED" : "FAILED", Result = request.Result, CompletedAt = DateTimeOffset.UtcNow };
            ReplaceTask(completed);
            SaveState();
            return new BridgeResponse(true, completed);
        }
    }

    private BridgeResponse Heartbeat(HeartbeatRequest request)
        => new(true, new { worker = "ProjectHub Worker", client = request.Client ?? "extension", timestamp = DateTimeOffset.UtcNow, status = "ready" });

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
public sealed record BridgeTask(string Id, string ConversationId, string ProjectId, string Prompt, string Status, string? Result, DateTimeOffset? ClaimedAt, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record BridgeResponse(bool Ok, object Data);
public sealed record BindRequest(string ConversationId, string? ProjectId);
public sealed record CreateTaskRequest(string ConversationId, string Prompt, string? ProjectId);
public sealed record ResultRequest(bool Success, string? Result);
public sealed record HeartbeatRequest(string? Client);