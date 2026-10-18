using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections.Concurrent;

namespace ProjectHub.Worker;

public partial class MainWindow
{
    private async Task<WorkExecutionReport> ExecuteMilestoneWorkAsync(
        string jobId,
        string workingDirectory,
        MilestoneDefinition milestone,
        string workId,
        WorkerAiRoleSettings implementer,
        CancellationToken cancellationToken)
    {
        if (!milestone.WorkItems.TryGetValue(workId, out var work))
        {
            return new(
                workId,
                RoleTextProtocol.BuildResult(
                    "blocked",
                    "계획되지 않은 WORK_ITEM_ID입니다."));
        }

        RunOnUi(() =>
        {
            TaskDirection.Text = "작업";
            TaskTitle.Text = $"Unity WORK #{work.Id}";
            ResultTitle.Text = "WORK";
            SetFlowState(
                codexActive: false,
                workerActive: true,
                webActive: false,
                explicitStage: TaskStage.Implementer);
        });

        // Ownership is journaled before dispatch. CONTINUE retains the
        // actual session and prior report rather than re-investigating work
        // already completed in an earlier milestone.
        if (!WorkExecutionJournal.TryBegin(
                workingDirectory, jobId, milestone.Id, work, implementer,
                out var checkpoint, out var startError))
        {
            return new(work.Id, RoleTextProtocol.BuildResult(
                "blocked", startError));
        }

        var requiresEditorLane = UnityWorkSchedulingPolicy.RequiresEditorLane(work);
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            work.Id,
            MilestoneDefinitionContract.BuildWorkContext(work),
            work.WritePaths,
            workingDirectory,
            readOnly: work.ReadOnly,
            includeEncodingGuidance: checkpoint!.SessionId is null,
            includeRoleContract: checkpoint.SessionId is null);
        if (!requiresEditorLane)
            prompt += Environment.NewLine + Environment.NewLine +
                "WORK_LANE: FILE_ONLY. 지정된 C#·StreamingAssets 텍스트·문서 파일만 " +
                "편집한다. Editor/Unity CLI/MCP/AssetDatabase 및 빌드·테스트는 " +
                "호출하지 않는다. 신규 .meta, Packages/ProjectSettings 수정이나 " +
                "씬·프리팹 작업이 필요하면 직접 실행하지 말고 ISSUES에 보고해 " +
                "Worker/HQ의 다음 결정을 받는다.";
        if (work.ExecutionMode == "CONTINUE" &&
            !string.IsNullOrWhiteSpace(checkpoint!.LastReport))
        {
            prompt += Environment.NewLine + Environment.NewLine +
                "이전 WORK 체크포인트:" + Environment.NewLine +
                "이전 마일스톤의 저장된 작업 결과다. CLI 세션은 이번 마일스톤에 " +
                "새로 시작하므로 완료한 구현·파일 조사를 재실행하지 말고 " +
                "보고된 잔여 항목만 이어서 처리하라." + Environment.NewLine +
                MilestoneDefinitionContract.Limit(checkpoint.LastReport, 4000);
        }
        var sceneWritePath = work.ReadOnly ? null : work.WritePaths
            .FirstOrDefault(path => UnitySceneCliWorkflow.TryNormalizeScenePath(
                path, out _));
        // Only the Editor lane may open an existing Unity scene.
        if (requiresEditorLane && sceneWritePath is not null)
        {
            // The scheduler holds the project-wide Editor lease here.
            var sceneFile = Path.Combine(workingDirectory,
                sceneWritePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(sceneFile))
            {
                var opened = await UnitySceneCliWorkflow.OpenAsync(
                    workingDirectory, sceneWritePath, cancellationToken);
                prompt += Environment.NewLine +
                    "Unity 씬 열기 증거: " + opened.Detail +
                    (opened.Success ? "" :
                        " · 자동 재호출하지 말고 Editor 상태를 조회하여 필요한 경우 " +
                        "등록된 open_scene 명령으로 대체하라.");
            }
        }

