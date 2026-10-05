using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerHistoryEventTests
{
    [Theory]
    [InlineData("Coordinator", null, "설계 관제")]
    [InlineData("Qa", null, "QA")]
    [InlineData("HighLevel", null, "검토")]
    [InlineData("Manager", null, "통합")]
    [InlineData("Resource", null, "작업 (#0, 리소스)")]
    [InlineData("Implementer", "0", "작업 (#0, 리소스)")]
    [InlineData("Implementer", "10", "작업 (#10, 일반 작업)")]
    [InlineData("Implementer", "27", "작업 (#27, 일반 작업)")]
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

        Assert.Equal("작업 (#3)", item.Role);
    }
}
