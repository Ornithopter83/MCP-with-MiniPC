using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private sealed record AssetManagerRunResult(
        bool Success, string Detail, IReadOnlyList<string> ChangedPaths);

    private async Task<AssetManagerRunResult> ExecuteMilestoneAssetManagerAsync(
        string jobId, string root, MilestoneDefinition milestone,
        AssetManagerDefinition definition, WorkerAiRoleSettings implementer,
        CancellationToken cancellationToken)
    {
        AssetManagerCheckpoint checkpoint;
        try
        {
            checkpoint = AssetManagerExecutionJournal.Open(
                root, jobId, definition, implementer);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or ArgumentException)
        {
            return new(false, "CHECKPOINT_ERROR: " + e.Message, Array.Empty<string>());
        }

        var historyId = milestone.Id + ":ASSET_MANAGER:1:" + definition.RequestId;
        var isolatedRole = implementer with { ThreadSessionId = null, ThreadProjectPath = null };
        var phases = new[] { ("DISCOVER", "DISCOVERED"), ("ACQUIRE", "ACQUIRED"),
            ("INTEGRATE", "INTEGRATED") };
        var evidence = new List<string>();
        foreach (var (phase, completedState) in phases)
        {
            if (checkpoint.State == "INTEGRATED" ||
                checkpoint.State == completedState ||
                (phase == "DISCOVER" && checkpoint.State is "ACQUIRED" or "RUNNING_ACQUIRE") ||
                (phase != "INTEGRATE" && checkpoint.State == "RUNNING_INTEGRATE") ||
                (phase == "ACQUIRE" && checkpoint.State == "ACQUIRED"))
                continue;

            var expectedState = phase switch
            {
                "DISCOVER" => "READY",
                "ACQUIRE" => "DISCOVERED",
                _ => "ACQUIRED"
            };
            if (checkpoint.State != expectedState)
                return new(false, "ASSET_MANAGER_RECONCILIATION_REQUIRED: " +
                    checkpoint.State + " (동일 REQUEST_ID의 변경을 자동 재실행하지 않음)",
                    checkpoint.ChangedPaths);

            RunOnUi(() =>
            {
                TaskDirection.Text = "ASSET MANAGER";
                TaskTitle.Text = "#1 ASSET MANAGER · " + phase;
                ResultTitle.Text = "#1 ASSET MANAGER";
                SetFlowState(codexActive: true, workerActive: true,
                    webActive: false, explicitStage: TaskStage.Manager);
            });

            var before = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                root, cancellationToken);
            // Store the running phase before invoking Codex: an interruption
            // cannot accidentally replay an Import or scene edit.
            checkpoint = checkpoint with { State = "RUNNING_" + phase };
            AssetManagerExecutionJournal.Save(root, checkpoint);
            // DISCOVER never requires Editor APIs or optional shell tools.
            // This inventory is read-only, including when no target folder exists.
            var localInventory = phase == "DISCOVER"
                ? AssetManagerLocalInventory.Format(AssetManagerLocalInventory.Scan(
                    root, definition.ScenePath, definition.TargetPath, cancellationToken))
                : string.Empty;
            var prompt = BuildAssetManagerPhasePrompt(definition, phase,
                checkpoint.Evidence, root, localInventory);
            AddDataFlowHistory(WorkerRoleState.Manager, "#1 ASSET MANAGER",
                phase + " 시작 · " + definition.RequestId, status: "PROCESSING",
                workItemId: "1", referenceId: historyId,
                persistenceSource: "ASSET MANAGER");

            AiRoleRunResult run;
            try
            {
                run = await RunCoordinatorRoleAsync(
                    jobId, "ASSET_MANAGER_" + phase, prompt,
                    isolatedRole, root, checkpoint.SessionId, null,
                    cancellationToken, CodexSandboxMode.WorkspaceWrite,
                    sessionStarted: session =>
                    {
                        var normalized = CodexCliRunner.NormalizeSessionId(session);
                        if (normalized is not null)
                        {
                            checkpoint = checkpoint with { SessionId = normalized };
                            AssetManagerExecutionJournal.Save(root, checkpoint);
                        }
                    },
                    historyWorkItemId: "1",
                    historyReferenceId: historyId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                checkpoint = checkpoint with { State = "BLOCKED",
                    Evidence = phase + ": " + error.GetType().Name + ": " + error.Message };
                AssetManagerExecutionJournal.Save(root, checkpoint);
                return new(false, checkpoint.Evidence, checkpoint.ChangedPaths);
            }

            // FULL_AUTONOMY: attempt one alternative acquisition strategy only
            // when no project files were changed. Never replay a partial Import.
            if (WorkerAutonomyPolicy.Enabled && phase == "ACQUIRE" &&
                run.ExitCode == 0)
            {
                var initial = RoleTextProtocol.ParseWork(run.FinalMessage);
                if (initial.IsValid &&
                    string.Equals(initial.Status, "blocked", StringComparison.OrdinalIgnoreCase))
                {
                    var intermediate = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        root, cancellationToken);
                    if (!MilestoneMechanicalExecutor.DiffChangeStates(before, intermediate).Any())
                    {
                        AddTaskMessage("AUTONOMY RECOVERY",
                            "ACQUIRE 차단 감지 · 파일 변경 없음 · 대체 획득 경로 1회 자동 시도",
                            status: "PROCESSING");
                        var recoveryPrompt =
                            "동일 ASSET MANAGER ACQUIRE 단계의 자동 복구다. " +
                            "앞서 실패한 도구/도메인 경로만 반복하지 말고 " +
                            "다른 HTTPS 클라이언트·공식 CC0 에셋 후보·" +
                            "공식 다운로드 경로를 조사하여 실제 획득을 완료하라. " +
                            "사용자 승인 대기 없이 가능한 방식을 직접 실행하되 " +
                            "TLS 인증 및 라이선스 검증, Import·GUID 검증은 유지하라. " +
                            "실제 완료하지 못하면 blocked로 정확히 보고하라.\\n" +
                            "PREVIOUS_BLOCKED_RESULT:\\n" + run.FinalMessage + "\\n" +
                            BuildAssetManagerPhasePrompt(definition, phase,
                                checkpoint.Evidence, root);
                        try
                        {
                            run = await RunCoordinatorRoleAsync(
                                jobId, "ASSET_MANAGER_" + phase + "_RECOVERY",
                                recoveryPrompt, isolatedRole, root,
                                CodexCliRunner.NormalizeSessionId(run.SessionId) ??
                                    checkpoint.SessionId,
                                null, cancellationToken, CodexSandboxMode.WorkspaceWrite,
                                historyWorkItemId: "1",
                                historyReferenceId: historyId);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception retryError)
                        {
                            AddTaskMessage("AUTONOMY RECOVERY",
                                "대체 획득 시도 오류: " + retryError.Message,
                                status: "FAILED");
                        }
                    }
                }
            }

            var after = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                root, cancellationToken);
            var changed = MilestoneMechanicalExecutor.DiffChangeStates(before, after)
                .ToArray();
            var outOfScope = changed.Where(p =>
                !AssetManagerPathPolicy.IsAllowed(p, definition, phase)).ToArray();
            var parsed = RoleTextProtocol.ParseWork(run.FinalMessage);
            var successful = run.ExitCode == 0 && parsed.IsValid &&
                string.Equals(parsed.Status, "completed", StringComparison.OrdinalIgnoreCase);
            var additional = changed.Where(p =>
                !checkpoint.ChangedPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var allChanged = checkpoint.ChangedPaths.Concat(additional)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var detail = phase + " · exit=" + run.ExitCode +
                " · protocol=" + (parsed.IsValid ? parsed.Status : "invalid") +
                " · summary=" + MilestoneDefinitionContract.Limit(
                    parsed.IsValid ? parsed.Summary : run.FinalMessage, 1000);
            if (phase == "DISCOVER" && changed.Length > 0)
                detail += " · DISCOVERY_MODIFIED_PROJECT";
            if (outOfScope.Length > 0)
                detail += " · ASSET_MANAGER_SCOPE_VIOLATION: " +
                    string.Join(",", outOfScope.Take(8));
            if (phase == "INTEGRATE" &&
                (!File.Exists(Path.Combine(root, definition.ScenePath.Replace('/',
                    Path.DirectorySeparatorChar))) ||
                 !changed.Any(p => p.Equals(definition.ScenePath,
                     StringComparison.OrdinalIgnoreCase))))
                detail += " · SCENE_SAVE_NOT_OBSERVED";
            else if (phase == "INTEGRATE" && successful)
                detail += " · scene saved at " + definition.ScenePath;
            if (phase == "ACQUIRE" && successful &&
                !Directory.Exists(Path.Combine(root,
                    definition.TargetPath.Replace('/', Path.DirectorySeparatorChar))) &&
                definition.SourcePolicy != "PROJECT_ONLY")
                detail += " · IMPORT_DIRECTORY_MISSING";

            var phasePassed = successful && outOfScope.Length == 0 &&
                (phase != "DISCOVER" || changed.Length == 0) &&
                (phase != "INTEGRATE" || (File.Exists(Path.Combine(root,
                    definition.ScenePath.Replace('/', Path.DirectorySeparatorChar))) &&
                    changed.Any(p => p.Equals(definition.ScenePath,
                        StringComparison.OrdinalIgnoreCase)))) &&
                (phase != "ACQUIRE" || definition.SourcePolicy == "PROJECT_ONLY" ||
                 Directory.Exists(Path.Combine(root,
                     definition.TargetPath.Replace('/', Path.DirectorySeparatorChar))));

            AddRoleResponseHistory(WorkerRoleState.Manager,
                "#1 ASSET MANAGER " + phase, run.FinalMessage,
                run.Usage, run.Files, status: phasePassed ? "COMPLETED" : "BLOCKED",
                providerWireId: implementer.Provider, fullMessage: run.FinalMessage,
                workItemId: "1", referenceId: historyId);
            evidence.Add(detail);
            checkpoint = checkpoint with
            {
                State = phasePassed ? completedState : "BLOCKED",
                SessionId = CodexCliRunner.NormalizeSessionId(run.SessionId) ??
                    checkpoint.SessionId,
                Evidence = checkpoint.Evidence + "\n" + detail,
                ChangedPaths = allChanged
            };
            AssetManagerExecutionJournal.Save(root, checkpoint);
            if (!phasePassed)
                return new(false, detail, allChanged);
        }
        return new(true,
            "ASSET_MANAGER_COMPLETED: " + definition.RequestId + "\n" +
            checkpoint.Evidence, checkpoint.ChangedPaths);
    }

    private static string BuildAssetManagerPhasePrompt(
        AssetManagerDefinition definition, string phase,
        string priorEvidence, string root, string localInventory = "")
    {
        return "[ROLE=ASSET_MANAGER]\nASSET_MANAGER_ID: 1\nREQUEST_ID: " +
            definition.RequestId +
            "\nPHASE: " + phase +
            "\nPROJECT_ROOT: " + root +
            "\nSCENE: " + definition.ScenePath +
            "\nTARGET_PATH: " + definition.TargetPath +
            "\nSOURCE_POLICY: " + definition.SourcePolicy +
            "\nGOAL: " + definition.Goal +
            "\nINSTRUCTIONS: " + definition.Instructions +
            "\nPREVIOUS_PHASE_EVIDENCE:\n" + priorEvidence +
            (phase == "DISCOVER"
                ? "\nLOCAL_ASSET_INVENTORY:\n" + localInventory +
                  "\nDISCOVER는 로컬 파일 검색으로 완료할 수 있다. " +
                  "AssetDatabase/Search 조회 불가만으로 blocked를 반환하지 마라. " +
                  "rg 명령이 없으면 Get-ChildItem을 사용하라. " +
                  "Unity Editor가 열려 있다면 Unity.exe -help/-batchmode로 " +
                  "중복 실행하지 마라. CC0 공식 배포처 페이지·라이선스·" +
                  "모델 형식이 확인되면 직접 다운로드 URL·크기 미공개는 " +
                  "ISSUES에 명시하고 ACQUIRE에서 검증하라. " +
                  "이 단계에서는 다운로드하거나 프로젝트를 수정하지 마라.\n"
                : string.Empty) +
            "\n현재 PHASE만 실제 수행하라. 이후 단계로 미리 진입하지 마라. " +
            "완료 기준을 충족하지 못한 경우 blocked 또는 in_progress로 보고하라. " +
            "검색/획득/씬 연결의 도구 결과와 실제 파일 증거를 기록하라.\n\n" +
            RoleContractLoader.LoadAssetManagerFooter();
    }
}
