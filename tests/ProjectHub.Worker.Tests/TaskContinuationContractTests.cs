using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class TaskContinuationContractTests
{
    [Theory]
    [InlineData("PAUSED")]
    [InlineData("CANCELED")]
    public void PausedAndCanceledRemainResumable(string status)
    {
        Assert.True(TaskContinuationContract.IsResumableStatus(status));
        Assert.True(TaskContinuationContract.CanAcceptFollowupStatus(status));
        Assert.False(TaskContinuationContract.IsFreshStartStatus(status));
    }

    [Theory]
    [InlineData("DONE")]
    [InlineData("DONE_WITH_ERROR")]
    public void CompletedStatesAcceptFollowupAsFreshJob(string status)
    {
        Assert.False(TaskContinuationContract.IsResumableStatus(status));
        Assert.True(TaskContinuationContract.IsFreshStartStatus(status));
        Assert.True(TaskContinuationContract.CanAcceptFollowupStatus(status));
    }

    [Fact]
    public void HqFollowupInputRejectsCompletedState()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            TaskContinuationContract.BuildHqFollowupInput(
                "DONE",
                "previous",
                "continue"));

        Assert.Equal("FOLLOWUP_STATUS_NOT_RESUMABLE", error.Message);
    }
}
