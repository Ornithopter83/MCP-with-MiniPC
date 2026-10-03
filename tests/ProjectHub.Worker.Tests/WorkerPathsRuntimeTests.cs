using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerPathsRuntimeTests
{
    [Fact]
    public void RepositoryRuntimeLivesOutsideProjectRoot()
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
            var workspaceRoot = Path.GetFullPath(workspace)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var runtimeRoot = Path.GetFullPath(runtime.Root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            Assert.False(
                string.Equals(runtimeRoot, workspaceRoot, comparison) ||
                runtimeRoot.StartsWith(
                    workspaceRoot + Path.DirectorySeparatorChar,
                    comparison));
            Assert.StartsWith(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ProjectHub",
                    "RepositoryRuntime"),
                runtime.Root,
                comparison);
            Assert.Equal(
                Path.Combine(workspaceRoot, ".projecthub", "runtime"),
                WorkerPaths.GetLegacyRepositoryRuntimeRoot(workspace));
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
            var publishRoot = WorkerPaths.GetPublishedArtifactDirectory(
                workspace,
                "job-alpha",
                12);

            Assert.StartsWith(runtime.Worktrees, worktree, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(runtime.IntegrationClones, integration, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(runtime.TempRoot, workTemp, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(
                WorkerPaths.PublishedArtifactsRoot,
                publishRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Assert.False(
                Path.GetFullPath(publishRoot).StartsWith(
                    Path.GetFullPath(runtime.Root) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

            WorkerPaths.EnsureWorkToolDirectories(runtime, workTemp);
            var environment = WorkerPaths.BuildWorkToolEnvironment(runtime, workTemp);
            Assert.Equal(Path.Combine(workTemp, "build"), environment["PROJECTHUB_BUILD_ROOT"]);
            Assert.Equal(Path.Combine(workTemp, "build", "bin"), environment["PROJECTHUB_BUILD_BIN"]);
            Assert.Equal(Path.Combine(workTemp, "build", "obj"), environment["PROJECTHUB_BUILD_OBJ"]);
            Assert.True(Directory.Exists(environment["PROJECTHUB_BUILD_BIN"]));
            Assert.True(Directory.Exists(environment["PROJECTHUB_BUILD_OBJ"]));
            Assert.Equal(Path.Combine(workTemp, "appdata"), environment["APPDATA"]);
            Assert.Equal(Path.Combine(workTemp, "localappdata"), environment["LOCALAPPDATA"]);
            Assert.Equal("1", environment["DOTNET_CLI_TELEMETRY_OPTOUT"]);
            Assert.True(File.Exists(Path.Combine(environment["APPDATA"], "NuGet", "NuGet.Config")));
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }
}
