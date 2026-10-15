using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ProjectHandoffJournalTests
{
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "unityworker-handoff-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void HqPlanMilestoneAndUserUpdatesSurviveEndAndNewTask()
    {
        var root = NewRoot();
        try
        {
            Assert.Null(ProjectHandoffJournal.ReadForNewTask(root));
            ProjectHandoffJournal.RecordUserInstruction(
                root, "job-one", "NEW-TASK", "퀴즈 게임 UI를 개선하라");
            ProjectHandoffJournal.RecordHqPlan(
                root, "job-one", "M30",
                "HQ가 설계한 화면 위계와 완료 기준",
                "[GOTO=WORKER]\n[ACTION=WORK]\n@@MILESTONE\n<ID>M30</>");
            ProjectHandoffJournal.RecordMilestoneReport(
                root, "job-one", "M30", "검증: 컴파일 PASS, Game View 미검증");
            ProjectHandoffJournal.RecordUserInstruction(
                root, "job-one", "USER-2", "사용자 지시: 버튼 배색 개선");
            ProjectHandoffJournal.RecordHqControl(
                root, "job-one", "DONE", "[ACTION=END]");

            var handoff = ProjectHandoffJournal.ReadForNewTask(root);
            Assert.NotNull(handoff);
            Assert.Contains("HQ가 설계한 화면 위계와 완료 기준", handoff);
            Assert.Contains("컴파일 PASS, Game View 미검증", handoff);
            Assert.Contains("버튼 배색 개선", handoff);
            Assert.Contains("상태: DONE", handoff);
            Assert.DoesNotContain("HQ 세션:", handoff);

            ProjectHandoffJournal.RecordUserInstruction(
                root, "job-two", "NEW-TASK", "이어서 화면 연출 개선");
            var next = ProjectHandoffJournal.ReadForNewTask(root);
            Assert.NotNull(next);
            Assert.Contains("HQ가 설계한 화면 위계와 완료 기준", next);
            Assert.Contains("이어서 화면 연출 개선", next);
            Assert.DoesNotContain("USER-2", next);
            Assert.DoesNotContain("컴파일 PASS, Game View 미검증", next);
            ProjectHandoffJournal.RecordHqPlan(
                root, "job-two", "M31", "새 HQ 설계 갱신", "M31 설계");
            Assert.Contains("새 HQ 설계 갱신",
                ProjectHandoffJournal.ReadForNewTask(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ResumeSnapshotDoesNotOverwriteProjectHandoff()
    {
        var root = NewRoot();
        try
        {
            ProjectHandoffJournal.RecordHqPlan(
                root, "job", "M1", "원본 HQ 장기 계획", "M1");
            var snapshot = new CoordinatorContinuationState(
                "job", root,
                new WorkerAiRoleSettings(Model: "gpt-6-sol"),
                new WorkerAiRoleSettings(Model: "gpt-6-luna"),
                "old-session", null, "INTERRUPTED", "일시 중단");
            Assert.True(ProjectWorkspacePersistence.SaveContinuation(snapshot));
            Assert.Contains("원본 HQ 장기 계획",
                File.ReadAllText(ProjectWorkspacePersistence.HandoffPath(root)));
            Assert.DoesNotContain("HQ 세션:",
                File.ReadAllText(ProjectWorkspacePersistence.HandoffPath(root)));
            ProjectWorkspacePersistence.ClearContinuation(root);
            Assert.True(File.Exists(ProjectWorkspacePersistence.HandoffPath(root)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DuplicateMilestoneReportReplacesInsteadOfAppending()
    {
        var root = NewRoot();
        try
        {
            ProjectHandoffJournal.RecordMilestoneReport(root, "job", "M1", "첫 보고");
            ProjectHandoffJournal.RecordMilestoneReport(root, "job", "M1", "정정 보고");
            var handoff = ProjectHandoffJournal.ReadForNewTask(root)!;
            Assert.Contains("정정 보고", handoff);
            Assert.DoesNotContain("첫 보고", handoff);
        }
        finally { Directory.Delete(root, true); }
    }
}
