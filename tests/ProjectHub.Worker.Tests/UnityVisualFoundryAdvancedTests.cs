using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityVisualFoundryAdvancedTests
{
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "visual-foundry-advanced-" + Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void AccessiblePaletteSelfCorrectsWithoutChangingStyleIdentity()
    {
        var original = new UnityVisualFoundry.Style("test", "Test", "#111111",
            "#202020", "#333333", "#353535", "#777777",
            "#383838", "#444444", "TEST");
        var tuned = UnityVisualFoundryQuality.AutoTune(original);
        Assert.Equal(original.Id, tuned.Id);
        Assert.True(UnityVisualFoundryQuality.Contrast(tuned.Text,tuned.Panel) >= 4.5);
        Assert.True(UnityVisualFoundryQuality.Contrast(tuned.Muted,tuned.Panel) >= 4.5);
        Assert.True(UnityVisualFoundryQuality.Contrast(tuned.Accent,tuned.Panel) >= 3);
    }

    [Fact]
    public void HqDirectionIsCarriedIntoTheDesignDnaAndPrefabSelection()
    {
        using var workspace = new Workspace();
        var plan = UnityVisualFoundry.Prepare(workspace.Root,"job","M1",
            "정보 밀도 높은 SF 퀴즈 대시보드", "neon", "Editorial");
        Assert.Equal("Editorial", plan.PreferredLayout);
        Assert.EndsWith("Quiz-Editorial.prefab",
            UnityVisualFoundryQuality.PrefabPath(plan,"Quiz",plan.PreferredLayout));
        var dna = File.ReadAllText(plan.ManifestPath);
        Assert.Contains("TextContrast",dna);
        Assert.Contains("recommendedPrefab",dna);
        Assert.Contains("Editorial",dna);
    }

    [Fact]
    public void ExistingVisualAssetsAreListedWithoutRankingOrModification()
    {
        using var workspace = new Workspace();
        var assets = Path.Combine(workspace.Root,"Assets","ThirdParty","PremiumUI");
        Directory.CreateDirectory(assets);
        var premium = Path.Combine(assets,"CyberNeonHUD.prefab");
        File.WriteAllText(premium,"original");
        File.WriteAllText(Path.Combine(assets,"random.txt"),"ignored");
        var discovered = UnityVisualFoundryCatalog.Inventory(workspace.Root);
        Assert.Contains(discovered,x=>x.Path.EndsWith("CyberNeonHUD.prefab"));
        Assert.Equal("original",File.ReadAllText(premium));
    }

    [Fact]
    public void RuntimeAdapterIsInstalledIdempotentlyAndNeverOverwritesUserFiles()
    {
        using var workspace = new Workspace();
        var first = UnityVisualFoundryRuntime.Install(workspace.Root);
        Assert.True(first.Available, first.Detail);
        Assert.True(first.Changed);
        var second = UnityVisualFoundryRuntime.Install(workspace.Root);
        Assert.True(second.Available, second.Detail);
        Assert.False(second.Changed);
        var source = Path.Combine(workspace.Root,
            UnityVisualFoundryRuntime.RelativeSourcePath.Replace('/',
                Path.DirectorySeparatorChar));
        Assert.Contains("ApplyQuiz",File.ReadAllText(source));
        Assert.Contains("ActionPressed",File.ReadAllText(source));
        var watcher = Path.Combine(workspace.Root,
            UnityVisualFoundryRuntime.RelativeWatcherPath.Replace('/',
                Path.DirectorySeparatorChar));
        Assert.Contains("MonoBehaviour", File.ReadAllText(watcher));
        File.AppendAllText(watcher,"// custom user edit");
        var conflict = UnityVisualFoundryRuntime.Install(workspace.Root);
        Assert.False(conflict.Available);
        Assert.Contains("CONFLICT",conflict.Detail);
        Assert.Contains("custom user edit",File.ReadAllText(watcher));
    }

    [Fact]
    public void NoPngNeverReceivesAVisualQualityPass()
    {
        var image = UnityVisualFoundryPixelAudit.Inspect(null);
        Assert.False(image.Readable);
        Assert.False(image.FlatFrame);
        Assert.Contains("UNVERIFIED",image.Detail);
    }

    [Fact]
    public void EditorScriptIsSideEffectIsolatedAndHasDetailedVariants()
    {
        var script = UnityVisualFoundry.BuildEditorExpression(
            "Assets/ProjectHub/VisualFoundry/example", new UnityVisualFoundry.Style("neon", "Neon Command", "#07101F",
                "#132D49", "#102238", "#18D5F7", "#FFAC5B", "#ECF8FF",
                "#A4BCCF", "SYSTEM / ARCADE"));
        Assert.Contains("VISUAL_FOUNDRY_EXISTING_ASSETS_INCOMPLETE_NO_OVERWRITE",script);
        Assert.Contains("PrismaticUI.shader",script);
        Assert.Contains("Halo.png",script);
        Assert.Contains("EditorSceneManager.CloseScene",script);
        Assert.Contains("Preview-",script);
        Assert.Contains("SetActiveScene",script);
        Assert.Contains("PROJECTHUB_VISUAL_FOUNDRY_REUSED",script);
        Assert.DoesNotContain("SetForegroundWindow",script);
        Assert.DoesNotContain("Process.Start",script);
    }

    [Fact]
    public void RejectsUnknownPrefabVariantsWithoutChangingProject()
    {
        using var workspace = new Workspace();
        var plan = UnityVisualFoundry.Prepare(workspace.Root,"j","m","quiz", "neon", "Base");
        Assert.Throws<ArgumentException>(() =>
            UnityVisualFoundryQuality.PrefabPath(plan,"Unknown"));
        Assert.Throws<ArgumentException>(() =>
            UnityVisualFoundryQuality.PrefabPath(plan,"Quiz","Outside"));
    }
}
