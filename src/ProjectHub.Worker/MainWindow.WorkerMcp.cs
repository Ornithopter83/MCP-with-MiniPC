using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private readonly TemporaryWorkScopeRegistry _temporaryWorkScopes = new();

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
        var paths = pathsElement.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!_temporaryWorkScopes.TryAcquire(session.ProjectRoot, paths, out var reservation))
            throw new InvalidOperationException("WORK_PATH_INVALID_OR_CONFLICT");
        using (reservation!)
        {
            var settings = _targetSettings.EffectiveImplementer with
            {
                ThreadSessionId = null,
                ThreadProjectPath = null
            };
            var reference = WorkerHistoryCardPolicy.NewInvocationReference(
                session.JobId, "TEMP-WORK");
            RunOnUi(() => AddDataFlowHistory(WorkerRoleState.Work,
                "SV → WORK 배정", "TASK: " + task + Environment.NewLine +
                "PATHS: " + string.Join(", ", paths),
                settings.Provider, status: "DISPATCHED", referenceId: reference,
                persistenceSource: "MCP DELEGATION"));

            var before = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                session.ProjectRoot, cancellationToken).ConfigureAwait(false);
            var instructions = "SV가 배정한 단발성 하위 WORK다. 새 세션만 사용한다. " +
                "HQ WORKITEM을 만들지 말고 SV에게만 완료 결과와 미해결 항목을 보고하라. " +
                "재위임과 Git commit/push는 금지한다. 지정된 파일 범위에서만 " +
                "구현·조사·테스트를 수행하라. 다른 파일 변경이 필요하면 보고하라." +
                Environment.NewLine + task;
            var prompt = RoleContractLoader.BuildDirectWorkPrompt(
                "TEMP", instructions, paths, session.ProjectRoot);
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
                    CodexSandboxMode.WorkspaceWrite,
                    historyReferenceId: reference, mcpToken: childToken)
                    .ConfigureAwait(false);
                var after = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    session.ProjectRoot, cancellationToken).ConfigureAwait(false);
                var allChanges = MilestoneMechanicalExecutor.DiffChangeStates(
                    before, after).ToArray();
                var scopedChanges = allChanges.Where(change =>
                    paths.Any(path => TemporaryWorkScopeRegistry.Overlaps(path, change)))
                    .ToArray();
                var summary = "INVOCATION: " + reference +
                    Environment.NewLine + "EXIT: " + result.ExitCode +
                    Environment.NewLine + "MODEL: " + result.Model +
                    Environment.NewLine + "TOKENS: " + result.Usage.TotalTokens +
                    Environment.NewLine + "CHANGED_PATHS: " +
                    string.Join(", ", scopedChanges.Take(30)) +
                    Environment.NewLine + "RESULT:" + Environment.NewLine +
                    MilestoneDefinitionContract.Limit(result.FinalMessage, 5000);
                RunOnUi(() => AddRoleResponseHistory(WorkerRoleState.Work,
                    "임시 WORK 결과", summary, result.Usage, result.Files,
                    status: result.ExitCode == 0 ? "COMPLETED" : "BLOCKED",
                    providerWireId: settings.Provider, referenceId: reference));
                if (result.ExitCode != 0)
                    throw new InvalidOperationException("TEMP_WORK_FAILED: " +
                        MilestoneDefinitionContract.Limit(summary, 1200));
                return summary;
            }
            finally
            {
                _bridgeServer?.UnregisterMcpSession(childToken);
            }
        }
    }
}
