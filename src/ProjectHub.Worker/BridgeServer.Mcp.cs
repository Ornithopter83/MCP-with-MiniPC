using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace ProjectHub.Worker;

// Each specialist's tool access is scoped to its live invocation and its workspace.
// Tokens are never stored in project files or passed to temporary WORK children.
internal sealed record WorkerMcpSession(
    string Role, string JobId, string ProjectRoot, CancellationToken Cancellation)
{
    internal SemaphoreSlim SerialCalls { get; } = new(1, 1);
    internal int Initialized;
    internal int ToolsListed;
    internal int ToolsCalled;
}

public sealed partial class BridgeServer
{
    internal const string WorkerMcpUrl = "http://127.0.0.1:43821/mcp";
    private readonly ConcurrentDictionary<string, WorkerMcpSession> _mcpSessions =
        new(StringComparer.Ordinal);

    internal Func<WorkerMcpSession, string, JsonElement, CancellationToken, Task<string>>?
        McpToolExecutor { get; set; }
    // Metadata-only diagnostics. Never log bearer tokens, prompts or tool arguments.
    internal Action<string, string>? McpActivity { get; set; }

    internal static IReadOnlyList<string> CodexMcpOverrides() =>
    [
        "mcp_servers.projecthub.url=\"" + WorkerMcpUrl + "\"",
        "mcp_servers.projecthub.bearer_token_env_var=\"PROJECTHUB_MCP_BEARER\"",
        // One-shot codex exec must not silently omit a slow optional MCP server.
        "mcp_servers.projecthub.required=true",
        "mcp_servers.projecthub.startup_timeout_sec=15",
        "mcp_servers.projecthub.tool_timeout_sec=720"
    ];

    internal string RegisterMcpSession(
        string role, string jobId, string projectRoot, CancellationToken cancellation)
    {
        if (role is not ("DESIGN" or "HIGH" or "ASSET_MANAGER"))
            throw new ArgumentException("Only specialist roles can expose Worker MCP tools.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _mcpSessions[token] = new WorkerMcpSession(
            role, jobId, Path.GetFullPath(projectRoot), cancellation);
        McpActivity?.Invoke(role, "SESSION_REGISTERED (not yet connected)");
        return token;
    }

    internal void UnregisterMcpSession(string token)
    {
        if (!_mcpSessions.TryRemove(token, out var session)) return;
        McpActivity?.Invoke(session.Role,
            "SESSION_FINISHED initialized=" + (Volatile.Read(ref session.Initialized) != 0) +
            " tools_listed=" + (Volatile.Read(ref session.ToolsListed) != 0) +
            " tool_calls=" + Volatile.Read(ref session.ToolsCalled));
    }

    private async Task HandleMcpAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";
        // Do not turn this local, privileged protocol into a browser-accessible API.
        if (request.Headers["Origin"] is { Length: > 0 })
        {
            response.StatusCode = 403;
            return;
        }

        var bearer = request.Headers["Authorization"];
        var token = bearer?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? bearer["Bearer ".Length..] : null;
        if (token is null || !_mcpSessions.TryGetValue(token, out var session) ||
            session.Cancellation.IsCancellationRequested)
        {
            response.StatusCode = 401;
            return;
        }

        if (request.HttpMethod != "POST")
        {
            response.StatusCode = 405; // Streamable HTTP may omit optional GET/SSE.
            return;
        }

