using System.IO;

namespace ProjectHub.Worker;

public sealed record CodexWorkItemProgress(
    string WorkItemId,
    string Message);

public sealed record CodexWorkItemSessionStarted(
    string WorkItemId,
    string SessionId);

public sealed record CodexWorkItemContextPrepared(
    string WorkItemId,
    string Branch,
    string WorktreePath);

public sealed record CodexWorkItemCallCompleted(
    string WorkItemId,
    string InboundType,
    int PromptBytes,
    long LatencyMs,
    AiRoleRunResult Result);

public sealed class CodexWorkItemExecutor : IWorkItemExecutor
{
    private readonly string _jobId;
    private readonly string _workspace;
    private readonly WorkerAiRoleSettings _role;
    private readonly IAiRoleRunner _runner;
    private readonly GitWorktreeManager _worktrees;
    private readonly bool _judgeAvailable;
    private readonly Func<string, string?>? _observationRequestDirectory;
    private readonly IWorkItemObservationGate? _observationGate;
    private readonly string? _expectedPrimaryBranch;

    public CodexWorkItemExecutor(
        string jobId,
        string workspace,
        WorkerAiRoleSettings role,
        IAiRoleRunner runner,
        GitWorktreeManager? worktrees = null,
        bool judgeAvailable = false,
        Func<string, string?>? observationRequestDirectory = null,
        IWorkItemObservationGate? observationGate = null,
        string? expectedPrimaryBranch = null)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID가 비어 있습니다.", nameof(jobId));
        if (string.IsNullOrWhiteSpace(workspace))
            throw new ArgumentException("작업공간이 비어 있습니다.", nameof(workspace));

