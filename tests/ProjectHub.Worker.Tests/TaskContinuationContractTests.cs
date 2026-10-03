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
    public void ContinuationCarriesRemainingHighOneShotPermission()
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
            new WorkerAiRoleSettings(Model: "gpt-6-astra", Reasoning: "high"),
            HighLevelPermitAvailable: true);

        Assert.True(state.HighLevelPermitAvailable);
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
