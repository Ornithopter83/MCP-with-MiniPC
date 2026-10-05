using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerHistoryEventTests
{
    [Theory]
    [InlineData("10", "작업 (#10, 일반 작업)")]
    [InlineData("27", "작업 (#27, 일반 작업)")]
    [InlineData("W17", "작업 (#W17)")]
    [InlineData("0", "작업 (#0, 리소스)")]
    [InlineData("1", "작업 (#1, 이미지 가공)")]
    [InlineData("2", "작업 (#2, 예약 번호)")]
    [InlineData("7", "작업 (#7, 예약 번호)")]
    [InlineData("8", "작업 (#8, 파일 매니저)")]
    [InlineData("9", "작업 (#9, 빌드 매니저)")]
    public void WorkHistoryCard_UsesActualWorkItemIdInsteadOfExecutionNumber(
        string workItemId,
        string expectedRole)
    {
        var item = new MainWindow.WorkerHistoryEvent(
            DateTimeOffset.UtcNow,
            "Implementer",
            "ROLE_RESPONSE",
            "작업 응답",
            "완료",
            null,
            null,
            null,
            "RECEIVED",
            workItemId)
        {
            WorkNumber = 1,
            WorkItemId = workItemId
        };

        Assert.Equal(expectedRole, item.Role);
    }

    [Fact]
    public void WorkHistoryCard_FallsBackToExecutionNumberWhenNoWorkItemIdExists()
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
