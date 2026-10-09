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
                    "testRequired": true,
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
            workReports);
        var highContext = MilestoneDefinitionContract.BuildHighContext(
            milestone,
            workReports,
            qaReport: null);

        Assert.Contains("@@WORK_GOALS", qaContext);
        Assert.Contains("@@WORK_RESULTS", qaContext);
        Assert.DoesNotContain("QA_INSTRUCTIONS:", qaContext);
        Assert.DoesNotContain("RESOURCE_RESULTS:", qaContext);
        Assert.DoesNotContain("MECHANICAL_RESULTS:", qaContext);
        Assert.DoesNotContain("PREPARED_OUTPUT_ROOT:", qaContext);
        Assert.Contains("@@WORK_GOALS", highContext);
        Assert.DoesNotContain("MILESTONE_VALIDATION:", highContext);
        Assert.DoesNotContain("RESOURCE_RESULTS:", highContext);
        Assert.DoesNotContain("MECHANICAL_RESULTS:", highContext);
        Assert.Contains("STATUS: completed", highContext);
        Assert.Contains("구현 완료", highContext);
        Assert.DoesNotContain("\"status\": \"completed\"", highContext);
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
                    "testRequired": false,
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
        Assert.Null(action.GotoTarget);
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
            "WORK",
            "[ACTION=RESULT]\n{ malformed }",
            "JSON_INVALID",
            "RESULT");

        Assert.Contains("일회성 임시 WORK", prompt);
        Assert.Contains("역할: WORK", prompt);
        Assert.Contains("기대 ACTION: RESULT", prompt);
        Assert.Contains("[ACTION=RESULT]", prompt);
        Assert.Contains("ACTION은 응답 envelope로 표현", prompt);
    }

    [Fact]
    public void HqResponseRecovery_ReinjectsCurrentContractForOneFullRetry()
    {
        var prompt = HqResponseRecoveryContract.BuildBody(
            "[ACTION=WORK]\n이전 응답",
            "WORK:JSON_INVALID");

        Assert.Contains("전체 응답 재요청", prompt);
        Assert.Contains("참고 데이터", prompt);
        Assert.Contains("현재 HQ 역할 계약 전문", prompt);
        Assert.Contains("완결된 전체 응답", prompt);
        Assert.Contains("한 번 다시 출력", prompt);
        Assert.Contains("WORK:JSON_INVALID", prompt);
        Assert.Contains("[ACTION=WORK]\n이전 응답", prompt);
        Assert.DoesNotContain("금지", prompt);
        Assert.DoesNotContain("FORBIDDEN", prompt);
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
                    "testRequired": false,
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
                    "testRequired": false,
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
    public void MilestoneDefinition_DoesNotRequireWorkOrder()
    {
        const string message = """
            [ACTION=WORK]
            {
              "milestone": {
                "id": "NO_ORDER_REQUIRED",
                "branch": "main",
                "goal": "독립 작업 계약 검증",
                "entrypoint": null,
                "qa": {
                  "required": false,
                  "instructions": ""
                },
                "resource": null,
                "workItems": [
                  {
                    "id": 10,
                    "testRequired": false,
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

        var parsed = ActionBlockContract.ParseHq(message);
        Assert.True(MilestoneDefinitionContract.TryBuild(
            message,
            parsed,
            out var milestone,
            out var error),
            error);
        Assert.NotNull(milestone);
        Assert.Single(milestone!.WorkItems);
    }

    [Fact]
    public void MilestoneDefinition_RejectsLegacyWorkOrder()
    {
        const string message = """
            [ACTION=WORK]
            {
              "milestone": {
                "id": "ORDER_FORBIDDEN",
                "branch": "main",
                "goal": "구형 순서 필드 거부",
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

        var parsed = ActionBlockContract.ParseHq(message);
        Assert.False(MilestoneDefinitionContract.TryBuild(
            message,
            parsed,
            out _,
            out var error));
        Assert.Contains("ORDER_FORBIDDEN", error);
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
    public void WorkReportNormalizer_UsesAnnotationStatus()
    {
        var valid = MilestoneDefinitionContract.NormalizeWorkReport(
            0,
            """
            [ACTION=RESULT]
            STATUS: completed

            @@SUMMARY
            완료

            @@CHANGED_PATHS
            - src/A.cs

            @@ISSUES
            없음

            [RESPONSE=OK]
            """,
            null);

        Assert.Contains("STATUS: completed", valid);

        var invalid = MilestoneDefinitionContract.NormalizeWorkReport(
            0,
            "완료",
            null);

        Assert.Contains("<STATUS>blocked</>", invalid);
        Assert.Contains("WORK_REPORT_CONTRACT_INVALID", invalid);
    }

    [Fact]
    public void QaAndHighReports_UseAnnotationContracts()
    {
        var qa = MilestoneDefinitionContract.NormalizeQaReport(
            0,
            """
            [ACTION=RESULT]
            STATUS: passed

            @@SUMMARY
            동작 확인

            @@CHANGED_PATHS
            없음

            @@ISSUES
            없음

            [RESPONSE=OK]
            """,
            null);

        Assert.Equal("passed", MilestoneDefinitionContract.ReadQaStatus(qa));

        var high = MilestoneDefinitionContract.NormalizeHighReport(
            0,
            """
            [ACTION=RESULT]
            STATUS: completed

            @@SUMMARY
            보완 완료

            @@CHANGED_PATHS
            - src/Fix.cs

            @@ISSUES
            없음

            [RESPONSE=OK]
            """,
            null);

        Assert.Equal("completed", MilestoneDefinitionContract.ReadHighStatus(high));
    }

    [Fact]
    public void QaBlocked_IsDistinctFromIssue()
    {
        var qa = MilestoneDefinitionContract.NormalizeQaReport(
            0,
            """
            [ACTION=RESULT]
            STATUS: blocked

            @@SUMMARY
            실행환경 문제

            @@CHANGED_PATHS
            없음

            @@ISSUES
            - helper setup failed

            [RESPONSE=OK]
            """,
            null);

        Assert.Equal("blocked", MilestoneDefinitionContract.ReadQaStatus(qa));
    }

    [Fact]
    public void LegacyManagerSetting_RemainsReadableButIsNotPartOfActiveFlow()
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
    public void RoleContracts_DescribeManagerlessWorkerFlow()
    {
        var hq = RoleContractLoader.LoadHqFooter();
        var work = RoleContractLoader.LoadWorkFooter();
        var qa = RoleContractLoader.LoadQaFooter();
        var high = RoleContractLoader.LoadHighFooter();

        Assert.Contains("[ACTION=WORK]", hq);
        Assert.Contains("[RESPONSE=OK]", hq);
        Assert.Contains("<ID>M1</>", hq);
        Assert.Contains("@@WORK=10", hq);
        Assert.Contains("<PATH>", hq);
        Assert.Contains("@@QA", hq);
        Assert.Contains("@@HIGH", hq);
        Assert.Contains("Git", hq);
        Assert.DoesNotContain("@@MANAGER", hq);
        Assert.DoesNotContain("@@MECHANICAL", hq);

        Assert.Contains("[ACTION=RESULT]", work);
        Assert.Contains("<STATUS>passed</>", qa);
        Assert.Contains("<STATUS>는 passed, issue, blocked", qa);
        Assert.DoesNotContain("[GOTO : HIGH]", qa);
        Assert.DoesNotContain("[GOTO : MANAGER]", high);
        Assert.Contains("Git 오류", high);

        var historyPrompt = RoleContractLoader.BuildHistoryPrompt(
            RoleContractLoader.BuildQaPrompt("runtime 확인"));
        Assert.Contains("runtime 확인", historyPrompt);
        Assert.DoesNotContain("당신은 QA다.", historyPrompt);
        Assert.DoesNotContain("ROLE_CONTRACT:", historyPrompt);
        Assert.DoesNotContain(RoleContractLoader.QaContractPath, historyPrompt);
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
            "COMPLETED_WITH_HIGH",
            new Dictionary<string, string>
            {
                ["10"] = "[ACTION=RESULT]\nSTATUS: completed\n\n@@SUMMARY\ndone\n\n@@CHANGED_PATHS\n없음\n\n@@ISSUES\n없음\n\n[RESPONSE=OK]"
            },
            new Dictionary<string, string>
            {
                ["0"] = "RESOURCE_STATUS: PENDING"
            },
            new[] { "MECHANICAL BUILD\nstatus=COMPLETED" },
            "QA DETAIL",
            "[ACTION=RESULT]\nSTATUS: completed\n\n@@SUMMARY\nHIGH 보완 완료\n\n@@CHANGED_PATHS\n- src/A.cs\n\n@@ISSUES\n없음\n\n[RESPONSE=OK]",
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
        Assert.Contains("OUTCOME: COMPLETED_WITH_HIGH", report);
        Assert.Contains("HIGH_STATUS: completed", report);
        Assert.Contains("HIGH 보완 완료", report);
        Assert.Contains("GIT_RESULT:", report);
        Assert.Contains("- commit=abc123", report);
        Assert.Contains("DECISION_REQUIRED:", report);
        Assert.Contains("RESOURCE_STATUS: PENDING", report);
        Assert.DoesNotContain("WORK_RESULTS:", report);
        Assert.Contains("QA DETAIL", report);
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
            "WORK_BLOCKED:10",
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
    public void HqTextProtocol_ParsesPlainTextWorkWithTestFlag()
    {
        const string response = """
            [ACTION=WORK]
            MILESTONE: M1
            BRANCH: main
            POLICY: DEFAULT
            ENTRYPOINT: src/App/App.csproj
            GIT_INIT: NO

            @@GOAL
            플레이어 동작을 보완한다.

            @@WORK 10
            READ_ONLY: NO
            TEST: ON
            WRITE_PATH: scripts/player

            @@WORK_GOAL
            이동 보완

            @@WORK_INSTRUCTIONS
            C:\AI-AGENT\Worker\Demo에서 현재 구조를 유지한다.

            @@WORK_COMPLETION
            이동 검증 완료
            """;

        var hq = HqTextProtocol.Parse(response);

        Assert.True(hq.IsValid, string.Join(", ", hq.Errors));
        Assert.Equal("WORK", hq.ActionName);
        Assert.Empty(hq.UnknownSections);
        Assert.Contains("플레이어 동작을 보완한다.", hq.CompatibilityMessage);
        Assert.False(
            hq.CompatibilityMessage.Contains(
                "\\uD50C",
                StringComparison.OrdinalIgnoreCase));

        Assert.True(MilestoneDefinitionContract.TryBuild(
            hq.CompatibilityMessage,
            hq.Parse,
            out var milestone,
            out var error),
            error);
        Assert.True(milestone!.QaReserved);
        Assert.True(Assert.Single(milestone.WorkItems.Values).TestRequired);
    }

    [Fact]
    public void HqTextProtocol_RejectsWorkWithoutTestFlag()
    {
        const string response = """
            [ACTION=WORK]
            MILESTONE: M1
            BRANCH: main
            POLICY: DEFAULT
            ENTRYPOINT: NONE
            GIT_INIT: NO

            @@GOAL
            문서를 수정한다.

            @@WORK 10
            READ_ONLY: NO
            WRITE_PATH: README.md

            @@WORK_GOAL
            문서 수정

            @@WORK_INSTRUCTIONS
            README를 수정한다.

            @@WORK_COMPLETION
            문서가 수정된다.
            """;

        var hq = HqTextProtocol.Parse(response);

        Assert.False(hq.IsValid);
        Assert.Contains("WORK 10.TEST", hq.Errors);
    }

    [Fact]
    public void HqTextProtocol_RejectsLegacyExecutionSections()
    {
        const string response = """
            [ACTION=WORK]
            MILESTONE: M1
            BRANCH: main
            POLICY: DEFAULT
            ENTRYPOINT: NONE
            GIT_INIT: NO

            @@GOAL
            문서를 수정한다.

            @@WORK 10
            READ_ONLY: NO
            TEST: OFF
            WRITE_PATH: README.md

            @@WORK_GOAL
            문서 수정

            @@WORK_INSTRUCTIONS
            README를 수정한다.

            @@WORK_COMPLETION
            완료

            @@MECHANICAL
            BUILD: npm run build
            """;

        var hq = HqTextProtocol.Parse(response);

        Assert.False(hq.IsValid);
        Assert.Contains("UNKNOWN_SECTION_FORBIDDEN", hq.Errors);
    }

    [Fact]
    public void RoleTextProtocol_ParsesWorkAnnotation()
    {
        var result = RoleTextProtocol.ParseWork(
            """
            [ACTION=RESULT]
            STATUS: completed

            @@SUMMARY
            구현 완료

            @@CHANGED_PATHS
            - src/A.cs

            @@ISSUES
            없음

            [RESPONSE=OK]
            """);

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
        Assert.Equal("completed", result.Status);
        Assert.Equal("구현 완료", result.Summary);
        Assert.Equal(new[] { "src/A.cs" }, result.ChangedPaths);
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
        using var json = System.Text.Json.JsonDocument.Parse(action.JsonPayload!);
        Assert.Contains("C:\\AI-AGENT\\Worker\\Demo",
            json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void HqTextProtocol_ControlActionRejectsUnknownSection()
    {
        const string response = """
            [ACTION=PAUSE]

            @@MESSAGE
            사용자 확인 필요

            @@ARCHITECTURE_NOTE
            HIGH 판단이 필요한 임의 section
            """;

        var hq = HqTextProtocol.Parse(response);

        Assert.False(hq.IsValid);
        Assert.Contains(
            "UNKNOWN_SECTION_REQUIRES_WORK",
            hq.Errors);
        Assert.Contains("@@ARCHITECTURE_NOTE", hq.UnknownSections[0]);
    }

    [Fact]
    public void ProjectHubJson_PreservesReadableUnicode()
    {
        var compact = ProjectHubJson.Serialize(new { message = "한글 인코딩 확인" });
        var indented = ProjectHubJson.SerializeIndented(new { message = "한글 인코딩 확인" });
        var roleResult = MilestoneDefinitionContract.BuildRoleResult(
            null,
            "completed",
            "한글 결과",
            issues: new[] { "문제 없음" });

        Assert.Contains("한글 인코딩 확인", compact);
        Assert.Contains("한글 인코딩 확인", indented);
        Assert.Contains("한글 결과", roleResult);
        Assert.Contains("문제 없음", roleResult);
        Assert.False(compact.Contains("\\uD55C", StringComparison.OrdinalIgnoreCase));
        Assert.False(indented.Contains("\\uD55C", StringComparison.OrdinalIgnoreCase));
        Assert.False(roleResult.Contains("\\uD55C", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkProjection_OmitsWorkerOrchestrationMetadata()
    {
        var work = new MilestoneWorkDefinition(
            "10",
            new[] { "src" },
            false,
            true,
            """
            {
              "id": 10,
              "readOnly": false,
              "testRequired": true,
              "writePaths": ["src"],
              "goal": "기능 구현",
              "instructions": "코드를 수정한다.",
              "completionCriteria": ["기능이 동작한다."]
            }
            """,
            string.Empty);

        var body = MilestoneDefinitionContract.BuildWorkContext(work);
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            "10",
            body,
            new[] { "src" },
            "C:\\repo");

        Assert.Contains("기능 구현", body);
        Assert.Contains("코드를 수정한다.", body);
        Assert.Contains("기능이 동작한다.", body);
        Assert.DoesNotContain("testRequired", body);
        Assert.DoesNotContain("readOnly", body);
        Assert.DoesNotContain("writePaths", body);
        Assert.DoesNotContain("Git commit", prompt);
        Assert.DoesNotContain("RESOURCE 임시 루트", prompt);
        Assert.DoesNotContain("WORK 임시 산출물 루트", prompt);
    }

    [Fact]
    public void RolePrompts_RepeatUtf8Guidance()
    {
        var workPrompt = RoleContractLoader.BuildDirectWorkPrompt(
            "10",
            "{}",
            new[] { "src" },
            "C:\\repo");
        var qaPrompt = RoleContractLoader.BuildQaPrompt("{}");
        var highPrompt = RoleContractLoader.BuildHighPrompt("{}");
        foreach (var prompt in new[] { workPrompt, qaPrompt, highPrompt })
        {
            Assert.Contains("문자 인코딩:", prompt);
            Assert.Contains("UTF-8", prompt);
            Assert.Contains("Get-Content -Encoding UTF8", prompt);
        }
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

        Assert.Contains("@@WRITE_PATH", prompt);
        Assert.Contains("없음", prompt);
    }

    [Fact]
    public void QaCommandGate_UsesQaSpecificReason()
    {
        var script = BuildExecutionPolicy.CreateQaCodexPreToolHookScript();
        var hook = BuildExecutionPolicy.BuildCodexPreToolHookOverride(
            "C:\\temp\\qa-gate.ps1",
            "ProjectHub QA command gate");

        Assert.Contains("QA keeps Git finalization in Worker", script);
        Assert.DoesNotContain("dotnet", script);
        Assert.Contains("ProjectHub QA command gate", hook);
    }

    [Fact]
    public void ReadOnlyGitResult_IsMarkedSkippedWithoutFailure()
    {
        var result = MilestoneGitResult.SkippedReadOnly("main");

        Assert.True(result.Success);
        Assert.False(result.PauseRequired);
        Assert.Null(result.CommitSha);
        Assert.Contains("SKIPPED_READ_ONLY", result.Summary);
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
    public void WorkScopes_ReturnOnlyConflictingWorkItemIds()
    {
        var conflicts =
            MilestoneMechanicalExecutor.FindOverlappingWorkItemIds(
                new (string Id, IReadOnlyList<string> Scopes)[]
                {
                    ("10", new[] { "src/Feature" }),
                    ("11", new[] { "src/Feature/View.cs" }),
                    ("12", new[] { "src/Independent" })
                });

        Assert.Contains("10", conflicts);
        Assert.Contains("11", conflicts);
        Assert.DoesNotContain("12", conflicts);
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
