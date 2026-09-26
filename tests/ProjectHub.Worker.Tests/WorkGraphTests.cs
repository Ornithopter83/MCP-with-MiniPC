using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkGraphTests
{
    [Fact]
    public void IndependentItemsBecomeReadyInStableCreationOrder()
    {
        var graph = new WorkGraph("job", 4);

        var result = graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("W2", "두 번째")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("W1", "첫 번째")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("W3", "세 번째"))
        }));

        Assert.True(result.Success);
        Assert.Equal(1, graph.Revision);
        Assert.Equal(new[] { "W2", "W1", "W3" }, graph.GetReadyItems().Select(item => item.Id));
    }

    [Fact]
    public void DependencyStaysBlockedUntilPredecessorCompletes()
    {
        var graph = new WorkGraph("job", 4);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "기반 작업")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("B", "후속 작업", new[] { "A" }))
        })).Success);

        Assert.Equal(WorkItemState.Ready, graph.Find("A")!.State);
        Assert.Equal(WorkItemState.Blocked, graph.Find("B")!.State);

        Assert.True(graph.TryMarkRunning("A", "branch-a", "worktree-a"));
        Assert.True(graph.TryMarkCompleted("A", "commit-a"));

        Assert.Equal(WorkItemState.Completed, graph.Find("A")!.State);
        Assert.Equal("commit-a", graph.Find("A")!.ResultRef);
        Assert.Equal(WorkItemState.Ready, graph.Find("B")!.State);
    }

    [Fact]
    public void NoOpPatchKeepsRevisionAndCurrentGraph()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("W1", "작업"))
        })).Success);

        var revision = graph.Revision;
        var result = graph.ApplyPatch(new WorkGraphPatch(
            revision,
            Array.Empty<WorkGraphPatchOperation>()));

        Assert.True(result.Success);
        Assert.Equal(revision, result.Revision);
        Assert.Equal(revision, graph.Revision);
        Assert.Equal(WorkItemState.Ready, graph.Find("W1")!.State);
    }

    [Fact]
    public void ContinuationRecoveryReactivatesOnlyReferencedLegacyPreparationFailures()
    {
        var graph = new WorkGraph("job", 4);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("old_design", "과거 설계")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("old_wave", "과거 웨이브")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("design_v3", "현재 설계")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("wave_v3", "현재 웨이브")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("implementation", "구현", new[] { "design_v3" })),
            WorkGraphPatchOperation.Add(new WorkItemSpec("integration", "통합", new[] { "implementation", "wave_v3" }, WorkItemKind.Integration))
        })).Success);

        foreach (var id in new[] { "old_design", "old_wave", "design_v3", "wave_v3" })
        {
            Assert.True(graph.TryMarkRunning(id));
            Assert.True(graph.TryMarkFailed(id, "WORKTREE_CREATE_FAILED", "과거 준비 실패"));
        }

        var recovered = graph.RecoverPreparationFailuresForContinuation();

        Assert.Equal(new[] { "design_v3", "wave_v3" }, recovered);
        Assert.Equal(WorkItemState.Failed, graph.Find("old_design")!.State);
        Assert.Equal(WorkItemState.Failed, graph.Find("old_wave")!.State);
        Assert.Equal(WorkItemState.Ready, graph.Find("design_v3")!.State);
        Assert.Equal(WorkItemState.Ready, graph.Find("wave_v3")!.State);
        Assert.Null(graph.Find("design_v3")!.FailureCode);
        Assert.Null(graph.Find("wave_v3")!.ResultSummary);
        Assert.Equal(WorkItemState.Blocked, graph.Find("implementation")!.State);
        Assert.Equal(WorkItemState.Blocked, graph.Find("integration")!.State);
    }

    [Fact]
    public void ContinuationRecoveryReleasesCurrentPreparationBlock()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("W1", "작업"))
        })).Success);
        Assert.True(graph.TryMarkRunning("W1"));
        Assert.True(graph.TryMarkBlocked(
            "W1",
            "WORKTREE_CREATE_FAILED",
            "git worktree add 실패"));

        var recovered = graph.RecoverPreparationFailuresForContinuation();

        Assert.Equal(new[] { "W1" }, recovered);
        var item = graph.Find("W1")!;
        Assert.Equal(WorkItemState.Ready, item.State);
        Assert.Null(item.BlockCode);
        Assert.Null(item.ResultSummary);
        Assert.Null(item.StartedAtUtc);
        Assert.Null(item.FinishedAtUtc);
    }

    [Fact]
    public void PatchRevisionMismatchIsRejectedWithoutMutation()
    {
        var graph = new WorkGraph("job");

        var result = graph.ApplyPatch(new WorkGraphPatch(3, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "작업"))
        }));

        Assert.False(result.Success);
        Assert.Equal("WORK_GRAPH_REVISION_MISMATCH", result.ErrorCode);
        Assert.Equal(0, graph.Revision);
        Assert.Empty(graph.Items);
    }

    [Theory]
    [InlineData("WORK_GRAPH_SELF_DEPENDENCY", "SELF")]
    [InlineData("WORK_GRAPH_DEPENDENCY_NOT_FOUND", "UNKNOWN")]
    [InlineData("WORK_GRAPH_CYCLE_DETECTED", "CYCLE")]
    public void InvalidDependencyGraphIsRejectedAtomically(string expectedError, string mode)
    {
        var graph = new WorkGraph("job");

        WorkGraphPatch patch = mode switch
        {
            "SELF" => new WorkGraphPatch(0, new[]
            {
                WorkGraphPatchOperation.Add(new WorkItemSpec("A", "작업", new[] { "A" }))
            }),
            "UNKNOWN" => new WorkGraphPatch(0, new[]
            {
                WorkGraphPatchOperation.Add(new WorkItemSpec("A", "작업", new[] { "MISSING" }))
            }),
            _ => new WorkGraphPatch(0, new[]
            {
                WorkGraphPatchOperation.Add(new WorkItemSpec("A", "A", new[] { "B" })),
                WorkGraphPatchOperation.Add(new WorkItemSpec("B", "B", new[] { "A" }))
            })
        };

        var result = graph.ApplyPatch(patch);

        Assert.False(result.Success);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.Equal(0, graph.Revision);
        Assert.Empty(graph.Items);
    }

    [Fact]
    public void FailedDependencyKeepsDependentBlockedWhileIndependentWorkRemainsReady()
    {
        var graph = new WorkGraph("job", 4);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "실패할 작업")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("B", "A 의존", new[] { "A" })),
            WorkGraphPatchOperation.Add(new WorkItemSpec("C", "독립 작업"))
        })).Success);

        Assert.True(graph.TryMarkRunning("A"));
        Assert.True(graph.TryMarkFailed("A", "TEST_FAILURE"));

        Assert.Equal(WorkItemState.Failed, graph.Find("A")!.State);
        Assert.Equal(WorkItemState.Blocked, graph.Find("B")!.State);
        Assert.Equal(WorkItemState.Ready, graph.Find("C")!.State);
    }

    [Fact]
    public void RunningItemDefinitionCannotBeRewrittenByPatch()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "원래 목표"))
        })).Success);
        Assert.True(graph.TryMarkRunning("A"));

        var result = graph.ApplyPatch(new WorkGraphPatch(1, new[]
        {
            WorkGraphPatchOperation.SetGoal("A", "바뀐 목표")
        }));

        Assert.False(result.Success);
        Assert.Equal("WORK_GRAPH_RUNNING_OR_TERMINAL_ITEM_IMMUTABLE", result.ErrorCode);
        Assert.Equal("원래 목표", graph.Find("A")!.Goal);
        Assert.Equal(1, graph.Revision);
    }

    [Fact]
    public void CancelingPredecessorDoesNotImplicitlyReleaseDependentWork()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "선행")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("B", "후행", new[] { "A" }))
        })).Success);

        var cancel = graph.ApplyPatch(new WorkGraphPatch(1, new[]
        {
            WorkGraphPatchOperation.Cancel("A")
        }));

        Assert.True(cancel.Success);
        Assert.Equal(WorkItemState.Canceled, graph.Find("A")!.State);
        Assert.Equal(WorkItemState.Blocked, graph.Find("B")!.State);
    }

    [Fact]
    public void TerminalCancelIsIdempotentAndDoesNotBlockRetryPatch()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("failed", "실패 작업")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("completed", "완료 작업")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("dependent", "후속 작업", new[] { "failed" }))
        })).Success);

        Assert.True(graph.TryMarkRunning("failed"));
        Assert.True(graph.TryMarkFailed("failed", "TEST_FAILURE"));
        Assert.True(graph.TryMarkRunning("completed"));
        Assert.True(graph.TryMarkCompleted("completed", "completed-ref"));

        var retry = graph.ApplyPatch(new WorkGraphPatch(graph.Revision, new[]
        {
            WorkGraphPatchOperation.Cancel("failed"),
            WorkGraphPatchOperation.Cancel("completed"),
            WorkGraphPatchOperation.Add(new WorkItemSpec("retry", "재시도 작업")),
            WorkGraphPatchOperation.SetDependencies("dependent", new[] { "retry" })
        }));

        Assert.True(retry.Success);
        Assert.Equal(WorkItemState.Failed, graph.Find("failed")!.State);
        Assert.Equal(WorkItemState.Completed, graph.Find("completed")!.State);
        Assert.Equal(WorkItemState.Ready, graph.Find("retry")!.State);
        Assert.Equal(WorkItemState.Blocked, graph.Find("dependent")!.State);
        Assert.Equal(new[] { "retry" }, graph.Find("dependent")!.Dependencies);

        Assert.True(graph.TryMarkRunning("retry"));
        Assert.True(graph.TryMarkCompleted("retry", "retry-ref"));
        Assert.Equal(WorkItemState.Ready, graph.Find("dependent")!.State);
    }

    [Fact]
    public void ConcurrencyCanBeChangedOnlyWithinMechanicalBounds()
    {
        var graph = new WorkGraph("job", 1);

        var accepted = graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.SetMaxConcurrency(4)
        }));
        Assert.True(accepted.Success);
        Assert.Equal(4, graph.MaxConcurrentWork);

        var rejected = graph.ApplyPatch(new WorkGraphPatch(1, new[]
        {
            WorkGraphPatchOperation.SetMaxConcurrency(9)
        }));
        Assert.False(rejected.Success);
        Assert.Equal("WORK_GRAPH_CONCURRENCY_INVALID", rejected.ErrorCode);
        Assert.Equal(4, graph.MaxConcurrentWork);
    }

    [Fact]
    public void HqHoldRemainsBlockedUntilExplicitRelease()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "분할 판단이 필요한 작업"))
        })).Success);

        Assert.True(graph.TryMarkRunning("A", sessionId: "session-a"));
        Assert.True(graph.TryMarkBlocked("A", "SPLIT_REQUEST", "새 독립 작업이 필요합니다."));

        var held = graph.Find("A")!;
        Assert.Equal(WorkItemState.Blocked, held.State);
        Assert.Equal("SPLIT_REQUEST", held.BlockCode);
        Assert.Equal("session-a", held.SessionId);
        Assert.Empty(graph.GetReadyItems());

        var release = graph.ApplyPatch(new WorkGraphPatch(
            graph.Revision,
            new[] { WorkGraphPatchOperation.Release("A", "HQ_RESUME", "분할 작업을 추가했으니 계속 진행하세요.") }));

        Assert.True(release.Success);
        Assert.Equal(WorkItemState.Ready, graph.Find("A")!.State);
        Assert.Null(graph.Find("A")!.BlockCode);
        Assert.Equal("HQ_RESUME", graph.Find("A")!.ResumeInputType);
        Assert.Equal("분할 작업을 추가했으니 계속 진행하세요.", graph.Find("A")!.ResumeBody);
        Assert.Equal("session-a", graph.Find("A")!.SessionId);
    }

    [Fact]
    public void IntegrationItemUsesTheSameWorkRoleAndWaitsForAllDependencies()
    {
        var graph = new WorkGraph("job", 4);
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "기능 A")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("B", "기능 B")),
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                "I",
                "통합",
                new[] { "A", "B" },
                WorkItemKind.Integration))
        })).Success);

        Assert.Equal(WorkItemKind.Integration, graph.Find("I")!.Kind);
        Assert.Equal(WorkItemState.Blocked, graph.Find("I")!.State);

        Assert.True(graph.TryMarkRunning("A"));
        Assert.True(graph.TryMarkCompleted("A", "a-ref"));
        Assert.Equal(WorkItemState.Blocked, graph.Find("I")!.State);

        Assert.True(graph.TryMarkRunning("B"));
        Assert.True(graph.TryMarkCompleted("B", "b-ref"));
        Assert.Equal(WorkItemState.Ready, graph.Find("I")!.State);
    }
}
