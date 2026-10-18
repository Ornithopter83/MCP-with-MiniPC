using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace ProjectHub.Worker;

// Each parent/WORK invocation receives its own per-session MCP token.
// WORK exposes execution commands only; parent delegation remains private to its role.
internal sealed record WorkerMcpSession(
    string Role, string JobId, string ProjectRoot, CancellationToken Cancellation)
{
    internal SemaphoreSlim SerialCalls { get; } = new(1, 1);
    internal int Initialized;
    internal int ToolsListed;
    internal int ToolsCalled;
    internal int SuccessfulDelegations;
    internal byte[]? NativeScreenshot;
    internal string InvocationId { get; } = Guid.NewGuid().ToString("N");
    internal IReadOnlyList<string> AllowedPaths { get; init; } = Array.Empty<string>();
    // Direct Editor-lane WORK already owns the scheduler's gate. A nested MCP
    // request must not acquire the same non-reentrant semaphore again.
    internal bool EditorLaneHeld { get; init; }
    internal bool IsWork => Role == "WORK";
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
        string role, string jobId, string projectRoot, CancellationToken cancellation,
        IReadOnlyList<string>? allowedPaths = null, bool editorLaneHeld = false)
    {
        if (role is not ("DESIGN" or "HIGH" or "ASSET_MANAGER" or "WORK"))
            throw new ArgumentException("Only specialist or WORK roles can expose Worker MCP tools.");
        if (role == "WORK" && (allowedPaths is null || allowedPaths.Count == 0))
            throw new ArgumentException("WORK_MCP_REQUIRES_NARROW_PATHS");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _mcpSessions[token] = new WorkerMcpSession(
            role, jobId, Path.GetFullPath(projectRoot), cancellation)
        {
            AllowedPaths = allowedPaths?.ToArray() ?? Array.Empty<string>(),
            EditorLaneHeld = editorLaneHeld
        };
        McpActivity?.Invoke(role, "SESSION_REGISTERED (not yet connected)");
        return token;
    }

    internal void UnregisterMcpSession(string token)
    {
        if (!_mcpSessions.TryRemove(token, out var session)) return;
        McpActivity?.Invoke(session.Role,
            "SESSION_FINISHED initialized=" + (Volatile.Read(ref session.Initialized) != 0) +
            " tools_listed=" + (Volatile.Read(ref session.ToolsListed) != 0) +
            " tool_calls=" + Volatile.Read(ref session.ToolsCalled) +
            " delegate_work_completed=" + Volatile.Read(ref session.SuccessfulDelegations));
    }

    internal int SuccessfulDelegationCount(string token) =>
        _mcpSessions.TryGetValue(token, out var session)
            ? Volatile.Read(ref session.SuccessfulDelegations)
            : 0;

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
                    instructions = session.IsWork
                        ? "WORK executes only its assigned bounded task. Unity CLI, Editor commands, capture and build are available through worker_command. Follow the declared file paths and never delegate or choose other tasks. No HQ number is created for specialist-owned WORK."
                        : "Specialists decide scope, delegate bounded execution through delegate_work and judge results. worker_command also provides Unity commands. Specialist-delegated WORK remains private to its parent; HQ receives no new WORK number.",
                };
                break;
            case "ping":
                result = new { };
                break;
            case "tools/list":
                Interlocked.Exchange(ref session.ToolsListed, 1);
                McpActivity?.Invoke(session.Role,
                    session.IsWork ? "TOOLS_LISTED worker_command" :
                        "TOOLS_LISTED delegate_work, worker_command");
                result = new { tools = ToolDefinitions(session.IsWork) };
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
                if (toolName is not ("delegate_work" or "worker_command") ||
                    (session.IsWork && toolName == "delegate_work"))
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
                byte[]? screenshot = null;
                try
                {
                    await session.SerialCalls.WaitAsync(linked.Token).ConfigureAwait(false);
                    held = true;
                    var handler = McpToolExecutor ??
                        throw new InvalidOperationException("Worker MCP handler unavailable");
                    session.NativeScreenshot = null;
                    output = await handler(session, toolName, arguments, linked.Token)
                        .ConfigureAwait(false);
                    screenshot = session.NativeScreenshot;
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
                    session.NativeScreenshot = null;
                    if (held) session.SerialCalls.Release();
                }
                if (!failed && toolName == "delegate_work")
                    Interlocked.Increment(ref session.SuccessfulDelegations);
                McpActivity?.Invoke(session.Role,
                    (failed ? "TOOL_FAILED " : "TOOL_COMPLETED ") + toolName);
                if (failed) screenshot = null;
                result = new
                {
                    content = screenshot is { Length: > 0 }
                        ? new object[]
                        {
                            new { type = "text", text = output },
                            new { type = "image", mimeType = "image/png",
                                data = Convert.ToBase64String(screenshot) }
                        }
                        : new object[] { new { type = "text", text = output } },
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

    internal static object[] ToolDefinitions(bool workOnly = false) =>
        workOnly ? new object[] { WorkerCommandDefinition() } :
        new object[] { DelegateWorkDefinition(), WorkerCommandDefinition() };

    private static object DelegateWorkDefinition() =>
        new
        {
            name = "delegate_work",
            description = "MANDATORY for DESIGN, HIGH and ASSET MANAGER: delegate ONE bounded deliverable or read-only check per call. Split large UI implementation, capture and audit into successive small delegations; never combine them in one 720-second tool call. Uses the configured WORK model; no HQ WORK number.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    task = new { type = "string", description = "One focused deliverable (up to 2000 characters). Do not combine asset production, PlayMode capture, build and report audit in the same call." },
                    paths = new { type = "array", items = new { type = "string" }, description = "One to five precise write scopes relative to the project, each a file or a narrow subdirectory under Assets/ or docs/ (e.g. docs/projecthub/m110-environment/environment-audit.md). Do not use Assets/, docs/, project root or absolute paths. Read-only investigation still requires a specific report path." }
                },
                required = new[] { "task", "paths" },
                additionalProperties = false
            }
        };

    private static object WorkerCommandDefinition() =>
        new
        {
            name = "worker_command",
            description = "Execute Editor operations through the existing Editor's precompiled official-API bridge by default; initial project bootstrap auto-installs the receiver without GUI focus or elevation. scene_status inspects loaded scenes; sync_scene backs up the current active scene and reloads it from disk (requires explicitly scoped scene and backup paths for WORK); refresh_assets performs AssetDatabase.Refresh then reports import/scene state. Legacy arbitrary editor_eval uses verified CLI only; no guessed fallback for uncertain mutations.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    action = new { type = "string", @enum = new[] { "status", "recompile", "unity_query", "command_catalog", "editor_eval", "editor_bridge_install", "editor_bridge_ping", "editor_bridge_scene_status", "editor_bridge_sync_scene", "editor_bridge_refresh_assets", "editor_bridge_invoke", "editor_bridge_menu", "native_capture", "visual_foundry" } },
                    command = new { type = "string", description = "For unity_query only: editor_state (via CLI status), or a read-only command present in the live command_catalog." },
                    expression = new { type = "string", description = "For editor_eval: specialist-selected Editor C# expression, max 5000 characters. For a build use: return QuizStandaloneBuild.BuildQuizStandalone().summary.result.ToString();" },
                    expected_result = new { type = "string", description = "Required for editor_eval/editor_bridge_invoke exact return-value assertion. Optional for editor_bridge_menu: menu success is True (handled); BuildReport Succeeded is NOT a menu return and must be validated separately." },
                    type = new { type = "string", description = "For editor_bridge_invoke: exact C# type name, including namespace if applicable (e.g. QuizStandaloneBuild)." },
                    method = new { type = "string", description = "For editor_bridge_invoke: public static zero-argument method name (e.g. BuildQuizStandalone)." },
                    menu_path = new { type = "string", description = "For editor_bridge_menu: complete Unity menu item path as registered with MenuItem." },
                    scene_path = new { type = "string", description = "For editor_bridge_sync_scene only: exact currently ACTIVE Assets/.../*.unity path. Requires disk snapshot and, if dirty, a SaveAsCopy scene backup before reloading. This can discard current in-memory changes after backup; never call merely to inspect state." },
                    style = new { type = "string", @enum = new[] { "neon", "arcane", "luxury", "playful" }, description = "For visual_foundry: explicit DESIGN-selected theme." },
                    layout = new { type = "string", @enum = new[] { "Base", "Editorial", "Immersive" }, description = "For visual_foundry: explicit DESIGN-selected layout." },
                    description = new { type = "string", description = "For visual_foundry: DESIGN's explicit brief, never interpreted for automatic activation." }
                },
                required = new[] { "action" },
                additionalProperties = false
            }
        };
}
