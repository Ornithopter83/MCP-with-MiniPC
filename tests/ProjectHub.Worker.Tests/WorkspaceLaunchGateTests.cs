using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkspaceLaunchGateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingWorkspaceIsRejectedSynchronously(string? path)
        => Assert.Equal(
            "WORKSPACE_NOT_SELECTED",
            WorkspaceLaunchGate.Validate(path));

    [Fact]
    public void NonexistentWorkspaceIsRejectedSynchronously()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "projecthub-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(
            "WORKSPACE_NOT_FOUND",
            WorkspaceLaunchGate.Validate(path));
    }

    [Fact]
    public void ExistingWorkspacePassesWithoutAnyAsyncDependency()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "projecthub-workspace-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        try
        {
            Assert.Null(WorkspaceLaunchGate.Validate(path));
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }
}
