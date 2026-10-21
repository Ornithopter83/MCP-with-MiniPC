using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace ProjectHub.Worker;

// A separate, unguessable token is issued for each SV and temporary WORK invocation.
internal sealed record WorkerMcpSession(
    string Role, string JobId, string ProjectRoot, CancellationToken Cancellation)
{
    internal string InvocationId { get; } = Guid.NewGuid().ToString("N");
    internal IReadOnlyList<string> AllowedPaths { get; init; } = Array.Empty<string>();
    internal int Initialized;
    internal int ToolsListed;
    internal int ToolsCalled;
    internal int SuccessfulDelegations;
    internal bool IsWork => Role == "WORK";
}

public sealed partial class BridgeServer
{
    internal const string WorkerMcpUrl = "http://127.0.0.1:43821/mcp";
    private readonly ConcurrentDictionary<string, WorkerMcpSession> _mcpSessions =
        new(StringComparer.Ordinal);

    internal Func<WorkerMcpSession, string, JsonElement, CancellationToken, Task<string>>?
        McpToolExecutor { get; set; }
    internal Action<string, string>? McpActivity { get; set; }

    internal static IReadOnlyList<string> CodexMcpOverrides() =>
    [
        "mcp_servers.projecthub.url=\"" + WorkerMcpUrl + "\"",
        "mcp_servers.projecthub.bearer_token_env_var=\"PROJECTHUB_MCP_BEARER\"",
        "mcp_servers.projecthub.required=true",
        "mcp_servers.projecthub.startup_timeout_sec=15",
        "mcp_servers.projecthub.tool_timeout_sec=720"
    ];

    internal string RegisterMcpSession(
        string role, string jobId, string projectRoot, CancellationToken cancellation,
        IReadOnlyList<string>? allowedPaths = null)
    {
        if (role is not ("SV" or "WORK"))
            throw new ArgumentException("Only SV and temporary WORK have delegated MCP access.");
        if (role == "WORK" && (allowedPaths is null || allowedPaths.Count == 0))
            throw new ArgumentException("WORK_PATHS_REQUIRED");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _mcpSessions[token] = new WorkerMcpSession(
            role, jobId, Path.GetFullPath(projectRoot), cancellation)
        {
            AllowedPaths = allowedPaths?.ToArray() ?? Array.Empty<string>()
        };
        McpActivity?.Invoke(role, "SESSION_REGISTERED");
        return token;
    }

    internal int SuccessfulDelegationCount(string token) =>
        _mcpSessions.TryGetValue(token, out var s)
            ? Volatile.Read(ref s.SuccessfulDelegations) : 0;

    internal void UnregisterMcpSession(string token)
    {
        if (!_mcpSessions.TryRemove(token, out var s)) return;
        McpActivity?.Invoke(s.Role, "SESSION_FINISHED calls=" +
            Volatile.Read(ref s.ToolsCalled) + " completed=" +
            Volatile.Read(ref s.SuccessfulDelegations));
    }

    private async Task HandleMcpAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";
        // The MCP endpoint has no CORS capability, and never accepts browser Origins.
        response.Headers.Remove("Access-Control-Allow-Origin");
        if (!string.IsNullOrEmpty(request.Headers["Origin"]))
        {
            response.StatusCode = 403;
            return;
        }
        var authorization = request.Headers["Authorization"];
        var token = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authorization["Bearer ".Length..] : null;
        if (token is null || !_mcpSessions.TryGetValue(token, out var session) ||
            session.Cancellation.IsCancellationRequested)
        {
            response.StatusCode = 401;
            return;
        }
        if (request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            return;
        }

        using var json = await JsonDocument.ParseAsync(request.InputStream)
            .ConfigureAwait(false);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("method", out var methodElement) ||
            methodElement.ValueKind != JsonValueKind.String)
        {
            response.StatusCode = 400;
            return;
        }
        // MCP notifications do not have JSON-RPC responses.
        if (!root.TryGetProperty("id", out var id))
        {
            response.StatusCode = 202;
            return;
        }
        object output;
        switch (methodElement.GetString())
        {
            case "initialize":
                Interlocked.Exchange(ref session.Initialized, 1);
                output = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "projecthub-worker", version = "1.0.0" },
                    instructions = session.IsWork
                        ? "Complete only your bounded task within authorized paths. Never delegate and never commit or push."
                        : "SV implements the milestone and may delegate independent tasks using delegate_work. Integrate all results before reporting to HIGH. Git finalization belongs to Worker."
                };
                break;
            case "ping":
                output = new { };
                break;
            case "tools/list":
                Interlocked.Exchange(ref session.ToolsListed, 1);
                output = new { tools = ToolDefinitions(session.IsWork) };
                break;
            case "tools/call":
            {
                if (!root.TryGetProperty("params", out var parameters) ||
                    parameters.ValueKind != JsonValueKind.Object ||
                    !parameters.TryGetProperty("name", out var toolElement) ||
                    toolElement.ValueKind != JsonValueKind.String ||
                    toolElement.GetString() != "delegate_work" || session.IsWork)
                {
                    await WriteJsonAsync(response, JsonRpcError(id, -32601, "Tool unavailable."))
                        .ConfigureAwait(false);
                    return;
                }
                Interlocked.Increment(ref session.ToolsCalled);
                var args = parameters.TryGetProperty("arguments", out var a) ? a : default;
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation);
                linked.CancelAfter(TimeSpan.FromMinutes(12));
                var failed = false;
                string message;
                try
                {
                    var handler = McpToolExecutor ??
                        throw new InvalidOperationException("MCP_EXECUTOR_NOT_CONFIGURED");
                    // Deliberately NO parent-wide serialization: only overlapping paths lock.
                    message = await handler(session, "delegate_work", args, linked.Token)
                        .ConfigureAwait(false);
                    Interlocked.Increment(ref session.SuccessfulDelegations);
                }
                catch (OperationCanceledException)
                {
                    failed = true;
                    message = "WORK_CANCELED_OR_TIMEOUT";
                }
                catch (Exception error)
                {
                    failed = true;
                    message = error.GetType().Name + ": " + error.Message;
                }
                McpActivity?.Invoke(session.Role, (failed ? "TOOL_FAILED" : "TOOL_DONE") +
                    " delegate_work");
                output = new
                {
                    content = new[] { new { type = "text", text = message } },
                    isError = failed
                };
                break;
            }
            default:
                await WriteJsonAsync(response, JsonRpcError(id, -32601, "Unknown method."))
                    .ConfigureAwait(false);
                return;
        }
        await WriteJsonAsync(response, new { jsonrpc = "2.0", id, result = output })
            .ConfigureAwait(false);
    }

    private static object JsonRpcError(JsonElement id, int code, string message) =>
        new { jsonrpc = "2.0", id, error = new { code, message } };

    internal static object[] ToolDefinitions(bool workOnly = false) =>
        workOnly ? Array.Empty<object>() : new object[]
        {
            new
            {
                name = "delegate_work",
                description = "Delegate one bounded implementation, investigation or test task to a fresh WORK session. Calls with nonoverlapping write paths can run concurrently. Results return only to SV.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        task = new { type = "string", description = "Focused task, up to 2000 characters." },
                        paths = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "One to five project-relative files or directories. No root or protected metadata."
                        }
                    },
                    required = new[] { "task", "paths" },
                    additionalProperties = false
                }
            }
        };
}
