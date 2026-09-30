using System.IO;

namespace ProjectHub.Worker;

public sealed record CodexWorkItemProgress(
    string WorkItemId,
    string Message,
    long? WorkNumber = null);

public sealed record CodexWorkItemSessionStarted(
    string WorkItemId,
    string SessionId);

public sealed record CodexWorkItemContextPrepared(
    string WorkItemId,
    string Branch,
    string WorktreePath,
    string BaseRef);

public sealed record CodexWorkItemCallCompleted(
    string WorkItemId,
    string InboundType,
    int PromptBytes,
    long LatencyMs,
    AiRoleRunResult Result,
    long? WorkNumber = null);

public sealed class CodexWorkItemExecutor : IWorkItemExecutor
{
    private const int MaximumCheckpointAttempts = 3;
    private const int MaximumOutputContractCorrections = 2;
    private const string CheckpointRetryInboundType = "WORKTREE_CHECKPOINT_RETRY";
    private const string CheckpointPendingHeader = "WORKTREE_CHECKPOINT_PENDING";

    private readonly string _jobId;
    private readonly string _workspace;
    private readonly WorkerAiRoleSettings _role;
    private readonly IAiRoleRunner _runner;
    private readonly GitWorktreeManager _worktrees;
    private readonly bool _judgeAvailable;
    private readonly Func<string, string?>? _observationRequestDirectory;
    private readonly IWorkItemObservationGate? _observationGate;
    private readonly string? _expectedPrimaryBranch;
    private readonly IReadOnlyList<UserAttachmentInput> _userAttachments;

    public CodexWorkItemExecutor(
        string jobId,
        string workspace,
        WorkerAiRoleSettings role,
        IAiRoleRunner runner,
        GitWorktreeManager? worktrees = null,
        bool judgeAvailable = false,
        Func<string, string?>? observationRequestDirectory = null,
        IWorkItemObservationGate? observationGate = null,
        string? expectedPrimaryBranch = null,
        IReadOnlyList<UserAttachmentInput>? userAttachments = null)
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
        _userAttachments = userAttachments?.ToArray() ?? Array.Empty<UserAttachmentInput>();
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
        var integrationNeedsPreparation =
            item.Kind == WorkItemKind.Integration &&
            string.IsNullOrWhiteSpace(item.WorktreePath);

        if (item.Kind != WorkItemKind.Integration &&
            string.IsNullOrWhiteSpace(item.BaseRef))
            return WorkItemExecutionResult.Blocked("WORKTREE_BASE_REF_MISSING", "WorkItem baseRef가 없습니다.");

        var effectiveBaseRef = item.BaseRef;
        if (item.Kind == WorkItemKind.Normal)
        {
            var codeDependencies = request.Dependencies
                .Where(dependency => dependency.ResultType == WorkItemResultType.CodeChange)
                .ToArray();
            var missingResultRef = codeDependencies
                .FirstOrDefault(dependency => string.IsNullOrWhiteSpace(dependency.ResultRef));
            if (missingResultRef is not null)
            {
                return WorkItemExecutionResult.Blocked(
                    "NORMAL_CODE_DEPENDENCY_RESULT_REF_MISSING",
                    $"WorkItem {missingResultRef.WorkItemId}의 CODE_CHANGE resultRef가 없습니다.",
                    item.ResultRef,
                    item.Branch,
                    item.WorktreePath,
                    item.SessionId,
                    blockDetailCode: "NORMAL_CODE_DEPENDENCY_RESULT_REF_MISSING",
                    resultType: item.ResultType,
                    commitManifestPath: item.CommitManifestPath);
            }

            if (codeDependencies.Length > 0)
            {
                var baseResolution = await _worktrees.ResolveNormalBaseRefAsync(
                    _workspace,
                    item.BaseRef!,
                    codeDependencies
                        .Select(dependency => dependency.ResultRef!)
                        .ToArray(),
                    cancellationToken).ConfigureAwait(false);

                if (!baseResolution.Success || string.IsNullOrWhiteSpace(baseResolution.EffectiveBaseRef))
                {
                    var errorCode = baseResolution.ErrorCode ?? "WORKTREE_DEPENDENCY_BASE_RESOLUTION_FAILED";
                    return WorkItemExecutionResult.Blocked(
                        errorCode,
                        baseResolution.ErrorDetail ?? "NORMAL WorkItem의 실제 코드 기준점을 계산하지 못했습니다.",
                        item.ResultRef,
                        item.Branch,
                        item.WorktreePath,
                        item.SessionId,
                        blockDetailCode: errorCode,
                        resultType: item.ResultType,
                        commitManifestPath: item.CommitManifestPath);
                }

                effectiveBaseRef = baseResolution.EffectiveBaseRef;
            }
        }

