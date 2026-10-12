using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerHistoryEventTests
{
    [Theory]
    [InlineData("Coordinator", null, "Unity HQ · 게임 설계")]
    [InlineData("Qa", null, "Unity QA · 플레이테스트")]
    [InlineData("HighLevel", null, "Unity HIGH · 통합")]
    [InlineData("Manager", null, "#1 ASSET MANAGER")]
    [InlineData("Resource", null, "Unity 에셋 (#0)")]
    [InlineData("Implementer", "0", "Unity 에셋 (#0)")]
    [InlineData("Implementer", "10", "Unity WORK (#10)")]
    [InlineData("Implementer", "27", "Unity WORK (#27)")]
    public void HistoryRole_UsesFiveRolePresentation(
        string stage,
        string? workItemId,
        string expectedRole)
    {
        var item = new MainWindow.WorkerHistoryEvent(
            DateTimeOffset.UtcNow,
            stage,
            "ROLE_RESPONSE",
            "결과",
            "완료",
            null,
            null,
            null,
            "RECEIVED",
            null)
        {
            WorkItemId = workItemId
        };

        Assert.Equal(expectedRole, item.Role);
    }

    [Fact]
    public void RunningHistory_UsesCompactPreviewWithoutChangingResultCard()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var running = new MainWindow.WorkerHistoryEvent(
            timestamp, "Implementer", "ROLE_PROGRESS",
            "작업 진행", "현재 진행 중", null, null, null,
            "RUNNING", "M4:27:invocation");
        var completed = running with
        {
            EventType = "ROLE_RESPONSE",
            Status = "RECEIVED"
        };

        Assert.Equal(90d, running.CardHeight);
        Assert.Equal(56d, running.SummaryHeight);
        Assert.Equal(56d, running.SummaryMaxHeight);
        Assert.True(double.IsNaN(completed.CardHeight));
        Assert.True(double.IsNaN(completed.SummaryHeight));
    }

    [Fact]
    public void WorkHistory_FallsBackToExecutionNumberWhenIdIsMissing()
    {
        var item = new MainWindow.WorkerHistoryEvent(
            DateTimeOffset.UtcNow,
            "Implementer",
            "ROLE_RESPONSE",
            "직통 작업",
            "완료",
            null,
            null,
            null,
            "RECEIVED",
            null)
        {
            WorkNumber = 3
        };

        Assert.Equal("Unity WORK (#3)", item.Role);
    }
}
