using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CoordinatorFirstContractTests
{
    [Theory]
    [InlineData(true, true, 1, "existing-session", true)]
    [InlineData(false, false, 1, "selected-session", true)]
    [InlineData(false, false, 2, "existing-session", false)]
    [InlineData(false, true, 1, "existing-session", false)]
    [InlineData(false, true, 2, "existing-session", false)]
    [InlineData(false, true, 1, null, true)]
    [InlineData(false, true, 1, " ", true)]
    public void HqContractInjection_ReusedCliSessionUsesReference(
        bool isWebTransport, bool continuing, int milestoneIndex,
        string? existingSessionId, bool expectedFullContract)
    {
        var injectFull = RoleContractLoader.RequiresFullHqContract(
            isWebTransport, continuing, milestoneIndex, existingSessionId);
        Assert.Equal(expectedFullContract, injectFull);
        var contract = RoleContractLoader.HqContractForTurn(injectFull);
        if (expectedFullContract)
            Assert.Contains("[ACTION=WORK]", contract);
        else
            Assert.StartsWith("계약 참조:", contract);
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

        Assert.Contains("<STATUS>completed</>", valid);
        Assert.DoesNotContain("STATUS: completed", valid);
        Assert.Equal("completed", MilestoneDefinitionContract.ReadWorkStatus(valid));

        var invalid = MilestoneDefinitionContract.NormalizeWorkReport(
            0,
            "완료",
            null);

        Assert.Contains("<STATUS>blocked</>", invalid);
        Assert.Contains("WORK_REPORT_CONTRACT_INVALID", invalid);
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
    public void ReadOnlyGitResult_IsMarkedSkippedWithoutFailure()
    {
        var result = MilestoneGitResult.SkippedReadOnly("main");

        Assert.True(result.Success);
        Assert.False(result.PauseRequired);
        Assert.Null(result.CommitSha);
        Assert.Contains("SKIPPED_READ_ONLY", result.Summary);
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
