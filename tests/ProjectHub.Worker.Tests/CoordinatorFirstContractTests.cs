using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CoordinatorFirstContractTests
{
    [Fact]
    public void HqActionParser_ParsesCompleteMilestoneJson()
    {
        const string message = """
            [ACTION=WORK]
            {
              "action": "work",
              "milestone": {
                "id": "M1",
                "branch": "AUTO",
                "goal": "첫 마일스톤",
                "entrypoint": "bin/App.exe",
                "qa": {
                  "required": true,
                  "instructions": "앱 실행과 핵심 동작을 확인한다."
                },
                "resource": {
                  "id": 0,
                  "type": "image",
                  "targetPath": "assets/hero.png",
                  "instructions": "대표 이미지를 생성한다.",
                  "style": "flat"
                },
                "workItems": [
                  {
                    "id": 10,
                    "writePaths": ["src/A", "src/B.cs"],
                    "goal": "기능을 구현한다.",
                    "instructions": "지정 경로 안에서 구현한다.",
                    "completionCriteria": ["구현이 완료된다."],
                    "constraints": ["추가 JSON 조건도 보존한다."]
                  }
                ],
                "completionCriteria": ["마일스톤 목표가 충족된다."],
                "validation": ["변경 결과를 검증한다."]
              }
            }
            """;

        var parsed = ActionBlockContract.ParseHq(message);

        Assert.False(parsed.HasErrors);
        var action = Assert.Single(parsed.ValidActions);
        Assert.Equal("WORK", action.Name);
        Assert.NotNull(action.JsonPayload);

        Assert.True(MilestoneDefinitionContract.TryBuild(
            message,
            parsed,
            out var milestone,
            out var error),
            error);
        Assert.NotNull(milestone);
        Assert.Equal("M1", milestone!.Id);
        Assert.Equal("AUTO", milestone.TargetBranch);
        Assert.True(milestone.QaReserved);
        Assert.Equal("bin/App.exe", milestone.Entrypoint);
        Assert.False(milestone.InitializeGitIfMissing);
        var work = Assert.Single(milestone.WorkItems.Values);
        Assert.Equal("10", work.Id);
        Assert.Equal(new[] { "src/A", "src/B.cs" }, work.WritePaths);
        Assert.Contains("\"constraints\"", work.Body);

        var resource = Assert.Single(milestone.Resources.Values);
        Assert.Equal("IMAGE", resource.Type);
        Assert.Equal("assets/hero.png", resource.TargetPath);
        Assert.Contains("\"style\": \"flat\"", resource.Body);
    }

    [Fact]
    public void MilestoneDefinition_ParsesWorkerGitInitializationFlag()
    {
        const string message = """
            [ACTION=WORK]
            {
              "action": "work",
              "milestone": {
                "id": "GIT_INIT_TEST",
                "branch": "AUTO",
                "goal": "새 저장소 준비",
                "entrypoint": null,
                "initializeGitIfMissing": true,
                "qa": {
                  "required": false,
                  "instructions": ""
                },
                "resource": null,
                "workItems": [],
                "completionCriteria": [],
                "validation": []
              }
            }
            """;

        var parsed = ActionBlockContract.ParseHq(message);

        Assert.True(MilestoneDefinitionContract.TryBuild(
            message,
            parsed,
            out var milestone,
            out var error),
            error);
        Assert.NotNull(milestone);
        Assert.True(milestone!.InitializeGitIfMissing);
    }

    [Fact]
    public void MilestoneDefinition_ReadOnlyPolicyMakesWorkReadOnly()
    {
        const string message = """
            [ACTION=WORK]
            {
              "action": "work",
              "milestone": {
                "id": "READ_ONLY_TEST",
                "branch": "AUTO",
                "goal": "역할 검증",
                "entrypoint": null,
                "projectPolicy": "READ_ONLY_NO_FILE_CHANGES",
                "qa": {
                  "required": true,
                  "instructions": "읽기 전용 검증"
                },
                "resource": null,
                "workItems": [
                  {
                    "id": 10,
                    "writePaths": ["."],
                    "goal": "읽기 전용 확인",
                    "instructions": "파일을 바꾸지 않는다.",
                    "completionCriteria": ["변경 없음"]
                  }
                ],
                "completionCriteria": ["역할 검증 완료"],
                "validation": ["변경 없음 확인"]
              }
            }
            """;

        var parsed = ActionBlockContract.ParseHq(message);

        Assert.True(MilestoneDefinitionContract.TryBuild(
            message,
            parsed,
            out var milestone,
            out var error),
            error);
        Assert.NotNull(milestone);
        Assert.True(milestone!.ReadOnlyNoFileChanges);
        var work = Assert.Single(milestone.WorkItems.Values);
        Assert.True(work.ReadOnly);
        Assert.Equal(new[] { "." }, work.WritePaths);
    }

    [Fact]
    public void HqActionParser_RejectsMalformedJsonEnvelope()
    {
        const string message = """
            [ACTION=WORK]
            {
              "action": "work",
              "milestone":
            """;

        var parsed = ActionBlockContract.ParseHq(message);

        Assert.True(parsed.HasErrors);
        var action = Assert.Single(parsed.Actions);
        Assert.Contains("JSON_INVALID", action.Errors);
        Assert.Empty(parsed.ValidActions);
    }

    [Fact]
    public void MilestoneDefinition_RejectsWorkItemBelowTen()
    {
        const string message = """
            [ACTION=WORK]
            {
              "action": "work",
              "milestone": {
                "id": "M3",
                "branch": "main",
                "goal": "목표",
                "entrypoint": null,
                "qa": {
                  "required": false,
                  "instructions": ""
                },
                "resource": null,
                "workItems": [
                  {
                    "id": 9,
                    "writePaths": ["src/Bad.cs"],
                    "goal": "예약 번호를 잘못 사용했다.",
                    "instructions": "",
                    "completionCriteria": []
                  }
                ],
                "completionCriteria": [],
                "validation": []
              }
            }
            """;

        var parsed = ActionBlockContract.ParseHq(message);

        Assert.False(MilestoneDefinitionContract.TryBuild(
            message,
            parsed,
            out _,
            out var error));
        Assert.Contains("WORK_ITEM_ID_INVALID", error);
    }

    [Fact]
    public void MilestoneDefinition_RejectsPathEscapingProjectRoot()
    {
        const string message = """
            [ACTION=WORK]
            {
              "action": "work",
              "milestone": {
                "id": "M4",
                "branch": "AUTO",
                "goal": "경로 검증",
                "entrypoint": null,
                "qa": {
                  "required": false,
                  "instructions": ""
                },
                "resource": null,
                "workItems": [
                  {
                    "id": 10,
                    "writePaths": ["../outside.txt"],
                    "goal": "잘못된 경로",
                    "instructions": "",
                    "completionCriteria": []
                  }
                ],
                "completionCriteria": [],
                "validation": []
              }
            }
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

        Assert.Contains("[ACTION=WORK]", hq);
        Assert.Contains("\"action\": \"work\"", hq);
        Assert.Contains("[RESPONSE=OK]", hq);
        Assert.Contains("READ_ONLY_NO_FILE_CHANGES", hq);
        Assert.Contains("\"readOnly\"", hq);
        Assert.Contains("\"initializeGitIfMissing\"", hq);
        Assert.Contains("JSON 밖", hq);
        Assert.Contains("END_ACTION", hq);
        Assert.Contains("BODY_BEGIN", hq);
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

        var managerFollowup = RoleContractLoader.BuildManagerPrompt(
            "CURRENT_EVENT: NEXT",
            includeFullContract: false);
        Assert.Contains(
            RoleContractLoader.ManagerContractPath,
            managerFollowup);
        Assert.DoesNotContain(
            "제1조 (기본 책임)",
            managerFollowup);
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
