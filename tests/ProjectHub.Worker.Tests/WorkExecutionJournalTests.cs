using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkExecutionJournalTests
{
    private static readonly WorkerAiRoleSettings Luna =
        new(Model: "gpt-6-luna", Reasoning: "high");

    private static MilestoneWorkDefinition Work(string id = "10", string mode = "NEW",
        params string[] paths)
        => new(id, paths.Length == 0 ? new[] { "src/actor.cs" } : paths,
            false, true, "{}", "{}") { ExecutionMode = mode };

    [Fact]
    public void NewWork_PersistsAndContinuesAcrossMilestonesAndRestart()
    {
        WithDirectory(root =>
        {
            Assert.True(WorkExecutionJournal.TryBegin(root, "job01", "M1",
                Work(), Luna, out var original, out var error), error);
            Assert.Null(original!.SessionId);
            Assert.True(WorkExecutionJournal.Record(root, "job01", "10",
                "RUNNING", "session-abc", invocationStarted: true));
            Assert.True(WorkExecutionJournal.Record(root, "job01", "10",
                "IN_PROGRESS", "session-abc", "partial implementation"));
            Assert.True(WorkExecutionJournal.TryBegin(root, "job01", "M2",
                Work(mode: "CONTINUE"), Luna, out var resumed, out error), error);
            Assert.Equal("session-abc", resumed!.SessionId);
            Assert.Equal("M1", resumed.OriginMilestoneId);
            Assert.Equal(1, resumed.Attempts);
            Assert.True(WorkExecutionJournal.Record(root, "job01", "10",
                "COMPLETED", resumed.SessionId, "tests passed"));
            Assert.Equal("COMPLETED", WorkExecutionJournal.Read(root, "job01", "10")!.State);
            Assert.False(WorkExecutionJournal.TryBegin(root, "job01", "M3",
                Work(mode: "CONTINUE"), Luna, out _, out error));
            Assert.Equal("WORK_CONTINUATION_TERMINAL", error);
        });
    }

    [Fact]
    public void NewId_NeedNotIncrease_ButReuseIsForbidden()
    {
        WithDirectory(root =>
        {
            Assert.True(WorkExecutionJournal.TryBegin(root, "job02", "M1",
                Work("54"), Luna, out _, out _));
            Assert.True(WorkExecutionJournal.TryBegin(root, "job02", "M2",
                Work("10"), Luna, out _, out _));
            Assert.False(WorkExecutionJournal.TryBegin(root, "job02", "M3",
                Work("54"), Luna, out _, out var error));
            Assert.Equal("WORK_ID_ALREADY_USED", error);
            Assert.Equal(new[] { "10", "54" },
                WorkExecutionJournal.ReadAll(root, "job02").Select(item => item.WorkItemId));
        });
    }

    [Fact]
    public void Continue_RequiresExistingUnfinishedScopeAndModel()
    {
        WithDirectory(root =>
        {
            Assert.False(WorkExecutionJournal.TryBegin(root, "job03", "M1",
                Work(mode: "CONTINUE"), Luna, out _, out var error));
            Assert.Equal("WORK_CONTINUATION_NOT_FOUND", error);
            Assert.True(WorkExecutionJournal.TryBegin(root, "job03", "M1",
                Work(), Luna, out _, out _));
            Assert.False(WorkExecutionJournal.TryBegin(root, "job03", "M2",
                Work(mode: "CONTINUE", paths: new[] { "src/other.cs" }),
                Luna, out _, out error));
            Assert.Equal("WORK_CONTINUATION_SCOPE_OR_MODEL_CHANGED", error);
            Assert.False(WorkExecutionJournal.TryBegin(root, "job03", "M2",
                Work(mode: "CONTINUE"),
                Luna with { Model = "other-model" }, out _, out error));
            Assert.Equal("WORK_CONTINUATION_SCOPE_OR_MODEL_CHANGED", error);
            Assert.True(WorkExecutionJournal.Record(root, "job03", "10",
                "CANCELED", null));
            Assert.False(WorkExecutionJournal.TryBegin(root, "job03", "M2",
                Work(mode: "CONTINUE"), Luna, out _, out error));
            Assert.Equal("WORK_CONTINUATION_TERMINAL", error);
        });
    }

    [Fact]
    public void IndependentJobsCanUseSameNumericId()
    {
        WithDirectory(root =>
        {
            Assert.True(WorkExecutionJournal.TryBegin(root, "a", "M1",
                Work(), Luna, out _, out _));
            Assert.True(WorkExecutionJournal.TryBegin(root, "b", "M1",
                Work(), Luna, out _, out _));
            Assert.True(WorkExecutionJournal.Record(root, "a", "10", "IN_PROGRESS", "a-session"));
            Assert.Null(WorkExecutionJournal.Read(root, "b", "10")!.SessionId);
        });
    }

    [Fact]
    public void OneReResolutionClaimSurvivesRestartAndContinuation()
    {
        WithDirectory(root =>
        {
            Assert.True(WorkExecutionJournal.TryBegin(root, "reviewjob", "M1",
                Work("628"), Luna, out _, out _));
            Assert.True(WorkExecutionJournal.Record(root, "reviewjob", "628",
                "RUNNING", "session-review", invocationStarted: true));

            Assert.True(WorkExecutionJournal.TryClaimReResolution(root,
                "reviewjob", "628", "session-review", "first report"));
            var checkpoint = WorkExecutionJournal.Read(root, "reviewjob", "628")!;
            Assert.Equal(1, checkpoint.ReResolutionAttempts);
            Assert.Equal("IN_PROGRESS", checkpoint.State);
            Assert.Equal("session-review", checkpoint.SessionId);
            Assert.Equal("first report", checkpoint.LastReport);
            Assert.False(WorkExecutionJournal.TryClaimReResolution(root,
                "reviewjob", "628", "session-review", "second report"));

            Assert.True(WorkExecutionJournal.TryBegin(root, "reviewjob", "M2",
                Work("628", mode: "CONTINUE"), Luna, out var resumed, out _));
            Assert.Equal(1, resumed!.ReResolutionAttempts);
            Assert.False(WorkExecutionJournal.TryClaimReResolution(root,
                "reviewjob", "628", "session-review", "third report"));

            Assert.True(WorkExecutionJournal.Record(root, "reviewjob", "628",
                "COMPLETED", resumed.SessionId, "latest final report"));
            Assert.False(WorkExecutionJournal.TryClaimReResolution(root,
                "reviewjob", "628", "session-review", "fourth report"));
            Assert.Equal("latest final report",
                WorkExecutionJournal.Read(root, "reviewjob", "628")!.LastReport);
        });
    }

    [Fact]
    public void ReResolutionCannotStartWithoutSessionOrCheckpoint()
    {
        WithDirectory(root =>
        {
            Assert.False(WorkExecutionJournal.TryClaimReResolution(
                root, "unknown", "628", "session", "report"));
            Assert.True(WorkExecutionJournal.TryBegin(root, "reviewjob2",
                "M1", Work("628"), Luna, out _, out _));
            Assert.False(WorkExecutionJournal.TryClaimReResolution(
                root, "reviewjob2", "628", null, "report"));
            Assert.Equal(0, WorkExecutionJournal.Read(
                root, "reviewjob2", "628")!.ReResolutionAttempts);
        });
    }

    private static void WithDirectory(Action<string> callback)
    {
        var root = Path.Combine(Path.GetTempPath(), "projecthub-work-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { callback(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
