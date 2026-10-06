using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerPathsRuntimeTests
{
    [Fact]
    public void RepositoryRuntimeUsesProjectTempOnly()
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
            var expectedTemp = Path.Combine(
                Path.GetFullPath(workspace),
                "temp");
            var expectedRuntime = Path.Combine(
                expectedTemp,
                "ProjectHub");

            Assert.Equal(
                Path.GetFullPath(expectedTemp),
                Path.GetFullPath(runtime.TempRoot));
            Assert.Equal(
                Path.GetFullPath(expectedRuntime),
                Path.GetFullPath(runtime.Root));
            Assert.DoesNotContain(
                ".projecthub",
                runtime.Root,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void GitIgnoreIncludesBinAndTempWithoutProjectHubRule()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubWorkerPathsTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "SampleProject");
        Directory.CreateDirectory(workspace);

        try
        {
            var gitIgnore = Path.Combine(workspace, ".gitignore");
            File.WriteAllText(gitIgnore, "obj/\n");

            WorkerPaths.EnsureProjectHubGitIgnore(workspace);
            WorkerPaths.EnsureProjectHubGitIgnore(workspace);

            var lines = File.ReadAllLines(gitIgnore)
                .Select(line => line.Trim())
                .ToArray();

            Assert.Single(lines.Where(line => line == "bin/"));
            Assert.Single(lines.Where(line => line == "temp/"));
            Assert.DoesNotContain(".projecthub/", lines);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void GitIgnoreNeedDetection_AcceptsRecursiveBinAndTempRules()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubWorkerPathsTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "SampleProject");
        Directory.CreateDirectory(workspace);

        try
        {
            File.WriteAllText(
                Path.Combine(workspace, ".gitignore"),
                "**/bin/\n**/temp/\n");

            Assert.False(
                WorkerPaths.NeedsProjectHubGitIgnoreUpdate(workspace));
            Assert.False(
                WorkerPaths.EnsureProjectHubGitIgnore(workspace));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void GitIgnoreNeedDetection_ReportsMissingTempRule()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubWorkerPathsTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "SampleProject");
        Directory.CreateDirectory(workspace);

        try
        {
            File.WriteAllText(
                Path.Combine(workspace, ".gitignore"),
                "bin/\n");

            Assert.True(
                WorkerPaths.NeedsProjectHubGitIgnoreUpdate(workspace));
            Assert.True(
                WorkerPaths.EnsureProjectHubGitIgnore(workspace));
            var lines = File.ReadAllLines(
                Path.Combine(workspace, ".gitignore"));

            Assert.Contains("bin/", lines);
            Assert.Contains("temp/", lines);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void ResourceStagingUsesTempResource()
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
            var root = WorkerPaths.BuildResourceStagingRoot(
                runtime,
                "IMAGE");
            var directory = WorkerPaths.BuildResourceStagingDirectory(
                runtime,
                "IMAGE",
                "abc123");

            Assert.Equal(
                Path.Combine(workspace, "temp", "Resource"),
                root);
            Assert.Equal(
                Path.Combine(workspace, "temp", "Resource", "abc123"),
                directory);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void ProjectStateLivesUnderProjectRootProjectHub()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubWorkerPathsTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "SampleProject");
        Directory.CreateDirectory(workspace);

        try
        {
            var stateRoot = ProjectWorkspacePersistence.RootDirectory(workspace);

            Assert.Equal(
                Path.Combine(
                    Path.GetFullPath(workspace),
                    ".projecthub"),
                stateRoot);
            Assert.EndsWith(
                ".projecthub",
                stateRoot,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void ProjectExecutionLogsSurviveProjectTempReset()
    {
        WorkerPaths.EnsureCreated();
        var parent = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubWorkerPathsTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "SampleProject");
        Directory.CreateDirectory(workspace);

        var eventDirectory = ProjectWorkspacePersistence.EventDirectory(workspace);
        var transcriptDirectory = ProjectWorkspacePersistence.TranscriptDirectory(workspace);
        try
        {
            var eventId = ProjectWorkspacePersistence.AppendEvent(
                workspace,
                "job-1",
                DateTimeOffset.Now,
                "HQ RESPONSE",
                "response",
                "RECEIVED");
            Assert.False(string.IsNullOrWhiteSpace(eventId));

            var transcript = ProjectWorkspacePersistence.CommandTranscriptPath(
                workspace,
                DateTimeOffset.Now);
            Assert.True(ProjectWorkspacePersistence.InitializeCommandTranscript(
                transcript,
                "SampleProject",
                "NewThread",
                DateTimeOffset.Now));
            Assert.True(ProjectWorkspacePersistence.AppendCommandTranscript(
                transcript,
                DateTimeOffset.Now,
                "HQ RESPONSE",
                "response"));

            Assert.True(File.Exists(
                ProjectWorkspacePersistence.EventLogPath(workspace, "job-1")));
            Assert.True(File.Exists(transcript));

            Assert.True(
                WorkerPaths.TryResetProjectTemp(workspace, out var error),
                error);

            Assert.True(File.Exists(
                ProjectWorkspacePersistence.EventLogPath(workspace, "job-1")));
            Assert.True(File.Exists(transcript));
            Assert.StartsWith(
                Path.GetFullPath(ProjectWorkspacePersistence.RootDirectory(workspace)),
                Path.GetFullPath(eventDirectory),
                StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(
                Path.GetFullPath(ProjectWorkspacePersistence.RootDirectory(workspace)),
                Path.GetFullPath(transcriptDirectory),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void ProjectTempResetRemovesPreviousMilestoneState()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubWorkerPathsTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "SampleProject");
        var stale = Path.Combine(
            workspace,
            "temp",
            "ProjectHub",
            "state",
            "old.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "old");

        try
        {
            Assert.True(
                WorkerPaths.TryResetProjectTemp(
                    workspace,
                    out var error),
                error);
            Assert.True(Directory.Exists(
                Path.Combine(workspace, "temp")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(
                Path.Combine(workspace, "temp")));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void ProjectHubRuntimeUsesLocalGitExcludeWithoutChangingGitIgnore()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubWorkerPathsTests",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "SampleProject");
        var gitInfo = Path.Combine(workspace, ".git", "info");
        Directory.CreateDirectory(gitInfo);
        File.WriteAllText(
            Path.Combine(workspace, ".gitignore"),
            "bin/\n");

        try
        {
            Assert.True(WorkerPaths.EnsureProjectHubLocalExclude(workspace));
            Assert.True(WorkerPaths.EnsureProjectHubLocalExclude(workspace));

            var excludeLines = File.ReadAllLines(
                Path.Combine(gitInfo, "exclude"));
            Assert.Single(excludeLines.Where(line =>
                line.Trim() == ".projecthub/"));
            Assert.DoesNotContain(
                ".projecthub/",
                File.ReadAllLines(Path.Combine(workspace, ".gitignore")));
        }
        finally
        {
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
        Assert.Empty(Directory.EnumerateFileSystemEntries(
            WorkerPaths.Attachments));
        Assert.Empty(Directory.EnumerateFileSystemEntries(
            WorkerPaths.WebResults));
        Assert.Empty(Directory.EnumerateFileSystemEntries(
            WorkerPaths.Task));
    }
}
