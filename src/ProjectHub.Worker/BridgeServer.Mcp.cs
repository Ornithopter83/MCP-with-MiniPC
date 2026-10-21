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
    internal bool ForbidWrites { get; init; }
    internal int Initialized;
    internal int ToolsListed;
    internal int ToolsCalled;
    internal int SuccessfulDelegations;
    internal int RecoverableDelegationFailures;
    internal int RejectedDelegations;
    internal int UnsafeDelegationFailures;
    internal ConcurrentQueue<DelegatedWorkEvidence> Evidence { get; } = new();
    internal SemaphoreSlim SerialCalls { get; } = new(1, 1);
    internal bool IsWork => Role == "WORK";
}

internal sealed record DelegatedWorkEvidence(
    string Invocation, string Status, string Detail);

internal sealed record DelegationMetrics(
    int Attempts, int Successful, int RecoverableFailures,
    int Rejected, int UnsafeFailures, bool ToolsListed,
    IReadOnlyList<DelegatedWorkEvidence> Evidence)
{
    internal static DelegationMetrics Empty { get; } =
        new(0, 0, 0, 0, 0, false, Array.Empty<DelegatedWorkEvidence>());
}

public sealed partial class BridgeServer
{
    internal const string WorkerMcpUrl = "http://127.0.0.1:43821/mcp";
    private readonly ConcurrentDictionary<string, WorkerMcpSession> _mcpSessions =
        new(StringComparer.Ordinal);

    internal Func<WorkerMcpSession, string, JsonElement, CancellationToken, Task<string>>?
        McpToolExecutor { get; set; }
    internal Action<string, string>? McpActivity { get; set; }

    // A denied request never started a WORK process. It must not erase
    // successful independent children, nor authorize direct-write fallback.
    internal static bool IsRejectedDelegation(Exception error) =>
        error.Message.StartsWith("WORK_PATH_INVALID_OR_CONFLICT", StringComparison.Ordinal) ||
        error.Message.StartsWith("WORK_TASK_", StringComparison.Ordinal) ||
        error.Message.StartsWith("WORK_READ_ONLY_INVALID", StringComparison.Ordinal);

    internal static bool IsRecoverableWorkFailure(Exception error) =>
        error is InvalidOperationException &&
        error.Message.StartsWith("TEMP_WORK_FAILED:", StringComparison.Ordinal);

    internal static IReadOnlyList<string> CodexMcpOverrides() =>
    [
        "mcp_servers.projecthub.url=\"" + WorkerMcpUrl + "\"",
        "mcp_servers.projecthub.bearer_token_env_var=\"PROJECTHUB_MCP_BEARER\"",
        "mcp_servers.projecthub.required=true",
        "mcp_servers.projecthub.startup_timeout_sec=15",
        "mcp_servers.projecthub.tool_timeout_sec=720",
        // Only the authenticated, path-scoped subagent tool is pre-approved.
        // Codex exec otherwise rejects mutating MCP calls with approval_policy=never.
        "mcp_servers.projecthub.tools.delegate_work.approval_mode=\"approve\""
    ];

    internal static bool CanRegisterRole(string role) =>
        role is "SV" or "HIGH" or "WORK";

    internal string RegisterMcpSession(
        string role, string jobId, string projectRoot, CancellationToken cancellation,
        IReadOnlyList<string>? allowedPaths = null, bool forbidWrites = false)
    {
        if (!CanRegisterRole(role))
            throw new ArgumentException("Only SV, HIGH and temporary WORK have MCP access.");
        if (role == "WORK" && (allowedPaths is null || allowedPaths.Count == 0))
            throw new ArgumentException("WORK_PATHS_REQUIRED");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _mcpSessions[token] = new WorkerMcpSession(
            role, jobId, Path.GetFullPath(projectRoot), cancellation)
        {
            AllowedPaths = allowedPaths?.ToArray() ?? Array.Empty<string>(),
            ForbidWrites = forbidWrites
        };
        McpActivity?.Invoke(role, "SESSION_REGISTERED");
        return token;
    }

    internal int SuccessfulDelegationCount(string token) =>
        GetDelegationMetrics(token).Successful;

    internal DelegationMetrics GetDelegationMetrics(string token) =>
        _mcpSessions.TryGetValue(token, out var session)
            ? new DelegationMetrics(
                Volatile.Read(ref session.ToolsCalled),
                Volatile.Read(ref session.SuccessfulDelegations),
                Volatile.Read(ref session.RecoverableDelegationFailures),
                Volatile.Read(ref session.RejectedDelegations),
                Volatile.Read(ref session.UnsafeDelegationFailures),
                Volatile.Read(ref session.ToolsListed) != 0,
                session.Evidence.ToArray())
            : DelegationMetrics.Empty;

