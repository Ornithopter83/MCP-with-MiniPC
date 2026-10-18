using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityWorkerContractAlignmentTests
{
    private static MilestoneWorkDefinition Work(string id, bool dependsOnDesign) =>
        new(id, new[] { "docs/projecthub/" + id + ".md" }, false, true,
            "{}", "{}") { DependsOnDesign = dependsOnDesign };

    [Fact]
    public void DesignFailureBlocksOnlyExplicitlyDependentWork()
    {
        var tasks = new[] { Work("119", false), Work("120", true) };
        Assert.Equal(new[] { "120" },
            UnityDesignDependencyPolicy.BlockedWorkIds(tasks, false));
        Assert.Empty(UnityDesignDependencyPolicy.BlockedWorkIds(tasks, true));
    }

    [Theory]
    [InlineData("docs/projecthub/m119-high-evidence", "docs/projecthub/m119-high-evidence/audit.md", true)]
    [InlineData("docs/projecthub/m119-high-evidence/", "docs/projecthub/m119-high-evidence/audit.md", true)]
    [InlineData("docs/projecthub/m120-visual-evidence", "docs/projecthub/m120-visual-evidence/native-question.png", true)]
    [InlineData("docs/projecthub/m119-high-evidence/audit.md", "docs/projecthub/m119-high-evidence/audit.md", true)]
    [InlineData("docs/projecthub/m119-high-evidence/audit.md", "docs/projecthub/m119-high-evidence/audit.md/extra", false)]
    [InlineData("docs/projecthub/m119-high-evidence", "docs/projecthub/m119-high-evidence2/audit.md", false)]
    [InlineData("docs/projecthub/m119-high-evidence", "docs/projecthub/m120-high-evidence/audit.md", false)]
    public void DelegatedScopeMatchesOnlyExactFileOrNarrowSubtree(
        string scope, string change, bool expected) =>
        Assert.Equal(expected, MainWindow.MatchesMcpScope(scope, change));

    private const string Plan = """
        [ACTION=WORK]
        @@MILESTONE
        <ID>M119</>
        @@DESIGN=0
        <PATH>docs/projecthub/m119-plan.md</>
        <INSTRUCTIONS>Visual assets.</>
        @@WORK=119
        <PATH>Builds/QuizStandalone/</>
        <DEPENDS_ON_DESIGN>true</>
        <INSTRUCTIONS>Build after visual design completion.</>
        @@WORK=120
        <PATH>docs/projecthub/inspection.md</>
        <DEPENDS_ON_DESIGN>false</>
        <INSTRUCTIONS>Independent read-only inspection.</>
        @@HIGH
        <INSTRUCTIONS>Inspect actual evidence.</>
        [RESPONSE=OK]
        """;

    [Fact]
    public void HqParsesExplicitDesignDependencyWithoutBlockingIndependentWork()
    {
        var parsed = HqTextProtocol.Parse(Plan);
        Assert.True(parsed.IsValid, string.Join(";", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(parsed.CompatibilityMessage,
            parsed.Parse, out var milestone, out var error), error);
        Assert.True(milestone!.WorkItems["119"].DependsOnDesign);
        Assert.False(milestone.WorkItems["120"].DependsOnDesign);
    }

    [Fact]
    public void HqPreservesLegacyInstructionAfterDesignAsDependency()
    {
        var legacy = Plan.Replace("<DEPENDS_ON_DESIGN>true</>\n", "",
                StringComparison.Ordinal)
            .Replace("Build after visual design completion.",
                "DESIGN 종료 후 Windows Player 빌드.", StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(legacy);
        Assert.True(parsed.IsValid, string.Join(";", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(parsed.CompatibilityMessage,
            parsed.Parse, out var milestone, out var error), error);
        Assert.True(milestone!.WorkItems["119"].DependsOnDesign);
    }
}
