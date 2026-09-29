using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerPathsRuntimeTests
{
    [Fact]
    public void RepositoryRuntimeLivesOnlyInsideProjectRoot()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubWorkerPathsTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "SampleProject");
        Directory.CreateDirectory(workspace);

        try
        {
            var runtime = WorkerPaths.GetRepositoryRuntimePaths(workspace);
            var expectedRoot = Path.Combine(
                Path.GetFullPath(workspace),
                ".projecthub",
                "runtime");
            var legacySibling = Path.Combine(
                Path.GetFullPath(parent),
                "SampleProject.projecthub");

            Assert.Equal(expectedRoot, runtime.Root);
            Assert.StartsWith(
                Path.GetFullPath(workspace) + Path.DirectorySeparatorChar,
                runtime.Root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(
                Path.GetFullPath(legacySibling),
                Path.GetFullPath(runtime.Root));
            Assert.StartsWith(runtime.Root, runtime.Worktrees, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(runtime.Root, runtime.IntegrationClones, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(runtime.Root, runtime.NuGetRoot, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(runtime.Root, runtime.DotNetHome, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(runtime.Root, runtime.TempRoot, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void GlobalEphemeralDirectoriesAreRecreatedEmpty()
    {
        WorkerPaths.EnsureCreated();
        var attachment = Path.Combine(
            WorkerPaths.Attachments,
            "test-" + Guid.NewGuid().ToString("N") + ".tmp");
        var result = Path.Combine(
            WorkerPaths.WebResults,
            "test-" + Guid.NewGuid().ToString("N") + ".tmp");
        var task = Path.Combine(
            WorkerPaths.Task,
            "test-" + Guid.NewGuid().ToString("N") + ".tmp");

        File.WriteAllText(attachment, "attachment");
        File.WriteAllText(result, "result");
        File.WriteAllText(task, "task");

        Assert.True(
            WorkerPaths.TryResetEphemeralDirectories(out var error),
            error);
        Assert.True(Directory.Exists(WorkerPaths.Attachments));
        Assert.True(Directory.Exists(WorkerPaths.WebResults));
        Assert.True(Directory.Exists(WorkerPaths.Task));
        Assert.Empty(Directory.EnumerateFileSystemEntries(WorkerPaths.Attachments));
        Assert.Empty(Directory.EnumerateFileSystemEntries(WorkerPaths.WebResults));
        Assert.Empty(Directory.EnumerateFileSystemEntries(WorkerPaths.Task));
    }

    [Fact]
    public void WorkItemRuntimePathsStayUnderInternalRuntime()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubWorkerPathsTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "SampleProject");
        Directory.CreateDirectory(workspace);

        try
        {
            var runtime = WorkerPaths.GetRepositoryRuntimePaths(workspace);
            var worktree = GitWorktreeManager.BuildWorktreePath(
                workspace,
                "job-alpha",
                "W10");
            var integration = GitWorktreeManager.BuildIntegrationClonePath(
                workspace,
                "job-alpha",
                "W10");
            var workTemp = WorkerPaths.BuildWorkTempPath(
                runtime,
                "job-alpha",
                "W10");

            Assert.StartsWith(runtime.Worktrees, worktree, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(runtime.IntegrationClones, integration, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(runtime.TempRoot, workTemp, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }
}
