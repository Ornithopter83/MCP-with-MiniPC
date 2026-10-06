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
              "milestone": {
                "id": "M1",
                "branch": "main",
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
                    "order": 0,
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
        Assert.Equal("main", milestone.TargetBranch);
        Assert.True(milestone.QaReserved);
        Assert.Equal("bin/App.exe", milestone.Entrypoint);
        Assert.False(milestone.InitializeGitIfMissing);
        var work = Assert.Single(milestone.WorkItems.Values);
        Assert.Equal("10", work.Id);
        Assert.Equal(0, work.Order);
        Assert.Equal(new[] { "src/A", "src/B.cs" }, work.WritePaths);
        Assert.Contains("\"constraints\"", work.Body);

        var resource = Assert.Single(milestone.Resources.Values);
        Assert.Equal("IMAGE", resource.Type);
        Assert.Equal("assets/hero.png", resource.TargetPath);
        Assert.Contains("\"style\": \"flat\"", resource.Body);

        var workReports = new Dictionary<string, string>
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
        };
        var qaContext = MilestoneDefinitionContract.BuildQaContext(
            milestone,
            workReports,
            new Dictionary<string, string>(),
            Array.Empty<string>());
        var highContext = MilestoneDefinitionContract.BuildHighContext(
            milestone,
            workReports,
            new Dictionary<string, string>(),
            Array.Empty<string>(),
            qaReport: null);

        Assert.Contains("QA_INSTRUCTIONS:", qaContext);
        Assert.DoesNotContain("WORK_ITEM_INSTRUCTIONS:", qaContext);
        Assert.Contains("WORK_RESULTS:", qaContext);
        Assert.Contains("MILESTONE_VALIDATION:", highContext);
        Assert.DoesNotContain("HQ_DESIGN:", highContext);
        Assert.Contains("\"status\": \"completed\"", highContext);
    }

    [Fact]
    public void MilestoneDefinition_RejectsEveryNonMainBranch()
    {
        const string template = """
            [ACTION=WORK]
            {
              "milestone": {
                "id": "MAIN_ONLY",
                "branch": "__BRANCH__",
                "goal": "main 전용 검증",
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

        foreach (var branch in new[] { "AUTO", "master", "feature/test", "MAIN" })
        {
            var message = template.Replace(
                "__BRANCH__",
                branch,
                StringComparison.Ordinal);
            var parsed = ActionBlockContract.ParseHq(message);

            Assert.False(MilestoneDefinitionContract.TryBuild(
                message,
                parsed,
                out _,
                out var error));
            Assert.Equal("MILESTONE_MAIN_BRANCH_REQUIRED", error);
        }
    }

    [Fact]
    public void MilestoneDefinition_ParsesWorkerGitInitializationFlag()
    {
        const string message = """
            [ACTION=WORK]
            {
              "milestone": {
                "id": "GIT_INIT_TEST",
                "branch": "main",
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
              "milestone": {
                "id": "READ_ONLY_TEST",
                "branch": "main",
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
                    "order": 0,
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
                "branch": "main",
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
                    "order": 0,
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
              "milestone": {
                "id": "M4",
                "branch": "main",
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
                    "order": 0,
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
    public void MilestoneDefinition_RequiresNonNegativeWorkOrder()
    {
        const string missingOrder = """
            [ACTION=WORK]
            {
              "milestone": {
                "id": "ORDER_REQUIRED",
                "branch": "main",
                "goal": "순서 계약 검증",
                "entrypoint": null,
                "qa": {
                  "required": false,
                  "instructions": ""
                },
                "resource": null,
                "workItems": [
                  {
                    "id": 10,
                    "writePaths": ["src/A"],
                    "goal": "A",
                    "instructions": "A",
                    "completionCriteria": []
                  }
                ],
                "completionCriteria": [],
                "validation": []
              }
            }
            """;
        const string negativeOrder = """
            [ACTION=WORK]
            {
              "milestone": {
                "id": "ORDER_NEGATIVE",
                "branch": "main",
                "goal": "순서 계약 검증",
                "entrypoint": null,
                "qa": {
                  "required": false,
                  "instructions": ""
                },
                "resource": null,
                "workItems": [
                  {
                    "id": 10,
                    "order": -1,
                    "writePaths": ["src/A"],
                    "goal": "A",
                    "instructions": "A",
                    "completionCriteria": []
                  }
                ],
                "completionCriteria": [],
                "validation": []
              }
            }
            """;

        foreach (var message in new[] { missingOrder, negativeOrder })
        {
            var parsed = ActionBlockContract.ParseHq(message);
            Assert.False(MilestoneDefinitionContract.TryBuild(
                message,
                parsed,
                out _,
                out var error));
            Assert.Contains("ORDER_INVALID", error);
        }
    }

    [Fact]
    public void ManagerReport_UsesContentInsteadOfSummary()
    {
        const string valid = """
            [GOTO : HQ]
            [ACTION=REPORT]
            {
              "status": "partial",
              "content": "현재 결과와 HQ 판단에 필요한 의견"
            }
            """;
        const string legacy = """
            [GOTO : HQ]
            [ACTION=REPORT]
            {
              "status": "partial",
              "summary": "구형 보고"
            }
            """;

        var validParsed = ActionBlockContract.ParseManager(valid);
        Assert.False(validParsed.HasErrors);
        Assert.Equal(
            "현재 결과와 HQ 판단에 필요한 의견",
            Assert.Single(validParsed.ValidActions).Body);

        var legacyParsed = ActionBlockContract.ParseManager(legacy);
        Assert.True(legacyParsed.HasErrors);
        Assert.Contains(
            "CONTENT_REQUIRED",
            Assert.Single(legacyParsed.Actions).Errors);
    }

    [Fact]
    public void ManagerActionParser_AcceptsBatchDispatchJson()
    {
        const string message = """
            [ACTION=DISPATCH]
            {
              "workItemIds": [10, 11],
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
        Assert.Empty(
            ActionBlockContract.GetIdArray(
                action,
                "resourceIds"));
    }

    [Fact]
    public void ManagerDispatch_RejectsResourceIds()
    {
        const string message = """
            [ACTION=DISPATCH]
            {
              "workItemIds": [10],
              "resourceIds": [0],
              "mechanical": []
            }
            """;

        var parsed = ActionBlockContract.ParseManager(message);

        Assert.True(parsed.HasErrors);
        Assert.Contains(
            "RESOURCE_IDS_FORBIDDEN",
            Assert.Single(parsed.Actions).Errors);
    }

    [Fact]
    public void HighChangedPaths_RejectMarkdownWrappedPath()
    {
        const string message = """
            [GOTO : MANAGER]
            [ACTION=RESULT]
            {
              "status": "modified",
              "summary": "수정 완료",
              "changedPaths": ["`src/Fix.cs`"],
              "issues": []
            }
            """;

        var parsed = ActionBlockContract.ParseHigh(message);

        Assert.True(parsed.HasErrors);
        Assert.Contains(
            "CHANGED_PATH_INVALID",
            Assert.Single(parsed.Actions).Errors);
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
        Assert.Contains("[RESPONSE=OK]", hq);
        Assert.Contains("MILESTONE: M1", hq);
        Assert.Contains("BRANCH: main", hq);
        Assert.Contains("@@WORK 10", hq);
        Assert.Contains("ORDER: 0", hq);
        Assert.Contains("READ_ONLY: NO", hq);
        Assert.Contains("@@QA", hq);
        Assert.Contains("@@RESOURCE 0", hq);
        Assert.Contains("READ_ONLY_NO_FILE_CHANGES", hq);
        Assert.Contains("모르는 @@SECTION", hq);
        Assert.DoesNotContain("\"milestone\"", hq);
        Assert.DoesNotContain("BODY_BEGIN", hq);

        Assert.Contains("[ACTION=DISPATCH]", manager);
        Assert.Contains("\"workItemIds\"", manager);
        Assert.Contains("[ACTION=RESULT]", work);
        Assert.Contains("[GOTO : HIGH]", qa);
        Assert.Contains("build·run", qa);
        Assert.Contains("[GOTO : MANAGER]", high);
        Assert.Contains("HQ_UNKNOWN_SECTIONS_FOR_JUDGMENT", high);

        var managerFollowup = RoleContractLoader.BuildManagerPrompt(
            "CURRENT_EVENT: NEXT",
            includeFullContract: false);
        Assert.Contains(
            RoleContractLoader.ManagerContractPath,
            managerFollowup);

        var historyPrompt = RoleContractLoader.BuildHistoryPrompt(
            RoleContractLoader.BuildQaPrompt("runtime 확인"));
        Assert.Contains("runtime 확인", historyPrompt);
        Assert.DoesNotContain("당신은 QA다.", historyPrompt);
        Assert.DoesNotContain("ROLE_CONTRACT:", historyPrompt);
        Assert.DoesNotContain(RoleContractLoader.QaContractPath, historyPrompt);

        var mechanicalHistory = RoleContractLoader.BuildHistoryPrompt(
            "CURRENT_EVENT: START" +
            Environment.NewLine +
            Environment.NewLine +
            "Worker 기계 실행 보충 계약:" +
            Environment.NewLine +
            "- operation은 BUILD, RUN, PUBLISH 중 하나다.");
        Assert.Contains("CURRENT_EVENT: START", mechanicalHistory);
        Assert.DoesNotContain(
            "Worker 기계 실행 보충 계약:",
            mechanicalHistory);
        Assert.DoesNotContain(
            "operation은 BUILD, RUN, PUBLISH",
            mechanicalHistory);
    }

    [Fact]
    public void HqReport_UsesCompactDecisionPacketWithoutRawRoleReports()
    {
        const string message = """
            [ACTION=WORK]
            {
              "milestone": {
                "id": "COMPACT_REPORT",
                "branch": "main",
                "goal": "보고 축약 검증",
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
        Assert.True(MilestoneDefinitionContract.TryBuild(
            message,
            parsed,
            out var milestone,
            out var error),
            error);

        var report = MilestoneDefinitionContract.BuildHqReport(
            milestone!,
            "[GOTO : HQ]\n[ACTION=REPORT]\n{\"status\":\"completed\",\"content\":\"완료\"}",
            new Dictionary<string, string>
            {
                ["10"] = "[ACTION=RESULT]\n{\"status\":\"completed\",\"summary\":\"done\",\"changedPaths\":[],\"issues\":[]}"
            },
            new Dictionary<string, string>
            {
                ["0"] = "RESOURCE_STATUS: PENDING"
            },
            new[] { "MECHANICAL BUILD\nstatus=COMPLETED" },
            "QA DETAIL",
            "HIGH DETAIL",
            new MilestoneGitResult(
                true,
                false,
                "main",
                "abc123",
                "Git finalize 완료"),
            new[] { "old.txt" },
            new[] { "src/A.cs" },
            new[] { "src/A.cs", "old.txt" });

        Assert.Contains("MILESTONE_REPORT", report);
        Assert.Contains("MILESTONE: COMPACT_REPORT", report);
        Assert.Contains("RESULT: COMPLETED", report);
        Assert.Contains("BASELINE:", report);
        Assert.Contains("- commit=abc123", report);
        Assert.Contains("DONE:", report);
        Assert.Contains("UNRESOLVED:", report);
        Assert.Contains("완료", report);
        Assert.Contains("- relevantDirty=YES", report);
        Assert.Contains("- dirty=src/A.cs", report);
        Assert.Contains("DECISION_REQUIRED:", report);
        Assert.DoesNotContain("RESOURCE_STATUS: PENDING", report);
        Assert.DoesNotContain("WORK_RESULTS:", report);
        Assert.DoesNotContain("QA DETAIL", report);
        Assert.DoesNotContain("HIGH DETAIL", report);
        Assert.DoesNotContain("- old.txt", report);
    }

    [Fact]
    public void HqReport_SummarizesUnreadRecoveryElements()
    {
        const string message = """
            [ACTION=WORK]
            {
              "milestone": {
                "id": "RECOVERY_NOTICE",
                "branch": "main",
                "goal": "복구 보고",
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
        Assert.True(MilestoneDefinitionContract.TryBuild(
            message,
            parsed,
            out var milestone,
            out var error),
            error);

        var report = MilestoneDefinitionContract.BuildHqReport(
            milestone!,
            "[GOTO : HQ]\n[ACTION=REPORT]\n{\"status\":\"partial\",\"content\":\"복구 후 진행\"}",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            Array.Empty<string>(),
            string.Empty,
            string.Empty,
            MilestoneGitResult.NotStarted("main"),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            formatRecoveryOccurred: true,
            unreadRecoveryElements: new[] { "architecture", "repositoryBaseline" });

        Assert.Contains(
            "FORMAT_RECOVERY_NOTICE: 복구 후 읽지 못한 항목: architecture, repositoryBaseline",
            report);
        Assert.DoesNotContain("PARSER_ERROR", report);
        Assert.DoesNotContain("strict validation", report);
    }

    [Fact]
    public void HqTextProtocol_ParsesPlainTextAndForwardsUnknownSectionsToHigh()
    {
        const string response = """
            [ACTION=WORK]
            MILESTONE: M1
            BRANCH: main
            POLICY: DEFAULT
            ENTRYPOINT: project.godot
            GIT_INIT: NO

            @@GOAL
            플레이어 동작을 보완한다.

            @@WORK 10
            ORDER: 0
            READ_ONLY: NO
            WRITE_PATH: scripts/player

            @@WORK_GOAL
            이동 보완

            @@WORK_INSTRUCTIONS
            C:\AI-AGENT\Worker\Demo에서 현재 구조를 유지한다.

            @@WORK_COMPLETION
            이동 검증 완료

            @@QA
            REQUIRED: NO

            @@ARCHITECTURE_NOTE
            이 section은 Worker가 해석하지 않고 HIGH가 판단한다.

            @@MILESTONE_COMPLETION
            요구사항 완료

            @@VALIDATION
            정적 구조 확인
            """;

        var hq = HqTextProtocol.Parse(response);

        Assert.True(hq.IsValid, string.Join(", ", hq.Errors));
        Assert.Equal("WORK", hq.ActionName);
        Assert.Single(hq.UnknownSections);
        Assert.Contains("@@ARCHITECTURE_NOTE", hq.UnknownSections[0]);

        Assert.True(MilestoneDefinitionContract.TryBuild(
            hq.CompatibilityMessage,
            hq.Parse,
            out var milestone,
            out var error),
            error);

        var high = MilestoneDefinitionContract.BuildHighContext(
            milestone!,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            Array.Empty<string>(),
            null);

        Assert.Contains("HQ_UNKNOWN_SECTIONS_FOR_JUDGMENT:", high);
        Assert.Contains("@@ARCHITECTURE_NOTE", high);
        Assert.Contains("HIGH가 판단", high);
    }

    [Fact]
    public void HqTextProtocol_PauseAllowsRawWindowsPathWithoutJsonEscaping()
    {
        const string response = """
            [ACTION=PAUSE]

            @@MESSAGE
            C:\AI-AGENT\Worker\Demo\project.godot을 직접 확인해야 한다.

            @@RESUME
            확인 결과를 전달하면 재개한다.
            """;

        var hq = HqTextProtocol.Parse(response);

        Assert.True(hq.IsValid, string.Join(", ", hq.Errors));
        var action = Assert.Single(hq.Parse.ValidActions);
        Assert.Equal("PAUSE", action.Name);
        Assert.Contains("C:\\AI-AGENT\\Worker\\Demo", hq.CompatibilityMessage);
    }

    [Fact]
    public void DirectWorkPrompt_AllowsEmptyWritePathsWhenReadOnly()
    {
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            "10",
            "읽기 전용 조사",
            Array.Empty<string>(),
            "C:\\repo",
            readOnly: true);

        Assert.Contains("없음 (읽기 전용)", prompt);
    }

    [Theory]
    [InlineData(
        "BUILD",
        "dotnet build src/App/App.csproj --output \"C:\\\\Agent\\\\Project\\\\bin\"",
        "dotnet build src/App/App.csproj --output bin")]
    [InlineData(
        "PUBLISH",
        "dotnet publish src/App/App.csproj -o C:\\\\Agent\\\\Project\\\\publish",
        "dotnet publish src/App/App.csproj --output bin")]
    [InlineData(
        "BUILD",
        "dotnet build src/App/App.csproj",
        "dotnet build src/App/App.csproj --output bin")]
    [InlineData(
        "RUN",
        "dotnet run --project src/App/App.csproj",
        "dotnet run --project src/App/App.csproj")]
    public void MechanicalCommand_NormalizesBuildAndPublishOutput(
        string operation,
        string command,
        string expected)
    {
        Assert.Equal(
            expected,
            MilestoneMechanicalExecutor.NormalizeMechanicalCommand(
                operation,
                command));
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
    public void RoleElementRecovery_DefinesNonHqElementsAndAcceptsBareRecoveryJson()
    {
        Assert.Contains(
            RoleElementRecoveryContract.GetDefinitions("MANAGER", "DISPATCH"),
            item => item.Name == "mechanical" && item.Required);
        Assert.Contains(
            RoleElementRecoveryContract.GetDefinitions("WORK", "RESULT"),
            item => item.Name == "summary" && item.Required);
        Assert.Contains(
            RoleElementRecoveryContract.GetDefinitions("QA", "RESULT"),
            item => item.Name == "status" && item.Required);
        Assert.Contains(
            RoleElementRecoveryContract.GetDefinitions("HIGH", "RESULT"),
            item => item.Name == "changedPaths" && !item.Required);

        const string bareRecovery = """
            {
              "status": "completed",
              "summary": "복구",
              "changedPaths": [],
              "issues": [],
              "elements": {
                "qa": {
                  "required": false,
                  "instructions": ""
                }
              }
            }
            """;

        var recovered = RoleElementRecoveryContract.ReadRecoveredElements(
            bareRecovery,
            new[] { "qa" });

        Assert.Contains("qa", recovered.Keys);
    }

    [Fact]
    public void GenericElementRecovery_RebuildsManagerReportAndGoto()
    {
        const string malformed = """
            [ACTION=REPORT]
            {
              "status": "completed",
              "content": "첫 줄
            둘째 줄"
            }
            """;

        var scan = RoleElementRecoveryContract.Scan(
            "MANAGER",
            malformed,
            expectedAction: "REPORT",
            contractError: "REPORT:JSON_INVALID");

        Assert.Equal("REPORT", scan.ActionName);
        Assert.Contains("content", scan.RecoveryTargets);
        Assert.Contains("status", scan.Recovered.Keys);

        var recovered = new Dictionary<string, string>
        {
            ["content"] = "\"첫 줄 둘째 줄\""
        };

        Assert.True(RoleElementRecoveryContract.TryBuildRoleResponse(
            scan,
            recovered,
            "HQ",
            out var merged,
            out var remaining));
        Assert.Empty(remaining);

        var parsed = ActionBlockContract.ParseManager(merged);
        Assert.False(parsed.HasErrors);
        Assert.Equal("HQ", Assert.Single(parsed.ValidActions).GotoTarget);
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
