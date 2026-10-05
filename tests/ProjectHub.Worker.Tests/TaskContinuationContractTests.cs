using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class TaskContinuationContractTests
{
    [Fact]
    public void PausedRemainsResumable()
    {
        Assert.True(TaskContinuationContract.IsResumableStatus("PAUSED"));
        Assert.True(TaskContinuationContract.CanAcceptFollowupStatus("PAUSED"));
        Assert.False(TaskContinuationContract.IsFreshStartStatus("PAUSED"));
    }

    [Fact]
    public void CanceledIsTerminalAndDoesNotAcceptFollowup()
    {
        Assert.False(TaskContinuationContract.IsResumableStatus("CANCELED"));
        Assert.False(TaskContinuationContract.CanAcceptFollowupStatus("CANCELED"));
        Assert.False(TaskContinuationContract.IsFreshStartStatus("CANCELED"));
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
    public void ContinuationCarriesHighRoleSettings()
    {
        var state = new CoordinatorContinuationState(
            "job-high",
            "C:/work",
            new WorkerAiRoleSettings(Model: "gpt-6-sol", Reasoning: "medium"),
            new WorkerAiRoleSettings(Model: "gpt-6-luna", Reasoning: "medium"),
            null,
            null,
            "PAUSED",
            "사용자 입력 대기",
            new WorkerAiRoleSettings(Model: "gpt-6-astra", Reasoning: "high"));

        Assert.Equal("gpt-6-astra", state.HighLevel?.Model);
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
