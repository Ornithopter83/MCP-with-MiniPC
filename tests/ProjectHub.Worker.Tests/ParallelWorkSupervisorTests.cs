using System.Collections.Concurrent;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkSupervisorTests
{
    [Fact]
    public void MechanicalGraphEventExposesIntegrationRemoteDetailCode()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("I1", "통합", Kind: WorkItemKind.Integration))
        })).Success);
        Assert.True(graph.TryMarkRunning("I1"));
        Assert.True(graph.TryMarkBlocked(
            "I1",
            "INTEGRATION_REMOTE_FETCH_FAILED",
            new string('x', 1500),
            "ref-I1",
            "INTEGRATION_REMOTE_FETCH_TIMEOUT"));

        var text = ParallelWorkSupervisor.FormatMechanicalGraphEvent(
            new[] { "통합 원격 Git 준비가 차단되었습니다." },
            new ParallelWorkSchedulerSnapshot(
                graph.Snapshot(),
                Array.Empty<RunningWorkItemSnapshot>()));

        Assert.Contains("blockCode=INTEGRATION_REMOTE_FETCH_FAILED", text);
        Assert.Contains("blockDetailCode=INTEGRATION_REMOTE_FETCH_TIMEOUT", text);
        Assert.True(
            text.IndexOf("blockDetailCode=INTEGRATION_REMOTE_FETCH_TIMEOUT", StringComparison.Ordinal) <
            text.IndexOf("WORK_REPORT_BEGIN", StringComparison.Ordinal));
    }

    [Fact]
    public void MechanicalGraphEventIncludesMeasuredResultType()
    {
        var graph = new WorkGraph("job", 1);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("W10", "코드 변경"))
        })).Success);
        Assert.True(graph.TryMarkRunning("W10"));
        Assert.True(graph.TryMarkCompleted(
            "W10",
            "ref-W10",
            "구현 완료",
            WorkItemResultType.CodeChange));

        var text = ParallelWorkSupervisor.FormatMechanicalGraphEvent(
            new[] { "WorkItem W10가 COMPLETED 상태가 되었습니다." },
            new ParallelWorkSchedulerSnapshot(
                graph.Snapshot(),
                Array.Empty<RunningWorkItemSnapshot>()));

        Assert.Contains("workItemId=W10 kind=NORMAL state=COMPLETED resultType=CODE_CHANGE resultRef=ref-W10", text);
    }

    [Fact]
    public async Task IndependentWorkRunsToQuiescenceBeforeHqEndTurn()
    {
        var graph = new WorkGraph("job", 2);
        var executor = new SupervisorExecutor();
        executor.SetImmediate("W1");
        executor.SetImmediate("W2");

        var hq = new QueueHqRunner(
            ContinuePatch(0,
                Add("W1", "기능 1"),
                Add("W2", "기능 2")),
            End("모든 병렬 작업 결과를 확인했습니다."));

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "기능 두 개를 구현하세요.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Null(result.ErrorCode);
        Assert.Equal(2, result.Graph.Items.Count(item => item.State == WorkItemState.Completed));
        Assert.Equal(2, hq.Prompts.Count);
        Assert.Contains("WorkGraph revision: 0", hq.Prompts[0]);
        Assert.Contains("당신은 HQ다.", hq.Prompts[0]);
        Assert.Contains("입력 유형: WORK_GRAPH_QUIESCENT", hq.Prompts[1]);
        Assert.DoesNotContain("당신은 HQ다.", hq.Prompts[1]);
        Assert.Contains("WorkGraph 변경", hq.Prompts[1]);
        Assert.Contains("state=COMPLETED", hq.Prompts[1]);
        Assert.Contains("state=COMPLETED", hq.Prompts[1]);
    }

    [Fact]
    public void MechanicalGraphDeltaOmitsUnchangedCompletedItems()
    {
        var graph = new WorkGraph("job", 2);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("W1", "완료 작업")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("W2", "진행 작업"))
        })).Success);

        Assert.True(graph.TryMarkRunning("W1"));
        Assert.True(graph.TryMarkCompleted("W1", "ref-W1", "이미 HQ가 받은 완료 결과", WorkItemResultType.CodeChange));
        var previous = graph.Snapshot();

        Assert.True(graph.TryMarkRunning("W2"));
        var current = new ParallelWorkSchedulerSnapshot(
            graph.Snapshot(),
            Array.Empty<RunningWorkItemSnapshot>());

        var text = ParallelWorkSupervisor.FormatMechanicalGraphDeltaEvent(
            new[] { "W2 상태가 변경되었습니다." },
            previous,
            current);

        Assert.DoesNotContain("workItemId=W1", text);
        Assert.Contains("workItemId=W2", text);
        Assert.Contains("state=RUNNING", text);
    }

    [Fact]
    public void MechanicalGraphDeltaReportsChecklistOnceAndKeepsLaterUpdatesCompact()
    {
        var graph = new WorkGraph("job", 1);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                "W1",
                "작업",
                Checklist: new[] { "첫 단계", "둘째 단계" }))
        })).Success);

        var beforeRunning = graph.Snapshot();
        Assert.True(graph.TryMarkRunning("W1"));
        var running = new ParallelWorkSchedulerSnapshot(
            graph.Snapshot(),
            Array.Empty<RunningWorkItemSnapshot>());
        var reported = new HashSet<string>(StringComparer.Ordinal);

        var first = ParallelWorkSupervisor.FormatMechanicalGraphDeltaEvent(
            new[] { "W1 실행" },
            beforeRunning,
            running,
            reported);

        Assert.Contains("WORK_CHECKLIST_BEGIN", first);
        Assert.Contains("[1] 첫 단계", first);
        Assert.Contains("W1|" + running.Graph.Items.Single(item => item.Id == "W1").CreatedOrder, reported);

        var beforeBlocked = graph.Snapshot();
        Assert.True(graph.TryMarkBlocked(
            "W1",
            "HQ_BLOCKED",
            "추가 판단 필요"));
        var blocked = new ParallelWorkSchedulerSnapshot(
            graph.Snapshot(),
            Array.Empty<RunningWorkItemSnapshot>());

        var second = ParallelWorkSupervisor.FormatMechanicalGraphDeltaEvent(
            new[] { "W1 차단" },
            beforeBlocked,
            blocked,
            reported);

        Assert.DoesNotContain("WORK_CHECKLIST_BEGIN", second);
        Assert.DoesNotContain("[1] 첫 단계", second);
        Assert.Contains("WORK_REPORT_BEGIN", second);
        Assert.Contains("추가 판단 필요", second);
    }

    [Fact]
    public async Task NoOpContinueStartsExistingReadyGraphWithoutRevisionChange()
    {
        var graph = new WorkGraph("job", 1);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("W1", "복구 작업", BaseRef: "base123"))
        })).Success);
        var revision = graph.Revision;

        var executor = new SupervisorExecutor();
        executor.SetImmediate("W1");
        var hq = new QueueHqRunner(
            ContinuePatch(revision),
            End("복구 작업 완료"));

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        var result = await supervisor.RunAsync(
            "USER_FOLLOWUP",
            "계속 진행해줘.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Equal(revision, result.Graph.Revision);
        Assert.Equal(WorkItemState.Completed, Assert.Single(result.Graph.Items).State);
        Assert.Single(executor.Requests);
    }

    [Fact]
    public async Task EmptyContinueOnEmptyGraphIsRejectedAndHqCanRecoverWithAdd()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        var hq = new QueueHqRunner(
            ContinuePatch(0),
            ContinuePatch(0, Add("W10", "실제 작업")),
            End("완료"));

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "작업을 시작하세요.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Equal(3, hq.Prompts.Count);
        Assert.Contains("입력 유형: WORK_GRAPH_PATCH_SCHEMA_REJECTED", hq.Prompts[1]);
        Assert.Contains("WORK_GRAPH_EMPTY_CONTINUE", hq.Prompts[1]);
        Assert.Contains("attempt=1", hq.Prompts[1]);
        Assert.Contains("현재 revision에 맞는 patch 형식만 수정해 다시 응답하세요.", hq.Prompts[1]);
        Assert.Equal(WorkItemState.Completed, Assert.Single(result.Graph.Items).State);
    }

    [Fact]
    public async Task RepairedEmptyPatchIsRejectedInsteadOfBecomingQuiescentNoOp()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        var hq = new QueueHqRunner(
            "[ACTION=CONTINUE]\n[GOTO : WORK]\n손상된 patch",
            ContinuePatch(0, Add("W10", "복구된 실제 작업")),
            End("완료"));
        var helperCalls = 0;

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync,
            processWorkGraphPayloadAsync: (payload, _) =>
            {
                helperCalls++;
                if (helperCalls == 1)
                {
                    return Task.FromResult(new StructuredPayloadResult<WorkGraphPatch>(
                        Success: true,
                        Value: new WorkGraphPatch(0, Array.Empty<WorkGraphPatchOperation>()),
                        FinalPayload: """{"expectedRevision":0,"operations":[]}""",
                        RepairAttempted: true,
                        Repaired: true,
                        InitialErrorCode: "WORK_GRAPH_PATCH_JSON_INVALID",
                        FinalErrorCode: null));
                }

                return Task.FromResult(
                    WorkerStructuredPayloadHelper.ProcessDeterministically<WorkGraphPatch>(
                        payload,
                        WorkGraphTransportContract.TryParse));
            });

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "작업을 시작하세요.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Equal(3, hq.Prompts.Count);
        Assert.Contains("WORK_GRAPH_REPAIRED_EMPTY_PATCH", hq.Prompts[1]);
        Assert.Equal(WorkItemState.Completed, Assert.Single(result.Graph.Items).State);
    }

    [Fact]
    public async Task BlockedWorkWakesHqWhileIndependentWorkIsStillRunning()
    {
        var graph = new WorkGraph("job", 2);
        var executor = new SupervisorExecutor();
        executor.SetBlockedOnceThenComplete("W1");
        executor.SetControlled("W2");
        executor.SetImmediate("W3");

        var secondHqCalled = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var hq = new QueueHqRunner(
            ContinuePatch(0,
                Add("W1", "분할이 필요할 수 있는 작업"),
                Add("W2", "오래 실행되는 독립 작업")),
            ContinuePatch(
                1,
                """
                {"type":"ADD","workItemId":"W3","goal":"분리된 추가 작업","dependencies":[],"kind":"NORMAL","baseRef":"base123"}
                """,
                """
                {"type":"RELEASE","workItemId":"W1","inputType":"HQ_RESUME","value":"W3를 추가했습니다. 기존 W1을 이어서 완료하세요."}
                """),
            End("통합 전 병렬 작업이 완료되었습니다."));

        hq.OnTurn = turn =>
        {
            if (turn == 2)
                secondHqCalled.TrySetResult(true);
        };

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        var runTask = supervisor.RunAsync(
            "USER_REQUEST",
            "서로 독립적인 작업을 병렬로 진행하세요.");

        await secondHqCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(executor.IsRunning("W2"));
        Assert.False(executor.IsCompleted("W2"));
        Assert.Contains("blockCode=HQ_BLOCKED", hq.Prompts[1]);
        Assert.Contains("workItemId=W2", hq.Prompts[1]);
        Assert.Contains("state=RUNNING", hq.Prompts[1]);

        await executor.WhenStartedCount("W1", 2);
        var resumed = executor.Requests
            .Where(request => request.Item.Id == "W1")
            .OrderBy(request => request.Item.StartedAtUtc)
            .Last();
        Assert.Equal("HQ_RESUME", resumed.InboundType);
        Assert.Contains("W3를 추가", resumed.InboundBody);

        executor.Release("W2");

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.All(
            result.Graph.Items,
            item => Assert.Equal(WorkItemState.Completed, item.State));
        Assert.Contains(result.Graph.Items, item => item.Id == "W3");
    }


    [Fact]
    public async Task HqPauseDrainsRunningWorkAndDoesNotStartNewReadyWork()
    {
        var graph = new WorkGraph("job", 2);
        var executor = new SupervisorExecutor();
        executor.SetBlockedOnceThenComplete("W1");
        executor.SetControlled("W2");

        var secondHqCalled = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var hq = new QueueHqRunner(
            ContinuePatch(
                0,
                Add("W1", "HQ 판단이 필요한 작업"),
                Add("W2", "이미 실행 중인 작업"),
                """
                {"type":"ADD","workItemId":"W3","goal":"W2 이후 작업","dependencies":["W2"],"kind":"NORMAL","baseRef":"base123"}
                """),
            Pause("사용자 판단을 기다립니다."));

        hq.OnTurn = turn =>
        {
            if (turn == 2)
                secondHqCalled.TrySetResult(true);
        };

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        var runTask = supervisor.RunAsync(
            "USER_REQUEST",
            "작업을 실행하세요.");

        await secondHqCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(executor.IsRunning("W2"));
        Assert.False(executor.IsRunning("W3"));

        executor.Release("W2");

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ParallelWorkSupervisorExit.Paused, result.Exit);
        Assert.DoesNotContain(result.Graph.Items, item => item.State == WorkItemState.Running);
        Assert.Equal(WorkItemState.Completed, result.Graph.Items.Single(item => item.Id == "W2").State);
        Assert.Equal(WorkItemState.Ready, result.Graph.Items.Single(item => item.Id == "W3").State);
        Assert.Equal(WorkItemState.Blocked, result.Graph.Items.Single(item => item.Id == "W1").State);
        Assert.False(executor.IsRunning("W3"));
    }

    [Fact]
    public async Task ResourceBlockWaitsForSidecarResumeWithoutWakingHq()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        executor.SetResourceOnceThenComplete("W1");

        var externalBlock = new TaskCompletionSource<ParallelWorkExternalBlock>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var hq = new QueueHqRunner(
            ContinuePatch(0, Add("W1", "리소스가 필요한 작업")),
            End("리소스 결과까지 반영되었습니다."));

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);
        supervisor.ExternalBlockAvailable += block => externalBlock.TrySetResult(block);

        var runTask = supervisor.RunAsync("USER_REQUEST", "리소스를 포함해 구현하세요.");
        var block = await externalBlock.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("W1", block.WorkItemId);
        Assert.Equal("RESOURCE_REQUEST", block.BlockCode);
        Assert.Single(hq.Prompts);

        var revisionBeforeResume = graph.Revision;
        Assert.True(await supervisor.ResumeExternalWorkItemAsync(
            "W1",
            "RESOURCE_RESULT",
            "requestId=R1 status=SAVED"));
        Assert.Equal(revisionBeforeResume, graph.Revision);

        await executor.WhenStartedCount("W1", 2);
        var resumed = executor.Requests.Last(request => request.Item.Id == "W1");
        Assert.Equal("RESOURCE_RESULT", resumed.InboundType);
        Assert.Contains("requestId=R1", resumed.InboundBody);

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Equal(2, hq.Prompts.Count);
        Assert.Equal(WorkItemState.Completed, Assert.Single(result.Graph.Items).State);
    }

    [Fact]
    public async Task CompletedIntegrationBecomesDefaultBaseForLaterWorkItems()
    {
        var graph = new WorkGraph("job", 2);
        var executor = new SupervisorExecutor();

        var hq = new QueueHqRunner(
            ContinuePatch(
                0,
                Add("W1", "기능 구현"),
                """
                {"type":"ADD","workItemId":"I1","goal":"W1 통합","dependencies":["W1"],"kind":"INTEGRATION","baseRef":"base123"}
                """),
            "[ACTION=CONTINUE]\n[GOTO : WORK]\nWORK_GRAPH_PATCH:\n" +
            """{"expectedRevision":1,"operations":[{"type":"ADD","workItemId":"W2","goal":"통합 이후 후속 작업","dependencies":[]}]}""",
            End("후속 작업까지 완료"));

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "기능을 통합한 뒤 후속 작업을 진행하세요.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        var integration = executor.Requests.Single(request => request.Item.Id == "I1");
        Assert.Equal(WorkItemKind.Integration, integration.Item.Kind);

        var followup = executor.Requests.Single(request => request.Item.Id == "W2");
        Assert.Equal("ref-I1", followup.Item.BaseRef);
        Assert.Contains("기준 ref: ref-I1", hq.Prompts[1]);
    }

    [Fact]
    public async Task AddWithoutBaseRefUsesSupervisorMechanicalBaseRef()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();

        var hq = new QueueHqRunner(
            "[ACTION=CONTINUE]\n[GOTO : WORK]\nWORK_GRAPH_PATCH:\n" +
            """{"expectedRevision":0,"operations":[{"type":"ADD","workItemId":"W1","goal":"기준 ref 생략","dependencies":[],"kind":"NORMAL"}]}""",
            End("완료"));

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "작업을 실행하세요.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        var request = Assert.Single(executor.Requests);
        Assert.Equal("base123", request.Item.BaseRef);
        Assert.Equal(WorkItemState.Completed, Assert.Single(result.Graph.Items).State);
    }

    [Fact]
    public async Task HqEndWithBlockedWorkItemReturnsMechanicalStateInsteadOfClosingGraph()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        executor.SetBlockedOnceThenComplete("W1");

        var hq = new QueueHqRunner(
            ContinuePatch(0, Add("W1", "분할 제안이 필요한 작업")),
            End("종료 시도"),
            ContinuePatch(
                1,
                """
                {"type":"RELEASE","workItemId":"W1","inputType":"HQ_RESUME","value":"추가 분할 없이 현재 범위를 완료하세요."}
                """),
            End("완료"));

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "작업을 수행하세요.",
            timeout.Token);

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Equal(4, hq.Prompts.Count);
        Assert.Contains("입력 유형: WORK_GRAPH_END_REJECTED", hq.Prompts[2]);
        Assert.Contains("id=W1 state=BLOCKED", hq.Prompts[2]);
        Assert.Equal(WorkItemState.Completed, Assert.Single(result.Graph.Items).State);
    }

    [Fact]
    public async Task CompletionReviewWakesHqWhileOtherWorkIsStillRunning()
    {
        var graph = new WorkGraph("job", 2);
        var executor = new SupervisorExecutor();
        executor.SetControlled("W11");
        var hq = new QueueHqRunner(
            ContinuePatch(
                0,
                Add("W10", "먼저 완료"),
                Add("W11", "계속 실행")),
            ContinuePatch(1),
            End("완료"));
        hq.OnTurn = turn =>
        {
            if (turn == 2)
                executor.Release("W11");
        };

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync,
            enableCompletionReview: true);

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "transport 종류와 무관한 HQ 완료 점검을 확인한다.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Equal(3, hq.Prompts.Count);
        Assert.Contains(
            "입력 유형: WORK_GRAPH_PROGRESS_REVIEW",
            hq.Prompts[1]);
        Assert.Contains(
            "WorkItem W10가 COMPLETED 상태가 되었습니다.",
            hq.Prompts[1]);
        Assert.Contains(
            "HQ가 직전 관제 이후 완료된 WorkItem을 즉시 점검하고 추가·보완·통합·검증 작업 필요 여부를 판단합니다.",
            hq.Prompts[1]);
        Assert.Contains(
            "workItemId=W10 kind=NORMAL state=COMPLETED",
            hq.Prompts[1]);
        Assert.Contains(
            "입력 유형: WORK_GRAPH_QUIESCENT",
            hq.Prompts[2]);
    }

    [Fact]
    public async Task RejectedPatchReturnsControlToHqWithoutEndingRunningWork()
    {
        var graph = new WorkGraph("job", 2);
        var executor = new SupervisorExecutor();
        executor.SetImmediate("W10");
        executor.SetControlled("W11");

        var hq = new QueueHqRunner(
            ContinuePatch(
                0,
                Add("W10", "환경 조사"),
                Add("W11", "계속 실행되는 구현 작업")),
            ContinuePatch(
                1,
                """
                {"type":"SET_GOAL","workItemId":"W11","value":"실행 중 목표 변경 시도"}
                """),
            ContinuePatch(1),
            End("완료"));

        hq.OnTurn = turn =>
        {
            if (turn == 3)
                executor.Release("W11");
        };

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync,
            enableCompletionReview: true);

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "실행 중 WorkItem 수정 거부 뒤 HQ 재판단을 확인한다.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Null(result.ErrorCode);
        Assert.Equal(4, hq.Prompts.Count);
        Assert.Contains("입력 유형: WORK_GRAPH_PATCH_REJECTED", hq.Prompts[2]);
        Assert.Contains("errorCode=WORK_GRAPH_RUNNING_OR_TERMINAL_ITEM_IMMUTABLE", hq.Prompts[2]);
        Assert.Contains("attempt=1", hq.Prompts[2]);
        Assert.Equal(WorkItemState.Completed, result.Graph.Items.Single(item => item.Id == "W11").State);
    }

    [Fact]
    public void WorkGraphTransportAcceptsCommonHqAliasesInInitialPatch()
    {
        const string response = """
            [ACTION=CONTINUE]
            [GOTO : WORK]
            WORK_GRAPH_PATCH:
            {"expectedRevision":0,"operations":[
              {"op":"SET_GOAL","goal":"전체 사용자 목표"},
              {"op":"ADD","id":10,"title":"콘텐츠 준비","dependencies":[],"goal":"실제 콘텐츠를 준비한다."},
              {"op":"ADD","id":11,"title":"GUI 구현","dependencies":[],"goal":"GUI를 구현한다."}
            ]}
            """;

        Assert.True(
            WorkGraphTransportContract.TryParse(
                response,
                out var patch,
                out var error),
            error);

        Assert.NotNull(patch);
        Assert.Equal(2, patch!.Operations.Count);
        Assert.All(
            patch.Operations,
            operation => Assert.Equal(WorkGraphPatchOperationType.Add, operation.Type));
        Assert.Equal(new[] { "10", "11" }, patch.Operations.Select(operation => operation.WorkItemId));
    }

    [Fact]
    public void WorkGraphTransportIgnoresIdlessGlobalSetGoalWhenPatchAddsWork()
    {
        const string response = """
            [ACTION=CONTINUE]
            [GOTO : WORK]
            WORK_GRAPH_PATCH:
            {"expectedRevision":0,"operations":[
              {"type":"SET_GOAL","value":"전체 사용자 목표"},
              {"type":"ADD","id":10,"dependencies":[],"goal":"실제 작업"}
            ]}
            """;

        Assert.True(
            WorkGraphTransportContract.TryParse(
                response,
                out var patch,
                out var error),
            error);

        var operation = Assert.Single(patch!.Operations);
        Assert.Equal(WorkGraphPatchOperationType.Add, operation.Type);
        Assert.Equal("10", operation.WorkItemId);
        Assert.Equal("실제 작업", operation.Item!.Goal);
    }

    [Fact]
    public async Task StructuredSchemaErrorReturnsControlToHqWithFieldHint()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        executor.SetImmediate("W10");

        var hq = new QueueHqRunner(
            ContinuePatch(0, Add("W10", "초기 조사")),
            ContinuePatch(
                1,
                """
                {"type":"SET_GOAL","workItemId":"W10"}
                """),
            End("완료"));

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "SET_GOAL 스키마 오류를 HQ에 되돌린다.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Null(result.ErrorCode);
        Assert.Equal(3, hq.Prompts.Count);
        Assert.Contains("입력 유형: WORK_GRAPH_PATCH_SCHEMA_REJECTED", hq.Prompts[2]);
        Assert.Contains("errorCode=WORK_GRAPH_SET_GOAL_SCHEMA_INVALID", hq.Prompts[2]);
        Assert.Contains("path=operations[0].value", hq.Prompts[2]);
        Assert.Contains("requires a nonblank \"value\" field", hq.Prompts[2]);
        Assert.Equal(WorkItemState.Completed, Assert.Single(result.Graph.Items).State);
    }

    [Fact]
    public async Task InvalidHqEnvelopeReturnsControlToSameHqFlow()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        var hq = new QueueHqRunner(
            "형식이 잘못된 HQ 응답",
            End("완료"));

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "HQ 제어 형식 오류를 되돌린다.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Null(result.ErrorCode);
        Assert.Equal(2, hq.Prompts.Count);
        Assert.Contains("입력 유형: HQ_RESPONSE_CONTRACT_REJECTED", hq.Prompts[1]);
        Assert.Contains("errorCode=PARALLEL_HQ_", hq.Prompts[1]);
        Assert.Empty(result.Graph.Items);
    }

    [Fact]
    public async Task ConsecutiveRejectedPatchesStopAtRetryLimit()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        var invalid = ContinuePatch(
            0,
            """
            {"type":"SET_GOAL","workItemId":"W404","value":"없는 작업 변경"}
            """);
        var hq = new QueueHqRunner(
            invalid,
            invalid,
            invalid);

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync);

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "잘못된 patch 반복 한계를 확인한다.");

        Assert.Equal(ParallelWorkSupervisorExit.Failed, result.Exit);
        Assert.Equal("WORK_GRAPH_PATCH_RETRY_LIMIT", result.ErrorCode);
        Assert.Equal(3, hq.Prompts.Count);
        Assert.Contains("입력 유형: WORK_GRAPH_PATCH_REJECTED", hq.Prompts[1]);
        Assert.Contains("입력 유형: WORK_GRAPH_PATCH_REJECTED", hq.Prompts[2]);
        Assert.Contains("attempt=3", result.HqBody);
        Assert.Empty(result.Graph.Items);
    }

    [Fact]
    public async Task MalformedJsonBypassesStructuredHelperAndReturnsCorrectionToHq()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        executor.SetImmediate("W10");
        var malformed =
            "[ACTION=CONTINUE]\n[GOTO : WORK]\nWORK_GRAPH_PATCH:\n" +
            "{\"expectedRevision\":0,\"operations\":[";
        var hq = new QueueHqRunner(
            malformed,
            ContinuePatch(0, Add("W10", "JSON 재작성 뒤 실행")),
            End("완료"));
        var helperCalls = 0;

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync,
            processWorkGraphPayloadAsync: (payload, _) =>
            {
                helperCalls++;
                return Task.FromResult(
                    WorkerStructuredPayloadHelper.ProcessDeterministically<WorkGraphPatch>(
                        payload,
                        WorkGraphTransportContract.TryParse));
            });

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "깨진 JSON이면 Helper 없이 HQ가 다시 작성하게 한다.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Null(result.ErrorCode);
        Assert.Equal(3, hq.Prompts.Count);
        Assert.Equal(1, helperCalls);
        Assert.Contains("입력 유형: WORK_GRAPH_PATCH_SCHEMA_REJECTED", hq.Prompts[1]);
        Assert.Contains("errorCode=WORK_GRAPH_PATCH_JSON_INVALID", hq.Prompts[1]);
        Assert.Contains("직전 의미는 유지하고 완전한 WORK_GRAPH_PATCH JSON만 다시 출력하세요.", hq.Prompts[1]);
        Assert.Equal(WorkItemState.Completed, Assert.Single(result.Graph.Items).State);
    }

    [Fact]
    public async Task SupervisorAlwaysPassesContinuePayloadThroughStructuredHelper()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        var hq = new QueueHqRunner(
            ContinuePatch(0, Add("W10", "정상 JSON 작업")),
            End("완료"));
        var helperCalls = 0;

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync,
            processWorkGraphPayloadAsync: (payload, _) =>
            {
                helperCalls++;
                return Task.FromResult(
                    WorkerStructuredPayloadHelper.ProcessDeterministically<WorkGraphPatch>(
                        payload,
                        WorkGraphTransportContract.TryParse));
            });

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "정상 JSON도 Helper를 통과시킨다.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Equal(1, helperCalls);
        Assert.Equal(WorkItemState.Completed, Assert.Single(result.Graph.Items).State);
    }

    [Fact]
    public async Task EndFinalizationCanRejectEndAndReturnControlToHq()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        var hq = new QueueHqRunner(
            End("첫 종료 시도"),
            End("최종 종료"));
        var calls = 0;

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync,
            finalizeEndAsync: (_, _) =>
            {
                calls++;
                return Task.FromResult(
                    calls == 1
                        ? new ParallelEndFinalizationResult(
                            false,
                            "TARGET_INTEGRATION_REQUIRED",
                            "미반영 CODE_CHANGE가 둘 이상입니다.")
                        : new ParallelEndFinalizationResult(true));
            });

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "종료 전 target workspace finalization을 확인한다.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Equal(2, calls);
        Assert.Equal(2, hq.Prompts.Count);
        Assert.Contains("입력 유형: WORKSPACE_FINALIZATION_REQUIRED", hq.Prompts[1]);
        Assert.Contains("TARGET_INTEGRATION_REQUIRED", hq.Prompts[1]);
    }

    [Fact]
    public async Task EndFinalizationExceptionReturnsControlToHqInsteadOfFailingTask()
    {
        var graph = new WorkGraph("job", 1);
        var executor = new SupervisorExecutor();
        var hq = new QueueHqRunner(
            End("첫 종료 시도"),
            End("최종 종료"));
        var calls = 0;

        await using var supervisor = new ParallelWorkSupervisor(
            graph,
            executor,
            "base123",
            hq.RunAsync,
            finalizeEndAsync: (_, _) =>
            {
                calls++;
                if (calls == 1)
                    throw new ArgumentNullException("source");

                return Task.FromResult(new ParallelEndFinalizationResult(true));
            });

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "종료 후처리 예외를 HQ에 되돌린다.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Null(result.ErrorCode);
        Assert.Equal(2, calls);
        Assert.Equal(2, hq.Prompts.Count);
        Assert.Contains("입력 유형: WORKSPACE_FINALIZATION_REQUIRED", hq.Prompts[1]);
        Assert.Contains("errorCode=WORKSPACE_FINALIZATION_EXCEPTION", hq.Prompts[1]);
        Assert.Contains("stage=WORKSPACE_FINALIZATION", hq.Prompts[1]);
        Assert.Contains("exceptionType=ArgumentNullException", hq.Prompts[1]);
        Assert.Contains("Parameter 'source'", hq.Prompts[1]);
    }

    [Fact]
    public void ParallelHqTurnRequiresGraphPatchOnlyForContinue()
    {
        var end = End("완료");
        Assert.True(ParallelHqTurnContract.TryParse(end, out var endTurn, out var endError));
        Assert.Null(endError);
        Assert.Equal(WorkerAction.End, endTurn!.Action);
        Assert.Null(endTurn.Patch);

        const string invalid = "[ACTION=CONTINUE]\n[GOTO : WORK]\n일반 지시";
        Assert.False(ParallelHqTurnContract.TryParse(invalid, out _, out var error));
        Assert.Equal("WORK_GRAPH_PATCH_MARKER_MISSING", error);
    }

    private static string Add(string id, string goal)
        => $$"""
           {"type":"ADD","workItemId":"{{id}}","goal":"{{goal}}","dependencies":[],"kind":"NORMAL","baseRef":"base123"}
           """;

    private static string ContinuePatch(long revision, params string[] operations)
        => "[ACTION=CONTINUE]\n[GOTO : WORK]\nWORK_GRAPH_PATCH:\n" +
           $$"""{"expectedRevision":{{revision}},"operations":[{{string.Join(",", operations)}}]}""";

    private static string End(string body)
        => "[ACTION=END]\n" + body;

    private static string Pause(string body)
        => "[ACTION=PAUSE]\n" + body;

    [Theory]
    [InlineData("WEB_RESPONSE_TIMEOUT: response did not stabilize before deadline.")]
    [InlineData("WEB_RESPONSE_LOST_AFTER_STREAM_END: streaming ended without a recoverable response.")]
    [InlineData("response_timeout")]
    [InlineData("response_lost_after_stream_end")]
    [InlineData("WEB_RESPONSE_KEY_MISSING")]
    [InlineData("response_key_missing")]
    [InlineData("WEB_RESPONSE_BODY_MISSING_AFTER_STREAM_END")]
    public void RecoverableHqTransportFailure_IsRecognized(string message)
    {
        Assert.True(ParallelWorkSupervisor.IsRecoverableHqTransportFailure(
            new InvalidOperationException(message)));
    }

    [Fact]
    public void NonTransportHqFailure_IsNotRecoverable()
    {
        Assert.False(ParallelWorkSupervisor.IsRecoverableHqTransportFailure(
            new InvalidOperationException("WORK_GRAPH_PATCH_INVALID")));
    }

    private sealed class QueueHqRunner
    {
        private readonly Queue<string> _responses;
        private int _turn;

        public QueueHqRunner(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<string> Prompts { get; } = new();
        public Action<int>? OnTurn { get; set; }

        public Task<string> RunAsync(string prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            var turn = Interlocked.Increment(ref _turn);
            OnTurn?.Invoke(turn);
            if (_responses.Count == 0)
                throw new InvalidOperationException("예상보다 HQ 호출이 많습니다.");
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class SupervisorExecutor : IWorkItemExecutor
    {
        private readonly ConcurrentDictionary<string, string> _modes = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _release = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _starts = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _active = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _completed = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<WorkItemExecutionRequest> _requests = new();

        public IReadOnlyList<WorkItemExecutionRequest> Requests => _requests.ToArray();

        public void SetImmediate(string id) => _modes[id] = "IMMEDIATE";
        public void SetControlled(string id) => _modes[id] = "CONTROLLED";
        public void SetBlockedOnceThenComplete(string id) => _modes[id] = "BLOCK_ONCE";
        public void SetResourceOnceThenComplete(string id) => _modes[id] = "RESOURCE_ONCE";

        public bool IsRunning(string id)
            => _active.TryGetValue(id, out var count) && count > 0;

        public bool IsCompleted(string id)
            => _completed.TryGetValue(id, out var value) && value;

        public void Release(string id)
            => ReleaseSignal(id).TrySetResult(true);

        public async Task WhenStartedCount(string id, int expected)
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_starts.TryGetValue(id, out var count) && count >= expected)
                    return;
                await Task.Delay(10);
            }

            throw new TimeoutException($"WorkItem {id} 시작 횟수가 {expected}에 도달하지 못했습니다.");
        }

        public async Task<WorkItemExecutionResult> ExecuteAsync(
            WorkItemExecutionRequest request,
            CancellationToken cancellationToken)
        {
            _requests.Enqueue(request);
            var count = _starts.AddOrUpdate(request.Item.Id, 1, static (_, value) => value + 1);
            _active.AddOrUpdate(request.Item.Id, 1, static (_, value) => value + 1);

            try
            {
                var mode = _modes.TryGetValue(request.Item.Id, out var configured)
                    ? configured
                    : "IMMEDIATE";

                if (mode == "CONTROLLED")
                    await ReleaseSignal(request.Item.Id).Task.WaitAsync(cancellationToken);

                if (mode == "BLOCK_ONCE" && count == 1)
                {
                    return WorkItemExecutionResult.Blocked(
                        "HQ_BLOCKED",
                        "현재 배정 작업에서 확인한 차단 사실입니다.",
                        "checkpoint-" + request.Item.Id,
                        "branch-" + request.Item.Id,
                        "worktree-" + request.Item.Id,
                        "session-" + request.Item.Id);
                }

                if (mode == "RESOURCE_ONCE" && count == 1)
                {
                    return WorkItemExecutionResult.Blocked(
                        "RESOURCE_REQUEST",
                        "RESOURCE_TYPE: IMAGE\n아이콘을 생성해 주세요.",
                        "checkpoint-" + request.Item.Id,
                        "branch-" + request.Item.Id,
                        "worktree-" + request.Item.Id,
                        "session-" + request.Item.Id);
                }

                _completed[request.Item.Id] = true;
                var summary = request.Item.Kind == WorkItemKind.Integration
                    ? "완료" + Environment.NewLine + Environment.NewLine +
                      "REMOTE_CODE_RESULT" + Environment.NewLine +
                      "resultRef: ref-" + request.Item.Id
                    : "완료";
                return WorkItemExecutionResult.Completed(
                    "ref-" + request.Item.Id,
                    summary,
                    "branch-" + request.Item.Id,
                    "worktree-" + request.Item.Id,
                    "session-" + request.Item.Id);
            }
            finally
            {
                _active.AddOrUpdate(request.Item.Id, 0, static (_, value) => Math.Max(0, value - 1));
            }
        }

        private TaskCompletionSource<bool> ReleaseSignal(string id)
            => _release.GetOrAdd(
                id,
                _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
