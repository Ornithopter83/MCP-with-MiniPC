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

        if (tool == "delegate_work")
        {
            if (session.IsWork)
                throw new InvalidOperationException("WORK_CANNOT_DELEGATE");
            return await RunTemporaryMcpWorkAsync(
                session, arguments, cancellationToken).ConfigureAwait(false);
        }
        if (tool != "worker_command")
            throw new InvalidOperationException("WORKER_MCP_UNKNOWN_TOOL");
        if (!session.IsWork)
            return await RunWorkerMcpCommandAsync(
                session, arguments, cancellationToken).ConfigureAwait(false);

        // Read-only status/discovery should not repeatedly scan Git or
        // attribute concurrent file-only WORK edits to this MCP session.
        var action = RequiredMcpString(arguments, "action", 40);
        if (action == "editor_bridge_install" &&
            !session.AllowedPaths.Any(scope =>
                MatchesMcpScope(scope, UnityEditorDirectBridge.EditorSourceRelativePath)))
            throw new InvalidOperationException(
                "EDITOR_BRIDGE_INSTALL_REQUIRES_EXPLICIT_SOURCE_PATH_IN_WORK_SCOPE");
        if (action is not ("recompile" or "editor_eval" or "visual_foundry" or
            "editor_bridge_install" or "editor_bridge_invoke" or "editor_bridge_menu"))
            return await RunWorkerMcpCommandAsync(
                session, arguments, cancellationToken).ConfigureAwait(false);
        // Best-effort after-execution audit: not a Windows filesystem sandbox.
        var before = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
            session.ProjectRoot, cancellationToken, requireSuccess: true)
            .ConfigureAwait(false);
        string? output = null;
        Exception? failure = null;
        try
        {
            output = await RunWorkerMcpCommandAsync(
                session, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        var after = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
            session.ProjectRoot, cancellationToken, requireSuccess: true)
            .ConfigureAwait(false);
        var unexpected = MilestoneMechanicalExecutor.DiffChangeStates(before, after)
            .Where(path => !session.AllowedPaths.Any(scope =>
                MatchesMcpScope(scope, path))).ToArray();
        if (unexpected.Length > 0)
            throw new InvalidOperationException("WORK_MCP_OUT_OF_SCOPE_CHANGES: " +
                string.Join(", ", unexpected.Take(15)));
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return output!;
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
        var invalidPaths = paths.Where(path => !IsAllowedTemporaryPath(path)).ToArray();
        if (paths.Length is < 1 or > 5 || invalidPaths.Length > 0)
            throw new ArgumentException("WORK_PATHS_MUST_BE_SCOPED_TO_ASSETS_OR_DOCS: " +
                "use one to five specific project-relative paths, e.g. " +
                "docs/projecthub/m110-environment/audit.md or Assets/Scripts/Quiz.cs; " +
                "invalid=" + string.Join(", ", invalidPaths.Select(path =>
                    MilestoneDefinitionContract.Limit(path, 100))));

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
            var instruction = "당신은 " + session.Role +
                "가 자체 위임한 단발성 WORK다. HQ 마일스톤·WORK 번호를 만들거나 " +
                "다른 역할의 전문 판단을 대신하지 마라. 명시된 과업에 필요한 " +
                "셸·Unity CLI/MCP·Editor 명령·빌드·테스트 실행 권한을 갖는다. " +
                "공유 Editor 작업은 MCP worker_command로 실행한다. " +
                "다른 WORK로 재위임하거나 Git을 변경하지 마라. 파일 작성·수정은 " +
                "허용된 좁은 경로로 제한하고 필요한 추가 경로는 보고하라. " +
                "결과와 증거는 위임한 부모 역할에만 반환한다." +
                Environment.NewLine + "허용 파일 범위:" + Environment.NewLine +
                string.Join(Environment.NewLine, paths) +
                Environment.NewLine + "작업:" + Environment.NewLine + task +
                Environment.NewLine + Environment.NewLine +
                UsageTelemetryStore.FormatForNextRole(session.JobId) +
                Environment.NewLine + Environment.NewLine +
                UnityEditorExecutionGuidance.Text;

            // Bypass the parent role wrapper: it consumes pending user messages,
            // creates HQ-visible handoffs and may re-enter its own MCP tools.
            // Child has its own command-only MCP token, no parent token or HQ ID.
            var childToken = _bridgeServer?.RegisterMcpSession(
                "WORK", session.JobId, session.ProjectRoot, cancellationToken,
                allowedPaths: paths) ??
                throw new InvalidOperationException("WORKER_MCP_NOT_AVAILABLE");
            try
            {
                var childTemp = UnityRoleRuntimePaths.Resolve(
                    session.JobId, "TEMP-WORK-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(childTemp);
                var childEnvironment = new Dictionary<string, string>
                {
                    ["PROJECTHUB_MCP_BEARER"] = childToken,
                    ["PROJECTHUB_CODEX_TEMP"] = childTemp
                };
                var childOverrides = new List<string>();
                var bypassHookTrust = false;
                if (WorkerAutonomyPolicy.Enabled)
                {
                    var hook = Path.Combine(childTemp, "projecthub-unity-background-gate.ps1");
                    await File.WriteAllTextAsync(hook,
                        BuildExecutionPolicy.CreateUnityBackgroundPreToolHookScript(),
                        ProjectHubJson.Utf8NoBom, cancellationToken).ConfigureAwait(false);
                    childOverrides.Add(
                        BuildExecutionPolicy.BuildCodexPreToolHookOverride(
                            hook, "UnityWorker foreground protection"));
                    bypassHookTrust = true;
                }
                if (roleSettings.ProviderKind == AiServiceProvider.OpenAI)
                    childOverrides.AddRange(BridgeServer.CodexMcpOverrides());
                var result = await runner.RunAsync(new AiRoleRunRequest(
                    instruction, roleSettings, session.ProjectRoot, null,
                    CodexSandboxMode.WorkspaceWrite, cancellationToken,
                    Progress: message => AddRoleProgressHistory(
                        WorkerRoleState.Work, message, roleSettings.Provider,
                        referenceId: reference),
                    EnvironmentVariables: childEnvironment,
                    AdditionalWritableDirectories: new[] { childTemp },
                    CodexConfigOverrides: childOverrides,
                    BypassHookTrust: bypassHookTrust,
                    DisableComputerUse: false,
                    IncludeAppBaseWritable: false,
                    BuildExecutionAllowed: true)).ConfigureAwait(false);

                UsageTelemetryStore.RecordCall(session.JobId, "WORK",
                    result.Model, result.Reasoning, "TEMP_WORK", result.Usage);
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
            finally
            {
                _bridgeServer?.UnregisterMcpSession(childToken);
            }
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
        if (action == "status")
            return await RunStatusCommandAsync(session.ProjectRoot,
                cancellationToken).ConfigureAwait(false);
        // Acquire once per MCP command unless the direct WORK scheduler already
        // owns the project lane. The probe, eval and mutation share this lease.
        using var editorLease = session.EditorLaneHeld ? null :
            await UnityEditorOperationGate.EnterAsync(
                session.ProjectRoot, cancellationToken).ConfigureAwait(false);
        if (action == "editor_bridge_install")
            return UnityEditorDirectBridge.Install(session.ProjectRoot);
        if (action is "editor_bridge_ping" or "editor_bridge_scene_status" or
            "editor_bridge_invoke" or "editor_bridge_menu")
            return await UnityEditorCommandGateway.ExecuteBridgeAsync(
                session.ProjectRoot, action,
                action == "editor_bridge_invoke"
                    ? RequiredMcpString(arguments, "type", 250) : null,
                action == "editor_bridge_invoke"
                    ? RequiredMcpString(arguments, "method", 150) : null,
                action == "editor_bridge_menu"
                    ? RequiredMcpString(arguments, "menu_path", 300) : null,
                action is "editor_bridge_menu" or "editor_bridge_invoke"
                    ? RequiredMcpString(arguments, "expected_result", 250) : null,
                cancellationToken).ConfigureAwait(false);
        if (action == "native_capture")
        {
            var plan = UnityBackgroundVisualTools.Prepare(session.ProjectRoot,
                session.JobId, "mcp-native-" + Guid.NewGuid().ToString("N"));
            var evidence = await UnityBackgroundVisualTools.CaptureNativeAsync(
                plan, cancellationToken).ConfigureAwait(false);
            if (!evidence.Captured || evidence.ImagePath is null)
                throw new InvalidOperationException("NATIVE_UNVERIFIED: " + evidence.Detail);
            session.NativeScreenshot = await File.ReadAllBytesAsync(
                evidence.ImagePath, cancellationToken).ConfigureAwait(false);
            return evidence.Detail;
        }
        if (action == "visual_foundry")
        {
            if (session.Role is not ("DESIGN" or "WORK"))
                throw new ArgumentException("VISUAL_FOUNDRY_REQUIRES_DESIGN_OR_DELEGATED_WORK");
            if (session.Role == "DESIGN" &&
                Volatile.Read(ref session.SuccessfulDelegations) == 0)
                throw new InvalidOperationException("VISUAL_FOUNDRY_REQUIRES_PRIOR_DELEGATE_WORK");
            var style = RequiredMcpString(arguments, "style", 30);
            var layout = RequiredMcpString(arguments, "layout", 30);
            var description = RequiredMcpString(arguments, "description", 1000);
            // DESIGN selects whether to generate and which style/layout.
            // Worker only performs the requested Editor operation.
            var plan = UnityVisualFoundry.Prepare(session.ProjectRoot,
                session.JobId, "mcp-foundry-" + session.InvocationId + "-" +
                    style + "-" + layout, description, style, layout);
            if (session.IsWork && !session.AllowedPaths.Any(scope =>
                MatchesMcpScope(scope, plan.AssetDirectory)))
                throw new InvalidOperationException(
                    "VISUAL_FOUNDRY_TARGET_OUT_OF_WORK_SCOPE: " + plan.AssetDirectory);
            var generated = await UnityVisualFoundry.GenerateAsync(
                plan, cancellationToken).ConfigureAwait(false);
            if (!generated.Success)
                throw new InvalidOperationException(generated.Detail);
            var runtime = UnityVisualFoundryRuntime.Install(session.ProjectRoot);
            return generated.Detail + "; " + runtime.Detail + "; SELECTED_STYLE=" +
                style + "; SELECTED_LAYOUT=" + layout + Environment.NewLine +
                plan.AgentGuidance;
        }
        if (action == "recompile")
        {
            var result = await UnityCliBuildRunner.RunAsync(
                session.ProjectRoot, session.JobId, cancellationToken,
                editorLaneHeld: true)
                .ConfigureAwait(false);
            return "BUILD_EXIT=" + result.ExitCode +
                "; LOG=" + result.LogPath +
                "; " + MilestoneDefinitionContract.Limit(result.Summary, 2000);
        }

        if (action == "command_catalog" || action == "unity_query" ||
            action == "editor_eval")
        {
            var capabilities = await UnityEditorCommandGateway.DiscoverAsync(
                session.ProjectRoot, cancellationToken).ConfigureAwait(false);
            if (!capabilities.Connection.Ready)
                throw new InvalidOperationException(
                    capabilities.Connection.Code + ": " + capabilities.Connection.Detail);
            if (action == "command_catalog")
                return "CATALOG_STATUS=" + capabilities.Connection.Code +
                    "; CATALOG=" + (capabilities.Catalog ?? "UNVERIFIED_NO_COMMANDS") +
                    "; DIRECT_BRIDGE_SOURCE=" +
                    (File.Exists(UnityEditorDirectBridge.EditorSourcePath(session.ProjectRoot))
                        ? "PRESENT_BUT_NOT_PING_VERIFIED" : "NOT_INSTALLED") +
                    "; " + capabilities.Evidence;
            if (action == "editor_eval")
                return await UnityEditorCommandGateway.ExecuteEvalAsync(
                    session.ProjectRoot, capabilities,
                    RequiredMcpString(arguments, "expression", 5000),
                    RequiredMcpString(arguments, "expected_result", 250),
                    cancellationToken).ConfigureAwait(false);
            var name = RequiredMcpString(arguments, "command", 60);
            if (name == "editor_state")
            {
                // editor_state is a logical status request, not a guessed
                // Editor command; route it to the verified CLI status API.
                return await RunStatusCommandAsync(session.ProjectRoot,
                    cancellationToken).ConfigureAwait(false);
            }
            if (name is not ("console" or "list_open_scenes" or
                "get_scene_hierarchy" or "recompile_status"))
                throw new ArgumentException("UNITY_COMMAND_NOT_ALLOWLISTED");
            if (!UnityToolchainPreflight.CatalogContainsCommand(
                    capabilities.Catalog, name))
                throw new InvalidOperationException(
                    "UNITY_COMMAND_NOT_IN_LIVE_CATALOG: " + name +
                    "; request command_catalog for available commands");
            var result = await UnityCliProcessRunner.RunAsync(
                session.ProjectRoot,
                ["command", name, "--project-path", session.ProjectRoot,
                    "--format", "json"],
                TimeSpan.FromSeconds(75), "UnityWorker MCP unity_query",
                "UNITY_MCP_COMMAND_TIMEOUT", cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("COMMAND_EXIT=" + result.ExitCode +
                    "; " + MilestoneDefinitionContract.Limit(
                        result.Output + Environment.NewLine + result.Error, 4500));
            return "COMMAND_EXIT=0" + Environment.NewLine +
                MilestoneDefinitionContract.Limit(
                    result.Output + Environment.NewLine + result.Error, 4500) +
                Environment.NewLine + "EVIDENCE=" + result.EvidencePath;
        }
        throw new ArgumentException("WORKER_COMMAND_NOT_ALLOWLISTED");
    }

    private static async Task<string> RunStatusCommandAsync(
        string projectRoot, CancellationToken token)
    {
        var result = await UnityCliProcessRunner.RunAsync(
            projectRoot, ["status", "--project-path", projectRoot, "--format", "json"],
            TimeSpan.FromSeconds(75), "UnityWorker MCP status",
            "UNITY_MCP_STATUS_TIMEOUT", token).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("COMMAND_EXIT=" + result.ExitCode +
                "; " + MilestoneDefinitionContract.Limit(
                    result.Output + Environment.NewLine + result.Error, 4500));
        return "COMMAND_EXIT=0" + Environment.NewLine +
            MilestoneDefinitionContract.Limit(
                result.Output + Environment.NewLine + result.Error, 4500);
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

    internal static bool IsAllowedTemporaryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 250 ||
            Path.IsPathRooted(value) || value.IndexOf(':') >= 0)
            return false;
        var path = value.Replace('\\', '/');
        if (path.Contains("//", StringComparison.Ordinal) ||
            path.Split('/').Any(part => part is "." or ".."))
            return false;
        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
            return false;
        // A child may own a specific file or a narrow subtree, never all Assets/.
        if (path.EndsWith("/", StringComparison.Ordinal))
            return path.TrimEnd('/').Split('/').Length >= 3;
        return path.Split('/').Length >= 3;
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