        GitWorktreePreparationResult preparation;
        if (item.Kind == WorkItemKind.Integration)
        {
            preparation = integrationNeedsPreparation
                ? await _worktrees.PrepareIntegrationAsync(
                    _workspace,
                    _jobId,
                    item.Id,
                    _expectedPrimaryBranch,
                    cancellationToken).ConfigureAwait(false)
                : await _worktrees.ResumeIntegrationAsync(
                    _workspace,
                    item.WorktreePath!,
                    item.Branch,
                    item.BaseRef,
                    cancellationToken).ConfigureAwait(false);
        }
        else
        {
            preparation = await _worktrees.PrepareAsync(
                _workspace,
                _jobId,
                item.Id,
                effectiveBaseRef!,
                cancellationToken).ConfigureAwait(false);
        }

        if (!preparation.Success)
        {
            var detail = string.IsNullOrWhiteSpace(preparation.ErrorDetail)
                ? "WorkItem worktree 준비에 실패했습니다."
                : "WorkItem worktree 준비에 실패했습니다." + Environment.NewLine + preparation.ErrorDetail;
            return WorkItemExecutionResult.Blocked(
                preparation.ErrorCode ?? "WORKTREE_PREPARE_FAILED",
                detail,
                resultRef: null,
                branch: preparation.Branch,
                worktreePath: preparation.WorktreePath,
                sessionId: item.SessionId);
        }

        _observationGate?.RegisterWorkItemRoot(item.Id, preparation.WorktreePath);
        ContextPrepared?.Invoke(new CodexWorkItemContextPrepared(
            item.Id,
            preparation.Branch,
            preparation.WorktreePath,
            preparation.BaseRef));

