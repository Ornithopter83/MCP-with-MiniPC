using System.Collections.ObjectModel;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerHistoryCardPolicyTests
{
    private static MainWindow.WorkerHistoryEvent Card(
        DateTimeOffset timestamp,
        string stage,
        string type,
        string title,
        string message,
        string? referenceId = null,
        string? workId = null,
        string? status = "RECEIVED") =>
        new(timestamp, stage, type, title, message, null, null, null, status, referenceId)
        {
            FullMessage = message,
            WorkItemId = workId
        };

    [Theory]
    [InlineData("전달 데이터", "SENT", "WORKER → WORK CLI", true)]
    [InlineData("재요청 데이터", "SENT", "WORKER → HQ WEB", true)]
    [InlineData("Worker 분배", "DISPATCHED", "WORKER RESOURCE QUEUE", true)]
    [InlineData("Worker 분배", "DISPATCHED", "WORKER DISPATCH", false)]
    [InlineData("Git 최종화", "COMPLETED", "WORKER ACTION", true)]
    [InlineData("Git 최종화", "FAILED", "WORKER ACTION", true)]
    [InlineData("Worker 작업", "RECOVERED", "WORKER ACTION", true)]
    [InlineData("Worker 작업", "BLOCKED", "WORKER ACTION", true)]
    [InlineData("Worker 작업", "FORWARDED", "WORKER ACTION", true)]
    [InlineData("Worker 작업", "RECOVERY", "WORKER ACTION", false)]
    [InlineData("Worker 작업", "EXECUTING", "WORKER ACTION", false)]
    [InlineData("Worker 작업", "PROCESSING", "WORKER ACTION", false)]
    [InlineData("Worker 작업", "PARSED", "WORKER ACTION", false)]
    [InlineData("Worker 작업", "COMPLETED", "WORKER ACTION", false)]
    public void WorkerCards_FilterDetailsButPreserveDispatchAndRecovery(
        string title, string status, string source, bool expected)
    {
        Assert.Equal(expected,
            WorkerHistoryCardPolicy.ShouldShowDataFlow(title, status, source));
    }

    [Fact]
    public void WorkerTransferCards_IdentifyRecipientAndResumedWork()
    {
        Assert.Equal("Worker → WORK #21 전달",
            WorkerHistoryCardPolicy.DataFlowTitle(
                "전달 데이터", "SENT", "WORKER → WORK CLI", "21"));
        Assert.Equal("Worker → HQ 재요청",
            WorkerHistoryCardPolicy.DataFlowTitle(
                "재요청 데이터", "SENT", "WORKER → HQ WEB", null));
        Assert.Equal("Worker → QA 전달",
            WorkerHistoryCardPolicy.DataFlowTitle(
                "전달 데이터", "SENT", "WORKER → QA CLI", "QA"));
    }

    [Fact]
    public void RepeatedWorkItem_UsesNewInvocationReferenceWithoutLosingSessionIdentity()
    {
        var first = WorkerHistoryCardPolicy.NewInvocationReference("M2", "21");
        var continued = WorkerHistoryCardPolicy.NewInvocationReference("M2", "21");
        Assert.StartsWith("M2:21:", first);
        Assert.StartsWith("M2:21:", continued);
        Assert.NotEqual(first, continued);
    }

    [Fact]
    public void PublishedWorkReturns_HaveOneCardPerInvocation()
    {
        var t = DateTimeOffset.UtcNow;
        var cards = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        WorkerHistoryCardPolicy.Publish(cards,
            Card(t, "Implementer", "ROLE_RESPONSE", "작업 응답",
                "첫 번째 in_progress", "M2:21:first", "21"));
        WorkerHistoryCardPolicy.Publish(cards,
            Card(t.AddMinutes(3), "Coordinator", "ROLE_RESPONSE",
                "마일스톤 설계", "CONTINUE"));
        WorkerHistoryCardPolicy.Publish(cards,
            Card(t.AddMinutes(5), "Implementer", "ROLE_RESPONSE", "작업 응답",
                "두 번째 in_progress", "M2:21:second", "21"));
        Assert.Equal(3, cards.Count);
        Assert.Equal("첫 번째 in_progress", cards[0].FullMessage);
        Assert.Equal("두 번째 in_progress", cards[2].FullMessage);
    }

    [Fact]
    public void RefreshedInvocation_StaysSingleAndMovesToLatestHistoryPosition()
    {
        var t = DateTimeOffset.UtcNow;
        var cards = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        WorkerHistoryCardPolicy.Publish(cards, Card(t, "Worker", "DATA_FLOW",
            "Worker → WORK #21 전달", "첫 요청", "M2:21:current", "21", "SENT"));
        WorkerHistoryCardPolicy.Publish(cards, Card(t.AddMinutes(1), "Coordinator",
            "ROLE_RESPONSE", "마일스톤 설계", "HQ 응답"));
        WorkerHistoryCardPolicy.Publish(cards, Card(t.AddMinutes(2), "Worker",
            "DATA_FLOW", "Worker → WORK #21 전달", "동일 실행 내 연속 요청",
            "M2:21:current", "21", "SENT"));
        Assert.Equal(2, cards.Count);
        Assert.Equal("Coordinator", cards[0].StageKey);
        Assert.Equal("동일 실행 내 연속 요청", cards[1].FullMessage);
        Assert.Equal("Worker", cards[1].Role);
    }

    [Fact]
    public void DuplicateHqResponse_IsCollapsedOnlyWhenActuallyIdenticalAndNearSimultaneous()
    {
        var t = DateTimeOffset.UtcNow;
        var cards = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        WorkerHistoryCardPolicy.Publish(cards, Card(t, "Coordinator",
            "ROLE_RESPONSE", "응답 데이터 · 마일스톤 설계", "[ACTION=WORK] M2"));
        WorkerHistoryCardPolicy.Publish(cards, Card(t.AddMilliseconds(250),
            "Coordinator", "ROLE_RESPONSE", "응답 데이터 · 마일스톤 설계",
            "[ACTION=WORK] M2"));
        Assert.Single(cards);
        WorkerHistoryCardPolicy.Publish(cards, Card(t.AddSeconds(3),
            "Coordinator", "ROLE_RESPONSE", "응답 데이터 · 마일스톤 설계",
            "[ACTION=WORK] M2"));
        Assert.Equal(2, cards.Count);
        WorkerHistoryCardPolicy.Publish(cards, Card(t.AddSeconds(3.2),
            "Coordinator", "ROLE_RESPONSE", "응답 데이터 · 마일스톤 설계",
            "[ACTION=WORK] M3"));
        Assert.Equal(3, cards.Count);
    }

    [Fact]
    public void SameMilestone_QaAndHighCardsPreserveLaterExecutionRound()
    {
        var t = DateTimeOffset.UtcNow;
        var cards = new ObservableCollection<MainWindow.WorkerHistoryEvent>();
        WorkerHistoryCardPolicy.Publish(cards,
            Card(t, "Qa", "ROLE_RESPONSE", "QA 조사 결과", "issue",
                "M2:QA:round1", "QA"));
        WorkerHistoryCardPolicy.Publish(cards,
            Card(t.AddMinutes(5), "Qa", "ROLE_RESPONSE", "QA 조사 결과",
                "passed", "M2:QA:round2", "QA"));
        WorkerHistoryCardPolicy.Publish(cards,
            Card(t.AddMinutes(7), "HighLevel", "ROLE_RESPONSE", "검토 결과",
                "reviewed", "M2:HIGH:round2", "HIGH"));
        Assert.Equal(3, cards.Count);
        Assert.Equal("issue", cards[0].FullMessage);
        Assert.Equal("passed", cards[1].FullMessage);
    }
}
