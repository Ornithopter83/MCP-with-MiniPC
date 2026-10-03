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

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("8")]
    [InlineData("9")]
    public void FixedSlotsCanBeAddedAgainAfterTerminalCompletion(string id)
    {
        var graph = new WorkGraph("job");

        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                id,
                "첫 실행",
                BaseRef: "base-a",
                Checklist: new[] { "첫 실행 작업" }))
        })).Success);

        var first = graph.Find(id)!;
        Assert.True(graph.TryMarkRunning(id, "branch-a", "worktree-a", "session-a"));
        Assert.True(graph.TryMarkCompleted(
            id,
            "result-a",
            "첫 실행 완료",
            WorkItemResultType.Analysis));

        var result = graph.ApplyPatch(new WorkGraphPatch(graph.Revision, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                id,
                "두 번째 실행",
                BaseRef: "base-b",
                Checklist: new[] { "두 번째 실행 작업" }))
        }));

        Assert.True(result.Success);
        var second = graph.Find(id)!;
        Assert.Equal(WorkItemState.Ready, second.State);
        Assert.True(second.CreatedOrder > first.CreatedOrder);
        Assert.Equal("두 번째 실행", second.Goal);
        Assert.Equal("base-b", second.BaseRef);
        Assert.Null(second.Branch);
        Assert.Null(second.WorktreePath);
        Assert.Null(second.SessionId);
        Assert.Null(second.ResultRef);
        Assert.Null(second.ResultSummary);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("8")]
    [InlineData("9")]
    public void FixedSlotsCannotDeclareDependencies(string id)
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("10", "일반 작업"))
        })).Success);

        var result = graph.ApplyPatch(new WorkGraphPatch(graph.Revision, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                id,
                "고정 슬롯",
                new[] { "10" }))
        }));

        Assert.False(result.Success);
        Assert.Equal("WORK_GRAPH_FIXED_SLOT_DEPENDENCY_UNSUPPORTED", result.ErrorCode);
        Assert.Null(graph.Find(id));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("8")]
    [InlineData("9")]
    public void GeneralWorkCannotDependOnFixedSlots(string id)
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(id, "고정 슬롯"))
        })).Success);

        var result = graph.ApplyPatch(new WorkGraphPatch(graph.Revision, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                "10",
                "일반 작업",
                new[] { id }))
        }));

        Assert.False(result.Success);
        Assert.Equal("WORK_GRAPH_FIXED_SLOT_DEPENDENCY_UNSUPPORTED", result.ErrorCode);
        Assert.Null(graph.Find("10"));
    }

    [Fact]
    public void SetDependenciesCannotConnectFixedSlots()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("9", "고정 슬롯")),
            WorkGraphPatchOperation.Add(new WorkItemSpec("10", "일반 작업"))
        })).Success);

        var fixedToGeneral = graph.ApplyPatch(new WorkGraphPatch(graph.Revision, new[]
        {
            WorkGraphPatchOperation.SetDependencies("9", "10")
        }));
        Assert.False(fixedToGeneral.Success);
        Assert.Equal("WORK_GRAPH_FIXED_SLOT_DEPENDENCY_UNSUPPORTED", fixedToGeneral.ErrorCode);

        var generalToFixed = graph.ApplyPatch(new WorkGraphPatch(graph.Revision, new[]
        {
            WorkGraphPatchOperation.SetDependencies("10", "9")
        }));
        Assert.False(generalToFixed.Success);
        Assert.Equal("WORK_GRAPH_FIXED_SLOT_DEPENDENCY_UNSUPPORTED", generalToFixed.ErrorCode);
        Assert.Empty(graph.Find("9")!.Dependencies);
        Assert.Empty(graph.Find("10")!.Dependencies);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("4")]
    [InlineData("7")]
    [InlineData("10")]
    public void NonFixedIdsCanBeUsedForOrdinaryWorkItems(string id)
    {
        var graph = new WorkGraph("job");

        var result = graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(id, "일반 작업"))
        }));

        Assert.True(result.Success);
        Assert.NotNull(graph.Find(id));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("8")]
    [InlineData("9")]
    public void FixedSlotsMustUseNormalKind(string id)
    {
        var graph = new WorkGraph("job");

        var result = graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                id,
                "고정 슬롯",
                Kind: WorkItemKind.Integration))
        }));

        Assert.False(result.Success);
        Assert.Equal("WORK_GRAPH_FIXED_SLOT_KIND_INVALID", result.ErrorCode);
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
        Assert.True(graph.TryMarkCompleted(
            "A",
            "commit-a",
            resultType: WorkItemResultType.CodeChange));

        Assert.Equal(WorkItemState.Completed, graph.Find("A")!.State);
        Assert.Equal("commit-a", graph.Find("A")!.ResultRef);
        Assert.Equal(WorkItemResultType.CodeChange, graph.Find("A")!.ResultType);
        Assert.Equal(WorkItemState.Ready, graph.Find("B")!.State);
    }

    [Fact]
    public void CodeChangeProvenanceSurvivesBlockedResumeWithoutNewCommit()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "구현 작업"))
        })).Success);

        Assert.True(graph.TryMarkRunning("A"));
        Assert.True(graph.TryMarkBlocked(
            "A",
            "HQ_BLOCKED",
            "빌드 환경 확인이 필요합니다.",
            "checkpoint-a",
            resultType: WorkItemResultType.CodeChange));

        var blocked = graph.Find("A")!;
        Assert.Equal(WorkItemResultType.CodeChange, blocked.ResultType);
        Assert.Equal("checkpoint-a", blocked.ResultRef);

        Assert.True(graph.TryReleaseBlocked("A", "WORK_RESULT", "계속 진행하세요."));
        Assert.True(graph.TryMarkRunning("A"));
        Assert.True(graph.TryMarkCompleted(
            "A",
            "checkpoint-a",
            "검증 완료",
            WorkItemResultType.Analysis));

        var completed = graph.Find("A")!;
        Assert.Equal(WorkItemState.Completed, completed.State);
        Assert.Equal(WorkItemResultType.CodeChange, completed.ResultType);
        Assert.Equal("checkpoint-a", completed.ResultRef);
    }

    [Fact]
    public void BlockDetailCodePersistsAcrossSnapshotRestore()
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
            "원격 Git 접근 실패",
            "ref-I1",
            "INTEGRATION_REMOTE_FETCH_TIMEOUT"));

        var restored = WorkGraph.Restore(graph.Snapshot(), markRunningAsRecoveryBlocked: false);
        var item = restored.Find("I1")!;

        Assert.Equal("INTEGRATION_REMOTE_FETCH_FAILED", item.BlockCode);
        Assert.Equal("INTEGRATION_REMOTE_FETCH_TIMEOUT", item.BlockDetailCode);
    }

    [Fact]
    public void RunningContextCanPersistEffectiveIntegrationBaseRef()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                "I1",
                "통합",
                Kind: WorkItemKind.Integration,
                BaseRef: "stale"))
        })).Success);
        Assert.True(graph.TryMarkRunning("I1"));

        Assert.True(graph.TryUpdateExecutionContext(
            "I1",
            branch: "projecthub/job/I1",
            worktreePath: "C:/wt/I1",
            sessionId: null,
            baseRef: "primary999"));

        Assert.Equal("primary999", graph.Find("I1")!.BaseRef);
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
    public void ContinuationRecoveryReleasesIntegrationClonePreparationBlock()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                "I1",
                "통합",
                Kind: WorkItemKind.Integration))
        })).Success);
        Assert.True(graph.TryMarkRunning("I1"));
        Assert.True(graph.TryMarkBlocked(
            "I1",
            "INTEGRATION_REMOTE_FETCH_FAILED",
            "origin fetch 실패"));

        var recovered = graph.RecoverPreparationFailuresForContinuation();

        Assert.Equal(new[] { "I1" }, recovered);
        var item = graph.Find("I1")!;
        Assert.Equal(WorkItemState.Ready, item.State);
        Assert.Null(item.BlockCode);
        Assert.Null(item.ResultSummary);
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
            "WORK_CLONE_CREATE_FAILED",
            "git clone 실패"));

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
    public void CheckpointPendingReleaseUsesMechanicalRetryInput()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "구현 작업", BaseRef: "base123"))
        })).Success);

        Assert.True(graph.TryMarkRunning("A", sessionId: "session-a"));
        Assert.True(graph.TryMarkBlocked(
            "A",
            "WORKTREE_CHECKPOINT_PENDING",
            "WORKTREE_CHECKPOINT_PENDING\nreportStatus: Completed\n\n구현 완료",
            "base123",
            blockDetailCode: "WORKTREE_CHECKPOINT_ADD_FAILED"));

        var release = graph.ApplyPatch(new WorkGraphPatch(
            graph.Revision,
            new[] { WorkGraphPatchOperation.Release("A", "HQ_RESUME", "다시 실행") }));

        Assert.True(release.Success);
        var item = graph.Find("A")!;
        Assert.Equal(WorkItemState.Ready, item.State);
        Assert.Equal("WORKTREE_CHECKPOINT_RETRY", item.ResumeInputType);
        Assert.Null(item.ResumeBody);
        Assert.Equal("session-a", item.SessionId);
        Assert.Contains("구현 완료", item.ResultSummary);
    }

    [Fact]
    public void BuildRequestReleaseWithoutParsableInputStillAuthorizesBuildForSlot9()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                FixedWorkItemSlots.BuildPublish,
                "중간 milestone 빌드",
                BaseRef: "base"))
        })).Success);
        Assert.True(graph.TryMarkRunning(FixedWorkItemSlots.BuildPublish, sessionId: "session-a"));
        Assert.True(graph.TryMarkBlocked(
            FixedWorkItemSlots.BuildPublish,
            "BUILD_REQUEST",
            "BUILD_REQUEST\n빌드 필요",
            "checkpoint-a",
            resultType: WorkItemResultType.CodeChange));

        var release = graph.ApplyPatch(new WorkGraphPatch(
            graph.Revision,
            new[] { WorkGraphPatchOperation.Release(
                FixedWorkItemSlots.BuildPublish,
                null,
                "구조화되지 않은 BUILD 승인") }));

        Assert.True(release.Success);
        var item = graph.Find(FixedWorkItemSlots.BuildPublish)!;
        Assert.Equal(WorkItemState.Ready, item.State);
        Assert.Equal("BUILD_AUTHORIZED", item.ResumeInputType);
        Assert.Equal("구조화되지 않은 BUILD 승인", item.ResumeBody);
        Assert.Equal("checkpoint-a", item.ResultRef);
        Assert.Equal(WorkItemResultType.CodeChange, item.ResultType);
    }

    [Fact]
    public void GeneralWorkCannotCarryBuildExecutionChecklist()
    {
        var graph = new WorkGraph("job");

        var result = graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                "10",
                "문서 모델 구현",
                Checklist: new[]
                {
                    "Document 모델을 구현한다.",
                    "Release 빌드를 실행해 성공을 확인한다."
                }))
        }));

        Assert.False(result.Success);
        Assert.Equal("WORK_GRAPH_BUILD_STEP_REQUIRES_SLOT_9", result.ErrorCode);
        Assert.Null(graph.Find("10"));
    }

    [Fact]
    public void StaticImplementationGoalDoesNotTripBuildGate()
    {
        var graph = new WorkGraph("job");

        var result = graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                "10",
                "PSD export 기능을 구현한다.",
                Checklist: new[]
                {
                    "PSD exporter 코드를 구현한다.",
                    "빌드는 수행하지 않고 정적 검토 결과를 보고한다."
                }))
        }));

        Assert.True(result.Success);
        Assert.Equal(WorkItemState.Ready, graph.Find("10")!.State);
    }

    [Fact]
    public void BuildSlotCannotBeScheduledWhileImplementationWaveIsActive()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("10", "중간 구현"))
        })).Success);

        var result = graph.ApplyPatch(new WorkGraphPatch(graph.Revision, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                FixedWorkItemSlots.BuildPublish,
                "중간 milestone 빌드",
                BaseRef: "base"))
        }));

        Assert.False(result.Success);
        Assert.Equal("WORK_GRAPH_BUILD_SLOT_REQUIRES_MILESTONE", result.ErrorCode);
        Assert.Null(graph.Find(FixedWorkItemSlots.BuildPublish));
    }

    [Fact]
    public void BuildSlotCanRunAfterCurrentMilestoneIsCompleted()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("10", "중간 구현"))
        })).Success);
        Assert.True(graph.TryMarkRunning("10"));
        Assert.True(graph.TryMarkCompleted(
            "10",
            "milestone-ref",
            resultType: WorkItemResultType.CodeChange));

        var result = graph.ApplyPatch(new WorkGraphPatch(graph.Revision, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec(
                FixedWorkItemSlots.BuildPublish,
                "중간 milestone 빌드",
                BaseRef: "milestone-ref"))
        }));

        Assert.True(result.Success);
        Assert.Equal(WorkItemState.Ready, graph.Find(FixedWorkItemSlots.BuildPublish)!.State);
    }

    [Fact]
    public void GeneralLegacyBuildRequestCannotBeAuthorized()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("10", "구현"))
        })).Success);
        Assert.True(graph.TryMarkRunning("10"));
        Assert.True(graph.TryMarkBlocked("10", "BUILD_REQUEST", "legacy"));

        var result = graph.ApplyPatch(new WorkGraphPatch(
            graph.Revision,
            new[] { WorkGraphPatchOperation.Release("10", null, "승인") }));

        Assert.False(result.Success);
        Assert.Equal("WORK_GRAPH_BUILD_SLOT_REQUIRED", result.ErrorCode);
        Assert.Equal(WorkItemState.Blocked, graph.Find("10")!.State);
    }

    [Fact]
    public void HqHoldRemainsBlockedUntilExplicitRelease()
    {
        var graph = new WorkGraph("job");
        Assert.True(graph.ApplyPatch(new WorkGraphPatch(0, new[]
        {
            WorkGraphPatchOperation.Add(new WorkItemSpec("A", "HQ 확인이 필요한 작업"))
        })).Success);

        Assert.True(graph.TryMarkRunning("A", sessionId: "session-a"));
        Assert.True(graph.TryMarkBlocked("A", "HQ_BLOCKED", "현재 작업의 차단 사실입니다."));

        var held = graph.Find("A")!;
        Assert.Equal(WorkItemState.Blocked, held.State);
        Assert.Equal("HQ_BLOCKED", held.BlockCode);
        Assert.Equal("session-a", held.SessionId);
        Assert.Empty(graph.GetReadyItems());

        var release = graph.ApplyPatch(new WorkGraphPatch(
            graph.Revision,
            new[] { WorkGraphPatchOperation.Release("A", "HQ_RESUME", "HQ 확인 후 계속 진행하세요.") }));

        Assert.True(release.Success);
        Assert.Equal(WorkItemState.Ready, graph.Find("A")!.State);
        Assert.Null(graph.Find("A")!.BlockCode);
        Assert.Equal("HQ_RESUME", graph.Find("A")!.ResumeInputType);
        Assert.Equal("HQ 확인 후 계속 진행하세요.", graph.Find("A")!.ResumeBody);
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
