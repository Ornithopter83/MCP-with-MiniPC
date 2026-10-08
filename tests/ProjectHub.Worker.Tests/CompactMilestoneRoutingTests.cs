using System.Reflection;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CompactMilestoneRoutingTests
{
    private const string Minimal = """
        [ACTION=WORK]
        MILESTONE: M1

        @@WORK=10
        <PATH>a.txt</>
        a.txt를 생성하여 설정 읽기를 구현하라.

        @@WORK=11
        <PATH>b.cs</>
        b.cs 입력 처리를 완성하라.

        @@QA
        c.exe의 방향키 입력을 실행·검증하라.

        @@HIGH
        M1 완료 여부와 미해결 문제를 검토하라.

        [RESPONSE=OK]
        """;

    [Fact]
    public void CompactHq_ProducesMachineReadableMilestone()
    {
        var parsed = HqTextProtocol.Parse(Minimal);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone,
            out var error), error);
        Assert.NotNull(milestone);
        Assert.Equal("M1", milestone!.Id);
        Assert.Equal("main", milestone.TargetBranch);
        Assert.Equal(2, milestone.WorkItems.Count);
        Assert.Equal(new[] { "a.txt" }, milestone.WorkItems["10"].WritePaths);
        Assert.Equal(new[] { "b.cs" }, milestone.WorkItems["11"].WritePaths);
        Assert.True(milestone.QaReserved);
        Assert.Contains("방향키", milestone.QaInstructions);
        Assert.Contains("검토", milestone.HighInstructions);
        Assert.Empty(milestone.Resources);
        Assert.DoesNotContain("git", MilestoneDefinitionContract.BuildWorkContext(
            milestone.WorkItems["10"]));
    }

    [Fact]
    public void CompactHq_WebBridgeExtractsCompletionMarkerBeforeParsing()
    {
        const string key = "1234ABCDabcde";
        var rawWebResponse = WebCorrelationContract.Marker(key) + "\n" + Minimal;

        // The completion marker is validated by the Web transport, then
        // removed from the message before the Worker parser sees it.
        Assert.True(WebCorrelationContract.TryExtractResponse(
            rawWebResponse, key, out var extracted));
        Assert.DoesNotContain(WebCorrelationContract.ResponseOkMarker, extracted);

        var parsed = HqTextProtocol.Parse(
            extracted, completionValidatedByTransport: true);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone,
            out var error), error);
        Assert.Equal("10", milestone!.WorkItems["10"].Id);

        // Non-Web CLI responses must still provide the marker themselves.
        var unvalidated = HqTextProtocol.Parse(extracted);
        Assert.False(unvalidated.IsValid);
        Assert.Contains("COMPACT_RESPONSE_TERMINATOR_INVALID",
            unvalidated.Errors);
    }

    [Fact]
    public void CompactHq_RejectsTrailingContentEvenIfWebValidated()
    {
        var invalid = HqTextProtocol.Parse(
            Minimal + "\nSURPRISE_TRAILING_TEXT",
            completionValidatedByTransport: true);
        Assert.False(invalid.IsValid);
        Assert.Contains("COMPACT_RESPONSE_TERMINATOR_INVALID",
            invalid.Errors);
    }

    [Fact]
    public void CompactHq_AllowsColonsUrlsAndDrivePathsInNaturalLanguage()
    {
        var body = Minimal.Replace(
            "a.txt를 생성하여 설정 읽기를 구현하라.",
            "Electron file://에서 base: './'를 적용하라.\n" +
            "https://example.org/api 와 C:\\Project\\src\\main.ts:1을 확인하라.",
            StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(body);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse,
            out var milestone, out var error), error);
        Assert.Equal(new[] { "a.txt" }, milestone!.WorkItems["10"].WritePaths);
        Assert.Contains("file://", MilestoneDefinitionContract.BuildWorkContext(milestone.WorkItems["10"]));
        Assert.Contains("base: './'", MilestoneDefinitionContract.BuildWorkContext(milestone.WorkItems["10"]));
        Assert.Contains("https://example.org/api", MilestoneDefinitionContract.BuildWorkContext(milestone.WorkItems["10"]));
    }

    [Fact]
    public void CompactHq_ParsesMultiplePathTagsWithoutMixingWithInstructions()
    {
        var body = Minimal.Replace(
            "<PATH>a.txt</>",
            "<PATH>a.txt</>\n<PATH>src/main.tsx</>",
            StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(body);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse,
            out var milestone, out var error), error);
        Assert.Equal(
            new[] { "a.txt", "src/main.tsx" },
            milestone!.WorkItems["10"].WritePaths);
        Assert.DoesNotContain("<PATH>", MilestoneDefinitionContract.BuildWorkContext(milestone.WorkItems["10"]));
    }

    [Theory]
    [InlineData("<PATH></>")]
    [InlineData("<PATH>a.txt")]
    [InlineData("<PATH>a.txt</PATH>")]
    [InlineData("<PATH>a.txt</><PATH>b.cs</>")]
    [InlineData("<PATH>a.txt</> trailing")]
    public void CompactHq_RejectsMalformedPathTags(string tag)
    {
        var parsed = HqTextProtocol.Parse(Minimal.Replace(
            "<PATH>a.txt</>", tag, StringComparison.Ordinal));
        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, error => error.Contains(
            "COMPACT_PATH_TAG", StringComparison.Ordinal));
    }

    [Fact]
    public void CompactHq_RejectsLegacyPathField()
    {
        var parsed = HqTextProtocol.Parse(Minimal.Replace(
            "<PATH>a.txt</>", "PATH: a.txt", StringComparison.Ordinal));
        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, error => error.Contains(
            "COMPACT_LEGACY_PATH", StringComparison.Ordinal));
    }

    [Fact]
    public void CompactHq_RejectsMissingPathOrQa()
    {
        var missingPath = HqTextProtocol.Parse(Minimal.Replace(
            "<PATH>a.txt</>", "지시만 있고 경로는 없음", StringComparison.Ordinal));
        Assert.False(missingPath.IsValid);

        var missingQa = HqTextProtocol.Parse(Minimal.Replace(
            "@@QA", "@@MISSING_QA", StringComparison.Ordinal));
        Assert.False(missingQa.IsValid);
    }

    [Fact]
    public void CompactHq_LimitsWorkInstructions()
    {
        var verbose = HqTextProtocol.Parse(Minimal.Replace(
            "a.txt를 생성하여 설정 읽기를 구현하라.",
            new string('가', 601), StringComparison.Ordinal));
        Assert.False(verbose.IsValid);
        Assert.Contains(verbose.Errors, error => error.Contains(
            "COMPACT_LIMIT", StringComparison.Ordinal));
    }

    [Fact]
    public void DesignDocument_OnlyAppearsInFirstPlanSection()
    {
        var text = Minimal.Replace("@@WORK=10",
            "@@PLAN\n전체 설계, 마일스톤, 완료 기준.\n\n@@WORK=10",
            StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(text);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone,
            out _));
        Assert.Contains("전체 설계", milestone!.PlanDocument);
    }

    [Fact]
    public void HistoryCardKey_SeparatesInstructionProgressAndResponse()
    {
        var method = typeof(MainWindow).GetMethod(
            "HistoryCardKey", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        object? Key(string kind, string title) => method!.Invoke(null,
            new object?[] { new MainWindow.WorkerHistoryEvent(
                DateTimeOffset.UtcNow, "Implementer", kind, title,
                "message", null, null, null, null, "M1:10") });
        Assert.NotEqual(Key("DATA_FLOW", "전달 데이터"),
            Key("ROLE_RESPONSE", "응답 데이터"));
        Assert.NotEqual(Key("DATA_FLOW", "Worker 분배"),
            Key("DATA_FLOW", "전달 데이터"));
        Assert.Equal(Key("ROLE_PROGRESS", "작업 진행"),
            Key("ROLE_PROGRESS", "작업 진행"));
    }
}
