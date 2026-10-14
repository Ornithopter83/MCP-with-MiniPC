using System.Collections.ObjectModel;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerHistoryLifecycleTests
{
    private static MainWindow.WorkerHistoryEvent Card(
        string reference,
        string stage,
        string type,
        string body,
        DateTimeOffset? timestamp = null) =>
        new(timestamp ?? DateTimeOffset.UtcNow, stage, type,
            type == "ROLE_PROGRESS" ? "작업 진행" : "응답 데이터 · 작업 응답",
            body, null, null, null,
            type == "ROLE_PROGRESS" ? "RUNNING" : "RECEIVED", reference)
        {
            FullMessage = body,
            WorkItemId = stage == "Implementer" ? "26" : null
        };

    [Theory]
    [InlineData("Coordinator")]
    [InlineData("Implementer")]
    [InlineData("Qa")]
    [InlineData("HighLevel")]
    [InlineData("Resource")]
    public void RoleAppearsAtActivation_AndResponseFinalizesSameCard(string stage)
    {
        var cards = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        var id = WorkerHistoryCardPolicy.NewInvocationReference("M4", stage);
        WorkerHistoryCardPolicy.Publish(cards,
            Card(id, stage, "ROLE_PROGRESS", "전송 완료 · 응답 대기"));
        Assert.Single(cards);
        Assert.Equal("RUNNING", cards[0].Status);
        Assert.Equal("ROLE_PROGRESS", cards[0].EventType);

        WorkerHistoryCardPolicy.PublishRoleResponse(cards,
            Card(id, stage, "ROLE_RESPONSE", "작업 종료 보고"));
        Assert.Single(cards);
        Assert.Equal("ROLE_RESPONSE", cards[0].EventType);
        Assert.StartsWith("작업 종료 보고", cards[0].FullMessage);
        if (stage == "Implementer")
            Assert.Contains("----- WORK DETAIL -----", cards[0].FullMessage);
    }

    [Theory]
    [InlineData("HIGH", "HIGH", "M17:HIGH:review-1", true)]
    [InlineData("HIGH", null, "M17:HIGH:review-2", true)]
    [InlineData("HIGH", "HIGH", null, false)]
    [InlineData("WORK", "27", "M17:WORK:27", true)]
    [InlineData("WORK", null, "M17:WORK:27", false)]
    [InlineData("DESIGN", "0", "M17:DESIGN:0", false)]
    public void CliProgressRoutesToActiveRoleCardOnly(
        string roleName, string? workId, string? reference, bool expected)
    {
        Assert.Equal(expected,
            WorkerHistoryCardPolicy.ShouldStreamToRoleCard(
                roleName, workId, reference));
    }

    [Fact]
    public void HighProgress_UpdatesSameLiveCardAndPreservesEvidenceOnCompletion()
    {
        var cards = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        var first = WorkerHistoryCardPolicy.NewInvocationReference("M17", "HIGH");
        WorkerHistoryCardPolicy.AccumulateRoleProgress(cards,
            Card(first, "HighLevel", "ROLE_PROGRESS",
                "요청 전달 완료 · 응답 대기"));
        WorkerHistoryCardPolicy.AccumulateRoleProgress(cards,
            Card(first, "HighLevel", "ROLE_PROGRESS",
                "명령 실행 시작 · unity command get_scene_hierarchy"));
        WorkerHistoryCardPolicy.AccumulateRoleProgress(cards,
            Card(first, "HighLevel", "ROLE_PROGRESS",
                "명령 실행 종료 · exit 0 · unity command get_scene_hierarchy"));
        Assert.Single(cards);
        Assert.Equal("HighLevel", cards[0].StageKey);
        Assert.Equal("RUNNING", cards[0].Status);
        Assert.Contains("명령 실행 종료", cards[0].Summary);
        Assert.Contains("명령 실행 시작", cards[0].FullMessage);
        Assert.Contains("응답 대기", cards[0].FullMessage);

        WorkerHistoryCardPolicy.PublishRoleResponse(cards,
            Card(first, "HighLevel", "ROLE_RESPONSE", "최종 HIGH 검토 완료"));
        Assert.Single(cards);
        Assert.Equal("ROLE_RESPONSE", cards[0].EventType);
        Assert.Contains("----- HIGH DETAIL -----", cards[0].FullMessage);
        Assert.Contains("unity command get_scene_hierarchy", cards[0].FullMessage);
        Assert.StartsWith("최종 HIGH 검토 완료", cards[0].FullMessage);

        // Late CLI events must not resurrect a RUNNING card.
        WorkerHistoryCardPolicy.AccumulateRoleProgress(cards,
            Card(first, "HighLevel", "ROLE_PROGRESS", "늦은 진행 이벤트"));
        Assert.Single(cards);
        Assert.Equal("ROLE_RESPONSE", cards[0].EventType);
    }

    [Fact]
    public void HighResumedReview_HasIndependentProgressAndResponseCard()
    {
        var cards = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        var first = WorkerHistoryCardPolicy.NewInvocationReference("M17", "HIGH");
        var second = WorkerHistoryCardPolicy.NewInvocationReference("M17", "HIGH");
        WorkerHistoryCardPolicy.AccumulateRoleProgress(cards,
            Card(first, "HighLevel", "ROLE_PROGRESS", "첫 검토 진행"));
        WorkerHistoryCardPolicy.PublishRoleResponse(cards,
            Card(first, "HighLevel", "ROLE_RESPONSE", "design_handoff"));
        WorkerHistoryCardPolicy.AccumulateRoleProgress(cards,
            Card(second, "HighLevel", "ROLE_PROGRESS", "DESIGN 수정 재점검"));
        Assert.Equal(2, cards.Count);
        Assert.Contains(cards, x => x.ReferenceId == first &&
            x.EventType == "ROLE_RESPONSE");
        Assert.Contains(cards, x => x.ReferenceId == second &&
            x.EventType == "ROLE_PROGRESS" &&
            x.Summary!.Contains("DESIGN 수정 재점검"));
        WorkerHistoryCardPolicy.PublishRoleResponse(cards,
            Card(second, "HighLevel", "ROLE_RESPONSE", "재점검 완료"));
        Assert.Equal(2, cards.Count);
        Assert.All(cards, x => Assert.Equal("ROLE_RESPONSE", x.EventType));
    }

    [Fact]
    public void DesignProgress_ReplacesRunningDesignCardOnCompletion()
    {
        var cards = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        var refId = "M17:DESIGN:0";
        WorkerHistoryCardPolicy.Publish(cards,
            Card(refId, "Design", "ROLE_PROGRESS", "Unity 그래픽 제작 중"));
        Assert.Equal("Unity DESIGN (#0)", cards[0].Role);
        WorkerHistoryCardPolicy.PublishRoleResponse(cards,
            Card(refId, "Design", "ROLE_RESPONSE", "시각 파일 변경 완료"));
        Assert.Single(cards);
        Assert.Equal("Design", cards[0].StageKey);
        Assert.Equal("ROLE_RESPONSE", cards[0].EventType);
        Assert.Equal("Unity DESIGN (#0)", cards[0].Role);
    }

    [Fact]
    public void WorkDetails_AccumulateWithinOneGreenCard_AndPromoteAtCompletion()
    {
        var cards = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        var reference = WorkerHistoryCardPolicy.NewInvocationReference("M4", "26");
        WorkerHistoryCardPolicy.AccumulateWorkProgress(cards,
            Card(reference, "Implementer", "ROLE_PROGRESS", "WORK 시작"));
        WorkerHistoryCardPolicy.AccumulateWorkProgress(cards,
            Card(reference, "Implementer", "ROLE_PROGRESS", "프로젝트 조사"));
        WorkerHistoryCardPolicy.AccumulateWorkProgress(cards,
            Card(reference, "Implementer", "ROLE_PROGRESS", "cmd.exe 실행"));
        Assert.Single(cards);
        Assert.Contains("WORK 시작", cards[0].FullMessage);
        Assert.Contains("프로젝트 조사", cards[0].FullMessage);
        Assert.Contains("cmd.exe 실행", cards[0].FullMessage);
        Assert.Contains("cmd.exe 실행", cards[0].Summary);

        WorkerHistoryCardPolicy.PublishRoleResponse(cards,
            Card(reference, "Implementer", "ROLE_RESPONSE", "COMPLETED"));
        Assert.Single(cards);
        Assert.StartsWith("COMPLETED", cards[0].FullMessage);
        Assert.Contains("----- WORK DETAIL -----", cards[0].FullMessage);
        Assert.Contains("프로젝트 조사", cards[0].FullMessage);
        Assert.Contains("cmd.exe 실행", cards[0].FullMessage);

        // Dispatcher callbacks delivered out of order must not re-open the card.
        WorkerHistoryCardPolicy.AccumulateWorkProgress(cards,
            Card(reference, "Implementer", "ROLE_PROGRESS", "뒤늦은 이벤트"));
        Assert.Single(cards);
        Assert.Equal("ROLE_RESPONSE", cards[0].EventType);
    }

    [Fact]
    public void ConcurrentWork_AndContinueInvocationRemainSeparate()
    {
        var cards = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        var first = WorkerHistoryCardPolicy.NewInvocationReference("M2", "21");
        var second = WorkerHistoryCardPolicy.NewInvocationReference("M2", "22");
        var resumed = WorkerHistoryCardPolicy.NewInvocationReference("M2", "21");
        foreach (var id in new[] { first, second, resumed })
            WorkerHistoryCardPolicy.AccumulateWorkProgress(cards,
                Card(id, "Implementer", "ROLE_PROGRESS", "실행 중"));
        Assert.Equal(3, cards.Count);
        WorkerHistoryCardPolicy.PublishRoleResponse(cards,
            Card(first, "Implementer", "ROLE_RESPONSE", "첫 번째 결과"));
        Assert.Equal(3, cards.Count);
        Assert.Contains(cards, x => x.ReferenceId == resumed && x.EventType == "ROLE_PROGRESS");
        Assert.Contains(cards, x => x.ReferenceId == second && x.EventType == "ROLE_PROGRESS");
    }

    [Fact]
    public void CliHqHistory_ShowsDispatchRunningResponseAndWorkerReceipt()
    {
        // Verifies card sequencing only; a real CLI HQ session is the next
        // end-to-end manual check, not simulated by this unit test.
        var history = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        var requestId = WorkerHistoryCardPolicy.NewInvocationReference("HQ", "MILESTONE_1");
        var now = DateTimeOffset.UtcNow;
        WorkerHistoryCardPolicy.Publish(history,
            new MainWindow.WorkerHistoryEvent(now, "Worker", "DATA_FLOW",
                "Worker → HQ 전달", "CLI 요청", null, null, null,
                "SENT", requestId));
        WorkerHistoryCardPolicy.Publish(history,
            Card(requestId, "Coordinator", "ROLE_PROGRESS",
                "CLI HQ 응답 대기", now.AddSeconds(1)));

        Assert.Equal(2, history.Count);
        Assert.Equal("RUNNING", history[1].Status);

        WorkerHistoryCardPolicy.PublishRoleResponse(history,
            Card(requestId, "Coordinator", "ROLE_RESPONSE",
                "[ACTION=WORK]", now.AddSeconds(2)));
        WorkerHistoryCardPolicy.Publish(history,
            new MainWindow.WorkerHistoryEvent(now.AddSeconds(3), "Worker",
                "DATA_FLOW", "Worker · HQ 응답 수신", "HQ ACTION: WORK",
                null, null, null, "RECEIVED", null));

        Assert.Equal(3, history.Count);
        Assert.Equal("ROLE_RESPONSE", history[1].EventType);
        Assert.Equal("Worker · HQ 응답 수신", history[2].Title);
    }

    [Fact]
    public void HqReception_BecomesWorkerControlCard_NotRepeatedCommandDetail()
    {
        Assert.True(WorkerHistoryCardPolicy.ShouldShowDataFlow(
            "HQ 응답 수신", "RECEIVED", "WORKER ACTION"));
        Assert.Equal("Worker · HQ 응답 수신",
            WorkerHistoryCardPolicy.DataFlowTitle(
                "HQ 응답 수신", "RECEIVED", "WORKER ACTION", null));
        Assert.False(WorkerHistoryCardPolicy.ShouldShowDataFlow(
            "Worker 작업", "PROCESSING", "WORKER ACTION"));
    }
}
