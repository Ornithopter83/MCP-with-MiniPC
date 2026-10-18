using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityVisualFoundryTests
{
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(),
            "projecthub-visual-foundry-" + Guid.NewGuid().ToString("N"));
        internal Fixture() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void StyleAndLayoutRequireExplicitDesignChoice()
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => UnityVisualFoundry.Prepare(
            fixture.Root, "job", "M1", "화면 제작", "", "Base"));
        Assert.Throws<ArgumentException>(() => UnityVisualFoundry.Prepare(
            fixture.Root, "job", "M1", "화면 제작", "neon", ""));
        var plan = UnityVisualFoundry.Prepare(fixture.Root, "job", "M1",
            "어떤 키워드도 자동 선택하지 않음", "arcane", "Immersive");
        Assert.Equal("arcane", plan.Theme.Id);
        Assert.Equal("Immersive", plan.PreferredLayout);
    }

    [Fact]
    public void PrepareMakesIsolatedManifestAndExecutableUnityEditorScript()
    {
        using var fixture = new Fixture();
        var plan = UnityVisualFoundry.Prepare(fixture.Root, "../job", "M:1",
            "화려한 SF 퀴즈 게임 화면", "neon", "Base");
        Assert.Equal(Path.GetFullPath(fixture.Root), plan.ProjectRoot);
        Assert.StartsWith(Path.Combine(fixture.Root, "temp", "ProjectHub"),
            plan.ScriptPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", plan.AssetDirectory);
        Assert.True(File.Exists(plan.ManifestPath));
        Assert.True(File.Exists(plan.ScriptPath));
        using var json = JsonDocument.Parse(File.ReadAllText(plan.ManifestPath));
        Assert.Equal("neon",
            json.RootElement.GetProperty("style").GetProperty("Id").GetString());
        Assert.Equal("Base", plan.PreferredLayout);
        Assert.Equal(1920,
            json.RootElement.GetProperty("viewport").GetProperty("width").GetInt32());
        var expression = File.ReadAllText(plan.ScriptPath);
        Assert.Contains("PrefabUtility.SaveAsPrefabAsset", expression);
        Assert.Contains("EditorSceneManager.NewScene", expression);
        Assert.Contains("AnimationClip", expression);
        Assert.Contains("Texture2D", expression);
        Assert.Contains("CanvasScaler", expression);
        Assert.Contains("1920f,1080f", expression);
        Assert.DoesNotContain("SetForegroundWindow", expression);
        Assert.DoesNotContain("Process.Start", expression);
        Assert.DoesNotContain("__OUTPUT__", expression);
        Assert.Contains("VISUAL_FOUNDRY", plan.AgentGuidance);
        Assert.Contains("게임 로직이 아니다", plan.AgentGuidance);
        Assert.True(File.Exists(Path.Combine(plan.Directory, "existing-visual-assets.json")));
        Assert.Contains("PrismaticUI.shader", expression);
        Assert.Contains("Halo.png", expression);
        Assert.Contains("Editorial", expression);
        Assert.Contains("Immersive", expression);
        Assert.Contains("Preview-", expression);
        Assert.Contains("vfPulse", expression);
        Assert.Contains("PrefabUtility.SaveAsPrefabAsset", expression);
        Assert.Contains("PROJECTHUB_VISUAL_FOUNDRY_REUSED", expression);
    }

    [Fact]
    public async Task SuccessfulCliMarkerReportsGeneratedButUninspectedAssets()
    {
        using var fixture = new Fixture();
        var plan = UnityVisualFoundry.Prepare(fixture.Root, "job", "M1", "SF 퀴즈 UI", "neon", "Base");
        var calls = 0;
        var marker = "PROJECTHUB_VISUAL_FOUNDRY_READY|" + plan.Theme.Id +
            "|9|" + plan.AssetDirectory;
        var result = await UnityVisualFoundry.GenerateAsync(plan, CancellationToken.None,
            (args, _) =>
            {
                calls++;
                Assert.Equal("command", args[0]);
                Assert.Equal("eval_file", args[1]);
                Assert.Equal(plan.ScriptPath, args[2]);
                Assert.Contains("--project-path", args);
                Assert.Contains(fixture.Root, args);
                var response = JsonSerializer.Serialize(new
                {
                    success = true,
                    data = new { result = marker }
                });
                return Task.FromResult(new UnityAssetCliResult(0, response, ""));
            });
        Assert.Equal(1, calls);
        Assert.True(result.Success, result.Detail);
        Assert.Equal(plan.AssetDirectory, result.AssetDirectory);
        Assert.Contains("UNINSPECTED", result.Detail);
    }

    [Fact]
    public async Task CliEchoWithoutAConfirmedResultIsNotEvidence()
    {
        using var fixture = new Fixture();
        var plan = UnityVisualFoundry.Prepare(fixture.Root, "job", "M1", "SF 퀴즈 UI", "neon", "Base");
        var result = await UnityVisualFoundry.GenerateAsync(plan, CancellationToken.None,
            (_, _) => Task.FromResult(new UnityAssetCliResult(0,
                """{"success":true,"data":{"expression":"PROJECTHUB_VISUAL_FOUNDRY_READY|neon|3|Assets/ProjectHub/VisualFoundry/job_M1_neon"}}""", "")));
        Assert.False(result.Success);
        Assert.Contains("ACK_UNVERIFIED", result.Detail);
    }

    [Fact]
    public async Task FailedCliCannotClaimGeneratedPremiumAssets()
    {
        using var fixture = new Fixture();
        var plan = UnityVisualFoundry.Prepare(fixture.Root, "job", "M1", "SF 퀴즈 UI", "neon", "Base");
        var result = await UnityVisualFoundry.GenerateAsync(plan, CancellationToken.None,
            (_, _) => Task.FromResult(new UnityAssetCliResult(-1, "", "Editor disconnected")));
        Assert.False(result.Success);
        Assert.Null(result.AssetDirectory);
    }
}
