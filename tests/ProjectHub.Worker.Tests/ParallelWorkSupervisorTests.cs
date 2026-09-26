using System.Collections.Concurrent;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ParallelWorkSupervisorTests
{
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
        Assert.Contains("입력 유형: WORK_GRAPH_QUIESCENT", hq.Prompts[1]);
        Assert.Contains("state=COMPLETED", hq.Prompts[1]);
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
    public async Task SplitRequestWakesHqWhileIndependentWorkIsStillRunning()
    {
        var graph = new WorkGraph("job", 2);
        var executor = new SupervisorExecutor();
        executor.SetSplitOnceThenComplete("W1");
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
        Assert.Contains("blockCode=SPLIT_REQUEST", hq.Prompts[1]);
        Assert.Contains("id=W2", hq.Prompts[1]);
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
        executor.SetSplitOnceThenComplete("W1");

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

        var result = await supervisor.RunAsync(
            "USER_REQUEST",
            "작업을 수행하세요.");

        Assert.Equal(ParallelWorkSupervisorExit.Ended, result.Exit);
        Assert.Equal(4, hq.Prompts.Count);
        Assert.Contains("입력 유형: WORK_GRAPH_END_REJECTED", hq.Prompts[2]);
        Assert.Contains("id=W1 state=BLOCKED", hq.Prompts[2]);
        Assert.Equal(WorkItemState.Completed, Assert.Single(result.Graph.Items).State);
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
        public void SetSplitOnceThenComplete(string id) => _modes[id] = "SPLIT_ONCE";
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

                if (mode == "SPLIT_ONCE" && count == 1)
                {
                    return WorkItemExecutionResult.Blocked(
                        "SPLIT_REQUEST",
                        "W3라는 독립 WorkItem을 추가해 주세요.",
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
                      "INTEGRATION_LANDING" + Environment.NewLine +
                      "status: FAST_FORWARDED"
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
