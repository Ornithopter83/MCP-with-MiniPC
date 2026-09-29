using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ProjectHubExitCleanupTests
{
    [Fact]
    public void ImmediateCleanupDeletesEntireProjectHubRootButKeepsWorkspace()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubExitCleanupTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var projectHub = Path.Combine(workspace, ".projecthub");
        var runtime = Path.Combine(projectHub, "runtime", "integration-clones", "job", "W10");

        Directory.CreateDirectory(runtime);
        File.WriteAllText(Path.Combine(projectHub, "session-state.json"), "{}");
        File.WriteAllText(Path.Combine(runtime, "artifact.tmp"), "temp");

        try
        {
            Assert.True(
                ProjectHubExitCleanup.TryDeleteWorkspaceProjectHubRoot(
                    workspace,
                    out var error),
                error);
            Assert.True(Directory.Exists(workspace));
            Assert.False(Directory.Exists(projectHub));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectHubRootIsAlwaysWorkspaceLocal()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubExitCleanupTests",
            Guid.NewGuid().ToString("N"),
            "workspace");
        Directory.CreateDirectory(workspace);

        try
        {
            Assert.Equal(
                Path.Combine(Path.GetFullPath(workspace), ".projecthub"),
                ProjectHubExitCleanup.GetProjectHubRoot(workspace));
        }
        finally
        {
            var parent = Directory.GetParent(workspace)?.Parent?.FullName;
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
    }
}
