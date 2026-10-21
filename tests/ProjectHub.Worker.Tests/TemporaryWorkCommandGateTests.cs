using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class TemporaryWorkCommandGateTests
{
    [Fact]
    public void TemporaryWorkHook_AllowsBuildTestsAndKeepsGitMutationsBlocked()
    {
        var script = BuildExecutionPolicy.CreateTemporaryWorkCodexPreToolHookScript();

        Assert.Contains("git", script);
        Assert.Contains("commit|push", script);
        Assert.DoesNotContain("dotnet", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("QA", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewerHook_DoesNotReferToRemovedQaRole()
    {
        Assert.DoesNotContain("QA",
            BuildExecutionPolicy.CreateReviewCodexPreToolHookScript(),
            StringComparison.OrdinalIgnoreCase);
    }
}
