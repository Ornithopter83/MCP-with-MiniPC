using System.Collections.Concurrent;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityWorkSchedulingPolicyTests
{
    private static MilestoneWorkDefinition Work(
        string id, string path, string instructions = "") =>
        new(id, new[] { path }, false, true, instructions, instructions);

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(16, 3, 3)]
    [InlineData(32, 30, 30)]
    [InlineData(16, 0, 0)]
    public void IndependentWorkUsesConfiguredConcurrency(
        int configured, int runnableCount, int expected)
    {
        Assert.Equal(expected,
            UnityWorkSchedulingPolicy.MaxConcurrentWork(configured, runnableCount));
    }

    [Fact]
    public void InvalidConcurrencyIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UnityWorkSchedulingPolicy.MaxConcurrentWork(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UnityWorkSchedulingPolicy.MaxConcurrentWork(1, -1));
    }

    [Theory]
    [InlineData("Assets/Scripts/Player.cs", "", false)]
    [InlineData("Assets\\Scripts\\Camera.cs", "", false)]
    [InlineData("Assets/Scripts/Editor/SceneTool.cs", "", true)]
    [InlineData("Assets/Scenes/Level.unity", "", true)]
    [InlineData("Assets/Prefabs/Player.prefab", "", true)]
    [InlineData("Assets/Textures/Grass.png", "", true)]
    [InlineData("Assets/Scripts", "", true)]
    [InlineData("Packages/manifest.json", "", true)]
    [InlineData("Assets/Scripts/Player.cs", "Unity CLI로 컴파일 확인", true)]
    [InlineData("Assets/Scripts/Player.cs", "AssetDatabase.Refresh 사용", true)]
    public void OnlyEditorFreeSourcePathsUseParallelLane(
        string path, string instructions, bool requiresEditor)
    {
        Assert.Equal(requiresEditor,
            UnityWorkSchedulingPolicy.RequiresEditorLane(
                Work("80", path, instructions)));
    }

    [Fact]
    public async Task TwoIndependentSourcesRunWithOneEditorWhileSecondEditorWaits()
    {
        var workItems = new[]
        {
            Work("80", "Assets/Scripts/Player.cs"),
            Work("81", "Assets/Scripts/Camera.cs"),
            Work("82", "Assets/Scenes/Level.unity"),
            Work("83", "Assets/Prefabs/Hero.prefab")
        };
        var started = new ConcurrentQueue<string>();
        var threeStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var task = UnityWorkSchedulingPolicy.RunAsync(workItems, 3,
            async (work, _) =>
            {
                started.Enqueue(work.Id);
                if (started.Count == 3)
                    threeStarted.TrySetResult(true);
                await release.Task;
                return work.Id;
            }, CancellationToken.None);

        try
        {
            await threeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("80", started);
            Assert.Contains("81", started);
            Assert.Contains("82", started);
            Assert.DoesNotContain("83", started);
        }
        finally
        {
            release.TrySetResult(true);
        }

        var results = await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "80", "81", "82", "83" }, results);
    }

    [Fact]
    public async Task EditorWaiterDoesNotConsumeAWorkerSlot()
    {
        var workItems = new[]
        {
            Work("82", "Assets/Scenes/Level.unity"),
            Work("83", "Assets/Scenes/Next.unity"),
            Work("80", "Assets/Scripts/Player.cs"),
            Work("81", "Assets/Scripts/Camera.cs")
        };
        var started = new ConcurrentQueue<string>();
        var twoStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEditor = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fourthStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var task = UnityWorkSchedulingPolicy.RunAsync(workItems, 2,
            async (work, _) =>
            {
                started.Enqueue(work.Id);
                if (started.Count == 2)
                    twoStarted.TrySetResult(true);
                if (work.Id == "81")
                    fourthStarted.TrySetResult(true);
                if (work.Id == "82")
                    await releaseEditor.Task;
                if (work.Id == "80")
                    await releaseSource.Task;
                return work.Id;
            }, CancellationToken.None);
        try
        {
            await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(new[] { "82", "80" }, started.ToArray());
            releaseSource.TrySetResult(true);
            await fourthStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.DoesNotContain("83", started);
        }
        finally
        {
            releaseSource.TrySetResult(true);
            releaseEditor.TrySetResult(true);
        }

        Assert.Equal(4, (await task.WaitAsync(TimeSpan.FromSeconds(10))).Length);
    }

    [Fact]
    public void EmbeddedContractsDescribeUnityValidationBoundaries()
    {
        Assert.Contains("Unity HQ", RoleContractLoader.LoadHqFooter());
        Assert.Contains("Unity CLI/MCP", RoleContractLoader.LoadWorkFooter());
        Assert.Contains("PlayMode", RoleContractLoader.LoadQaFooter());
        Assert.Contains("GUID/.meta", RoleContractLoader.LoadHighFooter());
    }
}
