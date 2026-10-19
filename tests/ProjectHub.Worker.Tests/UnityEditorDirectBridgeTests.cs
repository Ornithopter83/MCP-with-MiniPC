using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorDirectBridgeTests
{
    private sealed class FakeProject : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "projecthub-bridge-test-" + Guid.NewGuid().ToString("N"));
        public string Backups { get; } = Path.Combine(Path.GetTempPath(),
            "projecthub-bridge-backups-test-" + Guid.NewGuid().ToString("N"));

        public FakeProject()
        {
            Directory.CreateDirectory(Path.Combine(Root, "Assets"));
            Directory.CreateDirectory(Path.Combine(Root, "Packages"));
            Directory.CreateDirectory(Path.Combine(Root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(Root, "Packages", "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(Root, "ProjectSettings",
                "ProjectVersion.txt"), "m_EditorVersion: 6000.0.0f1");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            var queue = UnityEditorDirectBridge.QueueDirectory(Root);
            if (Directory.Exists(queue)) Directory.Delete(queue, true);
            if (Directory.Exists(Backups)) Directory.Delete(Backups, true);
        }
    }

    [Fact]
    public void InstallationIsIdempotentAndSafelyUpgradesEditedManagedSource()
    {
        using var project = new FakeProject();
        var source = UnityEditorDirectBridge.EmbeddedSource();
        Assert.Contains("[InitializeOnLoad]", source);
        Assert.Contains("EditorApplication.update", source);
        Assert.Contains("BindingFlags.Static | BindingFlags.Public", source);
        Assert.Contains("BuildReport", source);
        Assert.Contains("REQUEST_EXPIRED_UNEXECUTED", source);
        Assert.Contains("scene_status", source);
        Assert.Contains("GetActiveScene()", source);
        Assert.Contains("ACTIVE_ROOTS=", source);
        Assert.Contains("LOADED_ROOTS=", source);
        Assert.Contains("COMPILING=", source);

        Assert.Contains("INSTALLED_WAIT",
            UnityEditorDirectBridge.Install(project.Root, project.Backups));
        var target = UnityEditorDirectBridge.EditorSourcePath(project.Root);
        Assert.Equal(source, File.ReadAllText(target));
        Assert.Contains("ALREADY_INSTALLED",
            UnityEditorDirectBridge.Install(project.Root, project.Backups));

        File.AppendAllText(target, "// USER EDIT\n");
        var altered = File.ReadAllBytes(target);
        var upgraded = UnityEditorDirectBridge.Install(project.Root, project.Backups);
        Assert.Contains("BACKED_UP_AND_UPGRADED", upgraded);
        Assert.Equal(source, File.ReadAllText(target));
        var backup = Assert.Single(Directory.GetFiles(project.Backups));
        Assert.Equal(altered, File.ReadAllBytes(backup));
        Assert.Contains(backup, upgraded);
        Assert.Contains("ALREADY_INSTALLED",
            UnityEditorDirectBridge.Install(project.Root, project.Backups));
        Assert.Single(Directory.GetFiles(project.Backups));
    }

    [Fact]
    public void ExistingLegacyReceiverWithWindowsLineEndingsUpgradesAndPreservesBytes()
    {
        using var project = new FakeProject();
        var target = UnityEditorDirectBridge.EditorSourcePath(project.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var previous = UnityEditorDirectBridge.LegacyEmbeddedSource()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
        File.WriteAllText(target, previous);
        Assert.DoesNotContain("PROJECTHUB_STARTUP_SIGNAL_IMPL_BEGIN", previous);
        var upgraded = UnityEditorDirectBridge.Install(project.Root, project.Backups);
        Assert.Contains("BACKED_UP_AND_UPGRADED", upgraded);
        Assert.Equal(UnityEditorDirectBridge.EmbeddedSource(), File.ReadAllText(target));
        Assert.Equal(previous, File.ReadAllText(
            Assert.Single(Directory.GetFiles(project.Backups))));
        Assert.Contains("PROJECTHUB_STARTUP_SIGNAL_IMPL_BEGIN",
            File.ReadAllText(target));
    }

    [Fact]
    public void CurrentReceiverWithCrLfIsValidWithoutRewriting()
    {
        using var project = new FakeProject();
        var target = UnityEditorDirectBridge.EditorSourcePath(project.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var current = UnityEditorDirectBridge.EmbeddedSource()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
        File.WriteAllText(target, current);
        Assert.True(UnityEditorDirectBridge.SameManagedSource(
            File.ReadAllText(target), UnityEditorDirectBridge.EmbeddedSource()));
        Assert.Contains("ALREADY_INSTALLED",
            UnityEditorDirectBridge.Install(project.Root, project.Backups));
        Assert.Equal(current, File.ReadAllText(target));
        Assert.False(Directory.Exists(project.Backups));
    }

    [Fact]
    public void ForeignBridgeSourceIsNeverOverwritten()
    {
        using var project = new FakeProject();
        var target = UnityEditorDirectBridge.EditorSourcePath(project.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        const string foreign = "// user-owned\npublic static class ProjectHubEditorBridge { }\n";
        File.WriteAllText(target, foreign);
        var error = Assert.Throws<InvalidOperationException>(() =>
            UnityEditorDirectBridge.Install(project.Root, project.Backups));
        Assert.Contains("EDITOR_BRIDGE_FOREIGN_SOURCE", error.Message);
        Assert.Equal(foreign, File.ReadAllText(target));
        Assert.False(Directory.Exists(project.Backups));
    }

    [Fact]
    public void BackupFailureNeverOverwritesOriginalManagedSource()
    {
        using var project = new FakeProject();
        var target = UnityEditorDirectBridge.EditorSourcePath(project.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var old = UnityEditorDirectBridge.LegacyEmbeddedSource();
        File.WriteAllText(target, old);
        // Directory.CreateDirectory refuses to overwrite an existing file.
        var unavailable = Path.Combine(project.Root, "backup-blocking-file");
        File.WriteAllText(unavailable, "not a directory");
        Assert.ThrowsAny<IOException>(() =>
            UnityEditorDirectBridge.Install(project.Root, unavailable));
        Assert.Equal(old, File.ReadAllText(target));
    }

    [Fact]
    public async Task ResponseMustBeFromSameRequestAndEqualExpectedResult()
    {
        using var project = new FakeProject();
        UnityEditorDirectBridge.Install(project.Root);
        var queue = UnityEditorDirectBridge.QueueDirectory(project.Root);
        Directory.CreateDirectory(queue);
        async Task Server(string actual, CancellationToken token)
        {
            string? file = null;
            for (var i = 0; i < 300; i++)
            {
                file = Directory.GetFiles(queue, "*.req.json").FirstOrDefault();
                if (file is not null) break;
                await Task.Delay(20, token);
            }
            Assert.NotNull(file);
            using var request = JsonDocument.Parse(await File.ReadAllTextAsync(file!, token));
            var input = request.RootElement;
            Assert.Equal("invoke", input.GetProperty("action").GetString());
            Assert.Equal("QuizStandaloneBuild", input.GetProperty("type").GetString());
            Assert.Equal("BuildQuizStandalone", input.GetProperty("method").GetString());
            var id = input.GetProperty("id").GetString()!;
            // Simulated Editor return; no Unity process is started by this test.
            var response = JsonSerializer.Serialize(new
            {
                id, success = true, code = "EDITOR_METHOD_RETURNED",
                value = actual, detail = "BuildResult=" + actual
            });
            // Model the Editor's atomic response publish. The client must
            // never observe a partially written .resp.json on Windows.
            var target = Path.Combine(queue, id + ".resp.json");
            var staging = target + ".tmp";
            await File.WriteAllTextAsync(staging, response, token);
            File.Move(staging, target);
        }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var service = Server("Succeeded", cts.Token);
        var response = await UnityEditorDirectBridge.ExecuteAsync(
            project.Root, "invoke", "QuizStandaloneBuild", "BuildQuizStandalone",
            null, "Succeeded", TimeSpan.FromSeconds(8), cts.Token);
        await service;
        Assert.Contains("EDITOR_BRIDGE_CONFIRMED", response);
        Assert.Contains("EVIDENCE=", response);

        var failedServer = Server("Failed", cts.Token);
        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UnityEditorDirectBridge.ExecuteAsync(
                project.Root, "invoke", "QuizStandaloneBuild", "BuildQuizStandalone",
                null, "Succeeded", TimeSpan.FromSeconds(8), cts.Token));
        await failedServer;
        Assert.Contains("RESULT_MISMATCH", failed.Message);
    }

    [Fact]
    public async Task TimeoutDoesNotLeaveExecutablePendingRequest()
    {
        using var project = new FakeProject();
        UnityEditorDirectBridge.Install(project.Root);
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            UnityEditorDirectBridge.ExecuteAsync(project.Root, "ping",
                null, null, null, null, TimeSpan.FromMilliseconds(350),
                CancellationToken.None));
        Assert.Contains("TIMEOUT_UNVERIFIED", error.Message);
        var queue = UnityEditorDirectBridge.QueueDirectory(project.Root);
        Assert.Empty(Directory.GetFiles(queue, "*.req.json"));
        Assert.Empty(Directory.GetFiles(queue, "*.resp.json"));
    }
    [Fact]
    public async Task MenuInvocationIsNotAPlayerBuildSucceededAssertion()
    {
        using var project = new FakeProject();
        UnityEditorDirectBridge.Install(project.Root);
        var queue = UnityEditorDirectBridge.QueueDirectory(project.Root);
        Directory.CreateDirectory(queue);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var emulator = Task.Run(async () =>
        {
            string? pending = null;
            for (var i = 0; i < 300; i++)
            {
                pending = Directory.GetFiles(queue, "*.req.json").FirstOrDefault();
                if (pending is not null) break;
                await Task.Delay(20, timeout.Token);
            }
            Assert.NotNull(pending);
            using var input = JsonDocument.Parse(
                await File.ReadAllTextAsync(pending!, timeout.Token));
            var request = input.RootElement;
            Assert.Equal("menu", request.GetProperty("action").GetString());
            var id = request.GetProperty("id").GetString()!;
            await File.WriteAllTextAsync(Path.Combine(queue, id + ".resp.json"),
                JsonSerializer.Serialize(new {
                    id, success = true, code = "EDITOR_MENU_INVOKED",
                    value = "True", detail = "" }), timeout.Token);
        }, timeout.Token);
        // Legacy callers used "Succeeded" as a menu expectation. It must
        // never be confused with the actual result of BuildPipeline.BuildPlayer.
        var answer = await UnityEditorDirectBridge.ExecuteAsync(project.Root,
            "menu", null, null, "Tools/Quiz/Build Windows",
            "Succeeded", TimeSpan.FromSeconds(8), timeout.Token);
        await emulator;
        Assert.Contains("MENU_TRIGGERED_ONLY", answer);
        Assert.Contains("verify BuildReport", answer);
        Assert.DoesNotContain("RESULT_MISMATCH", answer);
    }

}
