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
    internal ConcurrentQueue<JsonElement> PendingDelegations { get; } = new();
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
    /// The delegating CLI must have EXITED before child WORK starts.
    /// Draining is deliberately sequential: there is never a simultaneous
    /// role/child provider session, including SV/HIGH versus WORK.
    /// </summary>
    internal async Task<DelegationMetrics> DrainDelegatedWorkAsync(
        string token, CancellationToken cancellationToken)
    {
        if (!_mcpSessions.TryGetValue(token, out var session) || session.IsWork)
            throw new InvalidOperationException("PARENT_MCP_SESSION_NOT_FOUND");
        while (session.PendingDelegations.TryDequeue(out var args))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handler = McpToolExecutor ??
                throw new InvalidOperationException("MCP_EXECUTOR_NOT_CONFIGURED");
            try
            {
                var message = await handler(session, "delegate_work", args, cancellationToken)
                    .ConfigureAwait(false);
                Interlocked.Increment(ref session.SuccessfulDelegations);
                var invocation = message.StartsWith("INVOCATION: ", StringComparison.Ordinal)
                    ? message["INVOCATION: ".Length..].Split('\n')[0].Trim()
                    : session.InvocationId;
                session.Evidence.Enqueue(new DelegatedWorkEvidence(
                    invocation, "COMPLETED", "WORK result verified"));
                McpActivity?.Invoke(session.Role, "TOOL_DONE delegate_work");
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref session.UnsafeDelegationFailures);
                session.Evidence.Enqueue(new DelegatedWorkEvidence(
                    session.InvocationId, "CANCELED", "WORK_CANCELED_OR_TIMEOUT"));
                McpActivity?.Invoke(session.Role, "TOOL_FAILED delegate_work");
                throw;
            }
            catch (Exception error)
            {
                var classification = IsRejectedDelegation(error)
                    ? "REJECTED" : IsRecoverableWorkFailure(error)
                        ? "EXECUTION_FAILED" : "UNSAFE";
                if (classification == "REJECTED")
                    Interlocked.Increment(ref session.RejectedDelegations);
                else if (classification == "EXECUTION_FAILED")
                    Interlocked.Increment(ref session.RecoverableDelegationFailures);
                else
                    Interlocked.Increment(ref session.UnsafeDelegationFailures);
                var message = error.GetType().Name + ": " + error.Message;
                session.Evidence.Enqueue(new DelegatedWorkEvidence(
                    session.InvocationId, classification,
                    message.Length <= 500 ? message : message[..500]));
                McpActivity?.Invoke(session.Role, "TOOL_FAILED delegate_work");
            }
        }
        return GetDelegationMetrics(token);
    }

    private static void ValidateDelegationRequest(
        WorkerMcpSession session, JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("task", out var task) ||
            task.ValueKind != JsonValueKind.String ||
            task.GetString()?.Trim().Length is not > 0 and <= 2000 ||
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
                        ? "임시 WORK는 지정 경로에서만 작업한다. 재위임과 Git commit/push를 수행하지 않는다."
                        : session.Role == "HIGH"
                            ? "HIGH는 읽기 전용 최종 검토자다. 조사·검증 실행을 우선 delegate_work로 임시 WORK에 위임하고 결과를 독립 검토한다. WORK 호출 실패 후에는 읽기 전용 직접 검증만 가능하며 소스 수정은 금지한다."
                            : "SuperVisor는 실제 실행을 먼저 delegate_work로 임시 WORK에 위임하고 결과를 통합한다. 이 최초 세션은 읽기 전용이다. WORK 실행 실패를 Worker가 기계적으로 확인한 경우에만 별도 새 세션에서 직접 복구할 수 있다. Git 최종화는 Worker 책임이다."
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
                try
                {
                    ValidateDelegationRequest(session, args);
                    session.PendingDelegations.Enqueue(args.Clone());
                    McpActivity?.Invoke(session.Role, "TOOL_QUEUED delegate_work");
                    output = new
                    {
                        content = new[] { new
                        {
                            type = "text",
                            text = "DELEGATION_QUEUED: Worker가 부모 세션 종료 후 WORK를 독립적으로 순차 실행합니다. " +
                                   "이 턴에서는 자식 결과가 아직 없습니다. 추가 작업을 위임하거나 " +
                                   "계획만 요약하고 종료하십시오. Worker가 결과 통합을 위한 새 세션을 엽니다."
                        } },
                        isError = false
                    };
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException)
                {
                    Interlocked.Increment(ref session.RejectedDelegations);
                    var detail = error.Message;
                    session.Evidence.Enqueue(new DelegatedWorkEvidence(
                        session.InvocationId, "REJECTED", detail));
                    McpActivity?.Invoke(session.Role, "TOOL_REJECTED delegate_work");
                    output = new
                    {
                        content = new[] { new { type = "text", text = detail } },
                        isError = true
                    };
                }
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
                description = "Delegate one bounded implementation, investigation or test task to a fresh WORK session. Calls with nonoverlapping write paths can run concurrently. Results return only to the calling SV or HIGH.",
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