        // A fresh user message may arrive while WORK is running. The AI
        // receives it without keyword-based Worker interpretation; once a
        // message arrives, the next decision belongs to HQ, not automatic
        // continuation or self-review of the old mission.
        var interventionIdsBeforeWork = _hqInterventions?.ReadAll()
            .Where(item => !item.RecordOnly)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal) ??
            new HashSet<string>(StringComparer.Ordinal);
        var sessionId = checkpoint!.SessionId;
        var historyInvocationReference = WorkerHistoryCardPolicy.NewInvocationReference(
            milestone.Id, work.Id);
        var isolatedRole = implementer with
        {
            ThreadSessionId = null,
            ThreadProjectPath = null
        };

        async Task<AiRoleRunResult> RunAsync(string currentPrompt)
        {
            if (!WorkExecutionJournal.Record(
                    workingDirectory, jobId, work.Id, "RUNNING",
                    sessionId, invocationStarted: true))
                throw new IOException("WORK_CHECKPOINT_WRITE_FAILED");

            var run = await RunCoordinatorRoleAsync(
                jobId,
                "WORK",
                currentPrompt,
                isolatedRole,
                workingDirectory,
                sessionId,
                null,
                cancellationToken,
                work.ReadOnly
                    ? CodexSandboxMode.ReadOnly
                    : CodexSandboxMode.WorkspaceWrite,
                sessionStarted: id =>
                {
                    sessionId = CodexCliRunner.NormalizeSessionId(id) ?? sessionId;
                    WorkExecutionJournal.Record(
                        workingDirectory, jobId, work.Id, "RUNNING", sessionId);
                },
                historyWorkItemId: work.Id,
                historyReferenceId: historyInvocationReference);

            sessionId = CodexCliRunner.NormalizeSessionId(run.SessionId) ?? sessionId;
            return run;
        }

        AiRoleRunResult result;
        try
        {
            var currentPrompt = prompt;
            var continuationTurns = 0;
            var reResolutionPerformed = false;
            for (;;)
            {
                var beforeTurn = await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                    workingDirectory, cancellationToken);
                result = await RunAsync(currentPrompt);
                var parsed = RoleTextProtocol.ParseWork(result.FinalMessage);
                if (result.ExitCode == 0 && !parsed.IsValid && sessionId is not null)
                {
                    // Reformat only; never re-execute the original implementation.
                    _formatRecoveryJobs[jobId] = 1;
                    result = await RunAsync(
                        "직전 WORKITEM의 작업·명령·파일 수정은 다시 실행하지 마라. " +
                        "직전 실행 결과를 그대로 유지하고 형식만 " +
                        "@@REPORT의 <STATUS>, <SUMMARY>, <CHANGED_PATH>, <ISSUES>, " +
                        "[RESPONSE=OK] 계약에 맞추어 재출력하라.\n\n" +
                        RoleContractLoader.LoadWorkFooter());
                    parsed = RoleTextProtocol.ParseWork(result.FinalMessage);
                }

                var state = result.ExitCode != 0 || !parsed.IsValid
                    ? "BLOCKED"
                    : parsed.Status switch
                    {
                        "completed" => "COMPLETED",
                        "in_progress" => "IN_PROGRESS",
                        _ => "BLOCKED"
                    };

                // Existing progress-based CONTINUE remains bounded to two
                // continuations and is separate from the one-time self-review.
                // The review is only considered when the current WORK attempt
                // would otherwise finish; it never interrupts active progress.
                var userMessageDuringWork = _hqInterventions?.ReadAll()
                    .Any(item => !item.RecordOnly &&
                        !interventionIdsBeforeWork.Contains(item.Id)) == true;
                var progress = false;
                if (!userMessageDuringWork && state == "IN_PROGRESS" && continuationTurns < 2 &&
                    !reResolutionPerformed)
                {
                    var afterTurn =
                        await MilestoneMechanicalExecutor.SnapshotChangeStateAsync(
                            workingDirectory, cancellationToken);
                    progress = MilestoneMechanicalExecutor.DiffChangeStates(
                        beforeTurn, afterTurn).Any(path =>
                            MilestoneMechanicalExecutor.IsPathWithinScopes(
                                path, work.WritePaths));
                }
                if (!userMessageDuringWork && state == "IN_PROGRESS" && progress)
                {
                    if (!WorkExecutionJournal.Record(
                            workingDirectory, jobId, work.Id, "IN_PROGRESS",
                            sessionId, result.FinalMessage))
                        throw new IOException("WORK_CHECKPOINT_WRITE_FAILED");

                    continuationTurns++;
                    currentPrompt =
                        "동일 WORKITEM #" + work.Id + "의 같은 세션에서 이어서 작업하라. " +
                        "새 WORKITEM이 아니며 쓰기 범위는 바꾸지 않는다. " +
                        "남은 구현과 파일 변경 정리를 진행하고 완료하면 completed, " +
                        "아직 해결 가능한 잔여 작업이 있으면 in_progress를 보고하라. " +
                        "같은 실패를 그대로 반복하지 마라.";
                    continue;
                }

                // A provisional final report with an actual unresolved item
                // earns exactly one self-review. Claim it durably BEFORE the
                // invocation; the ID must never become terminal and then be
                // reassigned. No retry on CLI errors or invalid protocols.
                var firstReport = MilestoneDefinitionContract.NormalizeWorkReport(
                    result.ExitCode, result.FinalMessage, result.StandardError);
                if (!userMessageDuringWork && !reResolutionPerformed &&
                    sessionId is not null &&
                    WorkReResolutionPolicy.IsEligible(result.ExitCode, parsed) &&
                    WorkExecutionJournal.TryClaimReResolution(
                        workingDirectory, jobId, work.Id, sessionId, firstReport))
                {
                    reResolutionPerformed = true;
                    AddDataFlowHistory(
                        WorkerRoleState.Work,
                        "작업 재해결",
                        $"WORK_ITEM_ID: {work.Id} · 미해결 항목 재검토 1/1",
                        status: "PROCESSING",
                        workItemId: work.Id,
                        referenceId: historyInvocationReference,
                        persistenceSource: "WORKER ACTION");
                    currentPrompt = WorkReResolutionPolicy.BuildPrompt(
                        firstReport, work.Id);
                    continue;
                }

                if (state == "COMPLETED" && sceneWritePath is not null)
                {
                    // Save only once after the role has finished, while this
                    // Editor WORK still owns the shared gate. A timeout may
                    // already have saved the scene: never replay it blindly.
                    var saved = await UnitySceneCliWorkflow.SaveAsync(
                        workingDirectory, sceneWritePath, cancellationToken);
                    if (!saved.Success)
                    {
                        state = "IN_PROGRESS";
                        result = result with
                        {
                            FinalMessage = RoleTextProtocol.BuildResult(
                                "in_progress", parsed.Summary,
                                parsed.ChangedPaths,
                                parsed.Issues.Concat(new[] {
                                    "UNITY_SCENE_SAVE_UNVERIFIED: " + saved.Detail +
                                    " · HQ 확인 전 자동 재시도 금지"
                                }).ToArray())
                        };
                    }
                }
                if (!WorkExecutionJournal.Record(
                        workingDirectory, jobId, work.Id, state,
                        sessionId, result.FinalMessage))
                    throw new IOException("WORK_CHECKPOINT_WRITE_FAILED");
                break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WorkExecutionJournal.Record(workingDirectory, jobId, work.Id,
                _userCanceledTask ? "CANCELED" : "IN_PROGRESS", sessionId);
            throw;
        }
        catch
        {
            WorkExecutionJournal.Record(workingDirectory, jobId, work.Id,
                "IN_PROGRESS", sessionId);
            throw;
        }

        var report = MilestoneDefinitionContract.NormalizeWorkReport(
            result.ExitCode,
            result.FinalMessage,
            result.StandardError);
        var reportStatus =
            MilestoneDefinitionContract.ReadWorkStatus(report);

        AddRoleResponseHistory(
            WorkerRoleState.Work,
            "작업 응답",
            report,
            result.Usage,
            result.Files,
            status: string.Equals(
                reportStatus,
                "blocked",
                StringComparison.OrdinalIgnoreCase)
                    ? "BLOCKED"
                    : "RECEIVED",
            providerWireId: implementer.Provider,
            fullMessage: report,
            workItemId: work.Id,
            referenceId: historyInvocationReference);

        return new(work.Id, report);
    }

}
