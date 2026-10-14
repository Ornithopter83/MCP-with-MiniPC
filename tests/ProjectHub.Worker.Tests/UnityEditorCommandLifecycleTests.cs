using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorCommandLifecycleTests
{
    private sealed class UnityProject : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "unity-editor-command-" + Guid.NewGuid().ToString("N"));
        public UnityProject()
        {
            Directory.CreateDirectory(Path.Combine(Root, "Assets"));
            Directory.CreateDirectory(Path.Combine(Root, "Packages"));
            Directory.CreateDirectory(Path.Combine(Root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(Root, "Packages", "manifest.json"),
                """{"dependencies":{"com.unity.pipeline":"0.8.0-exp.1"}}""");
            File.WriteAllText(Path.Combine(Root, "ProjectSettings", "ProjectVersion.txt"),
                "m_EditorVersion: 6000.6.5f1");
        }
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private static UnityToolchainPreflight.Result Offline() =>
        new(false, "UNITY_EDITOR_UNAVAILABLE",
            "Unity CLI exit 6: No Pipeline instance found for project");

    private static UnityToolchainPreflight.Result Ready() =>
        new(true, "UNITY_EDITOR_CONNECTED", "ready");

    private static Task WaitInstantly(TimeSpan _, CancellationToken __) =>
        Task.CompletedTask;

    [Fact]
    public async Task SubmittedCommand_DoesNotAutoOpenForegroundEditor()
    {
        using var project = new UnityProject();
        var attempts = 0;
        var launches = 0;
        var state = await UnityEditorCommandLifecycle.EnsureForAcceptedCommandAsync(
            project.Root, CancellationToken.None,
            (_, _) => Task.FromResult(++attempts >= 4 ? Ready() : Offline()),
            (_, _) =>
            {
                launches++;
                return Task.FromResult(new UnityEditorCommandLifecycle.LaunchResult(
                    true, "unity open success"));
            },
            _ => false, WaitInstantly, 8);
        Assert.False(state.Ready);
        Assert.Equal("UNITY_EDITOR_UNAVAILABLE_BACKGROUND_ONLY", state.Code);
        Assert.Equal(0, launches);
        Assert.Equal(1, attempts);
        Assert.Equal(new[] { "open", Path.GetFullPath(project.Root) },
            UnityEditorCommandLifecycle.OpenArguments(project.Root));
    }

    [Fact]
    public async Task RunningEditorIsReusedWithoutOpeningAnotherInstance()
    {
        using var project = new UnityProject();
        var attempts = 0;
        var launches = 0;
        var state = await UnityEditorCommandLifecycle.EnsureForAcceptedCommandAsync(
            project.Root, CancellationToken.None,
            (_, _) => Task.FromResult(++attempts == 2 ? Ready() : Offline()),
            (_, _) =>
            {
                launches++;
                return Task.FromResult(new UnityEditorCommandLifecycle.LaunchResult(
                    true, "should not execute"));
            },
            _ => true, WaitInstantly, 5);
        Assert.False(state.Ready);
        Assert.Equal("UNITY_EDITOR_RUNNING_PIPELINE_UNAVAILABLE", state.Code);
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task ConnectedEditorDoesNotStartAnotherEditor()
    {
        using var project = new UnityProject();
        var launches = 0;
        var state = await UnityEditorCommandLifecycle.EnsureForAcceptedCommandAsync(
            project.Root, CancellationToken.None,
            (_, _) => Task.FromResult(Ready()),
            (_, _) =>
            {
                launches++;
                return Task.FromResult(new UnityEditorCommandLifecycle.LaunchResult(
                    true, "unreachable"));
            },
            _ => false, WaitInstantly, 5);
        Assert.True(state.Ready);
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task SafeModeOrNonMissingEditorErrorsDoNotAutoOpen()
    {
        using var project = new UnityProject();
        var launches = 0;
        var state = await UnityEditorCommandLifecycle.EnsureForAcceptedCommandAsync(
            project.Root, CancellationToken.None,
            (_, _) => Task.FromResult(new UnityToolchainPreflight.Result(
                false, "UNITY_EDITOR_UNAVAILABLE",
                "Unity CLI exit 7: Pipeline failed due to script compilation")),
            (_, _) =>
            {
                launches++;
                return Task.FromResult(new UnityEditorCommandLifecycle.LaunchResult(
                    true, "unreachable"));
            },
            _ => false, WaitInstantly, 5);
        Assert.False(state.Ready);
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task InvalidProjectDoesNotTryToLaunchUnity()
    {
        var launches = 0;
        var probes = 0;
        var state = await UnityEditorCommandLifecycle.EnsureForAcceptedCommandAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            CancellationToken.None,
            (_, _) =>
            {
                probes++;
                return Task.FromResult(Offline());
            },
            (_, _) =>
            {
                launches++;
                return Task.FromResult(new UnityEditorCommandLifecycle.LaunchResult(
                    true, "unreachable"));
            },
            _ => false, WaitInstantly, 5);
        Assert.Equal("UNITY_PROJECT_NOT_FOUND", state.Code);
        Assert.Equal(0, probes);
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task UnresponsiveLockedEditorIsNotDuplicated()
    {
        using var project = new UnityProject();
        var launches = 0;
        var state = await UnityEditorCommandLifecycle.EnsureForAcceptedCommandAsync(
            project.Root, CancellationToken.None,
            (_, _) => Task.FromResult(Offline()),
            (_, _) =>
            {
                launches++;
                return Task.FromResult(new UnityEditorCommandLifecycle.LaunchResult(
                    true, "unreachable"));
            },
            _ => true, WaitInstantly, 3);
        Assert.False(state.Ready);
        Assert.Equal("UNITY_EDITOR_RUNNING_PIPELINE_UNAVAILABLE", state.Code);
        Assert.Equal(0, launches);
    }

    [Fact]
    public void EditorConnectionMissingIsNotEquivalentToGenericCliFailure()
    {
        Assert.True(UnityEditorCommandLifecycle.CanAutoOpen(Offline()));
        Assert.False(UnityEditorCommandLifecycle.CanAutoOpen(
            new(false, "UNITY_CLI_LAUNCH_FAILED", "unity not installed")));
        Assert.False(UnityEditorCommandLifecycle.CanAutoOpen(
            new(false, "UNITY_EDITOR_UNAVAILABLE", "Editor Safe Mode")));
    }
}
