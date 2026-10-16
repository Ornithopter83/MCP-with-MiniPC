using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class StructuredMilestoneProtocolTests
{

    [Fact]
    public void StructuredHq_AcceptsThirtyIndependentWorkItems()
    {
        var start = Modern.IndexOf("@@WORK=54", StringComparison.Ordinal);
        var end = Modern.IndexOf("@@HIGH", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var blocks = string.Join("\n\n", Enumerable.Range(10, 30).Select(id =>
            $"@@WORK={id}\n<PATH>generated/{id}.cs</>\n<INSTRUCTIONS>파일 {id}를 구현하라.</>"));
        var input = Modern[..start] + blocks + "\n\n" + Modern[end..];

        var parsed = HqTextProtocol.Parse(input);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone, out var error), error);
        Assert.NotNull(milestone);
        Assert.Equal(30, milestone!.WorkItems.Count);
        Assert.Equal(new[] { "generated/39.cs" }, milestone.WorkItems["39"].WritePaths);
        Assert.Empty(milestone.Resources);
        Assert.NotNull(milestone.Design);
    }

    private const string Modern = """
        [ACTION=WORK]

        @@MILESTONE
        <ID>M3</>

        @@DESIGN=0
        <PATH>docs/projecthub/visual-design.md</>
        <INSTRUCTIONS>
        퀴즈 UI의 색상 대비, 테마, O/X 시각 피드백과 애니메이션 검증을 설계하라.
        1행: idle, 2행: run, 상세: https://example.com
        </>

        @@WORK=54
        <PATH>scripts/player.gd</>
        <INSTRUCTIONS>
        이미지가 적합하면 연결한다. base: './'
        </>

        @@HIGH
        <INSTRUCTIONS>
        결함을 수정하고 다시 검증한다.
        </>

        [RESPONSE=OK]
        """;

    [Fact]
    public void StructuredHq_ReadsDeclaredFieldsButNotNestedProse()
    {
        var parsed = HqTextProtocol.Parse(Modern);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone, out var error), error);
        Assert.NotNull(milestone);
        Assert.Equal("M3", milestone!.Id);
        Assert.Empty(milestone.Resources);
        var design = Assert.IsType<MilestoneDesignDefinition>(milestone.Design);
        Assert.Equal("docs/projecthub/visual-design.md", design.Path);
        Assert.Contains("1행: idle", design.Instructions);
        Assert.Contains("base: './'", MilestoneDefinitionContract.BuildWorkContext(milestone.WorkItems["54"]));
    }

    [Theory]
    [InlineData("PAUSE")]
    [InlineData("END")]
    public void HqControlMessageIsNotInterpretedAsAnotherMilestone(string action)
    {
        const string message = "@@MILESTONE와 <ID>M8</>은 사용자 설명이며 오프라인 검증은 미확인이다.";
        var original = "[KEY=control123]" + Environment.NewLine +
            "[GOTO=WORKER]" + Environment.NewLine +
            "[ACTION=" + action + "]" + Environment.NewLine +
            "@@MESSAGE" + Environment.NewLine +
            "<TEXT>" + message + "</>" + Environment.NewLine +
            "[RESPONSE=OK]";
        var result = HqTextProtocol.Parse(original);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        var control = Assert.Single(result.Parse.ValidActions);
        Assert.Equal(action, control.Name);
        Assert.Equal(message, control.Body);
        var wrapped = ActionBlockContract.ParseHq(result.CompatibilityMessage);
        Assert.False(wrapped.HasErrors);
        Assert.Contains(message, Assert.Single(wrapped.ValidActions).JsonPayload);
    }

    [Theory]
    [InlineData("PAUSE")]
    [InlineData("END")]
    public void HqMalformedControlTextDoesNotChangeAction(
        string action)
    {
        var original = "[ACTION=" + action + "]\n" +
            "@@MESSAGE\n<TEXT>원문 그대로 노출</TEXT>\n" +
            "[RESPONSE=OK]";
        var result = HqTextProtocol.Parse(original);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        var control = Assert.Single(result.Parse.ValidActions);
        Assert.Equal(action, control.Name);
        Assert.Equal("<TEXT>원문 그대로 노출</TEXT>", control.Body);
    }

    [Fact]
    public void HqValidPauseUsesItsMessageWithoutReparsingItAsWork()
    {
        const string original = """
            [ACTION=PAUSE]
            @@MESSAGE
            <TEXT>사용자 입력 대기</>
            @@RESUME
            <TEXT>입력 후</>
            [RESPONSE=OK]
            """;
        var result = HqTextProtocol.Parse(original);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("PAUSE", Assert.Single(result.Parse.ValidActions).Name);
        Assert.Equal("사용자 입력 대기", Assert.Single(result.Parse.ValidActions).Body);
    }

    [Fact]
    public void HqInvalidActionAndMalformedWorkStillFailValidation()
    {
        var unknown = HqTextProtocol.Parse(
            "[ACTION=PROCEED]\n@@MILESTONE\n<ID>M8</>\n[RESPONSE=OK]");
        Assert.False(unknown.IsValid);
        var missingWork = HqTextProtocol.Parse(
            "[ACTION=WORK]\n@@MILESTONE\n<ID>M8</>\n[RESPONSE=OK]");
        Assert.False(missingWork.IsValid);
    }

    [Fact]
    public void StructuredHq_RejectsUnclosedOrUnknownMiddleFields()
    {
        var malformed = HqTextProtocol.Parse(Modern.Replace(
            "<PATH>scripts/player.gd</>", "<PATH>scripts/player.gd</PATH>"));
        Assert.False(malformed.IsValid);
        var unknown = HqTextProtocol.Parse(Modern.Replace(
            "<PATH>docs/projecthub/visual-design.md</>",
            "<PATH>docs/projecthub/visual-design.md</>\n<SECRET>value</>"));
        Assert.False(unknown.IsValid);
    }

    [Fact]
    public void PartialHqRepair_PreservesPathsAndRejectsDroppedPaths()
    {
        var malformed = Modern.Replace(
            "<PATH>scripts/player.gd</>", "<PATH>scripts/player.gd</PATH>");
        Assert.True(HqResponseRecoveryContract.TryRepairClosingTags(
            malformed, out var repaired));
        Assert.True(HqTextProtocol.Parse(repaired).IsValid);
        Assert.True(HqResponseRecoveryContract.PreservesPaths(malformed, repaired));
        Assert.True(HqResponseRecoveryContract.TryRepairClosingTags(
            malformed.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal), out var windowsRepaired));
        Assert.True(HqTextProtocol.Parse(windowsRepaired).IsValid);
        Assert.False(HqResponseRecoveryContract.PreservesPaths(
            malformed, repaired.Replace("<PATH>scripts/player.gd</>", "")));
    }

    [Fact]
    public void ReportProtocol_ParsesStatusOnlyAndKeepsProseOpaque()
    {
        var response = RoleTextProtocol.BuildResult(
            "blocked", "원본: FAIL\n세부 검사: 실제 실패", issues: new[] { "배경 크기 불일치" });
        var parsed = RoleTextProtocol.ParseWork(response);
        Assert.True(parsed.IsValid, string.Join(", ", parsed.Errors));
        Assert.Equal("blocked", parsed.Status);
        Assert.Contains("원본: FAIL", parsed.Summary);
        Assert.Contains("배경 크기 불일치", parsed.Issues);
    }

    [Fact]
    public void InvalidWorkAndHighReports_ArePreservedWithoutSyntheticSuccess()
    {
        const string raw = "ACTION_RESULT: FAIL\nSTATUS: issue\nUnity smoke 실패";
        Assert.Equal("blocked", MilestoneDefinitionContract.ReadWorkStatus(
            MilestoneDefinitionContract.NormalizeWorkReport(0, raw, null)));
        Assert.Equal(raw, MilestoneDefinitionContract.NormalizeHighReport(0, raw, null));
        Assert.Null(MilestoneDefinitionContract.ReadWorkStatus(raw));
    }

    [Fact]
    public void WorkMode_ParsesExplicitContinuationAndDefaultsToNew()
    {
        var normal = HqTextProtocol.Parse(Modern);
        Assert.True(normal.IsValid, string.Join(", ", normal.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            normal.CompatibilityMessage, normal.Parse, out var first, out _));
        Assert.Equal("NEW", first!.WorkItems["54"].ExecutionMode);

        var rawContinue = Modern.Replace(
            "<PATH>scripts/player.gd</>",
            "<MODE>CONTINUE</>\n<PATH>scripts/player.gd</>",
            StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(rawContinue);
        Assert.True(parsed.IsValid, string.Join(", ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var next, out var error), error);
        Assert.Equal("CONTINUE", next!.WorkItems["54"].ExecutionMode);

        var invalid = HqTextProtocol.Parse(rawContinue.Replace(
            "<MODE>CONTINUE</>", "<MODE>UNKNOWN</>", StringComparison.Ordinal));
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public void WorkInProgress_ReportRemainsMachineReadable()
    {
        var report = RoleTextProtocol.BuildResult(
            "in_progress", "파일 수정 후 오류 원인을 조사함", new[] { "src/actor.cs" });
        var parsed = RoleTextProtocol.ParseWork(report);
        Assert.True(parsed.IsValid, string.Join(", ", parsed.Errors));
        Assert.Equal("in_progress", parsed.Status);
    }

    [Fact]
    public void WorkHighFormattingTypos_DoNotDiscardRealTestResults()
    {
        const string work = """
            
            @@REPORT
            <STATUS>blocked</>
            <SUMMARY>
            실제 크기 불일치, smoke 통과
            </SUMMARY>
            <ISSUES>
            GUI 화면 미검증
            </>
            [RESPONSE=OK]
            """;
        var parsedWork = RoleTextProtocol.ParseWork(work);
        Assert.True(parsedWork.IsValid, string.Join(", ", parsedWork.Errors));
        Assert.Equal("blocked", parsedWork.Status);
        Assert.Contains("smoke 통과", parsedWork.Summary);

        const string high = """
            [RESPONSE=OK]
            @@REPORT
            <STATUS>completed</>
            <SUMMARY>
            결함 수정 완료
            </SUMMARY>
            <ISSUES>
            없음
            </ISSUES>
            [RESPONSE=OK]
            """;
        var parsedHigh = RoleTextProtocol.ParseHigh(high);
        Assert.True(parsedHigh.IsValid, string.Join(", ", parsedHigh.Errors));
        Assert.Equal("completed", parsedHigh.Status);
        Assert.Contains("결함 수정", parsedHigh.Summary);
    }

    [Fact]
    public void MixedXmlClosingTags_AreAcceptedWithoutChangingRealWorkOutcome()
    {
        const string report = """
            
            @@REPORT
            <STATUS>blocked</STATUS>
            <SUMMARY>
            테스트는 통과했지만 허용되지 않은 경로 수정이 필요합니다.
            </SUMMARY>
            <CHANGED_PATH>tests/a.gd</CHANGED_PATH>
            <CHANGED_PATH>tests/b.gd, tests/c.gd</>
            <CHANGED_PATH>
            - tests/d.gd
            - tests/e.gd
            </CHANGED_PATH>
            <CHANGED_PATH>tools/a.gd
            tests/new.gd
            docs/review.md</CHANGED_PATH>
            <CHANGED_PATH>[scripts/motion.gd](/C:/AI-AGENT/Worker/scripts/motion.gd:84)</>
            <ISSUES>쓰기 범위 외 파일 필요</ISSUES>
            [RESPONSE=OK]
            """;
        var parsed = RoleTextProtocol.ParseWork(report);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.Equal("blocked", parsed.Status);
        Assert.Contains("허용되지 않은 경로", parsed.Summary);
        Assert.Equal(new[] {
            "tests/a.gd", "tests/b.gd", "tests/c.gd", "tests/d.gd", "tests/e.gd",
            "tools/a.gd", "tests/new.gd", "docs/review.md", "scripts/motion.gd"
        }, parsed.ChangedPaths);
        Assert.Equal(new[] { "쓰기 범위 외 파일 필요" }, parsed.Issues);
        Assert.Equal("blocked", MilestoneDefinitionContract.ReadWorkStatus(report));
        var normalizedWork = MilestoneDefinitionContract.NormalizeWorkReport(0, report, null);
        Assert.Equal(RoleTextProtocol.BuildResult(parsed.Status, parsed.Summary,
            parsed.ChangedPaths, parsed.Issues), normalizedWork);
        Assert.True(RoleTextProtocol.ParseWork(normalizedWork).IsValid);
    }

    [Fact]
    public void LegacyWorkAndHighClosers_AreReadWithoutRetryOrStatusLoss()
    {
        const string work = """
            @@REPORT
            <STATUS>blocked</STATUS>
            <SUMMARY>
            전체 smoke 실패
            </SUMMARY>
            <ISSUES>
            Unity parser error
            </ISSUES>
            [RESPONSE=OK]
            """;
        Assert.Equal("blocked", MilestoneDefinitionContract.ReadWorkStatus(work));
        var normalizedWorkReport = MilestoneDefinitionContract.NormalizeWorkReport(0, work, null);
        Assert.Equal("blocked", MilestoneDefinitionContract.ReadWorkStatus(normalizedWorkReport));
        Assert.Contains("<STATUS>blocked</>", normalizedWorkReport);
        Assert.DoesNotContain("</STATUS>", normalizedWorkReport);

        const string high = """
            
            @@REPORT
            <STATUS>completed</STATUS>
            <SUMMARY>기존 결함 수정 및 검사 완료</SUMMARY>
            <CHANGED_PATH>src/A.cs, src/B.cs</CHANGED_PATH>
            <ISSUES>없음</ISSUES>
            [RESPONSE=OK]
            """;
        var parsed = RoleTextProtocol.ParseHigh(high);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.Equal("completed", MilestoneDefinitionContract.ReadHighStatus(high));
        Assert.Equal(new[] { "src/A.cs", "src/B.cs" }, parsed.ChangedPaths);
        var normalizedHigh = MilestoneDefinitionContract.NormalizeHighReport(0, high, null);
        Assert.Equal("completed", MilestoneDefinitionContract.ReadHighStatus(normalizedHigh));
        Assert.Contains("<CHANGED_PATH>src/A.cs</>", normalizedHigh);
        Assert.Contains("<CHANGED_PATH>src/B.cs</>", normalizedHigh);
        Assert.DoesNotContain("</CHANGED_PATH>", normalizedHigh);
    }

    [Fact]
    public void ModernHighReport_RequiresExplicitStructuredStatus()
    {
        var report = """
            [GOTO=HQ]
            @@REPORT
            <STATUS>completed</>
            <SUMMARY>실제 검증 정보</>
            <CHANGED_PATH>src/A.cs</>
            <ISSUES>남은 검사</>
            [RESPONSE=OK]
            """;
        var parsed = RoleTextProtocol.ParseHigh(report);
        Assert.True(parsed.IsValid);
        Assert.Equal("completed", parsed.Status);
        Assert.Equal(new[] { "src/A.cs" }, parsed.ChangedPaths);
    }

    [Fact]
    public void ReportCompatibility_DoesNotInferSuccessFromAmbiguousFields()
    {
        const string mismatched = """
            
            @@REPORT
            <STATUS>completed</STATUS>
            <SUMMARY>
            확인하지 못했습니다.
            </ISSUES>
            [RESPONSE=OK]
            """;
        Assert.False(RoleTextProtocol.ParseWork(mismatched).IsValid);

        const string ambiguousJson = """
            
            {"status":"success","summary":"검증했다고 주장함"}
            [RESPONSE=OK]
            """;
        Assert.False(RoleTextProtocol.ParseWork(ambiguousJson).IsValid);

        const string modifiedWithoutPaths = """
            
            {"status":"modified","summary":"수정을 주장하지만 변경 파일 없음"}
            [RESPONSE=OK]
            """;
        Assert.False(RoleTextProtocol.ParseHigh(modifiedWithoutPaths).IsValid);

        const string malformedJson = """
            
            {"status":"passed","summary":
            [RESPONSE=OK]
            """;
        Assert.False(RoleTextProtocol.ParseWork(malformedJson).IsValid);
    }

    [Fact]
    public void WorkWithInlineIssuesAndMissingSummaryClose_ReachesHighInCanonicalForm()
    {
        const string actualWork = """
            @@REPORT
            <STATUS>blocked</>
            <SUMMARY>
            Git HEAD와 origin/main이 일치하고 재검증했습니다.
            Unity 전체 smoke가 통과했습니다.
            #69 jump safe 검증, #70 Window 캡처도 통과.
            Git 추적 리뷰 증거 90개를 확인했습니다. <ISSUES>
            1. #73 미해결: 검증 산출물 정리 필요.
            2. 편집기 timeline 이벤트의 duration 검증이 남았습니다.
            3. 리뷰보드 사람 승인 미진행.
            </ISSUES>
            """;
        var parsed = RoleTextProtocol.ParseWork(actualWork);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.Equal("blocked", parsed.Status);
        Assert.Contains("전체 smoke", parsed.Summary);
        Assert.Contains("Git 추적 리뷰 증거 90개", parsed.Summary);
        Assert.DoesNotContain("<ISSUES>", parsed.Summary);
        Assert.Equal(3, parsed.Issues.Count);
        Assert.Contains("#73 미해결", parsed.Issues[0]);
        var canonical = MilestoneDefinitionContract.NormalizeWorkReport(0, actualWork, null);
        Assert.StartsWith("[GOTO=WORKER]", canonical.TrimStart());
        Assert.Contains("@@REPORT", canonical);
        Assert.Contains("<STATUS>blocked</>", canonical);
        Assert.Contains("<ISSUES>", canonical);
        Assert.Contains("</>", canonical);
        Assert.EndsWith("[RESPONSE=OK]", canonical);
        Assert.DoesNotContain("</ISSUES>", canonical);
        Assert.Equal("blocked", MilestoneDefinitionContract.ReadWorkStatus(canonical));
        Assert.Equal(canonical, MilestoneDefinitionContract.NormalizeWorkReport(0, canonical, null));
    }

    [Fact]
    public void MissingMiddleClosersAndMultiplePathBlocks_AreCanonicalized()
    {
        const string raw = """
            
            @@REPORT
            <STATUS>completed
            <SUMMARY>두 파일 구현
            <CHANGED_PATH>src/A.cs, src/B.cs
            <CHANGED_PATHS>
            - src/C.cs
            - src/D.cs
            <ISSUES>없음
            [RESPONSE=OK]
            """;
        var parsed = RoleTextProtocol.ParseWork(raw);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.Equal("completed", parsed.Status);
        Assert.Equal("두 파일 구현", parsed.Summary);
        Assert.Equal(new[] { "src/A.cs", "src/B.cs", "src/C.cs", "src/D.cs" },
            parsed.ChangedPaths);
        var canonical = MilestoneDefinitionContract.NormalizeWorkReport(0, raw, null);
        Assert.Equal(canonical, MilestoneDefinitionContract.NormalizeWorkReport(0, canonical, null));
        Assert.Equal(4, RoleTextProtocol.ParseWork(canonical).ChangedPaths.Count);
    }

    [Fact]
    public void FencedFieldExamplesAndInlineMentions_RemainSummaryEvidence()
    {
        const string raw = """
            
            @@REPORT
            <STATUS>completed</>
            <SUMMARY>
            테스트 코드에서 "<ISSUES>" 문자열을 검사했습니다.
            ```
            <ISSUES>
            This is only a code example.
            </ISSUES>
            ```
            실제 검증은 성공했습니다.
            </SUMMARY>
            <ISSUES>없음</ISSUES>
            [RESPONSE=OK]
            """;
        var parsed = RoleTextProtocol.ParseWork(raw);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.Contains("This is only a code example.", parsed.Summary);
        Assert.Contains("\"<ISSUES>\"", parsed.Summary);
        Assert.Empty(parsed.Issues);
        var canonical = MilestoneDefinitionContract.NormalizeWorkReport(0, raw, null);
        Assert.Equal(parsed.Summary, RoleTextProtocol.ParseWork(canonical).Summary);
    }

    [Fact]
    public void ConflictingStatusesAndMismatchedClosers_NeverBecomeSuccess()
    {
        const string conflicting = """
            @@REPORT
            <STATUS>completed</>
            <SUMMARY>완료했다고 주장</>
            <STATUS>blocked</>
            """;
        Assert.False(RoleTextProtocol.ParseWork(conflicting).IsValid);
        const string mismatched = """
            @@REPORT
            <STATUS>completed</>
            <SUMMARY>결과 설명
            </ISSUES>
            """;
        Assert.False(RoleTextProtocol.ParseWork(mismatched).IsValid);
        const string wrongAction = """
            [ACTION=WORK]
            @@REPORT
            <STATUS>completed</>
            <SUMMARY>형식 혼합</>
            """;
        Assert.False(RoleTextProtocol.ParseWork(wrongAction).IsValid);
        const string missingSummary = """
            @@REPORT
            <STATUS>completed</>
            완료한 것처럼 보이는 무태그 문장
            """;
        Assert.False(RoleTextProtocol.ParseWork(missingSummary).IsValid);
    }

    [Fact]
    public void ObsoleteJsonReport_IsNotPromotedToSuccess()
    {
        const string oldHigh = """
            
            {"status":"modified","summary":"코드 수정 후 통과",
             "changedPaths":["src/A.cs"],"issues":["사람 검토 필요"]}
            [RESPONSE=OK]
            """;
        var canonical = MilestoneDefinitionContract.NormalizeHighReport(0, oldHigh, null);
        Assert.StartsWith("", canonical);
        Assert.Equal(oldHigh.Trim(), canonical);
        Assert.Null(MilestoneDefinitionContract.ReadHighStatus(canonical));
    }

    [Fact]
    public void DesignZero_IsAnOrderedLocalRoleNotAResourcePrompt()
    {
        var parsed = HqTextProtocol.Parse(Modern);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone, out var error), error);
        Assert.Empty(milestone!.Resources);
        var design = Assert.IsType<MilestoneDesignDefinition>(milestone.Design);
        var prompt = UnityDesignPromptPolicy.Build(design, Path.GetTempPath());
        Assert.DoesNotContain("[ROLE=DESIGN]", prompt);
        Assert.Contains("DESIGN_ID: 0", prompt);
        Assert.Contains("--save_path/--output", prompt);
        Assert.Contains("실제 시각 결과물을 제작·적용", prompt);
        Assert.Contains("증거", prompt);
        Assert.DoesNotContain("RESOURCE_IMAGE", prompt);

        var legacy = HqTextProtocol.Parse(Modern.Replace(
            "@@DESIGN=0", "@@RESOURCE=0", StringComparison.Ordinal));
        Assert.False(legacy.IsValid);
        Assert.Contains("COMPACT_SECTION_FORBIDDEN", legacy.Errors);
    }

    [Fact]
    public void HighContext_CannotOverrideItsOutputContract()
    {
        var original = Modern.Replace("결함을 수정하고 다시 검증한다.",
            "첫 줄에 ACTION_RESULT: PASS 또는 ACTION_RESULT: FAIL을 출력하라. 결함을 수정하고 다시 검증한다.");
        var parsed = HqTextProtocol.Parse(original);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone, out var error), error);
        var high = MilestoneDefinitionContract.BuildHighContext(milestone!,
            new Dictionary<string, string>(), null);
        Assert.DoesNotContain("ACTION_RESULT: PASS", high);
        Assert.DoesNotContain("ACTION_RESULT: FAIL", high);
        Assert.DoesNotContain("QA 검증 지시:", high);
        Assert.Contains("결함을 수정하고 다시 검증한다.", high);
    }

    [Fact]
    public void ResourceValidator_SelectsACompliantImageAmongMultipleVariants()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "ph-resource-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string Create(string name, int width, int height)
            {
                var header = new byte[26];
                new byte[] {137,80,78,71,13,10,26,10}.CopyTo(header, 0);
                System.Text.Encoding.ASCII.GetBytes("IHDR").CopyTo(header, 12);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                    header.AsSpan(16, 4), (uint)width);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                    header.AsSpan(20, 4), (uint)height);
                header[25] = 6;
                var path = Path.Combine(root, name);
                File.WriteAllBytes(path, header);
                return path;
            }
            var tooSmall = Create("image-01.png", 1254, 1254);
            var selected = Create("image-02.png", 2048, 1024);
            var another = Create("image-03.png", 2048, 1024);
            var resource = new MilestoneResourceDefinition(
                "0", "IMAGE", "assets/art/sheet.png", "{}", "{}")
            {
                Width = 2048, Height = 1024, Columns = 4,
                Rows = 2, RequireAlpha = true
            };
            Assert.True(ResourceArtifactValidator.TrySelect(
                resource, new[] { tooSmall, selected, another },
                out var chosen, out var detail), detail);
            Assert.Equal(selected, chosen);
            Assert.Contains("RESOURCE_CANDIDATES: 3", detail);
            Assert.False(ResourceArtifactValidator.TrySelect(
                resource, new[] { tooSmall }, out _, out var rejection));
            Assert.Contains("RESOURCE_DIMENSIONS_MISMATCH", rejection);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResourceValidator_RejectsIncorrectDimensionsBeforeMoving()
    {
        var temp = Path.Combine(Path.GetTempPath(), "ph-artifact-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var header = new byte[26];
            new byte[] {137,80,78,71,13,10,26,10}.CopyTo(header, 0);
            System.Text.Encoding.ASCII.GetBytes("IHDR").CopyTo(header, 12);
            header[18] = 4; header[19] = 230; // 1254 width
            header[22] = 4; header[23] = 230; // 1254 height
            header[25] = 6; // RGBA
            File.WriteAllBytes(temp, header);

            var spec = new MilestoneResourceDefinition("0", "IMAGE", "assets/art/file.png", "{}", "{}")
            {
                Width = 4096, Height = 4096, Columns = 8, Rows = 8, RequireAlpha = true
            };
            Assert.False(ResourceArtifactValidator.TryValidate(spec, new[] { temp }, out var problem));
            Assert.Contains("RESOURCE_DIMENSIONS_MISMATCH", problem);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
