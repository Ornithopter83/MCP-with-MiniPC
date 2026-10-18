using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorDirectBridgeTests
{
    private sealed class FakeProject : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "projecthub-bridge-test-" + Guid.NewGuid().ToString("N"));

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
        }
    }

    [Fact]
    public void InstallationIsIdempotentAndNeverOverwritesEditedEditorSource()
    {
        using var project = new FakeProject();
        var source = UnityEditorDirectBridge.EmbeddedSource();
        Assert.Contains("[InitializeOnLoad]", source);
        Assert.Contains("EditorApplication.update", source);
        Assert.Contains("BindingFlags.Static | BindingFlags.Public", source);
        Assert.Contains("BuildReport", source);
        Assert.Contains("REQUEST_EXPIRED_UNEXECUTED", source);
        var first = UnityEditorDirectBridge.Install(project.Root);
        Assert.Contains("INSTALLED_WAIT", first);
        var target = UnityEditorDirectBridge.EditorSourcePath(project.Root);
        Assert.Equal(source, File.ReadAllText(target));
        Assert.Contains("ALREADY_INSTALLED",
            UnityEditorDirectBridge.Install(project.Root));
        File.AppendAllText(target, "// USER EDIT\n");
        var error = Assert.Throws<InvalidOperationException>(() =>
            UnityEditorDirectBridge.Install(project.Root));
        Assert.Contains("SOURCE_CONFLICT", error.Message);
        Assert.EndsWith("// USER EDIT\n", File.ReadAllText(target));
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
            await File.WriteAllTextAsync(Path.Combine(queue, id + ".resp.json"),
                response, token);
        }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var service = Server("Succeeded", cts.Token);
        var response = await UnityEditorDirectBridge.ExecuteAsync(
            project.Root, "invoke", "QuizStandaloneBuild", "BuildQuizStandalone",
            null, "Succeeded", TimeSpan.FromSeconds(8), cts.Token);
        await service;
        Assert.Contains("EDITOR_BRIDGE_CONFIRMED", response);

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
}
