using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class MilestoneProgressFactsTests
{
    [Fact]
    public void ReportsOnlyCheckpointStatesAndFinalizationCandidatePaths()
    {
        var facts = MilestoneProgressFacts.Format(
            "M9",
            TimeSpan.FromSeconds(12.9),
            new[] { "10", "11", "12", "10" },
            new Dictionary<string, string>
            {
                ["10"] = "COMPLETED",
                ["11"] = "IN_PROGRESS"
            },
            new[]
            {
                "Assets/Scenes/AIDemo.unity",
                "Assets/Scripts/Player.cs",
                "Assets/Scripts/Player.cs",
                "Assets/Tests/PlayMode/Gameplay.cs",
                "Assets/Scripts/Player.cs.meta"
            },
            gitSucceeded: false,
            qaReportedStatus: "issue",
            highReportedStatus: "completed");

        Assert.Contains("OBSERVED_DURATION_SECONDS: 12", facts);
        Assert.Contains("SCHEDULED_WORK_COUNT: 3", facts);
        Assert.Contains("WORK_CHECKPOINT_COMPLETED: 1", facts);
        Assert.Contains("WORK_CHECKPOINT_IN_PROGRESS: 1", facts);
        Assert.Contains("WORK_CHECKPOINT_NOT_RECORDED: 1", facts);
        Assert.Contains("GIT_FINALIZATION_SUCCEEDED: False", facts);
        Assert.Contains("GIT_CANDIDATE_PATH_COUNT: 4", facts);
        Assert.Contains("GIT_CANDIDATE_SCENE_COUNT: 1", facts);
        Assert.Contains("GIT_CANDIDATE_CS_COUNT: 2", facts);
        Assert.Contains("GIT_CANDIDATE_META_COUNT: 1", facts);
        Assert.Contains("MECHANICAL_VERIFICATION_OWNER: WORKER", facts);
        Assert.DoesNotContain("QA_REPORTED_STATUS", facts);
        Assert.Contains("HIGH_REPORTED_STATUS: completed", facts);
        Assert.Contains("TEST_EXECUTED_COUNT: NOT_MACHINE_RECORDED", facts);
        Assert.DoesNotContain("TEST_EXECUTED_COUNT: 0", facts);
    }

    [Fact]
    public void GoalProgressCandidatesSeparateGameChangesFromRepeatedDiagnosticArtifacts()
    {
        var summary = MilestoneProgressFacts.FormatGoalProgressCandidates(new[]
        {
            "docs/projecthub/m90-findings.md",
            "tools/projecthub/run-m90.ps1",
            "Assets/Tests/PlayMode/KeyboardInputTests.cs",
            "Assets/ProjectHub/VisualFoundry/M90/Menu.prefab",
            "Assets/ProjectHub/VisualFoundry/M90/Menu.prefab.meta",
            "Assets/Quiz/Scripts/QuizGameController.cs",
            "Assets/Quiz/Scripts/QuizGameController.cs",
            "Assets/Quiz/Scenes/QuizScene.unity.meta"
        });
        Assert.Contains("total=7", summary);
        Assert.Contains("product_or_config=1", summary);
        Assert.Contains("tests=1", summary);
        Assert.Contains("visual_kit_unverified=1", summary);
        Assert.Contains("docs_or_tools=2", summary);
        Assert.Contains("meta=2", summary);
        Assert.Contains("커밋 여부를 증명하지 않는다", summary);

        var diagnosticOnly = MilestoneProgressFacts.FormatGoalProgressCandidates(
            new[] { "docs/m91.md", "tools/m91.ps1", "Assets/Tests/Suite.cs" });
        Assert.Contains("product_or_config=0", diagnosticOnly);
        Assert.Contains("tests=1", diagnosticOnly);
    }

    [Fact]
    public void NoWorkOrGitChangesAreRecordedAsZeroNotInventedCompletion()
    {
        var facts = MilestoneProgressFacts.Format(
            "M10", TimeSpan.Zero, Array.Empty<string>(),
            new Dictionary<string, string>(), Array.Empty<string>(),
            false, null, null);

        Assert.Contains("SCHEDULED_WORK_COUNT: 0", facts);
        Assert.Contains("GIT_CANDIDATE_PATH_COUNT: 0", facts);
        Assert.Contains("MECHANICAL_VERIFICATION_OWNER: WORKER", facts);
        Assert.Contains("HIGH_REPORTED_STATUS: NOT_RECORDED", facts);
        Assert.Contains("TEST_EXECUTED_COUNT: NOT_MACHINE_RECORDED", facts);
    }
}
