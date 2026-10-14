using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class RoleGotoRoutingTests
{
    private const string Summary = """
        [ACTION=RESULT]
        @@REPORT
        <STATUS>completed</>
        <SUMMARY>작업 결과</>
        [RESPONSE=OK]
        """;

    private const string CanonicalHandoff = """
        [GOTO=DESIGN]
        [ACTION=RESULT]
        @@REPORT
        <STATUS>in_progress</>
        <SUMMARY>50개 승급 UI 문구 겹침</>
        <DESIGN_GOAL>50개 승급 화면에서 문구 겹침을 수정한다.</>
        <EVIDENCE_PATH>temp/screens/50.png; temp/screens/100.png
        temp/screens/missing.png</>
        <TARGET_PATH>Assets/UI/Promotion.prefab</>
        <EXPECTED_RESULT>각 문구가 가려지지 않는다.</>
        <REASON>화면 캡처를 통해 발견함</>
        [RESPONSE=OK]
        """;

    [Theory]
    [InlineData("[GOTO=DESIGN]", "DESIGN")]
    [InlineData("[GOTO : DESIGN]", "DESIGN")]
    [InlineData("[goto: design]", "DESIGN")]
    [InlineData("[Goto =  Design ]", "DESIGN")]
    [InlineData("[GOTO : ASSET MANAGER]", "ASSET_MANAGER")]
    [InlineData("[GOTO=WORKER]", "WORKER")]
    [InlineData("[GOTO : HQ]", "HQ")]
    public void EquivalentGotoSyntax_NormalizesToSameRole(string line, string role)
    {
        Assert.True(RoleGotoContract.TryReadLeading(
            line + "\n" + Summary, out var actual, out var error), error);
        Assert.Equal(role, actual);
    }

    [Theory]
    [InlineData("[GOTO : DESIGN]\n[GOTO=HQ]\n")]
    [InlineData("[GOTO==DESIGN]\n")]
    [InlineData("[GOTO=UNKNOWN]\n")]
    [InlineData("[GOTO=../DESIGN]\n")]
    public void MalformedConflictingOrUnknownGoto_FailsClosed(string leading)
    {
        Assert.False(RoleGotoContract.TryReadLeading(
            leading + Summary, out _, out _));
        Assert.False(RoleTextProtocol.ParseWork(leading + Summary).IsValid);
    }

    [Fact]
    public void DuplicateMatchingGoto_IsNotAnAmbiguousRoute()
    {
        Assert.True(RoleGotoContract.TryReadLeading(
            "[GOTO:DESIGN]\n[GOTO=design]\n" + Summary,
            out var role, out var error), error);
        Assert.Equal("DESIGN", role);
    }

    [Fact]
    public void RoutesInSummaryOrExampleText_AreNotExecuted()
    {
        var report = Summary.Replace("<SUMMARY>작업 결과</>",
            "<SUMMARY>문서 예시 [GOTO=DESIGN] 는 코드가 아니다</>");
        Assert.True(RoleGotoContract.TryReadLeading(report,
            out var destination, out var error), error);
        Assert.Null(destination);
        Assert.True(RoleTextProtocol.ParseWork(report).IsValid);
    }

    [Theory]
    [InlineData("[GOTO : DESIGN]")]
    [InlineData("[GOTO=DESIGN]")]
    public void CanonicalHighHandoff_UsesGotoAndPreservesSupportingEvidence(string gotoLine)
    {
        var report = CanonicalHandoff.Replace("[GOTO=DESIGN]", gotoLine);
        Assert.Equal("design_handoff", MilestoneDefinitionContract.ReadHighStatus(report));
        Assert.Equal(report,
            MilestoneDefinitionContract.NormalizeHighReport(0, report, null));
        Assert.True(HighDesignHandoffContract.TryRead(
            report, out var handoff, out var error), error);
        Assert.Equal("Assets/UI/Promotion.prefab", handoff!.TargetPath);
        Assert.Equal(3, handoff.EvidencePaths.Count);
        Assert.Equal("temp/screens/50.png", handoff.EvidencePaths[0]);
        Assert.Equal("temp/screens/100.png", handoff.EvidencePaths[1]);
    }

    [Fact]
    public void LegacyHandoff_WithoutGotoRemainsReadable()
    {
        var report = CanonicalHandoff.Replace("[GOTO=DESIGN]\n", "")
            .Replace("<STATUS>in_progress</>", "<STATUS>design_handoff</>");
        Assert.Equal("design_handoff", MilestoneDefinitionContract.ReadHighStatus(report));
        Assert.True(HighDesignHandoffContract.TryRead(report, out _, out _));
    }

    [Theory]
    [InlineData("completed", "DESIGN")]
    [InlineData("blocked", "DESIGN")]
    [InlineData("in_progress", "HQ")]
    [InlineData("design_handoff", "HQ")]
    [InlineData("in_progress", "WORK")]
    public void ContradictoryOrUnauthorizedHighRouteCannotCompleteOrDispatch(
        string status, string destination)
    {
        var report = CanonicalHandoff.Replace("[GOTO=DESIGN]",
            "[GOTO=" + destination + "]").Replace("<STATUS>in_progress</>",
            "<STATUS>" + status + "</>");
        Assert.Null(MilestoneDefinitionContract.ReadHighStatus(report));
        Assert.False(HighDesignHandoffContract.TryRead(report, out _, out _));
        var normalized = MilestoneDefinitionContract.NormalizeHighReport(0, report, null);
        Assert.Null(MilestoneDefinitionContract.ReadHighStatus(normalized));
    }

    [Fact]
    public void HighCompletionCanReturnToHqWithoutStatusChange()
    {
        const string report = """
            [GOTO : HQ]
            [ACTION=RESULT]
            @@REPORT
            <STATUS>completed</>
            <SUMMARY>검증 종료</>
            [RESPONSE=OK]
            """;
        Assert.Equal("completed", MilestoneDefinitionContract.ReadHighStatus(report));
        var normalized = MilestoneDefinitionContract.NormalizeHighReport(0, report, null);
        Assert.StartsWith("[GOTO=HQ]", normalized);
        Assert.Equal("completed", MilestoneDefinitionContract.ReadHighStatus(normalized));
    }

    [Fact]
    public void WorkCannotRedirectParallelSchedulerToAnotherRole()
    {
        Assert.False(RoleTextProtocol.ParseWork(
            "[GOTO=DESIGN]\n" + Summary).IsValid);
        Assert.True(RoleTextProtocol.ParseWork(
            "[GOTO : WORKER]\n" + Summary).IsValid);
    }

    [Fact]
    public void EvidenceResolverReportsOnlyExistingFilesWithinRoot()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "goto-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "temp", "screen"));
        try
        {
            var image = Path.Combine(root, "temp", "screen", "evidence.png");
            File.WriteAllBytes(image, new byte[] { 137, 80, 78, 71 });
            var verified = HighDesignHandoffContract.ExistingEvidencePaths(root,
                new[] { "temp/screen/evidence.png", "temp/screen/missing.png",
                    "../outside.png", "C:/outside.png" });
            Assert.Equal(new[] { "temp/screen/evidence.png" }, verified);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void WorkResultAndLegacyManagerUseCommonGotoParsing()
    {
        Assert.True(RoleTextProtocol.ParseWork("[GOTO=WORKER]\n" + Summary).IsValid);
        const string manager = """
            [GOTO : HQ]
            [ACTION=REPORT]
            {"status":"completed","content":"asset list"}
            """;
        var result = ActionBlockContract.ParseManager(manager);
        Assert.False(result.HasErrors);
        Assert.Equal("HQ", Assert.Single(result.ValidActions).GotoTarget);
    }

    [Fact]
    public void HqMilestoneAcceptsCanonicalAndLegacyGotoButNotArbitraryTarget()
    {
        const string milestone = """
            [ACTION=WORK]
            @@MILESTONE
            <ID>M1</>
            @@DESIGN=0
            <PATH>docs/projecthub/design.md</>
            <INSTRUCTIONS>UI 제작</>
            @@HIGH
            <INSTRUCTIONS>완료 검토</>
            [RESPONSE=OK]
            """;
        foreach (var prefix in new[] { "[GOTO=WORKER]", "[GOTO : WORKER]" })
        {
            var parsed = HqTextProtocol.Parse(prefix + "\n" + milestone);
            Assert.True(parsed.IsValid, string.Join(";", parsed.Errors));
        }
        Assert.False(HqTextProtocol.Parse("[GOTO=DESIGN]\n" + milestone).IsValid);
    }

    [Fact]
    public void MissingEvidenceAndOptionalDescriptions_DoNotBlockSafeHandoff()
    {
        var report = CanonicalHandoff.Replace(
            "<EVIDENCE_PATH>temp/screens/50.png; temp/screens/100.png\n" +
            "temp/screens/missing.png</>", "");
        report = report.Replace("<EXPECTED_RESULT>각 문구가 가려지지 않는다.</>", "")
            .Replace("<REASON>화면 캡처를 통해 발견함</>", "");
        Assert.True(HighDesignHandoffContract.TryRead(
            report, out var handoff, out var error), error);
        Assert.Empty(handoff!.EvidencePaths);
        Assert.NotEmpty(handoff.Warnings);
        Assert.Equal(handoff.Goal, handoff.ExpectedResult);
    }

    [Theory]
    [InlineData("../secrets/screenshot.png")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("Assets/UI/Panel.prefab;Assets/Other")]
    public void UnsafeOrAmbiguousWriteTargetsAreNeverAssumed(string target)
    {
        var report = CanonicalHandoff.Replace(
            "Assets/UI/Promotion.prefab", target);
        Assert.False(HighDesignHandoffContract.TryRead(
            report, out _, out _));
    }
}
