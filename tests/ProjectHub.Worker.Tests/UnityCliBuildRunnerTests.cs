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
    [InlineData("Assets/Quiz/QuizController.cs", true)]
    [InlineData("Assets/Tests/PlayMode/QuizNavigationTests.cs", true)]
    [InlineData("Assets/Quiz/QuizController.asmdef", true)]
    [InlineData("Assets/Quiz/QuizController.asmdef.meta", true)]
    [InlineData("Packages/manifest.json", true)]
    [InlineData("ProjectSettings/ProjectSettings.asset", true)]
    [InlineData("docs/projecthub/m90-report.md", false)]
    [InlineData("tools/projecthub/m90-runner.ps1", false)]
    [InlineData("Assets/Scenes/QuizScene.unity", false)]
    [InlineData("Assets/Quiz/UI/Menu.prefab", false)]
    [InlineData("Assets/Quiz/UI/Menu.prefab.meta", false)]
    [InlineData("Assets/ProjectHub/VisualFoundry/M90/Backdrop.png", false)]
    public void UnityRecompileOnlyForCompilerRelevantPaths(string path, bool shouldRecompile)
    {
        Assert.Equal(shouldRecompile, HighFinalBuildPolicy.RequiresBuild(
            new[] { path }));
    }

    [Fact]
    public async Task UnityDiagnosticOnlyMilestoneSkipsCompileWithoutEditor()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "unityworker-skip-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            Directory.CreateDirectory(Path.Combine(root, "Packages"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(root, "Packages", "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"),
                "m_EditorVersion: 6000.6.5f1");

            var skipped = await MilestoneBuildRunner.RunAsync(
                root, "diagnosis", Array.Empty<MilestoneWorkDefinition>(),
                CancellationToken.None, unityEditorReady: false,
                requiresUnityRecompile: false);
            Assert.True(Assert.Single(skipped).Success);
            Assert.Contains("BUILD_SKIPPED: NO_COMPILATION_AFFECTING_CHANGES",
                skipped[0].Summary);
            Assert.Empty(skipped[0].Command);
            Assert.DoesNotContain("BUILD_PASSED", skipped[0].Summary);

            var unavailable = await MilestoneBuildRunner.RunAsync(
                root, "code-change", Array.Empty<MilestoneWorkDefinition>(),
                CancellationToken.None, unityEditorReady: false,
                requiresUnityRecompile: true);
            Assert.False(Assert.Single(unavailable).Success);
            Assert.Contains("BUILD_UNVERIFIED", unavailable[0].Summary);
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