        _jobId = jobId.Trim();
        _workspace = Path.GetFullPath(workspace);
        _role = role ?? throw new ArgumentNullException(nameof(role));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _worktrees = worktrees ?? new GitWorktreeManager();
        _judgeAvailable = judgeAvailable;
        _observationRequestDirectory = observationRequestDirectory;
        _observationGate = observationGate;
        _expectedPrimaryBranch = string.IsNullOrWhiteSpace(expectedPrimaryBranch)
            ? null
            : expectedPrimaryBranch.Trim();
    }

    public event Action<CodexWorkItemProgress>? Progress;
    public event Action<CodexWorkItemSessionStarted>? SessionStarted;
    public event Action<CodexWorkItemContextPrepared>? ContextPrepared;
    public event Action<CodexWorkItemCallCompleted>? CallCompleted;

    public async Task<WorkItemExecutionResult> ExecuteAsync(
        WorkItemExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var item = request.Item;
        if (string.IsNullOrWhiteSpace(item.BaseRef))
            return WorkItemExecutionResult.Failed("WORKTREE_BASE_REF_MISSING", "WorkItem baseRef가 없습니다.");

        var preparation = await _worktrees.PrepareAsync(
            _workspace,
            _jobId,
            item.Id,
            item.BaseRef,
            cancellationToken).ConfigureAwait(false);

        if (!preparation.Success)
        {
            return WorkItemExecutionResult.Failed(
                preparation.ErrorCode ?? "WORKTREE_PREPARE_FAILED",
                "WorkItem worktree 준비에 실패했습니다.",
                preparation.Branch,
                preparation.WorktreePath,
                item.SessionId);
        }

        _observationGate?.RegisterWorkItemRoot(item.Id, preparation.WorktreePath);
        ContextPrepared?.Invoke(new CodexWorkItemContextPrepared(
            item.Id,
            preparation.Branch,
            preparation.WorktreePath));

        var dependencyResults = request.Dependencies
            .Select(result => new WorkItemDependencyPromptContext(
                result.WorkItemId,
                result.ResultRef,
                result.ResultSummary))
            .ToArray();

        var observationRequestDirectory = _observationGate is not null
            ? _observationGate.GetRequestDirectory(item.Id)
            : _observationRequestDirectory?.Invoke(item.Id);
        var inboundType = request.InboundType;
        var inboundBody = request.InboundBody;
        var sessionId = item.SessionId;
        AiRoleRunResult runResult;

        while (true)
        {
            var prompt = RoleContractLoader.BuildWorkPrompt(
                inboundType,
                inboundBody,
                _judgeAvailable,
                observationRequestDirectory,
                new WorkItemPromptContext(
                    item.Id,
                    item.Kind,
                    item.Goal,
                    item.Dependencies,
                    item.BaseRef,
                    preparation.Branch,
                    preparation.WorktreePath,
                    item.ResultSummary,
                    dependencyResults));

            string? startedSession = sessionId;
            var callStartedAt = DateTimeOffset.UtcNow;
            runResult = await _runner.RunAsync(new AiRoleRunRequest(
                prompt,
                _role,
                preparation.WorktreePath,
                sessionId,
                CodexSandboxMode.WorkspaceWrite,
                cancellationToken,
                null,
                message => Progress?.Invoke(new CodexWorkItemProgress(item.Id, message)),
                started =>
                {
                    var normalized = CodexCliRunner.NormalizeSessionId(started);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        startedSession = normalized;
                        SessionStarted?.Invoke(new CodexWorkItemSessionStarted(item.Id, normalized));
                    }
                },
                string.IsNullOrWhiteSpace(observationRequestDirectory)
                    ? null
                    : new[] { observationRequestDirectory })).ConfigureAwait(false);

            CallCompleted?.Invoke(new CodexWorkItemCallCompleted(
                item.Id,
                inboundType,
                System.Text.Encoding.UTF8.GetByteCount(prompt),
                Math.Max(0, (long)(DateTimeOffset.UtcNow - callStartedAt).TotalMilliseconds),
                runResult));

            sessionId = CodexCliRunner.NormalizeSessionId(runResult.SessionId) ??
                        startedSession ??
                        sessionId;

            if (runResult.ExitCode != 0)
            {
                return WorkItemExecutionResult.Failed(
                    "WORK_PROCESS_EXIT",
                    BuildFailureSummary(runResult.StandardError, runResult.FinalMessage),
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId);
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return WorkItemExecutionResult.Failed(
                    "WORK_SESSION_RESUME_FAILED",
                    "WORK 실행 후 이어갈 session ID가 없습니다.",
                    preparation.Branch,
                    preparation.WorktreePath,
                    null);
            }

            if (_observationGate is null)
                break;

            var requiredObservations = await _observationGate
                .CollectRequiredAsync(item.Id, cancellationToken)
                .ConfigureAwait(false);
            if (requiredObservations.Count == 0)
                break;

            inboundType = "OBSERVATION_RESULT";
            inboundBody = WorkItemObservationGate.FormatResultBody(
                item.Id,
                requiredObservations);
        }

        var route = WorkerGotoContract.Parse(WorkerRoleState.Work, runResult.FinalMessage);
        if (route.Error is not null)
        {
            return WorkItemExecutionResult.Failed(
                "WORK_ROUTE_" + route.Error,
                runResult.FinalMessage,
                preparation.Branch,
                preparation.WorktreePath,
                sessionId);
        }

        if (route.Target == WorkerRoleState.Judge)
        {
            if (!_judgeAvailable)
            {
                return WorkItemExecutionResult.Failed(
                    "JUDGE_UNAVAILABLE",
                    "현재 병렬 WorkItem에서는 JUDGE가 비활성화되어 있습니다.",
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId);
            }

            return WorkItemExecutionResult.Blocked(
                "JUDGE_REQUEST",
                route.Body,
                preparation.HeadCommit,
                preparation.Branch,
                preparation.WorktreePath,
                sessionId);
        }

        if (route.Target == WorkerRoleState.Resource)
        {
            return WorkItemExecutionResult.Blocked(
                "RESOURCE_REQUEST",
                route.Body,
                preparation.HeadCommit,
                preparation.Branch,
                preparation.WorktreePath,
                sessionId);
        }

        if (route.Target != WorkerRoleState.Hq)
        {
            return WorkItemExecutionResult.Failed(
                "WORK_ROUTE_UNSUPPORTED",
                runResult.FinalMessage,
                preparation.Branch,
                preparation.WorktreePath,
                sessionId);
        }

        if (!WorkItemReportContract.TryParse(route.Body, out var report, out var reportError))
        {
            return WorkItemExecutionResult.Failed(
                reportError ?? "WORK_ITEM_REPORT_INVALID",
                route.Body,
                preparation.Branch,
                preparation.WorktreePath,
                sessionId);
        }

        var checkpoint = await _worktrees.CreateCheckpointAsync(
            preparation.WorktreePath,
            item.Id,
            cancellationToken).ConfigureAwait(false);

        if (!checkpoint.Success)
        {
            return WorkItemExecutionResult.Failed(
                checkpoint.ErrorCode ?? "WORKTREE_CHECKPOINT_FAILED",
                report!.Body,
                preparation.Branch,
                preparation.WorktreePath,
                sessionId);
        }

        if (report!.Status == WorkItemReportStatus.Completed &&
            item.Kind == WorkItemKind.Integration)
        {
            if (string.IsNullOrWhiteSpace(checkpoint.HeadCommit))
            {
                return WorkItemExecutionResult.Blocked(
                    "INTEGRATION_LANDING_FAILED",
                    BuildIntegrationLandingFailure(
                        report.Body,
                        "INTEGRATION_RESULT_REF_MISSING",
                        checkpoint.HeadCommit,
                        null),
                    checkpoint.HeadCommit,
                    checkpoint.Branch ?? preparation.Branch,
                    checkpoint.WorktreePath,
                    sessionId);
            }

            var landing = await _worktrees.LandIntegrationAsync(
                _workspace,
                checkpoint.HeadCommit,
                _expectedPrimaryBranch,
                cancellationToken).ConfigureAwait(false);

            if (!landing.Success)
            {
                return WorkItemExecutionResult.Blocked(
                    "INTEGRATION_LANDING_FAILED",
                    BuildIntegrationLandingFailure(
                        report.Body,
                        landing.ErrorCode ?? "INTEGRATION_LANDING_FAILED",
                        checkpoint.HeadCommit,
                        landing),
                    checkpoint.HeadCommit,
                    checkpoint.Branch ?? preparation.Branch,
                    checkpoint.WorktreePath,
                    sessionId);
            }

            return WorkItemExecutionResult.Completed(
                checkpoint.HeadCommit,
                BuildIntegrationLandingSuccess(report.Body, landing),
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId);
        }

        return report.Status switch
        {
            WorkItemReportStatus.Completed => WorkItemExecutionResult.Completed(
                checkpoint.HeadCommit,
                report.Body,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId),
            WorkItemReportStatus.SplitRequest => WorkItemExecutionResult.Blocked(
                "SPLIT_REQUEST",
                report.Body,
                checkpoint.HeadCommit,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId),
            WorkItemReportStatus.Blocked => WorkItemExecutionResult.Blocked(
                "HQ_BLOCKED",
                report.Body,
                checkpoint.HeadCommit,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId),
            _ => WorkItemExecutionResult.Failed(
                "WORK_ITEM_REPORTED_FAILED",
                report.Body,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId)
        };
    }

    private static string BuildIntegrationLandingSuccess(
        string reportBody,
        GitIntegrationLandingResult landing)
    {
        var lines = new List<string>
        {
            reportBody.Trim(),
            string.Empty,
            "INTEGRATION_LANDING",
            "status: " + (landing.FastForwarded ? "FAST_FORWARDED" : "ALREADY_APPLIED"),
            "targetBranch: " + (landing.TargetBranch ?? "없음"),
            "beforeHead: " + (landing.BeforeHead ?? "없음"),
            "afterHead: " + (landing.AfterHead ?? "없음")
        };
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildIntegrationLandingFailure(
        string reportBody,
        string errorCode,
        string? integrationRef,
        GitIntegrationLandingResult? landing)
    {
        var lines = new List<string>
        {
            reportBody.Trim(),
            string.Empty,
            "INTEGRATION_LANDING",
            "status: BLOCKED",
            "errorCode: " + errorCode,
            "integrationRef: " + (integrationRef ?? "없음")
        };

        if (landing is not null)
        {
            lines.Add("targetBranch: " + (landing.TargetBranch ?? "없음"));
            lines.Add("beforeHead: " + (landing.BeforeHead ?? "없음"));
            lines.Add("afterHead: " + (landing.AfterHead ?? "없음"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildFailureSummary(string standardError, string finalMessage)
    {
        var value = !string.IsNullOrWhiteSpace(standardError)
            ? standardError
            : finalMessage;
        value = (value ?? string.Empty).Trim();
        return value.Length <= 4000 ? value : value[..4000];
    }
}
