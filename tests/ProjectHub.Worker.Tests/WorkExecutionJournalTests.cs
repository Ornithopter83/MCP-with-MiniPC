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

    private static void WithDirectory(Action<string> callback)
    {
        var root = Path.Combine(Path.GetTempPath(), "projecthub-work-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { callback(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
