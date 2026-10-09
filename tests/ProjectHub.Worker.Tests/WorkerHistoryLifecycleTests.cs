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
        Assert.Equal("작업 종료 보고", cards[0].FullMessage);
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
