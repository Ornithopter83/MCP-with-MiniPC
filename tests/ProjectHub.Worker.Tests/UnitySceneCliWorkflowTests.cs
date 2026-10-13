using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnitySceneCliWorkflowTests
{
    [Theory]
    [InlineData("Assets/Scenes/Level.unity", true)]
    [InlineData("Assets\\Scenes\\Level.unity", true)]
    [InlineData("Assets/Scenes/../Scripts/Hack.unity", false)]
    [InlineData("/Assets/Scenes/Level.unity", false)]
    [InlineData("Assets/Scenes/Level.prefab", false)]
    [InlineData("Packages/scene.unity", false)]
    public void OnlyProjectAssetScenesCanBeOpened(string path, bool expected)
    {
        Assert.Equal(expected, UnitySceneCliWorkflow.TryNormalizeScenePath(path, out _));
    }

    [Fact]
    public async Task OpeningUsesConnectedEditorAndAdditiveSceneMode()
    {
        var calls = new List<IReadOnlyList<string>>();
        Task<UnityAssetCliResult> Fake(IReadOnlyList<string> args, CancellationToken _)
        {
            calls.Add(args);
            return Task.FromResult(new UnityAssetCliResult(0,
                """{"success":true,"data":{"result":"PROJECTHUB_SCENE_OPEN_OK|Assets/Scenes/Level.unity"}}""",
                ""));
        }
        var result = await UnitySceneCliWorkflow.OpenAsync(".", "Assets/Scenes/Level.unity",
            CancellationToken.None, Fake);
        Assert.True(result.Success, result.Detail);
        Assert.Single(calls);
        Assert.Equal("eval", calls[0][1]);
        Assert.Contains("OpenSceneMode.Additive", calls[0][2]);
        Assert.Contains("SetActiveScene", calls[0][2]);
        Assert.Contains("--project-path", calls[0]);
        Assert.DoesNotContain("-batchmode", calls[0]);
    }

    [Fact]
    public async Task SaveRequiresEditorResultInsteadOfEchoedCommand()
    {
        var source = UnitySceneCliWorkflow.SaveExpression("Assets/Scenes/Level.unity");
        Assert.Contains("EditorSceneManager.SaveScene", source);
        Assert.Contains("PROJECTHUB_SCENE_SAVED", source);

        Task<UnityAssetCliResult> Fake(IReadOnlyList<string> _, CancellationToken token) =>
            Task.FromResult(new UnityAssetCliResult(0,
                """{"success":true,"data":{"expression":"PROJECTHUB_SCENE_SAVED|Assets/Scenes/Level.unity"}}""",
                ""));
        var result = await UnitySceneCliWorkflow.SaveAsync(".", "Assets/Scenes/Level.unity",
            CancellationToken.None, Fake);
        Assert.False(result.Success);
        Assert.Contains("UNVERIFIED", result.Detail);
    }

    [Fact]
    public async Task SaveAcceptsExactEditorEvidenceAndNeverRetriesFailures()
    {
        var count = 0;
        Task<UnityAssetCliResult> Fake(IReadOnlyList<string> _, CancellationToken token)
        {
            count++;
            return Task.FromResult(new UnityAssetCliResult(0,
                """{"success":true,"data":{"value":"PROJECTHUB_SCENE_SAVED|Assets/Scenes/Level.unity"}}""",
                ""));
        }
        var done = await UnitySceneCliWorkflow.SaveAsync(".", "Assets/Scenes/Level.unity",
            CancellationToken.None, Fake);
        Assert.True(done.Success, done.Detail);
        Assert.Equal(1, count);

        Task<UnityAssetCliResult> Failure(IReadOnlyList<string> _, CancellationToken token)
        {
            count++;
            return Task.FromResult(new UnityAssetCliResult(6, "", "Main thread operation timed out after 5000ms"));
        }
        var failed = await UnitySceneCliWorkflow.SaveAsync(".", "Assets/Scenes/Level.unity",
            CancellationToken.None, Failure);
        Assert.False(failed.Success);
        Assert.Contains("UNCERTAIN", failed.Detail);
        Assert.Equal(2, count);
    }
}
