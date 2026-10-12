using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityCliBuildRunnerTests
{
    [Fact]
    public void DetectsUnityProjectByRootManifests()
    {
        var root = Path.Combine(Path.GetTempPath(), "unityworker-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            Directory.CreateDirectory(Path.Combine(root, "Packages"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(root, "Packages", "manifest.json"), "{}");
            Assert.False(UnityCliBuildRunner.IsUnityProject(root));
            File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.6.5f1");
            Assert.True(UnityCliBuildRunner.IsUnityProject(root));
            Assert.Contains("--project-path", UnityCliBuildRunner.RecompileArguments(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    public void OnlyConfirmedCompilerErrorsCanTriggerCodeRepair(int exitCode, bool environment)
    {
        var report = new MilestoneMechanicalResult("BUILD", false, exitCode,
            "unity recompile --project-path C:/demo --format json", "build.log", "failure");
        Assert.Equal(environment, MilestoneBuildRunner.IsEnvironmentFailure(new[] { report }));
    }
}