        using var document = await JsonDocument.ParseAsync(request.InputStream)
            .ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("method", out var methodElement) ||
            methodElement.ValueKind != JsonValueKind.String)
        {
            response.StatusCode = 400;
            return;
        }

        var method = methodElement.GetString();
        // MCP notifications intentionally have no JSON-RPC response.
        if (!root.TryGetProperty("id", out var requestId))
        {
            response.StatusCode = 202;
            return;
        }
        object result;
        switch (method)
        {
            case "initialize":
                Interlocked.Exchange(ref session.Initialized, 1);
                McpActivity?.Invoke(session.Role, "INITIALIZED");
                result = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "projecthub-unity-worker", version = "1.0.0" },
                    instructions = "Use delegate_work only for bounded edits. Use worker_command for Unity status, compile, and permitted read-only commands. These calls are internal to the current specialist role."
                };
                break;
            case "ping":
                result = new { };
                break;
            case "tools/list":
                Interlocked.Exchange(ref session.ToolsListed, 1);
                McpActivity?.Invoke(session.Role, "TOOLS_LISTED delegate_work, worker_command");
                result = new { tools = ToolDefinitions() };
                break;
            case "tools/call":
            {
                if (!root.TryGetProperty("params", out var parameters) ||
                    !parameters.TryGetProperty("name", out var toolNameElement) ||
                    toolNameElement.ValueKind != JsonValueKind.String)
                {
                    await WriteJsonAsync(response, JsonRpcError(requestId, -32602,
                        "Missing tool name.")).ConfigureAwait(false);
                    return;
                }
                var toolName = toolNameElement.GetString()!;
                if (toolName is not ("delegate_work" or "worker_command"))
                {
                    await WriteJsonAsync(response, JsonRpcError(requestId, -32601,
                        "Unknown tool.")).ConfigureAwait(false);
                    return;
                }
                Interlocked.Increment(ref session.ToolsCalled);
                McpActivity?.Invoke(session.Role, "TOOL_STARTED " + toolName);
                var arguments = parameters.TryGetProperty("arguments", out var input)
                    ? input : default;
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    session.Cancellation);
                linked.CancelAfter(TimeSpan.FromMinutes(12));
                var held = false;
                var failed = false;
                string output;
                try
                {
                    await session.SerialCalls.WaitAsync(linked.Token).ConfigureAwait(false);
                    held = true;
                    var handler = McpToolExecutor ??
                        throw new InvalidOperationException("Worker MCP handler unavailable");
                    output = await handler(session, toolName, arguments, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    output = "WORKER_MCP_CANCELED_OR_TIMEOUT";
                    failed = true;
                }
                catch (Exception error)
                {
                    output = error.GetType().Name + ": " + error.Message;
                    failed = true;
                }
                finally
                {
                    if (held) session.SerialCalls.Release();
                }
                McpActivity?.Invoke(session.Role,
                    (failed ? "TOOL_FAILED " : "TOOL_COMPLETED ") + toolName);
                result = new
                {
                    content = new[] { new { type = "text", text = output } },
                    isError = failed
                };
                break;
            }
            default:
                await WriteJsonAsync(response, JsonRpcError(requestId, -32601,
                    "Unknown JSON-RPC method.")).ConfigureAwait(false);
                return;
        }
        await WriteJsonAsync(response, new { jsonrpc = "2.0", id = requestId, result })
            .ConfigureAwait(false);
    }

    private static object JsonRpcError(JsonElement id, int code, string message) =>
        new { jsonrpc = "2.0", id, error = new { code, message } };

    private static object[] ToolDefinitions() =>
    [
        new
        {
            name = "delegate_work",
            description = "Run one short, bounded temporary WORK using the WORK role's configured provider, model and reasoning. No HQ WORK number or checkpoint; result returns to this specialist.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    task = new { type = "string", description = "Exact, self-contained edit instruction (up to 2000 characters)." },
                    paths = new { type = "array", items = new { type = "string" }, description = "One to five allowed relative files or directories beneath Assets/ or docs/." }
                },
                required = new[] { "task", "paths" },
                additionalProperties = false
            }
        },
        new
        {
            name = "worker_command",
            description = "Execute a mechanical Unity status, compile or allowlisted read-only Editor command through UnityWorker and return output. This is not a shell.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    action = new { type = "string", @enum = new[] { "status", "recompile", "unity_query" } },
                    command = new { type = "string", description = "For unity_query only: console, editor_state, list_open_scenes, get_scene_hierarchy or recompile_status." }
                },
                required = new[] { "action" },
                additionalProperties = false
            }
        }
    ];
}
