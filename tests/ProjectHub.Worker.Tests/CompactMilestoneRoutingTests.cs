using System.Reflection;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CompactMilestoneRoutingTests
{
    private const string Minimal = """
        [ACTION=WORK]
        MILESTONE: M1

        @@WORK=10
        PATH: a.txt
        a.txt를 생성하여 설정 읽기를 구현하라.

        @@WORK=11
        PATH: b.cs
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
    public void CompactHq_RejectsMissingPathOrQa()
    {
        var missingPath = HqTextProtocol.Parse(Minimal.Replace(
            "PATH: a.txt", "NOT_PATH: a.txt", StringComparison.Ordinal));
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