        if (string.Equals(request.InboundType, CheckpointRetryInboundType, StringComparison.Ordinal))
        {
            if (!TryParseCheckpointPendingSummary(
                    item.ResultSummary,
                    out var pendingStatus,
                    out var pendingBody))
            {
                return WorkItemExecutionResult.Failed(
                    "WORKTREE_CHECKPOINT_PENDING_STATE_INVALID",
                    item.ResultSummary,
                    preparation.Branch,
                    preparation.WorktreePath,
                    item.SessionId);
            }

            return await FinalizeReportAsync(
                item,
                preparation,
                item.SessionId,
                pendingStatus,
                pendingBody,
                cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<AiInputAttachment> stagedUserAttachments;
        try
        {
            stagedUserAttachments = UserAttachmentTransport.StageForWorkspace(
                _userAttachments,
                preparation.WorktreePath,
                _jobId + "-" + item.Id);
        }
        catch (Exception exception)
        {
            return WorkItemExecutionResult.Blocked(
                "USER_ATTACHMENT_STAGE_FAILED",
                exception.Message,
                resultRef: null,
                branch: preparation.Branch,
                worktreePath: preparation.WorktreePath,
                sessionId: item.SessionId);
        }

        IReadOnlyDictionary<string, string> integrationSnapshots =
            new Dictionary<string, string>(StringComparer.Ordinal);
        if (item.Kind == WorkItemKind.Integration)
        {
            var stagedDependencies = await _worktrees.StageIntegrationDependenciesAsync(
                preparation.WorktreePath,
                request.Dependencies,
                cancellationToken).ConfigureAwait(false);

            if (!stagedDependencies.Success)
            {
                return WorkItemExecutionResult.Blocked(
                    "INTEGRATION_INPUT_STAGE_FAILED",
                    "Integration 선행 결과 snapshot 준비에 실패했습니다." +
                    Environment.NewLine +
                    "errorCode=" + (stagedDependencies.ErrorCode ?? "INTEGRATION_INPUT_STAGE_FAILED") +
                    Environment.NewLine +
                    (stagedDependencies.ErrorDetail ?? "추가 정보 없음"),
                    preparation.HeadCommit,
                    preparation.Branch,
                    preparation.WorktreePath,
                    item.SessionId,
                    blockDetailCode: stagedDependencies.ErrorCode ?? "INTEGRATION_INPUT_STAGE_FAILED");
            }

            integrationSnapshots = stagedDependencies.SnapshotPaths;
        }

        var dependencyResults = request.Dependencies
            .Select(result => new WorkItemDependencyPromptContext(
                result.WorkItemId,
                result.ResultRef,
                result.ResultSummary,
                result.ResultType,
                result.CommitManifestPath,
                integrationSnapshots.TryGetValue(result.WorkItemId, out var snapshotPath)
                    ? snapshotPath
                    : null))
            .ToArray();

        var observationRequestDirectory = _observationGate is not null
            ? _observationGate.GetRequestDirectory(item.Id)
            : _observationRequestDirectory?.Invoke(item.Id);

        RepositoryRuntimePaths runtimePaths;
        string workTempPath;
        IReadOnlyDictionary<string, string> workEnvironment;
        IReadOnlyList<string> workWritableDirectories;
        try
        {
            runtimePaths = WorkerPaths.GetRepositoryRuntimePaths(preparation.RepositoryRoot);
            workTempPath = WorkerPaths.BuildWorkTempPath(runtimePaths, _jobId, item.Id);
            WorkerPaths.EnsureWorkToolDirectories(runtimePaths, workTempPath);

            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in GitMetadataIsolationLease.BuildGitNetworkDenyEnvironment())
                environment[pair.Key] = pair.Value;
            foreach (var pair in WorkerPaths.BuildWorkToolEnvironment(runtimePaths, workTempPath))
                environment[pair.Key] = pair.Value;
            environment["PROJECTHUB_RESOURCE_TEMP"] = runtimePaths.TempRoot;
            environment["PROJECTHUB_WORK_TEMP"] = workTempPath;
            workEnvironment = environment;

            var writableDirectories = new List<string>
            {
                runtimePaths.NuGetRoot,
                runtimePaths.DotNetHome,
                workTempPath,
                Path.Combine(workTempPath, "build")
            };
            if (!string.IsNullOrWhiteSpace(observationRequestDirectory))
                writableDirectories.Add(observationRequestDirectory);
            workWritableDirectories = writableDirectories;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return WorkItemExecutionResult.Blocked(
                "WORK_TOOL_RUNTIME_PREPARE_FAILED",
                exception.Message,
                preparation.HeadCommit,
                preparation.Branch,
                preparation.WorktreePath,
                item.SessionId,
                blockDetailCode: "WORK_TOOL_RUNTIME_PREPARE_FAILED");
        }

        var inboundType = request.InboundType;
        var inboundBody = request.InboundBody;
        var sessionId = item.SessionId;
        AiRoleRunResult runResult;
        var outputContractCorrections = 0;

        while (true)
        {
            var prompt = RoleContractLoader.BuildWorkPrompt(
                inboundType,
                inboundBody,
                new WorkItemPromptContext(
                    item.Id,
                    item.Kind,
                    item.Goal,
                    item.Dependencies,
                    preparation.BaseRef,
                    preparation.Branch,
                    preparation.WorktreePath,
                    item.ResultSummary,
                    dependencyResults),
                observationRequestDirectory,
                includeContract: string.IsNullOrWhiteSpace(sessionId),
                resourceStagingRoot: runtimePaths.TempRoot,
                workTempRoot: workTempPath);

            string? startedSession = sessionId;
            var callStartedAt = DateTimeOffset.UtcNow;
            GitMetadataIsolationLease gitIsolation;
            try
            {
                gitIsolation = GitMetadataIsolationLease.Detach(
                    preparation.WorktreePath,
                    _jobId,
                    item.Id);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return WorkItemExecutionResult.Blocked(
                    "WORK_GIT_METADATA_ISOLATION_FAILED",
                    exception.Message,
                    preparation.HeadCommit,
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    blockDetailCode: "WORK_GIT_METADATA_ISOLATION_FAILED");
            }

            var gitRestore = new GitMetadataRestoreResult(true);
            try
            {
                runResult = await _runner.RunAsync(new AiRoleRunRequest(
                    prompt,
                    _role,
                    preparation.WorktreePath,
                    sessionId,
                    CodexSandboxMode.WorkspaceWrite,
                    cancellationToken,
                    null,
                    message => Progress?.Invoke(new CodexWorkItemProgress(item.Id, message, item.CreatedOrder + 1)),
                    started =>
                    {
                        var normalized = CodexCliRunner.NormalizeSessionId(started);
                        if (!string.IsNullOrWhiteSpace(normalized))
                        {
                            startedSession = normalized;
                            SessionStarted?.Invoke(new CodexWorkItemSessionStarted(item.Id, normalized));
                        }
                    },
                    workWritableDirectories,
                    InputAttachments: stagedUserAttachments,
                    EnvironmentVariables: workEnvironment,
                    DisableComputerUse: true,
                    IncludeAppBaseWritable: false)).ConfigureAwait(false);
            }
            finally
            {
                gitRestore = gitIsolation.Restore();
            }

            if (!gitRestore.Success)
            {
                return WorkItemExecutionResult.Failed(
                    gitRestore.ErrorCode ?? "WORK_GIT_METADATA_RESTORE_FAILED",
                    (gitRestore.ErrorDetail ?? "Git metadata 복원에 실패했습니다.") +
                    (string.IsNullOrWhiteSpace(gitRestore.QuarantinePath)
                        ? string.Empty
                        : Environment.NewLine + "quarantine=" + gitRestore.QuarantinePath),
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId);
            }

            CallCompleted?.Invoke(new CodexWorkItemCallCompleted(
                item.Id,
                inboundType,
                System.Text.Encoding.UTF8.GetByteCount(prompt),
                Math.Max(0, (long)(DateTimeOffset.UtcNow - callStartedAt).TotalMilliseconds),
                runResult,
                item.CreatedOrder + 1));

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

            if (_observationGate is not null)
            {
                var requiredObservations = await _observationGate
                    .CollectRequiredAsync(item.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (requiredObservations.Count > 0)
                {
                    inboundType = "OBSERVATION_RESULT";
                    inboundBody = WorkItemObservationGate.FormatResultBody(
                        item.Id,
                        requiredObservations);
                    continue;
                }
            }

            var route = WorkerGotoContract.Parse(WorkerRoleState.Work, runResult.FinalMessage);
            if (route.Error is not null)
            {
                if (outputContractCorrections >= MaximumOutputContractCorrections)
                {
                    return WorkItemExecutionResult.Failed(
                        "WORK_ROUTE_" + route.Error,
                        runResult.FinalMessage,
                        preparation.Branch,
                        preparation.WorktreePath,
                        sessionId);
                }

                outputContractCorrections++;
                inboundType = "WORK_OUTPUT_CONTRACT_REJECTED";
                inboundBody =
                    $"errorCode=WORK_ROUTE_{route.Error}{Environment.NewLine}" +
                    $"correctionAttempt={outputContractCorrections}/{MaximumOutputContractCorrections}{Environment.NewLine}" +
                    "의미 작업은 다시 수행하지 않는다. 직전 결과의 내용은 유지하고 WORK 출력 계약에 맞는 GOTO 제어행과 필요한 본문만 다시 반환한다.";
                continue;
            }

            if (route.Target == WorkerRoleState.Judge)
            {
                if (!_judgeAvailable)
                {
                    return WorkItemExecutionResult.Blocked(
                        "JUDGE_UNAVAILABLE",
                        route.Body,
                        preparation.HeadCommit,
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
                if (outputContractCorrections >= MaximumOutputContractCorrections)
                {
                    return WorkItemExecutionResult.Failed(
                        "WORK_ROUTE_UNSUPPORTED",
                        runResult.FinalMessage,
                        preparation.Branch,
                        preparation.WorktreePath,
                        sessionId);
                }

                outputContractCorrections++;
                inboundType = "WORK_OUTPUT_CONTRACT_REJECTED";
                inboundBody =
                    $"errorCode=WORK_ROUTE_UNSUPPORTED{Environment.NewLine}" +
                    $"correctionAttempt={outputContractCorrections}/{MaximumOutputContractCorrections}{Environment.NewLine}" +
                    "의미 작업은 다시 수행하지 않는다. 직전 결과를 현재 WORK가 허용하는 목적지 하나로만 다시 보고한다.";
                continue;
            }

            if (!WorkItemReportContract.TryParse(route.Body, out var report, out var reportError))
            {
                if (outputContractCorrections >= MaximumOutputContractCorrections)
                {
                    return WorkItemExecutionResult.Failed(
                        reportError ?? "WORK_ITEM_REPORT_INVALID",
                        route.Body,
                        preparation.Branch,
                        preparation.WorktreePath,
                        sessionId);
                }

                outputContractCorrections++;
                inboundType = "WORK_ITEM_REPORT_REJECTED";
                inboundBody =
                    $"errorCode={reportError ?? "WORK_ITEM_REPORT_INVALID"}{Environment.NewLine}" +
                    $"correctionAttempt={outputContractCorrections}/{MaximumOutputContractCorrections}{Environment.NewLine}" +
                    "의미 작업은 다시 수행하지 않는다. 직전 보고 내용은 유지하고 WORK_ITEM_STATUS 행을 정확히 하나만 포함한 [GOTO : HQ] 응답으로 다시 반환한다.";
                continue;
            }

            return await FinalizeReportAsync(
                item,
                preparation,
                sessionId,
                report!.Status,
                report.Body,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<WorkItemExecutionResult> FinalizeReportAsync(
        WorkItemSnapshot item,
        GitWorktreePreparationResult preparation,
        string? sessionId,
        WorkItemReportStatus reportStatus,
        string reportBody,
        CancellationToken cancellationToken)
    {
        GitWorktreeCheckpointResult checkpoint = default!;
        for (var attempt = 1; attempt <= MaximumCheckpointAttempts; attempt++)
        {
            checkpoint = await _worktrees.CreateCheckpointAsync(
                preparation.WorktreePath,
                item.Id,
                cancellationToken).ConfigureAwait(false);

            if (checkpoint.Success)
                break;

            if (attempt < MaximumCheckpointAttempts)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(attempt * 150),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (!checkpoint.Success)
        {
            var errorCode = checkpoint.ErrorCode ?? "WORKTREE_CHECKPOINT_FAILED";
            return WorkItemExecutionResult.Blocked(
                "WORKTREE_CHECKPOINT_PENDING",
                BuildCheckpointPendingSummary(reportStatus, reportBody),
                checkpoint.HeadCommit ?? item.ResultRef ?? preparation.HeadCommit,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId,
                blockDetailCode: errorCode,
                resultType: item.ResultType,
                commitManifestPath: item.CommitManifestPath);
        }

        var lifecycleResultRef = checkpoint.HeadCommit ?? item.ResultRef;
        var lifecycleHasCodeChange =
            item.ResultType == WorkItemResultType.CodeChange ||
            checkpoint.CreatedCommit;
        string? commitManifestPath = item.CommitManifestPath;

        if (reportStatus != WorkItemReportStatus.Failed &&
            lifecycleHasCodeChange &&
            !string.IsNullOrWhiteSpace(lifecycleResultRef))
        {
            var manifest = await _worktrees.CreateCommitManifestAsync(
                checkpoint.WorktreePath,
                _workspace,
                _jobId,
                item.Id,
                lifecycleResultRef,
                cancellationToken).ConfigureAwait(false);

            if (!manifest.Success)
            {
                return WorkItemExecutionResult.Blocked(
                    "COMMIT_MANIFEST_FAILED",
                    reportBody + Environment.NewLine + Environment.NewLine +
                    "COMMIT_MANIFEST" + Environment.NewLine +
                    "status: BLOCKED" + Environment.NewLine +
                    "errorCode: " + (manifest.ErrorCode ?? "COMMIT_MANIFEST_FAILED") + Environment.NewLine +
                    "detail: " + (manifest.ErrorDetail ?? "없음"),
                    lifecycleResultRef,
                    checkpoint.Branch ?? preparation.Branch,
                    checkpoint.WorktreePath,
                    sessionId,
                    blockDetailCode: manifest.ErrorCode ?? "COMMIT_MANIFEST_FAILED",
                    resultType: WorkItemResultType.CodeChange,
                    commitManifestPath: commitManifestPath);
            }

            commitManifestPath = manifest.ManifestPath;
        }

        var completedResultType = lifecycleHasCodeChange
            ? WorkItemResultType.CodeChange
            : WorkItemResultType.Analysis;
        var blockedResultType = lifecycleHasCodeChange
            ? WorkItemResultType.CodeChange
            : item.ResultType;

        if (reportStatus == WorkItemReportStatus.Completed &&
            item.Kind == WorkItemKind.Integration)
        {
            if (string.IsNullOrWhiteSpace(checkpoint.HeadCommit))
            {
                return WorkItemExecutionResult.Blocked(
                    "INTEGRATION_LANDING_FAILED",
                    BuildIntegrationLandingFailure(
                        reportBody,
                        "INTEGRATION_RESULT_REF_MISSING",
                        checkpoint.HeadCommit,
                        null),
                    checkpoint.HeadCommit,
                    checkpoint.Branch ?? preparation.Branch,
                    checkpoint.WorktreePath,
                    sessionId,
                    blockDetailCode: "INTEGRATION_RESULT_REF_MISSING",
                    resultType: completedResultType,
                    commitManifestPath: commitManifestPath);
            }

            var sourceBranch = checkpoint.Branch ?? preparation.Branch;
            if (string.IsNullOrWhiteSpace(sourceBranch))
            {
                return WorkItemExecutionResult.Blocked(
                    "INTEGRATION_LANDING_FAILED",
                    BuildIntegrationLandingFailure(
                        reportBody,
                        "INTEGRATION_SOURCE_BRANCH_UNAVAILABLE",
                        checkpoint.HeadCommit,
                        null),
                    checkpoint.HeadCommit,
                    preparation.Branch,
                    checkpoint.WorktreePath,
                    sessionId,
                    blockDetailCode: "INTEGRATION_SOURCE_BRANCH_UNAVAILABLE",
                    resultType: completedResultType,
                    commitManifestPath: commitManifestPath);
            }

            if (!string.Equals(sourceBranch, preparation.Branch, StringComparison.Ordinal))
            {
                return WorkItemExecutionResult.Blocked(
                    "INTEGRATION_LANDING_FAILED",
                    BuildIntegrationLandingFailure(
                        reportBody,
                        "INTEGRATION_SOURCE_BRANCH_CHANGED",
                        checkpoint.HeadCommit,
                        null),
                    checkpoint.HeadCommit,
                    sourceBranch,
                    checkpoint.WorktreePath,
                    sessionId,
                    blockDetailCode: "INTEGRATION_SOURCE_BRANCH_CHANGED",
                    resultType: completedResultType,
                    commitManifestPath: commitManifestPath);
            }

            var landing = await _worktrees.LandIntegrationCloneAsync(
                _workspace,
                checkpoint.WorktreePath,
                sourceBranch,
                checkpoint.HeadCommit,
                _expectedPrimaryBranch,
                cancellationToken).ConfigureAwait(false);

            if (!landing.Success)
            {
                var landingErrorCode = landing.ErrorCode ?? "INTEGRATION_LANDING_FAILED";
                return WorkItemExecutionResult.Blocked(
                    "INTEGRATION_LANDING_FAILED",
                    BuildIntegrationLandingFailure(
                        reportBody,
                        landingErrorCode,
                        checkpoint.HeadCommit,
                        landing),
                    checkpoint.HeadCommit,
                    checkpoint.Branch ?? preparation.Branch,
                    checkpoint.WorktreePath,
                    sessionId,
                    blockDetailCode: landingErrorCode,
                    resultType: completedResultType,
                    commitManifestPath: commitManifestPath);
            }

            await TryRemoveCompletedIntegrationCloneAsync(
                item,
                preparation,
                checkpoint.WorktreePath,
                cancellationToken).ConfigureAwait(false);

            return WorkItemExecutionResult.Completed(
                checkpoint.HeadCommit,
                BuildIntegrationLandingSuccess(reportBody, landing),
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId,
                completedResultType,
                commitManifestPath);
        }

        if (reportStatus == WorkItemReportStatus.Completed &&
            item.Kind == WorkItemKind.Normal)
        {
            await TryRemoveCompletedNormalWorktreeAsync(
                item,
                preparation,
                checkpoint,
                cancellationToken).ConfigureAwait(false);
        }

        return reportStatus switch
        {
            WorkItemReportStatus.Completed => WorkItemExecutionResult.Completed(
                lifecycleResultRef,
                reportBody,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId,
                completedResultType,
                commitManifestPath),
            WorkItemReportStatus.SplitRequest => WorkItemExecutionResult.Blocked(
                "SPLIT_REQUEST",
                reportBody,
                lifecycleResultRef,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId,
                resultType: blockedResultType,
                commitManifestPath: commitManifestPath),
            WorkItemReportStatus.Blocked => WorkItemExecutionResult.Blocked(
                "HQ_BLOCKED",
                reportBody,
                lifecycleResultRef,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId,
                resultType: blockedResultType,
                commitManifestPath: commitManifestPath),
            _ => WorkItemExecutionResult.Failed(
                "WORK_ITEM_REPORTED_FAILED",
                reportBody,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId)
        };
    }

    private async Task TryRemoveCompletedIntegrationCloneAsync(
        WorkItemSnapshot item,
        GitWorktreePreparationResult preparation,
        string clonePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var cleanup = await _worktrees.CleanupIntegrationCloneAsync(
                preparation.RepositoryRoot,
                clonePath,
                cancellationToken).ConfigureAwait(false);

            if (!cleanup.Success)
            {
                Progress?.Invoke(new CodexWorkItemProgress(
                    item.Id,
                    "완료 Integration clone 정리를 보류했습니다. " +
                    (cleanup.ErrorCode ?? "INTEGRATION_CLONE_CLEANUP_FAILED"),
                    item.CreatedOrder + 1));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Progress?.Invoke(new CodexWorkItemProgress(
                item.Id,
                "완료 Integration clone 정리 중 기계 오류가 발생해 최종 runtime 정리로 넘깁니다. " +
                exception.GetType().Name,
                item.CreatedOrder + 1));
        }
    }

    private async Task TryRemoveCompletedNormalWorktreeAsync(
        WorkItemSnapshot item,
        GitWorktreePreparationResult preparation,
        GitWorktreeCheckpointResult checkpoint,
        CancellationToken cancellationToken)
    {
        var branch = checkpoint.Branch ?? preparation.Branch;
        if (string.IsNullOrWhiteSpace(branch) ||
            string.IsNullOrWhiteSpace(checkpoint.WorktreePath))
            return;

        try
        {
            var removal = await _worktrees.RemoveAsync(
                preparation.RepositoryRoot,
                checkpoint.WorktreePath,
                branch,
                cancellationToken).ConfigureAwait(false);

            if (!removal.Success)
            {
                Progress?.Invoke(new CodexWorkItemProgress(
                    item.Id,
                    "완료 WorkItem의 linked worktree 정리를 보류했습니다. " +
                    (removal.ErrorCode ?? "WORKTREE_REMOVE_FAILED"),
                    item.CreatedOrder + 1));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Progress?.Invoke(new CodexWorkItemProgress(
                item.Id,
                "완료 WorkItem의 linked worktree 정리 중 기계 오류가 발생해 최종 runtime 정리로 넘깁니다. " +
                exception.GetType().Name,
                item.CreatedOrder + 1));
        }
    }

    private static string BuildCheckpointPendingSummary(
        WorkItemReportStatus status,
        string reportBody)
        => string.Join(
            Environment.NewLine,
            CheckpointPendingHeader,
            "reportStatus: " + status,
            string.Empty,
            reportBody.Trim());

    private static bool TryParseCheckpointPendingSummary(
        string? summary,
        out WorkItemReportStatus status,
        out string reportBody)
    {
        status = default;
        reportBody = string.Empty;
        var normalized = (summary ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length < 3 ||
            !string.Equals(lines[0].Trim(), CheckpointPendingHeader, StringComparison.Ordinal))
            return false;

        const string prefix = "reportStatus:";
        var statusLine = lines[1].Trim();
        if (!statusLine.StartsWith(prefix, StringComparison.Ordinal) ||
            !Enum.TryParse<WorkItemReportStatus>(
                statusLine[prefix.Length..].Trim(),
                ignoreCase: true,
                out status))
            return false;

        reportBody = string.Join(
            Environment.NewLine,
            lines.Skip(2)).TrimStart();
        return true;
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

        lines.Add(string.Empty);
        lines.Add(reportBody.Trim());
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
