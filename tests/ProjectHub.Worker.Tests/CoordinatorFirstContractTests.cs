using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CoordinatorFirstContractTests
{
    [Fact]
    public void HqActionParser_ParsesMilestoneAndWorkIndependently()
    {
        const string message = """
            [ACTION=MILESTONE]
            MILESTONE_ID: M1
            TARGET_BRANCH: AUTO
            QA: YES
            ENTRYPOINT: bin/App.exe
            BODY_BEGIN
            첫 마일스톤
            BODY_END
            [END_ACTION]

            [ACTION=WORK]
            WORK_ITEM_ID: 10
            WRITE_PATH: src/A
            WRITE_PATH: src/B.cs
            BODY_BEGIN
            구현한다.
            BODY_END
            [END_ACTION]
            """;

        var parsed = ActionBlockContract.ParseHq(message);

        Assert.False(parsed.HasErrors);
        Assert.Equal(2, parsed.ValidActions.Count);
        Assert.Equal("MILESTONE", parsed.ValidActions[0].Name);
        Assert.Equal("YES", parsed.ValidActions[0].GetSingle("QA"));
        Assert.Equal(new[] { "src/A", "src/B.cs" }, parsed.ValidActions[1].GetMany("WRITE_PATH"));
    }

    [Fact]
    public void HqActionParser_KeepsLaterValidBlockWhenEarlierBlockIsMalformed()
    {
        const string message = """
            [ACTION=WORK]
            WORK_ITEM_ID: 10
            WRITE_PATH: src/Broken.cs
            BODY_BEGIN
            END_ACTION이 없다.

            [ACTION=MILESTONE]
            MILESTONE_ID: M2
            TARGET_BRANCH: AUTO
            QA: NO
            BODY_BEGIN
            정상 블록
            BODY_END
            [END_ACTION]
            """;

        var parsed = ActionBlockContract.ParseHq(message);

        Assert.Contains(parsed.Actions, action =>
            action.Name == "WORK" &&
            action.Errors.Contains("ACTION_END_MISSING"));
        var milestone = Assert.Single(parsed.ValidActions);
        Assert.Equal("MILESTONE", milestone.Name);
        Assert.Equal("M2", milestone.GetSingle("MILESTONE_ID"));
    }

    [Fact]
    public void InvalidWorkBlock_DoesNotDiscardValidMilestone()
    {
        const string message = """
            [ACTION=MILESTONE]
            MILESTONE_ID: M3
            TARGET_BRANCH: main
            QA: NO
            BODY_BEGIN
            목표
            BODY_END
            [END_ACTION]

            [ACTION=WORK]
            WORK_ITEM_ID: 9
            WRITE_PATH: src/Bad.cs
            BODY_BEGIN
            예약 번호를 잘못 사용했다.
            BODY_END
            [END_ACTION]
            """;

        var parsed = ActionBlockContract.ParseHq(message);

        Assert.Single(parsed.ValidActions);
        Assert.Equal("MILESTONE", parsed.ValidActions[0].Name);
        Assert.Contains(parsed.Actions, action =>
            action.Name == "WORK" &&
            action.Errors.Contains("WORK_ITEM_ID_INVALID"));
    }

    [Fact]
    public void MilestoneDefinition_RejectsPathEscapingProjectRoot()
    {
        const string message = """
            [ACTION=MILESTONE]
            MILESTONE_ID: M4
            TARGET_BRANCH: AUTO
            QA: NO
            BODY_BEGIN
            경로 검증
            BODY_END
            [END_ACTION]

            [ACTION=WORK]
            WORK_ITEM_ID: 10
            WRITE_PATH: ../outside.txt
            BODY_BEGIN
            잘못된 경로
            BODY_END
            [END_ACTION]
            """;

        var parsed = ActionBlockContract.ParseHq(message);

        Assert.False(MilestoneDefinitionContract.TryBuild(
            message,
            parsed,
            out _,
            out var error));
        Assert.Contains("WRITE_PATH_OUTSIDE_PROJECT_ROOT", error);
    }

    [Fact]
    public void ManagerActionParser_AcceptsNewMilestoneActions()
    {
        const string message = """
            [ACTION=RUN_WORK]
            WORK_ITEM_ID: 10
            [END_ACTION]

            [ACTION=MECHANICAL]
            OPERATION: PUBLISH
            BODY_BEGIN
            COMMAND: dotnet publish App.csproj -o bin
            BODY_END
            [END_ACTION]

            [ACTION=READY_FOR_VALIDATION]
            [END_ACTION]

            [ACTION=GIT_FINALIZE]
            [END_ACTION]
            """;

        var parsed = ActionBlockContract.ParseManager(message);

        Assert.False(parsed.HasErrors);
        Assert.Equal(
            new[] { "RUN_WORK", "MECHANICAL", "READY_FOR_VALIDATION", "GIT_FINALIZE" },
            parsed.ValidActions.Select(action => action.Name));
    }

    [Fact]
    public void WorkReportNormalizer_RequiresManagerGotoAndOneTerminalStatus()
    {
        var valid = MilestoneDefinitionContract.NormalizeWorkReport(
            0,
            "[GOTO : MANAGER]\nWORK_ITEM_STATUS: COMPLETED\n완료",
            null);
        Assert.Contains("WORK_ITEM_STATUS: COMPLETED", valid);

        var invalid = MilestoneDefinitionContract.NormalizeWorkReport(
            0,
            "WORK_ITEM_STATUS: COMPLETED\n완료",
            null);
        Assert.Contains("WORK_ITEM_STATUS: BLOCKED", invalid);
        Assert.Contains("WORK_REPORT_CONTRACT_INVALID", invalid);
    }

    [Fact]
    public void QaAndHighReports_AreNormalizedToTheirRoleContracts()
    {
        var qa = MilestoneDefinitionContract.NormalizeQaReport(
            0,
            "QA_STATUS: COMPLETED\n동작 확인",
            null);
        Assert.Contains("QA_STATUS: COMPLETED", qa);

        var high = MilestoneDefinitionContract.NormalizeHighReport(
            0,
            "[GOTO : MANAGER]\nHIGH_STATUS: MODIFIED\nCHANGED_PATH: src/Fix.cs\n보완 완료",
            null);
        Assert.Contains("HIGH_STATUS: MODIFIED", high);
        Assert.Equal(
            new[] { "src/Fix.cs" },
            MilestoneDefinitionContract.ExtractHighChangedPaths(high));
    }

    [Fact]
    public void HighModifiedReport_WithoutChangedPath_IsRejected()
    {
        var high = MilestoneDefinitionContract.NormalizeHighReport(
            0,
            "[GOTO : MANAGER]\nHIGH_STATUS: MODIFIED\n보완 완료",
            null);

        Assert.Contains("HIGH_STATUS: INCOMPLETE", high);
        Assert.Contains("HIGH_REPORT_CONTRACT_INVALID", high);
        Assert.Empty(
            MilestoneDefinitionContract.ExtractHighChangedPaths(high));
    }

    [Fact]
    public void RoleSettings_KeepManagerAndQaIndependent()
    {
        var settings = new WorkerTargetSettings(
            null,
            null,
            null,
            null,
            Manager: new WorkerAiRoleSettings(
                Provider: "openai",
                Model: "manager-model",
                Reasoning: "medium"),
            Qa: new WorkerAiRoleSettings(
                Provider: "openai",
                Model: "qa-model",
                Reasoning: "high"));

        Assert.Equal("manager-model", settings.EffectiveManager.Model);
        Assert.Equal("qa-model", settings.EffectiveQa.Model);
        Assert.NotEqual(settings.EffectiveManager.Model, settings.EffectiveQa.Model);
    }

    [Fact]
    public void RoleContracts_DescribeFiveRoleMilestoneFlow()
    {
        var hq = RoleContractLoader.LoadHqFooter();
        var manager = RoleContractLoader.LoadManagerFooter();
        var work = RoleContractLoader.LoadWorkFooter();
        var qa = RoleContractLoader.LoadQaFooter();
        var high = RoleContractLoader.LoadHighFooter();

        Assert.Contains("[ACTION=MILESTONE]", hq);
        Assert.Contains("QA: YES 또는 NO", hq);
        Assert.Contains("[ACTION=READY_FOR_VALIDATION]", manager);
        Assert.Contains("[ACTION=GIT_FINALIZE]", manager);
        Assert.Contains("COMMAND:", manager);
        Assert.Contains("[GOTO : MANAGER]", work);
        Assert.Contains("코드나 프로젝트 파일을 수정하지 않는다", qa);
        Assert.Contains("[GOTO : MANAGER]", high);
        Assert.Contains("CHANGED_PATH:", high);
        Assert.Contains("managed process", manager, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("WorkGraph", hq);
        Assert.DoesNotContain("projecthub/*", manager);
    }

    [Fact]
    public void WorkScopes_DetectOverlapAndKeepDisjointPathsParallelSafe()
    {
        Assert.True(MilestoneMechanicalExecutor.HasOverlappingScopes(
            new IReadOnlyList<string>[]
            {
                new[] { "src/Feature" },
                new[] { "src/Feature/View.cs" }
            }));

        Assert.False(MilestoneMechanicalExecutor.HasOverlappingScopes(
            new IReadOnlyList<string>[]
            {
                new[] { "src/FeatureA" },
                new[] { "src/FeatureB" }
            }));
    }

    [Fact]
    public void WorkScopeFilter_MatchesOnlyDeclaredPathTree()
    {
        var scopes = new[] { "src/Feature", "README.md" };

        Assert.True(MilestoneMechanicalExecutor.IsPathWithinScopes(
            "src/Feature/View.cs",
            scopes));
        Assert.True(MilestoneMechanicalExecutor.IsPathWithinScopes(
            "README.md",
            scopes));
        Assert.False(MilestoneMechanicalExecutor.IsPathWithinScopes(
            "src/Other/File.cs",
            scopes));
    }

    [Fact]
    public void ChangeStateDiff_FindsNewRemovedAndModifiedDirtyPaths()
    {
        IReadOnlyDictionary<string, string> before =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/modified.cs"] = "FILE:10:1",
                ["src/removed.cs"] = "FILE:20:1",
                ["src/same.cs"] = "FILE:30:1"
            };
        IReadOnlyDictionary<string, string> after =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/modified.cs"] = "FILE:11:2",
                ["src/new.cs"] = "FILE:5:2",
                ["src/same.cs"] = "FILE:30:1"
            };

        var changed = MilestoneMechanicalExecutor.DiffChangeStates(
            before,
            after);

        Assert.Equal(
            new[]
            {
                "src/modified.cs",
                "src/new.cs",
                "src/removed.cs"
            },
            changed);
    }

    [Fact]
    public void PipelineRoleVisuals_ColorOnlyTheCurrentRoleAfterLaunch()
    {
        Assert.True(PipelineCardVisualPolicy.Resolve(
            initialInputIdle: true,
            isCurrent: false,
            disabled: false).IsColored);

        Assert.True(PipelineCardVisualPolicy.Resolve(
            initialInputIdle: false,
            isCurrent: true,
            disabled: false).IsColored);

        Assert.False(PipelineCardVisualPolicy.Resolve(
            initialInputIdle: false,
            isCurrent: false,
            disabled: false).IsColored);
    }
}
