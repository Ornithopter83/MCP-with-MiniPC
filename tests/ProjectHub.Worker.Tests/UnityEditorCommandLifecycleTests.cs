using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorCommandLifecycleTests
{
    private sealed class Project : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(),
            "unity-editor-gate-" + Guid.NewGuid().ToString("N"));
        internal Project()
        {
            Directory.CreateDirectory(Path.Combine(Root, "Assets"));
            Directory.CreateDirectory(Path.Combine(Root, "Packages"));
            Directory.CreateDirectory(Path.Combine(Root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(Root, "Packages", "manifest.json"),
                """{"dependencies":{"com.unity.pipeline":"0.8.0-exp.1"}}""");
            File.WriteAllText(Path.Combine(Root, "ProjectSettings", "ProjectVersion.txt"),
                "m_EditorVersion: 6000.6.5f1");
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    [Theory]
    [InlineData(true, false, "UNITY_EDITOR_CONNECTED")]
    [InlineData(false, true, "UNITY_EDITOR_RUNNING_PIPELINE_UNAVAILABLE")]
    [InlineData(false, false, "UNITY_EDITOR_UNAVAILABLE_BACKGROUND_ONLY")]
    public async Task CommandGateChecksConnectionWithoutLaunchingEditor(
        bool connected, bool locked, string expected)
    {
        using var project = new Project();
        var probes = 0;
        var actual = await UnityEditorCommandLifecycle.EnsureForAcceptedCommandAsync(
            project.Root, CancellationToken.None,
            (_, _) =>
            {
                probes++;
                return Task.FromResult(connected
                    ? new UnityToolchainPreflight.Result(true, "UNITY_EDITOR_CONNECTED", "ready")
                    : new UnityToolchainPreflight.Result(false,
                        "UNITY_EDITOR_UNAVAILABLE", "No Pipeline instance found"));
            },
            _ => locked);
        Assert.Equal(expected, actual.Code);
        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task UnrelatedToolErrorsNeverTriggerGuiRecovery()
    {
        using var project = new Project();
        var lockedChecks = 0;
        var result = await UnityEditorCommandLifecycle.EnsureForAcceptedCommandAsync(
            project.Root, CancellationToken.None,
            (_, _) => Task.FromResult(new UnityToolchainPreflight.Result(
                false, "UNITY_CLI_LAUNCH_FAILED", "unity executable missing")),
            _ => { lockedChecks++; return true; });
        Assert.Equal("UNITY_CLI_LAUNCH_FAILED", result.Code);
        Assert.Equal(0, lockedChecks);
    }

    [Fact]
    public async Task MissingProjectDoesNotRunEditorProbe()
    {
        var probes = 0;
        var result = await UnityEditorCommandLifecycle.EnsureForAcceptedCommandAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            CancellationToken.None,
            (_, _) =>
            {
                probes++;
                throw new InvalidOperationException("unexpected probe");
            },
            _ => false);
        Assert.Equal("UNITY_PROJECT_NOT_FOUND", result.Code);
        Assert.Equal(0, probes);
    }
}
