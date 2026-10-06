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

        var validationContext = MilestoneDefinitionContract.BuildValidationContext(
            milestone,
            new Dictionary<string, string>
            {
                ["10"] = """
                    [ACTION=RESULT]
                    {
                      "status": "completed",
                      "summary": "구현 완료",
                      "changedPaths": ["src/A"],
                      "issues": []
                    }
                    """
            },
            new Dictionary<string, string>(),
            Array.Empty<string>(),
            qaReport: null);

        Assert.Contains("WORK_ITEM_INSTRUCTIONS:", validationContext);
        Assert.Contains("기능을 구현한다.", validationContext);
        Assert.Contains("지정 경로 안에서 구현한다.", validationContext);
        Assert.Contains("WORK_RESULTS:", validationContext);
        Assert.Contains("\"status\": \"completed\"", validationContext);
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
    public void RoleJsonProtocol_RejectsJsonActionField()
    {
        const string message = """
            [ACTION=WORK]
            {
              "action": "work",
              "milestone": {
                "id": "M-OLD",
                "branch": "AUTO",
                "goal": "구형 문법",
                "entrypoint": null,
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

        Assert.True(parsed.HasErrors);
        Assert.Contains(
            "JSON_ACTION_FIELD_FORBIDDEN",
            Assert.Single(parsed.Actions).Errors);
    }

    [Fact]
    public void RoleJsonProtocol_PreservesGotoAndParsesResult()
    {
        const string message = """
            [GOTO : MANAGER]
            [ACTION=RESULT]
            {
              "status": "modified",
              "summary": "검토 후 수정",
              "changedPaths": ["src/Fix.cs"],
              "issues": []
            }
            """;

        var parsed = ActionBlockContract.ParseHigh(message);

        Assert.False(parsed.HasErrors);
        var action = Assert.Single(parsed.ValidActions);
        Assert.Equal("RESULT", action.Name);
        Assert.Equal("MANAGER", action.GotoTarget);
        Assert.Equal(
            new[] { "src/Fix.cs" },
            ActionBlockContract.GetStringArray(
                action,
                "changedPaths"));
    }

    [Fact]
    public void RoleJsonRepairPrompt_UsesOneTargetRoleSchema()
    {
        var prompt = RoleJsonRepairContract.BuildPrompt(
            "MANAGER",
            "[ACTION=DISPATCH]\n{ malformed }",
            "JSON_INVALID",
            "DISPATCH");

        Assert.Contains("일회성 임시 WORK", prompt);
        Assert.Contains("역할: MANAGER", prompt);
        Assert.Contains("기대 ACTION: DISPATCH", prompt);
        Assert.Contains("[ACTION=DISPATCH]", prompt);
        Assert.Contains("JSON 내부에는 action 필드를 만들지 않는다", prompt);
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
    public void ManagerActionParser_AcceptsBatchDispatchJson()
    {
        const string message = """
            [ACTION=DISPATCH]
            {
              "workItemIds": [10, 11],
              "resourceIds": [0],
              "mechanical": [
                {
                  "operation": "PUBLISH",
                  "command": "dotnet publish App.csproj -o bin"
                }
              ]
            }
            """;

        var parsed = ActionBlockContract.ParseManager(message);

        Assert.False(parsed.HasErrors);
        var action = Assert.Single(parsed.ValidActions);
        Assert.Equal("DISPATCH", action.Name);
        Assert.Equal(
            new[] { "10", "11" },
            ActionBlockContract.GetIdArray(
                action,
                "workItemIds"));
        Assert.Equal(
            new[] { "0" },
            ActionBlockContract.GetIdArray(
                action,
                "resourceIds"));
    }

    [Fact]
    public void WorkReportNormalizer_RequiresResultJsonStatus()
    {
        var valid = MilestoneDefinitionContract.NormalizeWorkReport(
            0,
            """
            [ACTION=RESULT]
            {
              "status": "completed",
              "summary": "완료",
              "changedPaths": ["src/A.cs"],
              "issues": []
            }
            """,
            null);
        Assert.Contains("\"status\": \"completed\"", valid);

        var invalid = MilestoneDefinitionContract.NormalizeWorkReport(
            0,
            "완료",
            null);
        Assert.Contains("\"status\": \"blocked\"", invalid);
        Assert.Contains("WORK_REPORT_CONTRACT_INVALID", invalid);
    }

    [Fact]
    public void QaAndHighReports_AreNormalizedToJsonContracts()
    {
        var qa = MilestoneDefinitionContract.NormalizeQaReport(
            0,
            """
            [GOTO : HIGH]
            [ACTION=RESULT]
            {
              "status": "completed",
              "summary": "동작 확인",
              "issues": []
            }
            """,
            null);
        Assert.Contains("\"status\": \"completed\"", qa);

        var high = MilestoneDefinitionContract.NormalizeHighReport(
            0,
            """
            [GOTO : MANAGER]
            [ACTION=RESULT]
            {
              "status": "modified",
              "summary": "보완 완료",
              "changedPaths": ["src/Fix.cs"],
              "issues": []
            }
            """,
            null);
        Assert.Contains("\"status\": \"modified\"", high);
        Assert.Equal(
            new[] { "src/Fix.cs" },
            MilestoneDefinitionContract.ExtractHighChangedPaths(high));
    }

    [Fact]
    public void HighModifiedReport_WithoutChangedPath_IsRejected()
    {
        var high = MilestoneDefinitionContract.NormalizeHighReport(
            0,
            """
            [GOTO : MANAGER]
            [ACTION=RESULT]
            {
              "status": "modified",
              "summary": "보완 완료",
              "changedPaths": [],
              "issues": []
            }
            """,
            null);

        Assert.Contains("\"status\": \"incomplete\"", high);
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
        Assert.DoesNotContain("\"action\":", hq);
        Assert.Contains("[RESPONSE=OK]", hq);
        Assert.Contains("READ_ONLY_NO_FILE_CHANGES", hq);
        Assert.Contains("\"readOnly\"", hq);
        Assert.Contains("\"initializeGitIfMissing\"", hq);
        Assert.Contains("[GOTO : 역할]", hq);
        Assert.DoesNotContain("END_ACTION", manager);
        Assert.DoesNotContain("BODY_BEGIN", manager);
        Assert.Contains("[ACTION=DISPATCH]", manager);
        Assert.Contains("\"workItemIds\"", manager);
        Assert.Contains("\"mechanical\"", manager);
        Assert.Contains("[ACTION=RESULT]", work);
        Assert.Contains("이미지 제작은 RESOURCE의 책임", work);
        Assert.Contains("[GOTO : HIGH]", qa);
        Assert.Contains("코드나 프로젝트 파일을 수정하지 않는다", qa);
        Assert.Contains("[GOTO : MANAGER]", high);
        Assert.Contains("\"changedPaths\"", high);

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
