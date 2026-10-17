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
            var recoveringAcquisition = checkpoint.State is "IMPORT_PENDING" or "RUNNING_ACQUIRE" ||
                (checkpoint.State == "BLOCKED" &&
                 (checkpoint.BlockedPhase == "ACQUIRE" ||
                  (checkpoint.BlockedPhase is null &&
                   checkpoint.Evidence.Contains("ACQUIRE ·", StringComparison.OrdinalIgnoreCase))));
            if (phase == "DISCOVER" && recoveringAcquisition)
                continue;
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

            // A download is durable even when the AI could not reach the
            // Editor. Verify the exact files (and their saved hashes) before
            // invoking Unity; never execute a second acquisition in this case.
            if (phase == "ACQUIRE" &&
                (checkpoint.State == "DISCOVERED" || recoveringAcquisition))
            {
                var available = AssetManagerFileEvidence.Inspect(root,
                    definition.TargetPath, definition.SourcePolicy == "CC0_AUTO",
                    checkpoint.AcquiredFiles,
                    requireModel: definition.SourcePolicy != "PROJECT_ONLY");
                if (available.Ready)
                {
                    checkpoint = checkpoint with
                    {
                        State = "IMPORT_PENDING",
                        AcquiredFiles = available.Files,
                        BlockedPhase = "ACQUIRE",
                        Evidence = checkpoint.Evidence + "\n" + available.Detail
                    };
                    AssetManagerExecutionJournal.Save(root, checkpoint);
                    AddTaskMessage("UNITY IMPORT",
                        "기존 에셋 " + available.Files.Count +
                        "개 해시 검증 완료 · 재다운로드 없이 Unity Import 재개",
                        status: "PROCESSING");
                    var beforeImport = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        root, cancellationToken);
                    var completed = await UnityAssetCliImporter.RunAsync(
                        root, available.Files, available.ModelCount, cancellationToken);
                    var afterImport = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                        root, cancellationToken);
                    var importDiff = MilestoneMechanicalExecutor.DiffChangeStates(
                        beforeImport, afterImport).ToArray();
                    var additionalPaths = importDiff.Where(path =>
                        !AssetManagerPathPolicy.IsDeclaredScope(path, definition, "ACQUIRE"))
                        .ToArray();
                    var recheck = AssetManagerFileEvidence.Inspect(root, definition.TargetPath,
                        definition.SourcePolicy == "CC0_AUTO", available.Files,
                        requireModel: definition.SourcePolicy != "PROJECT_ONLY");
                    if (completed.Success && recheck.Ready)
                    {
                        checkpoint = checkpoint with
                        {
                            State = "ACQUIRED",
                            BlockedPhase = null,
                            ChangedPaths = checkpoint.ChangedPaths.Concat(importDiff)
                                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                            Evidence = checkpoint.Evidence + "\n" + completed.Detail +
                                (additionalPaths.Length == 0 ? string.Empty :
                                    "\nADDITIONAL_PROJECT_CHANGES: " + string.Join(",", additionalPaths))
                        };
                        AssetManagerExecutionJournal.Save(root, checkpoint);
                        AddTaskMessage("UNITY IMPORT", completed.Detail, status: "COMPLETED");
                        continue;
                    }
                    var failure = !recheck.Ready ? recheck.Detail : completed.Detail;
                    checkpoint = checkpoint with
                    {
                        State = !recheck.Ready ? "BLOCKED" : "IMPORT_PENDING",
                        Evidence = checkpoint.Evidence + "\n" + failure,
                        ChangedPaths = checkpoint.ChangedPaths.Concat(importDiff)
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    };
                    AssetManagerExecutionJournal.Save(root, checkpoint);
                    AddTaskMessage("UNITY IMPORT", failure, status: "FAILED");
                    return new(false, "UNITY_IMPORT_PENDING: " + failure, checkpoint.ChangedPaths);
                }

                if (recoveringAcquisition)
                    return new(false, "ASSET_ACQUIRE_RECONCILIATION_REQUIRED: " +
                        available.Detail + " (실제 파일/해시를 확인한 후 HQ가 " +
                        "다른 다운로드 방법, TARGET_PATH 또는 PAUSE를 결정합니다)",
                        checkpoint.ChangedPaths);
            }

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

            // Keep the same shared Editor lease while the integrating agent
            // opens, edits, and saves the Scene, including recovery.
            using var editorLease = phase == "INTEGRATE"
                ? await UnityEditorOperationGate.EnterAsync(root, cancellationToken)
                : null;
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
                checkpoint.Evidence, root, localInventory, checkpoint.SessionId);
            if (phase == "INTEGRATE")
            {
                var sceneFile = Path.Combine(root, definition.ScenePath
                    .Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(sceneFile))
                {
                    var opened = await UnitySceneCliWorkflow.OpenAsync(
                        root, definition.ScenePath, cancellationToken);
                    prompt += "\nUnity 씬 열기 증거: " + opened.Detail +
                        (opened.Success ? "" :
                            " · 자동 재호출 금지. 명령 카탈로그로 Editor 상태를 확인하라.");
                }
            }
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
                    BlockedPhase = phase,
                    Evidence = checkpoint.Evidence + "\n" + phase + ": " +
                        error.GetType().Name + ": " + error.Message };
                AssetManagerExecutionJournal.Save(root, checkpoint);
                return new(false, checkpoint.Evidence, checkpoint.ChangedPaths);
            }

            // A bounded alternative attempt is allowed only if this phase did
            // not mutate the project. No partial Import or scene edit is replayed.
            if (WorkerAutonomyPolicy.Enabled)
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
                            phase + " 차단 감지 · 변경 없음 · 다른 실행 방법 1회 시도",
                            status: "PROCESSING");
                        var recoveryPrompt =
                            "동일 ASSET MANAGER " + phase +
                            " 단계의 대체 실행이다. 이전 실패를 똑같이 반복하지 마라. " +
                            (phase == "DISCOVER"
                                ? "로컬 파일 탐색 및 다른 공식 CC0 출처를 읽기 전용으로 조사하라. "
                                : phase == "ACQUIRE"
                                    ? "기존 다운로드를 먼저 검사하고 누락분만 다른 공식 HTTPS 경로로 확보하라. "
                                    : "실제 기존 Unity Editor/Pipeline 명령을 확인하고 씬을 저장·검증하라. ") +
                            "기존 파일과 검증 상태를 보존하고, 실제 수행하지 못한 것은 완료로 보고하지 마라. " +
                            "다른 방법도 없다면 미해결 이유를 blocked로 보고하라.\n" +
                            "PREVIOUS_BLOCKED_RESULT:\n" + run.FinalMessage + "\n" +
                            BuildAssetManagerPhasePrompt(definition, phase,
                                checkpoint.Evidence, root, existingSessionId:
                                    CodexCliRunner.NormalizeSessionId(run.SessionId) ?? checkpoint.SessionId);
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

            AssetFolderEvidence? acquiredAfterAi = null;
            UnityAssetImportResult? importAfterAi = null;
            if (phase == "ACQUIRE")
            {
                acquiredAfterAi = AssetManagerFileEvidence.Inspect(root,
                    definition.TargetPath, definition.SourcePolicy == "CC0_AUTO",
                    requireModel: definition.SourcePolicy != "PROJECT_ONLY");
                if (acquiredAfterAi.Ready)
                {
                    // Persist BEFORE asking a live Editor to import: a restart
                    // must never re-download these proven files.
                    checkpoint = checkpoint with
                    {
                        State = "IMPORT_PENDING",
                        BlockedPhase = "ACQUIRE",
                        AcquiredFiles = acquiredAfterAi.Files,
                        Evidence = checkpoint.Evidence + "\n" + acquiredAfterAi.Detail
                    };
                    AssetManagerExecutionJournal.Save(root, checkpoint);
                    AddTaskMessage("UNITY IMPORT",
                        acquiredAfterAi.Detail + " · 기존 Editor에서 Import/Importer/GUID 검증",
                        status: "PROCESSING");
                    importAfterAi = await UnityAssetCliImporter.RunAsync(
                        root, acquiredAfterAi.Files, acquiredAfterAi.ModelCount, cancellationToken);
                }
            }

            var parsed = RoleTextProtocol.ParseWork(run.FinalMessage);
            UnitySceneCliResult? sceneSaved = null;
            if (phase == "INTEGRATE" && run.ExitCode == 0 && parsed.IsValid &&
                string.Equals(parsed.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                // Explicit saved-Scene evidence from the connected Editor.
                // If this times out we cannot know whether the save happened:
                // preserve the checkpoint and ask HQ, do not repeat.
                sceneSaved = await UnitySceneCliWorkflow.SaveAsync(
                    root, definition.ScenePath, cancellationToken);
            }
            var after = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                root, cancellationToken);
            var changed = MilestoneMechanicalExecutor.DiffChangeStates(before, after)
                .ToArray();
            var additionalProjectChanges = changed.Where(path =>
                !AssetManagerPathPolicy.IsDeclaredScope(path, definition, phase)).ToArray();
            // A proven local payload + Editor-side Import can recover a
            // blocked/failed AI acquisition. Other phases still demand an
            // actual successfully completed role response.
            var successful = phase == "ACQUIRE"
                ? importAfterAi?.Success == true && acquiredAfterAi?.Ready == true
                : run.ExitCode == 0 && parsed.IsValid &&
                  string.Equals(parsed.Status, "completed",
                      StringComparison.OrdinalIgnoreCase);
            var additional = changed.Where(p =>
                !checkpoint.ChangedPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var allChanged = checkpoint.ChangedPaths.Concat(additional)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var detail = phase + " · exit=" + run.ExitCode +
                " · protocol=" + (parsed.IsValid ? parsed.Status : "invalid") +
                " · summary=" + MilestoneDefinitionContract.Limit(
                    parsed.IsValid ? parsed.Summary : run.FinalMessage, 1000);
            if (phase == "ACQUIRE")
                detail += " · " + (importAfterAi?.Detail ??
                    acquiredAfterAi?.Detail ?? "ASSET_IMPORT_NOT_VERIFIED");
            if (phase == "DISCOVER" && changed.Length > 0)
                detail += " · DISCOVERY_MODIFIED_PROJECT";
            if (phase == "INTEGRATE")
                detail += " · " + (sceneSaved?.Detail ?? "UNITY_SCENE_SAVE_NOT_CONFIRMED");
            if (additionalProjectChanges.Length > 0)
                detail += " · ADDITIONAL_PROJECT_CHANGES: " +
                    string.Join(",", additionalProjectChanges.Take(16));
            if (phase == "INTEGRATE" &&
                !File.Exists(Path.Combine(root, definition.ScenePath.Replace('/',
                    Path.DirectorySeparatorChar))))
                detail += " · SCENE_FILE_NOT_FOUND";
            else if (phase == "INTEGRATE" && successful)
                detail += " · scene saved at " + definition.ScenePath +
                    (changed.Any(p => p.Equals(definition.ScenePath,
                        StringComparison.OrdinalIgnoreCase))
                        ? "" : " · UNCHANGED_SCENE_REUSED");
            if (phase == "ACQUIRE" && successful &&
                !Directory.Exists(Path.Combine(root,
                    definition.TargetPath.Replace('/', Path.DirectorySeparatorChar))) &&
                definition.SourcePolicy != "PROJECT_ONLY")
                detail += " · IMPORT_DIRECTORY_MISSING";

            var phasePassed = successful &&
                (phase != "INTEGRATE" || sceneSaved?.Success == true) &&
                (phase != "INTEGRATE" || File.Exists(Path.Combine(root,
                    definition.ScenePath.Replace('/', Path.DirectorySeparatorChar)))) &&
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
                State = phasePassed ? completedState :
                    phase == "ACQUIRE" && acquiredAfterAi?.Ready == true
                        ? "IMPORT_PENDING" : "BLOCKED",
                BlockedPhase = phasePassed ? null : phase,
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
        string priorEvidence, string root, string localInventory = "",
        string? existingSessionId = null)
    {
        return RoleContractLoader.AppendAssetManagerContractForNewSession(
            "@@MESSAGE\nASSET_MANAGER_ID: 1\nREQUEST_ID: " +
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
                : phase == "ACQUIRE"
                    ? "\nACQUIRE는 다운로드 파일과 라이선스·크기·해시 검증에 집중하라. " +
                      "TARGET_PATH에 이미 있는 정상 파일은 삭제하거나 다시 다운로드하지 마라. " +
                      "이미 확보된 파일과 LICENSES.md를 검증해서 재사용하라. " +
                      "기존 Editor의 Import·GUID 검증은 이 응답 후 Worker가 " +
                      "공식 Unity CLI로 직접 수행한다. Editor 연결이 없다는 이유만으로 " +
                      "다운로드 완료 결과를 blocked로 낮추지 마라. " +
                      "실제 다운로드나 로컬 검증에 실패한 경우에만 blocked로 보고하라.\n"
                    : "\nINTEGRATE는 Editor에서 씬 구성과 저장까지 실제로 수행한다. " +
                  UnitySceneCliWorkflow.DirectSceneInstructions(definition.ScenePath)) +
            "\nWRITE_PATH·TARGET_PATH는 파일 작업 추적 및 동시 수정 충돌 조정용이다. " +
            "ProjectSettings, Packages, .meta, 에셋 및 필요한 프로젝트 내부 설정 수정은 허용한다. " +
            "선언 범위 밖 변경만으로 blocked를 반환하지 말고 변경 경로를 보고하라. " +
            "다른 WORK가 동시에 수정 중인 동일 파일은 수정하지 마라. " +
            "\n현재 PHASE만 실제 수행하라. 이후 단계로 미리 진입하지 마라. " +
            "완료 기준을 충족하지 못한 경우 blocked 또는 in_progress로 보고하라. " +
            "검색/획득/씬 연결의 도구 결과와 실제 파일 증거를 기록하라.\n\n" +
            string.Empty, existingSessionId);
    }
}
