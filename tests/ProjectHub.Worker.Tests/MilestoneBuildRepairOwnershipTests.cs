using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class MilestoneBuildRepairOwnershipTests
{
    private static MilestoneWorkDefinition Work(string id, string path) =>
        new(id, new[] { path }, false, false, "test", "raw");

    [Fact]
    public void SingleWorkUsesOriginalIdentity_NotFakeRepairWorker()
    {
        var items = new Dictionary<string, MilestoneWorkDefinition>
        {
            ["84"] = Work("84", "Assets/Tests/PlayMode/QuizVisualRegressionTests.cs")
        };
        Assert.Equal("84", MilestoneBuildRepairOwnership.Resolve(
            items, "Assets\\Tests\\PlayMode\\QuizVisualRegressionTests.cs(165): error CS0103"));
    }

    [Fact]
    public void SharedErrorWithMultipleOwnersRequiresHqDecision()
    {
        var items = new Dictionary<string, MilestoneWorkDefinition>
        {
            ["84"] = Work("84", "Assets/A.cs"),
            ["85"] = Work("85", "Assets/B.cs")
        };
        Assert.Null(MilestoneBuildRepairOwnership.Resolve(items, "error elsewhere"));
        Assert.Equal("85", MilestoneBuildRepairOwnership.Resolve(
            items, "Assets\\B.cs(2): error"));
    }
}
