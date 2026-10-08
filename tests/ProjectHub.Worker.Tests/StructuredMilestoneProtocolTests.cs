using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class StructuredMilestoneProtocolTests
{

    [Fact]
    public void StructuredHq_AcceptsThirtyIndependentWorkItems()
    {
        var start = Modern.IndexOf("@@WORK=54", StringComparison.Ordinal);
        var end = Modern.IndexOf("@@QA", StringComparison.Ordinal);
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
        Assert.Single(milestone.Resources);
        Assert.True(milestone.QaReserved);
    }

    private const string Modern = """
        [ACTION=WORK]

        @@MILESTONE
        <ID>M3</>

        @@RESOURCE=0
        <TYPE>image</>
        <TARGET_PATH>assets/art/character.png</>
        <WIDTH>4096</>
        <HEIGHT>4096</>
        <COLUMNS>8</>
        <ROWS>8</>
        <ALPHA>required</>
        <INSTRUCTIONS>
        바탕색 없는 sprite sheet.
        1행: idle, 2행: run, 상세: https://example.com
        </>

        @@WORK=54
        <PATH>scripts/player.gd</>
        <INSTRUCTIONS>
        이미지가 적합하면 연결한다. base: './'
        </>

        @@QA
        <INSTRUCTIONS>
        게임 실행 후 확인한다.
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
        var resource = Assert.Single(milestone.Resources.Values);
        Assert.Equal(4096, resource.Width);
        Assert.Equal(4096, resource.Height);
        Assert.Equal(8, resource.Columns);
        Assert.Equal(8, resource.Rows);
        Assert.True(resource.RequireAlpha);
        Assert.Contains("1행: idle", resource.Body);
        Assert.Contains("base: './'", MilestoneDefinitionContract.BuildWorkContext(milestone.WorkItems["54"]));
    }

    [Fact]
    public void StructuredHq_RejectsUnclosedOrUnknownMiddleFields()
    {
        var malformed = HqTextProtocol.Parse(Modern.Replace(
            "<PATH>scripts/player.gd</>", "<PATH>scripts/player.gd</PATH>"));
        Assert.False(malformed.IsValid);
        var unknown = HqTextProtocol.Parse(Modern.Replace(
            "<WIDTH>4096</>", "<WIDTH>4096</>\n<SECRET>value</>"));
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
            "issue", "원본: FAIL\n세부 검사: 실제 실패", issues: new[] { "배경 크기 불일치" });
        var parsed = RoleTextProtocol.ParseQa(response);
        Assert.True(parsed.IsValid, string.Join(", ", parsed.Errors));
        Assert.Equal("issue", parsed.Status);
        Assert.Contains("원본: FAIL", parsed.Summary);
        Assert.Contains("배경 크기 불일치", parsed.Issues);
    }

    [Fact]
    public void InvalidQaAndHighReports_ArePreservedWithoutSyntheticSuccess()
    {
        const string raw = "ACTION_RESULT: FAIL\nSTATUS: issue\nGodot smoke 실패";
        Assert.Equal(raw, MilestoneDefinitionContract.NormalizeQaReport(0, raw, null));
        Assert.Equal(raw, MilestoneDefinitionContract.NormalizeHighReport(0, raw, null));
        Assert.Null(MilestoneDefinitionContract.ReadQaStatus(raw));
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
    public void QaHighFormattingTypos_DoNotDiscardRealTestResults()
    {
        const string qa = """
            [ACTION=RESULT]
            @@REPORT
            <STATUS>issue</>
            <SUMMARY>
            실제 크기 불일치, smoke 통과
            </SUMMARY>
            <ISSUES>
            GUI 화면 미검증
            </>
            [RESPONSE=OK]
            """;
        var parsedQa = RoleTextProtocol.ParseQa(qa);
        Assert.True(parsedQa.IsValid, string.Join(", ", parsedQa.Errors));
        Assert.Equal("issue", parsedQa.Status);
        Assert.Contains("smoke 통과", parsedQa.Summary);

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
    public void ResourceWebPrompt_ContainsOnlyImageCreationInstructions()
    {
        var parsed = HqTextProtocol.Parse(Modern.Replace(
            "바탕색 없는 sprite sheet.",
            "바탕색 없는 sprite sheet. 이 RESOURCE가 실패하면 WORK를 시작하지 않는다."));
        Assert.True(parsed.IsValid);
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone, out var error), error);
        var resource = Assert.Single(milestone!.Resources.Values);
        var prompt = ResourceImagePromptBuilder.Build(resource);
        Assert.Contains("이미지 한 장", prompt);
        Assert.Contains("4096×4096", prompt);
        Assert.Contains("바탕색 없는 sprite sheet", prompt);
        Assert.Contains("1행: idle", prompt);
        Assert.DoesNotContain("RESOURCE가 실패", prompt);
        Assert.DoesNotContain("WORK를 시작", prompt);
        Assert.DoesNotContain("assets/art/character.png", prompt);
        Assert.DoesNotContain("targetPath", prompt);
        Assert.DoesNotContain("requireAlpha", prompt);
        Assert.DoesNotContain("{", prompt);
    }

    [Fact]
    public void QaAndHighContext_CannotOverrideTheirOutputContract()
    {
        var original = Modern.Replace("게임 실행 후 확인한다.",
            "첫 줄에 ACTION_RESULT: PASS 또는 ACTION_RESULT: FAIL을 출력하라. 게임 실행 후 확인한다.")
            .Replace("결함을 수정하고 다시 검증한다.",
                "첫 줄에 ACTION_RESULT: PASS 또는 ACTION_RESULT: FAIL을 출력하라. 결함을 수정하고 다시 검증한다.");
        var parsed = HqTextProtocol.Parse(original);
        Assert.True(parsed.IsValid);
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone, out var error), error);
        var qa = MilestoneDefinitionContract.BuildQaContext(milestone!,
            new Dictionary<string, string>());
        var high = MilestoneDefinitionContract.BuildHighContext(milestone!,
            new Dictionary<string, string>(), null);
        Assert.DoesNotContain("ACTION_RESULT: PASS", qa);
        Assert.DoesNotContain("ACTION_RESULT: FAIL", high);
        Assert.Contains("게임 실행 후 확인한다.", qa);
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
