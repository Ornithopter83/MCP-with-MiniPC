using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class TaskRecoveryPersistenceTests
{
    [Fact]
    public void InterruptedSnapshot_SurvivesProcessRestartAndPreservesOriginalJob()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProjectHubRecoveryTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var snapshot = new CoordinatorContinuationState(
                "persistent-job",
                root,
                new WorkerAiRoleSettings(Model: "gpt-6-sol"),
                new WorkerAiRoleSettings(Model: "gpt-6-luna"),
                null, null, "INTERRUPTED",
                "MILESTONE_REPORT\nWORK_IN_PROGRESS: 10",
                new WorkerAiRoleSettings(Model: "gpt-6-luna"));
            Assert.True(TaskContinuationContract.IsResumableStatus("INTERRUPTED"));
            Assert.True(ProjectWorkspacePersistence.SaveContinuation(snapshot));

            var recovered = ProjectWorkspacePersistence.TryLoad(root);
            Assert.NotNull(recovered);
            Assert.Equal("persistent-job", recovered!.JobId);
            Assert.Equal("INTERRUPTED", recovered.Status);
            Assert.Contains("WORK_IN_PROGRESS: 10", recovered.LastHqMessage);
            Assert.Equal("gpt-6-luna", recovered.Implementer.Model);

            ProjectWorkspacePersistence.ClearContinuation(root);
            Assert.Null(ProjectWorkspacePersistence.TryLoad(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CanceledJobCannotBeRecovered()
    {
        Assert.False(TaskContinuationContract.IsResumableStatus("CANCELED"));
        Assert.False(TaskContinuationContract.CanAcceptFollowupStatus("CANCELED"));
        Assert.True(TaskContinuationContract.CanAcceptFollowupStatus("INTERRUPTED"));
    }
}
