using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    // No HQ allocation, journal or user-message dequeue occurs in temporary WORK.
    private readonly SemaphoreSlim _temporaryWorkSerial = new(1, 1);

    private async Task<string> ExecuteMcpToolAsync(
        WorkerMcpSession session, string tool, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(_activeProjectJobId, session.JobId, StringComparison.Ordinal) ||
            !string.Equals(Path.GetFullPath(_activeWorkingDirectory ?? "."),
                session.ProjectRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WORKER_MCP_JOB_NOT_ACTIVE");
        if (!UnityCliBuildRunner.IsUnityProject(session.ProjectRoot))
            throw new InvalidOperationException("WORKER_MCP_UNITY_PROJECT_REQUIRED");

        return tool switch
        {
            "delegate_work" => await RunTemporaryMcpWorkAsync(
                session, arguments, cancellationToken).ConfigureAwait(false),
            "worker_command" => await RunWorkerMcpCommandAsync(
                session, arguments, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("WORKER_MCP_UNKNOWN_TOOL")
        };
    }

    private async Task<string> RunTemporaryMcpWorkAsync(
        WorkerMcpSession session, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var task = RequiredMcpString(arguments, "task", 2000);
        if (!arguments.TryGetProperty("paths", out var pathArray) ||
            pathArray.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("WORK_PATHS_REQUIRED");
        var paths = pathArray.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length is < 1 or > 5 || paths.Any(path => !IsAllowedTemporaryPath(path)))
            throw new ArgumentException("WORK_PATHS_MUST_BE_SCOPED_TO_ASSETS_OR_DOCS");

        await _temporaryWorkSerial.WaitAsync(cancellationToken).ConfigureAwait(false);
        var reference = WorkerHistoryCardPolicy.NewInvocationReference(
            session.JobId, "TEMP-WORK");
        var roleSettings = await Dispatcher.InvokeAsync(() =>
            NormalizeRoleSessionForWorkspace(
                _targetSettings.EffectiveImplementer, session.ProjectRoot));
        var runner = _aiRoleRunners.Resolve(roleSettings) ??
            throw new InvalidOperationException("WORK_PROVIDER_NOT_AVAILABLE");
        var parentStage = session.Role switch
        {
            "HIGH" => TaskStage.HighLevel,
            "ASSET_MANAGER" => TaskStage.Manager,
            _ => TaskStage.Resource
        };
        try
        {
            RunOnUi(() =>
            {
                SetFlowState(codexActive: true, workerActive: true,
                    webActive: false, explicitStage: TaskStage.Implementer);
                AddRoleProgressHistory(WorkerRoleState.Work,
                    "임시 WORK · " + session.Role + " 요청 · 모델 " +
                    roleSettings.Model + " / " + roleSettings.Reasoning,
                    roleSettings.Provider, referenceId: reference);
            });
            var before = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                session.ProjectRoot, cancellationToken).ConfigureAwait(false);
            var instruction = "당신은 상위 " + session.Role + " 역할이 호출한 일회성 WORK다. " +
                "아래 단순 작업 하나만 수행하라. 새 계획이나 하위 위임을 만들지 마라. " +
                "허용 경로 외 수정, Git 변경, Unity 빌드·테스트·Editor/CLI 호출은 금지한다. " +
                "필요한 최소 파일만 읽고, 불명확하면 추측하거나 장시간 조사하지 말고 보고하라. " +
                "작업을 완료한 뒤 변경한 파일과 남은 문제를 간단히 설명하라." +
                Environment.NewLine + "허용 경로:" + Environment.NewLine +
                string.Join(Environment.NewLine, paths) +
                Environment.NewLine + "작업:" + Environment.NewLine + task;

            // Bypass the parent role wrapper: it consumes pending user messages,
            // creates HQ-visible handoffs and may re-enter its own MCP tools.
            var result = await runner.RunAsync(new AiRoleRunRequest(
                instruction, roleSettings, session.ProjectRoot, null,
                CodexSandboxMode.WorkspaceWrite, cancellationToken,
                Progress: message => AddRoleProgressHistory(
                    WorkerRoleState.Work, message, roleSettings.Provider,
                    referenceId: reference),
                DisableComputerUse: true,
                IncludeAppBaseWritable: false,
                BuildExecutionAllowed: false)).ConfigureAwait(false);

            var after = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                session.ProjectRoot, cancellationToken).ConfigureAwait(false);
            var changes = MilestoneMechanicalExecutor.DiffChangeStates(before, after)
                .ToArray();
            var unexpected = changes.Where(path => !paths.Any(scope =>
                MatchesMcpScope(scope, path))).ToArray();
            var summary = "TEMP_WORK_EXIT=" + result.ExitCode +
                "; MODEL=" + result.Model + "; REASONING=" + result.Reasoning +
                "; TOKENS=" + result.Usage.TotalTokens +
                "; CHANGED=" + string.Join(", ", changes.Take(20)) +
                (unexpected.Length == 0 ? "" :
                    "; OUT_OF_SCOPE_CHANGES=" + string.Join(", ", unexpected.Take(20))) +
                Environment.NewLine +
                MilestoneDefinitionContract.Limit(result.FinalMessage, 4500);
            RunOnUi(() => AddRoleResponseHistory(
                WorkerRoleState.Work, "임시 WORK 완료", summary,
                result.Usage, result.Files,
                status: result.ExitCode == 0 && unexpected.Length == 0
                    ? "COMPLETED" : "BLOCKED",
                providerWireId: roleSettings.Provider,
                referenceId: reference));
            if (result.ExitCode != 0 || unexpected.Length > 0)
                throw new InvalidOperationException("TEMP_WORK_FAILED_OR_SCOPE_VIOLATION: " +
                    MilestoneDefinitionContract.Limit(summary, 1800));
            return summary;
        }
        catch (Exception ex)
        {
            RunOnUi(() => AddRoleResponseHistory(
                WorkerRoleState.Work, "임시 WORK 실패",
                ex.Message, status: "FAILED",
                providerWireId: roleSettings.Provider,
                referenceId: reference));
            throw;
        }
        finally
        {
            RunOnUi(() => SetFlowState(
                codexActive: true, workerActive: false, webActive: false,
                explicitStage: parentStage));
            _temporaryWorkSerial.Release();
        }
    }

    private async Task<string> RunWorkerMcpCommandAsync(
        WorkerMcpSession session, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var action = RequiredMcpString(arguments, "action", 40);
        if (action == "recompile")
        {
            var result = await UnityCliBuildRunner.RunAsync(
                session.ProjectRoot, session.JobId, cancellationToken)
                .ConfigureAwait(false);
            return "BUILD_EXIT=" + result.ExitCode +
                "; LOG=" + result.LogPath +
                "; " + MilestoneDefinitionContract.Limit(result.Summary, 2000);
        }

        string[] args;
        if (action == "status")
            args = ["status", "--project-path", session.ProjectRoot, "--format", "json"];
        else if (action == "unity_query")
        {
            var name = RequiredMcpString(arguments, "command", 60);
            if (name is not ("console" or "editor_state" or "list_open_scenes" or
                "get_scene_hierarchy" or "recompile_status"))
                throw new ArgumentException("UNITY_COMMAND_NOT_ALLOWLISTED");
            args = ["command", name, "--project-path", session.ProjectRoot, "--format", "json"];
        }
        else
            throw new ArgumentException("WORKER_COMMAND_NOT_ALLOWLISTED");

        using var editorLease = await UnityEditorOperationGate.EnterAsync(
            session.ProjectRoot, cancellationToken).ConfigureAwait(false);
        var resultCli = await UnityCliProcessRunner.RunAsync(
            session.ProjectRoot, args, TimeSpan.FromSeconds(75),
            "UnityWorker MCP " + action, "UNITY_MCP_COMMAND_TIMEOUT",
            cancellationToken).ConfigureAwait(false);
        var output = "COMMAND_EXIT=" + resultCli.ExitCode +
            Environment.NewLine + MilestoneDefinitionContract.Limit(
                resultCli.Output + Environment.NewLine + resultCli.Error, 4500);
        if (resultCli.ExitCode != 0)
            throw new InvalidOperationException(output);
        return output;
    }

    private static string RequiredMcpString(JsonElement args, string name, int max)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) ||
            value.GetString()!.Length > max)
            throw new ArgumentException("WORKER_MCP_INVALID_" + name);
        return value.GetString()!.Trim();
    }

    private static bool IsAllowedTemporaryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 250 ||
            Path.IsPathRooted(value) || value.IndexOf(':') >= 0)
            return false;
        var path = value.Replace('\\', '/');
        if (path.Split('/').Any(part => part is "." or ".." || part.Length == 0 && part != path.Split('/').Last()))
            return false;
        return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesMcpScope(string scope, string changedPath)
    {
        var normalizedScope = scope.Replace('\\', '/').TrimEnd('/');
        var normalizedChange = changedPath.Replace('\\', '/');
        return string.Equals(normalizedScope, normalizedChange,
                   StringComparison.OrdinalIgnoreCase) ||
               (scope.EndsWith("/", StringComparison.Ordinal) &&
                normalizedChange.StartsWith(normalizedScope + "/",
                    StringComparison.OrdinalIgnoreCase));
    }
}