    /// <summary>
    /// UnityWorker semantics: block the MCP tool call until a real child WORK
    /// has finished and return that child's output to the same parent session.
    /// SerialCalls protects one parent's overlapping MCP requests.
    /// </summary>
    internal async Task<string> ExecuteDelegatedWorkAsync(
        WorkerMcpSession session, JsonElement args, CancellationToken cancellationToken)
    {
        if (session.IsWork)
            throw new InvalidOperationException("WORK_CANNOT_DELEGATE");
        ValidateDelegationRequest(session, args);
        await session.SerialCalls.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            McpActivity?.Invoke(session.Role, "TOOL_STARTED delegate_work");
            var handler = McpToolExecutor ??
                throw new InvalidOperationException("MCP_EXECUTOR_NOT_CONFIGURED");
            var message = await handler(session, "delegate_work", args, cancellationToken)
                .ConfigureAwait(false);
            Interlocked.Increment(ref session.SuccessfulDelegations);
            var invocation = message.StartsWith("INVOCATION: ", StringComparison.Ordinal)
                ? message["INVOCATION: ".Length..].Split('\n')[0].Trim()
                : session.InvocationId;
            session.Evidence.Enqueue(new DelegatedWorkEvidence(
                invocation, "COMPLETED",
                MilestoneDefinitionContract.Limit(message, 5000)));
            return message;
        }
        finally
        {
            session.SerialCalls.Release();
        }
    }

    private static void ValidateDelegationRequest(
        WorkerMcpSession session, JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("task", out var task) ||
            task.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(task.GetString()) ||
            task.GetString()!.Trim().Length > 2000 ||
            !args.TryGetProperty("paths", out var declared) ||
            declared.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("WORK_TASK_OR_PATHS_INVALID");

        var paths = declared.EnumerateArray().ToArray();
        if (paths.Length is < 1 or > 5 || paths.Any(item =>
                item.ValueKind != JsonValueKind.String ||
                !TemporaryWorkScopeRegistry.IsAllowedPath(item.GetString() ?? "")))
            throw new InvalidOperationException("WORK_PATH_INVALID_OR_CONFLICT");

        if (args.TryGetProperty("read_only", out var readOnly) &&
            readOnly.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException("WORK_READ_ONLY_INVALID");

        if (session.Role == "HIGH" || session.ForbidWrites ||
            (args.TryGetProperty("read_only", out readOnly) &&
             readOnly.ValueKind == JsonValueKind.True))
        {
            // Reject nonexistent inspection paths before spawning a provider.
            foreach (var item in paths)
            {
                var relative = item.GetString()!;
                var fullPath = Path.GetFullPath(Path.Combine(session.ProjectRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!MilestoneDefinitionContract.IsPathInsideRoot(session.ProjectRoot, fullPath) ||
                    (!File.Exists(fullPath) && !Directory.Exists(fullPath)))
                    throw new InvalidOperationException("WORK_PATH_NOT_FOUND: " + relative);
            }
        }
    }

    internal void UnregisterMcpSession(string token)
    {
        if (!_mcpSessions.TryRemove(token, out var s)) return;
        McpActivity?.Invoke(s.Role, "SESSION_FINISHED calls=" +
            Volatile.Read(ref s.ToolsCalled) + " completed=" +
            Volatile.Read(ref s.SuccessfulDelegations) +
            " recoverable=" + Volatile.Read(ref s.RecoverableDelegationFailures) +
            " rejected=" + Volatile.Read(ref s.RejectedDelegations) +
            " unsafe=" + Volatile.Read(ref s.UnsafeDelegationFailures));
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
                        ? "WORK는 지정 경로만 실행하고 재위임·Git 변경을 하지 않는다."
                        : "실작업은 delegate_work로 즉시 실행한다. 도구 호출이 끝나면 실제 WORK 결과가 반환된다. 부모 역할은 결과를 검토하고 다음 작업을 배정하거나 보고한다. WORK 실패 시 직접 대행하지 않는다."
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
                    message = await ExecuteDelegatedWorkAsync(session, args, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    failed = true;
                    Interlocked.Increment(ref session.UnsafeDelegationFailures);
                    message = "WORK_CANCELED_OR_TIMEOUT";
                    session.Evidence.Enqueue(new DelegatedWorkEvidence(
                        session.InvocationId, "CANCELED", message));
                }
                catch (Exception error)
                {
                    failed = true;
                    var classification = IsRejectedDelegation(error)
                        ? "REJECTED" : IsRecoverableWorkFailure(error)
                            ? "EXECUTION_FAILED" : "UNSAFE";
                    if (classification == "REJECTED")
                        Interlocked.Increment(ref session.RejectedDelegations);
                    else if (classification == "EXECUTION_FAILED")
                        Interlocked.Increment(ref session.RecoverableDelegationFailures);
                    else
                        Interlocked.Increment(ref session.UnsafeDelegationFailures);
                    message = error.GetType().Name + ": " + error.Message;
                    session.Evidence.Enqueue(new DelegatedWorkEvidence(
                        session.InvocationId, classification,
                        MilestoneDefinitionContract.Limit(message, 500)));
                }
                McpActivity?.Invoke(session.Role,
                    (failed ? "TOOL_FAILED " : "TOOL_COMPLETED ") + "delegate_work");
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
                description = "MANDATORY for SuperVisor and HIGH: execute one bounded implementation, investigation or verification task immediately in a fresh WORK session. This MCP call waits for the actual WORK result; WORK calls are serialized. No HQ WORK number.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        task = new { type = "string", description = "Focused task, up to 2000 characters." },
                        read_only = new { type = "boolean", description = "True for verification without file modifications." },
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
