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

public sealed record CodexWorkItemMechanicalProgress(
    string WorkItemId,
    string Stage,
    string Message,
    bool Active,
    long? WorkNumber = null);

public sealed class CodexWorkItemExecutor : IWorkItemExecutor
{
    private const int MaximumCheckpointAttempts = 3;
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
    private readonly WorkspacePublishState _publishState;

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
        _publishState = new WorkspacePublishState(_workspace, _jobId);
    }

    public event Action<CodexWorkItemProgress>? Progress;
    public event Action<CodexWorkItemSessionStarted>? SessionStarted;
    public event Action<CodexWorkItemContextPrepared>? ContextPrepared;
    public event Action<CodexWorkItemCallCompleted>? CallCompleted;
    public event Action<CodexWorkItemMechanicalProgress>? MechanicalProgress;

    public async Task<WorkItemExecutionResult> ExecuteAsync(
        WorkItemExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var item = request.Item;
        var executionWorkItemId = FixedWorkItemSlots.BuildExecutionKey(
            item.Id,
            item.CreatedOrder);
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
                    resultType: item.ResultType);
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
                        resultType: item.ResultType);
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
                executionWorkItemId,
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

        var usesTargetWorkspace = FixedWorkItemSlots.AllowsTargetWorkspaceWrite(item.Id);
        var executionWorkingDirectory = usesTargetWorkspace
            ? _workspace
            : preparation.WorktreePath;
        _observationGate?.RegisterWorkItemRoot(item.Id, executionWorkingDirectory);
        ContextPrepared?.Invoke(new CodexWorkItemContextPrepared(
            item.Id,
            preparation.Branch,
            executionWorkingDirectory,
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
                    item.SessionId,
                    "CHECKPOINT_RETRY_STATE");
            }

            return await FinalizeReportAsync(
                item,
                preparation,
                item.SessionId,
                pendingStatus,
                pendingBody,
                publishCleanCheckpoint: true,
                cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<AiInputAttachment> stagedUserAttachments;
        try
        {
            stagedUserAttachments = UserAttachmentTransport.StageForWorkerRuntime(
                _userAttachments,
                preparation.RepositoryRoot,
                _jobId + "-" + executionWorkItemId);
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
        string? publishOutputDirectory = null;
        try
        {
            runtimePaths = WorkerPaths.GetRepositoryRuntimePaths(preparation.RepositoryRoot);
            workTempPath = WorkerPaths.BuildWorkTempPath(runtimePaths, _jobId, executionWorkItemId);
            WorkerPaths.EnsureWorkToolDirectories(runtimePaths, workTempPath);

            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in GitMetadataIsolationLease.BuildGitNonInteractiveEnvironment())
                environment[pair.Key] = pair.Value;
            foreach (var pair in WorkerPaths.BuildWorkToolEnvironment(runtimePaths, workTempPath))
                environment[pair.Key] = pair.Value;
            environment["PROJECTHUB_RESOURCE_TEMP"] = runtimePaths.TempRoot;
            environment["PROJECTHUB_WORK_TEMP"] = workTempPath;
            if (string.Equals(item.Id, FixedWorkItemSlots.BuildPublish, StringComparison.Ordinal))
            {
                publishOutputDirectory = WorkerPaths.GetPublishedArtifactDirectory(
                    preparation.RepositoryRoot,
                    _jobId,
                    item.CreatedOrder);
                Directory.CreateDirectory(publishOutputDirectory);
                environment["PROJECTHUB_PUBLISH_ROOT"] = publishOutputDirectory;
            }
            workEnvironment = environment;

            var writableDirectories = new List<string>
            {
                runtimePaths.NuGetRoot,
                runtimePaths.DotNetHome,
                workTempPath,
                Path.Combine(workTempPath, "build")
            };
            if (!string.IsNullOrWhiteSpace(publishOutputDirectory))
                writableDirectories.Add(publishOutputDirectory);
            if (!string.IsNullOrWhiteSpace(observationRequestDirectory))
                writableDirectories.Add(observationRequestDirectory);
            foreach (var snapshotPath in integrationSnapshots.Values)
            {
                if (!string.IsNullOrWhiteSpace(snapshotPath))
                    writableDirectories.Add(snapshotPath);
            }
            if (usesTargetWorkspace)
            {
                writableDirectories.Add(_workspace);
                environment["PROJECTHUB_TARGET_WORKSPACE"] = _workspace;
            }
            workWritableDirectories = writableDirectories
                .Distinct(OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
                .ToArray();
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

        if (string.Equals(inboundType, "BUILD_AUTHORIZED", StringComparison.Ordinal))
        {
            if (!BuildRequestContract.TryParseAuthorization(
                    inboundBody,
                    out var authorization,
                    out var authorizationError))
            {
                return WorkItemExecutionResult.Failed(
                    authorizationError ?? "BUILD_AUTHORIZATION_INVALID",
                    inboundBody,
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    "BUILD_AUTHORIZATION");
            }

            MechanicalProgress?.Invoke(new CodexWorkItemMechanicalProgress(
                item.Id,
                "BUILD",
                "HQ 승인 BUILD를 Worker가 기계 실행합니다.",
                true,
                item.CreatedOrder + 1));
            MechanicalBuildResult buildResult;
            try
            {
                buildResult = await MechanicalBuildExecutor.ExecuteAsync(
                    preparation.WorktreePath,
                    workTempPath,
                    workEnvironment,
                    authorization!,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var logPath = Path.Combine(workTempPath, "build", "build.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                var detail = exception.GetType().Name + ": " + exception.Message;
                try { await File.WriteAllTextAsync(logPath, detail, cancellationToken).ConfigureAwait(false); }
                catch { }

                return WorkItemExecutionResult.Failed(
                    "WORK_BUILD_EXECUTION_EXCEPTION",
                    "exceptionType=" + exception.GetType().Name + Environment.NewLine +
                    detail + Environment.NewLine + "logPath=" + logPath,
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    "BUILD_EXECUTION");
            }
            finally
            {
                MechanicalProgress?.Invoke(new CodexWorkItemMechanicalProgress(
                    item.Id,
                    "BUILD",
                    "HQ 승인 BUILD 기계 실행을 종료했습니다.",
                    false,
                    item.CreatedOrder + 1));
            }

            inboundType = "BUILD_RESULT";
            inboundBody = string.Join(
                Environment.NewLine,
                "BUILD_RESULT",
                "success: " + buildResult.Success.ToString().ToLowerInvariant(),
                "exitCode: " + buildResult.ExitCode,
                "target: " + buildResult.Target,
                "logPath: " + buildResult.LogPath,
                string.Empty,
                buildResult.Summary);
        }

        AiRoleRunResult runResult;

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
                    dependencyResults,
                    Checklist: item.Checklist),
                observationRequestDirectory,
                includeContract: string.IsNullOrWhiteSpace(sessionId),
                resourceStagingRoot: runtimePaths.TempRoot,
                workTempRoot: workTempPath,
                targetWorkspace: usesTargetWorkspace
                    ? _workspace
                    : null,
                publishOutputDirectory: publishOutputDirectory);

            string? startedSession = sessionId;
            var callStartedAt = DateTimeOffset.UtcNow;
            GitMetadataIsolationLease? gitIsolation = null;
            GitMetadataIsolationLease? targetWorkspaceGitIsolation = null;
            try
            {
                gitIsolation = GitMetadataIsolationLease.Detach(
                    preparation.WorktreePath,
                    _jobId,
                    executionWorkItemId);
                if (usesTargetWorkspace)
                {
                    targetWorkspaceGitIsolation = GitMetadataIsolationLease.Detach(
                        _workspace,
                        _jobId,
                        executionWorkItemId + "-target");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                try { targetWorkspaceGitIsolation?.Restore(); } catch { }
                try { gitIsolation?.Restore(); } catch { }
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
            var targetWorkspaceGitRestore = new GitMetadataRestoreResult(true);
            try
            {
                runResult = await _runner.RunAsync(new AiRoleRunRequest(
                    prompt,
                    _role,
                    executionWorkingDirectory,
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
                targetWorkspaceGitRestore =
                    targetWorkspaceGitIsolation?.Restore() ??
                    new GitMetadataRestoreResult(true);
                gitRestore =
                    gitIsolation?.Restore() ??
                    new GitMetadataRestoreResult(true);
            }

            if (!targetWorkspaceGitRestore.Success)
            {
                return WorkItemExecutionResult.Failed(
                    targetWorkspaceGitRestore.ErrorCode ?? "TARGET_WORKSPACE_GIT_METADATA_RESTORE_FAILED",
                    (targetWorkspaceGitRestore.ErrorDetail ?? "대상 프로젝트 Git metadata 복원에 실패했습니다.") +
                    (string.IsNullOrWhiteSpace(targetWorkspaceGitRestore.QuarantinePath)
                        ? string.Empty
                        : Environment.NewLine + "quarantine=" + targetWorkspaceGitRestore.QuarantinePath),
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    "TARGET_WORKSPACE_GIT_METADATA_RESTORE");
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
                    sessionId,
                    "GIT_METADATA_RESTORE");
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
                    "processExitCode=" + runResult.ExitCode + Environment.NewLine +
                    BuildFailureSummary(runResult.StandardError, runResult.FinalMessage),
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    "WORK_PROCESS");
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return WorkItemExecutionResult.Failed(
                    "WORK_SESSION_RESUME_FAILED",
                    "WORK 실행 후 이어갈 session ID가 없습니다.",
                    preparation.Branch,
                    preparation.WorktreePath,
                    null,
                    "WORK_SESSION");
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
                return WorkItemExecutionResult.Failed(
                    "WORK_ROUTE_" + route.Error,
                    runResult.FinalMessage,
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    "WORK_ROUTE");
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
                return WorkItemExecutionResult.Failed(
                    "WORK_ROUTE_UNSUPPORTED",
                    runResult.FinalMessage,
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    "WORK_ROUTE");
            }

            if (!WorkItemReportContract.TryParse(route.Body, out var report, out var reportError))
            {
                return WorkItemExecutionResult.Failed(
                    reportError ?? "WORK_ITEM_REPORT_INVALID",
                    route.Body,
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    "WORK_REPORT");
            }

            var reportBody = route.Body;
            return await FinalizeReportAsync(
                item,
                preparation,
                sessionId,
                report.Status,
                reportBody,
                publishCleanCheckpoint: item.ResultType == WorkItemResultType.CodeChange,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<WorkItemExecutionResult> FinalizeReportAsync(
        WorkItemSnapshot item,
        GitWorktreePreparationResult preparation,
        string? sessionId,
        WorkItemReportStatus reportStatus,
        string reportBody,
        bool publishCleanCheckpoint,
        CancellationToken cancellationToken)
    {
        if (string.Equals(item.Id, FixedWorkItemSlots.FileManager, StringComparison.Ordinal))
        {
            return await FinalizeFileManagerReportAsync(
                item,
                preparation,
                sessionId,
                reportStatus,
                reportBody,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(item.Id, FixedWorkItemSlots.BuildPublish, StringComparison.Ordinal))
        {
            return await FinalizeBuildPublishReportAsync(
                item,
                preparation,
                sessionId,
                reportStatus,
                reportBody,
                cancellationToken).ConfigureAwait(false);
        }

        GitWorktreeCheckpointResult checkpoint = default!;
        var checkpointCreatedCommit = false;
        var requireCleanCheckpointPublish = publishCleanCheckpoint;
        for (var attempt = 1; attempt <= MaximumCheckpointAttempts; attempt++)
        {
            checkpoint = await _worktrees.CreateCheckpointAsync(
                preparation.WorktreePath,
                item.Id,
                requireCleanCheckpointPublish,
                cancellationToken).ConfigureAwait(false);

            checkpointCreatedCommit |= checkpoint.CreatedCommit;
            if (checkpoint.CreatedCommit)
                requireCleanCheckpointPublish = true;

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
                BuildCheckpointPendingSummary(
                    reportStatus,
                    reportBody,
                    checkpoint.ErrorDetail),
                checkpoint.HeadCommit ?? item.ResultRef ?? preparation.HeadCommit,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId,
                blockDetailCode: errorCode,
                resultType: item.ResultType);
        }

        var lifecycleResultRef = checkpoint.HeadCommit ?? item.ResultRef;
        var recoveredCheckpointCommit =
            publishCleanCheckpoint &&
            !string.IsNullOrWhiteSpace(checkpoint.HeadCommit) &&
            !string.IsNullOrWhiteSpace(preparation.BaseCommit) &&
            !string.Equals(
                checkpoint.HeadCommit,
                preparation.BaseCommit,
                StringComparison.OrdinalIgnoreCase);
        var lifecycleHasCodeChange =
            item.ResultType == WorkItemResultType.CodeChange ||
            checkpointCreatedCommit ||
            recoveredCheckpointCommit;
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
                    "INTEGRATION_REMOTE_RESULT_MISSING",
                    reportBody + Environment.NewLine + Environment.NewLine +
                    "remoteResultError: checkpoint commit SHA가 없습니다.",
                    checkpoint.HeadCommit,
                    checkpoint.Branch ?? preparation.Branch,
                    checkpoint.WorktreePath,
                    sessionId,
                    blockDetailCode: "INTEGRATION_REMOTE_RESULT_MISSING",
                    resultType: completedResultType);
            }

            await TryRemoveCompletedIntegrationCloneAsync(
                item,
                preparation,
                checkpoint.WorktreePath,
                cancellationToken).ConfigureAwait(false);

            var completedSummary = completedResultType == WorkItemResultType.CodeChange
                ? reportBody + Environment.NewLine + Environment.NewLine +
                  "REMOTE_CODE_RESULT" + Environment.NewLine +
                  "resultRef: " + checkpoint.HeadCommit + Environment.NewLine +
                  "branch: " + (checkpoint.Branch ?? preparation.Branch)
                : reportBody;

            return WorkItemExecutionResult.Completed(
                checkpoint.HeadCommit,
                completedSummary,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId,
                completedResultType);
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
                completedResultType),
            WorkItemReportStatus.Blocked => WorkItemExecutionResult.Blocked(
                BuildRequestContract.ContainsRequest(reportBody)
                    ? "BUILD_REQUEST"
                    : "HQ_BLOCKED",
                reportBody,
                lifecycleResultRef,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId,
                resultType: blockedResultType),
            _ => WorkItemExecutionResult.Failed(
                "WORK_ITEM_REPORTED_FAILED",
                reportBody,
                checkpoint.Branch ?? preparation.Branch,
                checkpoint.WorktreePath,
                sessionId,
                "WORK_REPORT")
        };
    }

    private async Task<WorkItemExecutionResult> FinalizeFileManagerReportAsync(
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
            checkpoint = await _worktrees.CreateTargetWorkspaceCheckpointAsync(
                _workspace,
                preparation.BaseCommit ?? item.BaseRef ?? preparation.BaseRef,
                preparation.Branch,
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
            var errorCode = checkpoint.ErrorCode ?? "TARGET_WORKSPACE_CHECKPOINT_FAILED";
            return WorkItemExecutionResult.Blocked(
                "WORKTREE_CHECKPOINT_PENDING",
                BuildCheckpointPendingSummary(
                    reportStatus,
                    reportBody,
                    checkpoint.ErrorDetail),
                checkpoint.HeadCommit ?? item.ResultRef ?? preparation.HeadCommit,
                checkpoint.Branch ?? preparation.Branch,
                _workspace,
                sessionId,
                blockDetailCode: errorCode,
                resultType: item.ResultType);
        }

        var baseCommit = preparation.BaseCommit ?? preparation.HeadCommit;
        var lifecycleResultRef = checkpoint.HeadCommit ?? item.ResultRef;
        var lifecycleHasCodeChange =
            item.ResultType == WorkItemResultType.CodeChange ||
            checkpoint.CreatedCommit ||
            (!string.IsNullOrWhiteSpace(checkpoint.HeadCommit) &&
             !string.IsNullOrWhiteSpace(baseCommit) &&
             !string.Equals(
                 checkpoint.HeadCommit,
                 baseCommit,
                 StringComparison.OrdinalIgnoreCase));
        var completedResultType = lifecycleHasCodeChange
            ? WorkItemResultType.CodeChange
            : WorkItemResultType.Analysis;
        var blockedResultType = lifecycleHasCodeChange
            ? WorkItemResultType.CodeChange
            : item.ResultType;

        if (reportStatus == WorkItemReportStatus.Completed)
        {
            await TryRemoveCompletedNormalWorktreeAsync(
                item,
                preparation,
                new GitWorktreeCheckpointResult(
                    true,
                    null,
                    preparation.WorktreePath,
                    preparation.Branch,
                    preparation.HeadCommit,
                    false),
                cancellationToken).ConfigureAwait(false);
        }

        var completedSummary = reportBody;
        if (reportStatus == WorkItemReportStatus.Completed &&
            completedResultType == WorkItemResultType.CodeChange &&
            !string.IsNullOrWhiteSpace(lifecycleResultRef))
        {
            completedSummary =
                reportBody + Environment.NewLine + Environment.NewLine +
                "BOOTSTRAP_CODE_RESULT" + Environment.NewLine +
                "resultRef: " + lifecycleResultRef + Environment.NewLine +
                "branch: " + (checkpoint.Branch ?? preparation.Branch) + Environment.NewLine +
                "nextBaseRef: " + lifecycleResultRef;
        }

        return reportStatus switch
        {
            WorkItemReportStatus.Completed => WorkItemExecutionResult.Completed(
                lifecycleResultRef,
                completedSummary,
                checkpoint.Branch ?? preparation.Branch,
                _workspace,
                sessionId,
                completedResultType),
            WorkItemReportStatus.Blocked => WorkItemExecutionResult.Blocked(
                "HQ_BLOCKED",
                reportBody,
                lifecycleResultRef,
                checkpoint.Branch ?? preparation.Branch,
                _workspace,
                sessionId,
                resultType: blockedResultType),
            _ => WorkItemExecutionResult.Failed(
                "WORK_ITEM_REPORTED_FAILED",
                reportBody,
                checkpoint.Branch ?? preparation.Branch,
                _workspace,
                sessionId,
                "WORK_REPORT")
        };
    }

    private async Task<WorkItemExecutionResult> FinalizeBuildPublishReportAsync(
        WorkItemSnapshot item,
        GitWorktreePreparationResult preparation,
        string? sessionId,
        WorkItemReportStatus reportStatus,
        string reportBody,
        CancellationToken cancellationToken)
    {
        if (reportStatus == WorkItemReportStatus.Completed)
        {
            var publishOutputDirectory = WorkerPaths.GetPublishedArtifactDirectory(
                preparation.RepositoryRoot,
                _jobId,
                item.CreatedOrder);
            string[] publishedFiles;
            try
            {
                if (!Directory.Exists(publishOutputDirectory))
                {
                    return WorkItemExecutionResult.Blocked(
                        "PUBLISH_OUTPUT_MISSING",
                        reportBody + Environment.NewLine + Environment.NewLine +
                        "publishOutputError: 영구 게시 산출물 폴더를 찾을 수 없습니다." + Environment.NewLine +
                        "publishOutputDirectory: " + publishOutputDirectory,
                        preparation.HeadCommit,
                        preparation.Branch,
                        preparation.WorktreePath,
                        sessionId,
                        blockDetailCode: "PUBLISH_OUTPUT_MISSING");
                }

                publishedFiles = Directory.EnumerateFiles(
                        publishOutputDirectory,
                        "*",
                        new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = false,
                            AttributesToSkip = FileAttributes.ReparsePoint
                        })
                    .ToArray();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return WorkItemExecutionResult.Blocked(
                    "PUBLISH_OUTPUT_INSPECTION_FAILED",
                    reportBody + Environment.NewLine + Environment.NewLine +
                    "publishOutputError: " + exception.Message + Environment.NewLine +
                    "publishOutputDirectory: " + publishOutputDirectory,
                    preparation.HeadCommit,
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    blockDetailCode: "PUBLISH_OUTPUT_INSPECTION_FAILED");
            }

            if (publishedFiles.Length == 0)
            {
                return WorkItemExecutionResult.Blocked(
                    "PUBLISH_OUTPUT_EMPTY",
                    reportBody + Environment.NewLine + Environment.NewLine +
                    "publishOutputError: 영구 게시 산출물 폴더에 파일이 없습니다." + Environment.NewLine +
                    "publishOutputDirectory: " + publishOutputDirectory,
                    preparation.HeadCommit,
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    blockDetailCode: "PUBLISH_OUTPUT_EMPTY");
            }

            long publishedBytes = 0;
            foreach (var path in publishedFiles)
                publishedBytes += new FileInfo(path).Length;

            try
            {
                var sourceResultRef =
                    preparation.BaseCommit ??
                    preparation.HeadCommit ??
                    item.BaseRef;
                if (string.IsNullOrWhiteSpace(sourceResultRef))
                {
                    return WorkItemExecutionResult.Blocked(
                        "PUBLISH_SOURCE_REF_MISSING",
                        reportBody + Environment.NewLine + Environment.NewLine +
                        "publishSourceError: 원격 CODE_CHANGE 기준 commit을 확인할 수 없습니다.",
                        preparation.HeadCommit,
                        preparation.Branch,
                        preparation.WorktreePath,
                        sessionId,
                        blockDetailCode: "PUBLISH_SOURCE_REF_MISSING");
                }

                await _publishState.MarkPublishedAsync(
                    sourceResultRef,
                    item.CreatedOrder,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return WorkItemExecutionResult.Blocked(
                    "PUBLISH_STATE_WRITE_FAILED",
                    reportBody + Environment.NewLine + Environment.NewLine +
                    "publishStateError: " + exception.Message,
                    preparation.HeadCommit,
                    preparation.Branch,
                    preparation.WorktreePath,
                    sessionId,
                    blockDetailCode: "PUBLISH_STATE_WRITE_FAILED");
            }

            return WorkItemExecutionResult.Completed(
                "artifact-run-" + item.CreatedOrder.ToString("D12"),
                reportBody + Environment.NewLine + Environment.NewLine +
                "PUBLISH_OUTPUT" + Environment.NewLine +
                "path: " + publishOutputDirectory + Environment.NewLine +
                "fileCount: " + publishedFiles.Length + Environment.NewLine +
                "sizeBytes: " + publishedBytes,
                preparation.Branch,
                preparation.WorktreePath,
                sessionId,
                WorkItemResultType.Artifact);
        }

        if (reportStatus == WorkItemReportStatus.Blocked)
        {
            return WorkItemExecutionResult.Blocked(
                BuildRequestContract.ContainsRequest(reportBody)
                    ? "BUILD_REQUEST"
                    : "HQ_BLOCKED",
                reportBody,
                item.ResultRef,
                preparation.Branch,
                preparation.WorktreePath,
                sessionId,
                resultType: item.ResultType);
        }

        return WorkItemExecutionResult.Failed(
            "WORK_REPORTED_FAILED",
            reportBody,
            preparation.Branch,
            preparation.WorktreePath,
            sessionId,
            "WORK_REPORT");
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
                    "완료 WorkItem의 격리 clone 정리를 보류했습니다. " +
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
                "완료 WorkItem의 격리 clone 정리 중 기계 오류가 발생해 최종 runtime 정리로 넘깁니다. " +
                exception.GetType().Name,
                item.CreatedOrder + 1));
        }
    }

    private static string BuildCheckpointPendingSummary(
        WorkItemReportStatus status,
        string reportBody,
        string? checkpointDetail = null)
        => string.Join(
            Environment.NewLine,
            CheckpointPendingHeader,
            "reportStatus: " + status,
            string.IsNullOrWhiteSpace(checkpointDetail)
                ? string.Empty
                : "checkpointDetail: " + checkpointDetail.Trim(),
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
        if (!statusLine.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var statusToken = statusLine[prefix.Length..].Trim();
        if (string.Equals(statusToken, "SplitRequest", StringComparison.OrdinalIgnoreCase))
        {
            // 구버전 checkpoint 호환용이다. 새 WORK 출력에서는 SPLIT_REQUEST를 허용하지 않는다.
            status = WorkItemReportStatus.Blocked;
        }
        else if (!Enum.TryParse<WorkItemReportStatus>(
                     statusToken,
                     ignoreCase: true,
                     out status))
        {
            return false;
        }

        reportBody = string.Join(
            Environment.NewLine,
            lines.Skip(2)).TrimStart();
        return true;
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
