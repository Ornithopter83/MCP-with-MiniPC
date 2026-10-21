using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private readonly TemporaryWorkScopeRegistry _temporaryWorkScopes = new();
    private int _activeTemporaryWorkCount;

    private async Task<string> ExecuteMcpToolAsync(
        WorkerMcpSession session, string tool, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tool != "delegate_work" || session.IsWork)
            throw new InvalidOperationException("WORK_CANNOT_DELEGATE");
        if (!string.Equals(_activeProjectJobId, session.JobId, StringComparison.Ordinal) ||
            !string.Equals(Path.GetFullPath(_activeWorkingDirectory ?? "."),
                session.ProjectRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MCP_JOB_NOT_ACTIVE");
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("task", out var taskElement) ||
            taskElement.ValueKind != JsonValueKind.String)
            throw new ArgumentException("WORK_TASK_REQUIRED");
        var task = taskElement.GetString()?.Trim() ?? "";
        if (task.Length is < 1 or > 2000 ||
            !arguments.TryGetProperty("paths", out var pathsElement) ||
            pathsElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("WORK_TASK_OR_PATHS_INVALID");
        var readOnly = session.Role == "HIGH";
        if (arguments.TryGetProperty("read_only", out var readOnlyElement))
        {
            if (readOnlyElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArgumentException("WORK_READ_ONLY_INVALID");
            readOnly = readOnly || readOnlyElement.GetBoolean();
        }
        var paths = pathsElement.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!_temporaryWorkScopes.TryAcquire(session.ProjectRoot, paths, out var reservation))
            throw new InvalidOperationException("WORK_PATH_INVALID_OR_CONFLICT");
        using (reservation!)
        {
            var activeCount = Interlocked.Increment(ref _activeTemporaryWorkCount);
            RunOnUi(() =>
            {
                ImplementerWorkGaugeText.Text = FormatActiveWorkItemGauge(activeCount);
                UpdatePipelineVisuals();
            });
            try
            {
                var settings = _targetSettings.EffectiveImplementer with
                {
                    ThreadSessionId = null,
                    ThreadProjectPath = null
                };
                var reference = WorkerHistoryCardPolicy.NewInvocationReference(
                    session.JobId, "TEMP-WORK");
                RunOnUi(() => AddDataFlowHistory(WorkerRoleState.Work,
                    (session.Role == "HIGH" ? "HIGH → WORK 검증 위임" : "SuperVisor → WORK 배정"),
                    "TASK: " + task + Environment.NewLine +
                    "PATHS: " + string.Join(", ", paths),
                    settings.Provider, status: "DISPATCHED", referenceId: reference,
                    persistenceSource: "MCP DELEGATION"));

                var before = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    session.ProjectRoot, cancellationToken,
                    requireSuccess: true, scopedPaths: paths).ConfigureAwait(false);
                var parentName = session.Role == "HIGH" ? "HIGH" : "SuperVisor";
                var instructions = parentName + "가 배정한 단발성 하위 WORK다. 새 세션만 사용한다. " +
                    "HQ WORKITEM을 만들지 말고 " + parentName + "에게만 완료 결과와 미해결 항목을 보고하라. " +
                    "재위임과 Git commit/push는 금지한다. 지정된 파일 범위에서만 " +
                    (readOnly
                        ? "읽기 전용 조사·검증만 수행하라. 파일 변경은 금지한다."
                        : "구현·조사·테스트를 수행하라. 다른 파일 변경이 필요하면 보고하라.") +
                    Environment.NewLine + task;
                var prompt = RoleContractLoader.BuildDirectWorkPrompt(
                    "TEMP", instructions, paths, session.ProjectRoot,
                    readOnly: readOnly);
                // This is NOT a resumable HQ work-item and never enters its journal.
                // The child receives a separate token with no delegation tool.
                var childToken = _bridgeServer?.RegisterMcpSession(
                    "WORK", session.JobId, session.ProjectRoot,
                    cancellationToken, paths) ??
                    throw new InvalidOperationException("MCP_BRIDGE_NOT_RUNNING");
                try
                {
                    var result = await RunCoordinatorRoleAsync(
                        session.JobId, "TEMP_WORK", prompt, settings,
                        session.ProjectRoot, null, null, cancellationToken,
                        readOnly ? CodexSandboxMode.ReadOnly : CodexSandboxMode.WorkspaceWrite,
                        historyReferenceId: reference, mcpToken: childToken)
                        .ConfigureAwait(false);
                    var after = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        session.ProjectRoot, cancellationToken,
                        requireSuccess: true, scopedPaths: paths).ConfigureAwait(false);
                    var allChanges = MilestoneMechanicalExecutor.DiffChangeStates(
                        before, after).ToArray();
                    var siblingScopes = _temporaryWorkScopes.OtherConcurrentScopes(
                        session.ProjectRoot, paths);
                    var siblingChanges = allChanges.Where(change =>
                        siblingScopes.Any(scope =>
                            TemporaryWorkScopeRegistry.Overlaps(scope, change))).ToArray();
                    var ownChanges = allChanges.Except(siblingChanges,
                        StringComparer.OrdinalIgnoreCase).ToArray();
                    var scopedChanges = ownChanges.Where(change =>
                        paths.Any(path =>
                            TemporaryWorkScopeRegistry.Overlaps(path, change))).ToArray();
                    var unexpectedChanges = ownChanges.Except(scopedChanges,
                        StringComparer.OrdinalIgnoreCase).ToArray();
                    var parsedWork = RoleTextProtocol.ParseWork(result.FinalMessage);
                    var completed = result.ExitCode == 0 &&
                        parsedWork.IsValid &&
                        string.Equals(parsedWork.Status, "completed",
                            StringComparison.OrdinalIgnoreCase) &&
                        unexpectedChanges.Length == 0 &&
                        (!readOnly || ownChanges.Length == 0);
                    var summary = "INVOCATION: " + reference +
                        Environment.NewLine + "EXIT: " + result.ExitCode +
                        Environment.NewLine + "MODEL: " + result.Model +
                        Environment.NewLine + "TOKENS: " + result.Usage.TotalTokens +
                        Environment.NewLine + "CHANGED_PATHS: " +
                        string.Join(", ", scopedChanges.Take(30)) +
                        Environment.NewLine + "CONCURRENT_WORK_CHANGES: " +
                        string.Join(", ", siblingChanges.Take(30)) +
                        Environment.NewLine + "OUT_OF_SCOPE_CHANGES: " +
                        string.Join(", ", unexpectedChanges.Take(30)) +
                        Environment.NewLine + "RESULT:" + Environment.NewLine +
                        MilestoneDefinitionContract.Limit(result.FinalMessage, 5000);
                    RunOnUi(() => AddRoleResponseHistory(WorkerRoleState.Work,
                        "임시 WORK 결과", summary, result.Usage, result.Files,
                        status: completed ? "COMPLETED" : "BLOCKED",
                        providerWireId: settings.Provider, referenceId: reference));
                    if (readOnly && ownChanges.Length != 0)
                        throw new InvalidOperationException("WORK_READ_ONLY_VIOLATION: " +
                            MilestoneDefinitionContract.Limit(summary, 1200));
                    if (unexpectedChanges.Length != 0)
                        throw new InvalidOperationException("WORK_SCOPE_VIOLATION: " +
                            MilestoneDefinitionContract.Limit(summary, 1200));
                    // In-progress or malformed reports may represent partial,
                    // unaccounted changes. They must not unlock direct fallback.
                    if (!parsedWork.IsValid)
                        throw new InvalidOperationException("WORK_REPORT_INVALID: " +
                            MilestoneDefinitionContract.Limit(summary, 1200));
                    if (string.Equals(parsedWork.Status, "in_progress",
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("WORK_IN_PROGRESS: " +
                            MilestoneDefinitionContract.Limit(summary, 1200));
                    if (!completed)
                        throw new InvalidOperationException("TEMP_WORK_FAILED: " +
                            MilestoneDefinitionContract.Limit(summary, 1200));
                    return summary;
                }
                finally
                {
                    _bridgeServer?.UnregisterMcpSession(childToken);
                }
            }
            finally
            {
                var remaining = Interlocked.Decrement(ref _activeTemporaryWorkCount);
                RunOnUi(() =>
                {
                    ImplementerWorkGaugeText.Text = FormatActiveWorkItemGauge(remaining);
                    UpdatePipelineVisuals();
                });
            }
        }
    }
}
