using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerSettingsMigrationTests
{
    private sealed class Layout : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "worker-settings-migration-" + Guid.NewGuid().ToString("N"));
        public string Canonical => Path.Combine(Root, "Profile", "ProjectHub", "Worker", "config");
        public string LegacyRelease => Path.Combine(Root, "Worker", "Worker", "config");
        public string LegacyBuild => Path.Combine(Root, "Repository", "bin", "Worker", "config");

        public void Write(string folder, string file, string data)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, file), data);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void SettingsPathUsesWindowsUserLocalAppDataInsteadOfExeDirectory()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProjectHub", "Worker");
        Assert.Equal(expected, WorkerPaths.Root);
        Assert.Equal(Path.Combine(expected, "config"), WorkerPaths.Config);
        Assert.Equal(Path.Combine(expected, "logs"), WorkerPaths.Logs);
        Assert.Equal(Path.Combine(expected, "Task"), WorkerPaths.Task);
    }

    [Fact]
    public void MigrationImportsEachExistingSettingWithoutRequiringAnEditor()
    {
        using var f = new Layout();
        f.Write(f.LegacyRelease, "target-settings.json",
            """{"manualWorkingDirectory":"C:\\UnityProjects\\MyGame","maxConcurrentWork":7}""");
        f.Write(f.LegacyRelease, "worker-selection.json",
            """{"sessionId":"abc","projectPath":"C:\\UnityProjects\\MyGame"}""");
        f.Write(f.LegacyBuild, "window-placement.json",
            """{"left":320,"top":180}""");

        var imported = WorkerSettingsMigration.Migrate(
            f.Canonical, new[] { f.LegacyBuild, f.LegacyRelease });
        Assert.Equal(3, imported.Count);
        Assert.Contains("target-settings.json", imported);
        Assert.Contains("window-placement.json", imported);
        Assert.Contains("worker-selection.json", imported);
        Assert.Contains("""maxConcurrentWork":7""",
            File.ReadAllText(Path.Combine(f.Canonical, "target-settings.json")));
        Assert.True(File.Exists(Path.Combine(f.LegacyRelease, "target-settings.json")));
        Assert.True(File.Exists(Path.Combine(f.LegacyBuild, "window-placement.json")));
        Assert.Empty(Directory.EnumerateFiles(f.Canonical, "*.tmp"));
    }

    [Fact]
    public void ExistingCanonicalSettingsAlwaysWinEvenIfLegacyIsNewer()
    {
        using var f = new Layout();
        f.Write(f.Canonical, "target-settings.json", """{"maxConcurrentWork":9}""");
        f.Write(f.LegacyRelease, "target-settings.json", """{"maxConcurrentWork":2}""");
        var imported = WorkerSettingsMigration.Migrate(
            f.Canonical, new[] { f.LegacyRelease });

        Assert.Empty(imported);
        Assert.Contains("maxConcurrentWork\":9",
            File.ReadAllText(Path.Combine(f.Canonical, "target-settings.json")));
        Assert.Empty(WorkerSettingsMigration.Migrate(f.Canonical,
            new[] { f.LegacyRelease }));
    }

    [Fact]
    public void NewestValidSourceWinsAndInvalidLegacyJsonIsNeverCopied()
    {
        using var f = new Layout();
        f.Write(f.LegacyBuild, "target-settings.json", """{"maxConcurrentWork":11}""");
        f.Write(f.LegacyRelease, "target-settings.json", "invalid json");
        File.SetLastWriteTimeUtc(Path.Combine(f.LegacyBuild, "target-settings.json"),
            DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(Path.Combine(f.LegacyRelease, "target-settings.json"),
            DateTime.UtcNow.AddHours(-1));

        var imported = WorkerSettingsMigration.Migrate(f.Canonical,
            new[] { f.LegacyBuild, f.LegacyRelease });
        Assert.Single(imported);
        Assert.Equal("""{"maxConcurrentWork":11}""",
            File.ReadAllText(Path.Combine(f.Canonical, "target-settings.json")));
    }

    [Fact]
    public void KnownBuildAndDeploymentLayoutIsDiscovered()
    {
        using var f = new Layout();
        var repository = Path.Combine(f.Root, "Repository");
        var projectFile = Path.Combine(repository, "src", "ProjectHub.Worker",
            "ProjectHub.Worker.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectFile)!);
        File.WriteAllText(projectFile, "<Project />");
        var buildExeDirectory = Path.Combine(repository, "bin");
        Directory.CreateDirectory(buildExeDirectory);
        var deploymentExeDirectory = Path.Combine(f.Root, "Worker");
        Directory.CreateDirectory(deploymentExeDirectory);
        var expectedBuildLegacy = Path.Combine(buildExeDirectory, "Worker", "config");
        var expectedDeploymentLegacy = Path.Combine(deploymentExeDirectory,
            "Worker", "config");

        var fromBuild = WorkerSettingsMigration
            .FindLegacyConfigDirectories(buildExeDirectory);
        var fromDeployment = WorkerSettingsMigration
            .FindLegacyConfigDirectories(deploymentExeDirectory);
        Assert.Contains(expectedBuildLegacy, fromBuild);
        Assert.Contains(expectedDeploymentLegacy, fromBuild);
        Assert.Contains(expectedBuildLegacy, fromDeployment);
        Assert.Contains(expectedDeploymentLegacy, fromDeployment);
    }
}
