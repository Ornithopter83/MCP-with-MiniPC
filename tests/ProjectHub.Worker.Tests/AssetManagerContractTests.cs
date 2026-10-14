using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class AssetManagerContractTests
{
    private const string AssetOnly = """
        [ACTION=WORK]

        @@MILESTONE
        <ID>M1</>

        @@ASSET_MANAGER=1
        <REQUEST_ID>rock-001</>
        <GOAL>CC0 바위 모델 세 개를 AIDemo에 배치한다.</>
        <SCENE>Assets/Scenes/AIDemo.unity</>
        <TARGET_PATH>Assets/ThirdParty/Environment/Rocks</>
        <SOURCE_POLICY>CC0_AUTO</>
        <INSTRUCTIONS>
        프로젝트 내부 재사용을 우선하고 부족하면 합법적 CC0를 찾아 배치한다.
        </>

        @@HIGH
        <INSTRUCTIONS>에셋 출처·GUID·씬 저장을 검토한다.</>

        [RESPONSE=OK]
        """;

    [Fact]
    public void AssetManagerCanLeadMilestoneWithoutOrdinaryWork()
    {
        var parsed = HqTextProtocol.Parse(AssetOnly);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse,
            out var milestone, out var error), error);
        Assert.NotNull(milestone);
        Assert.Empty(milestone!.WorkItems);
        Assert.Equal("rock-001", milestone.AssetManager!.RequestId);
        Assert.Equal("CC0_AUTO", milestone.AssetManager.SourcePolicy);
        Assert.Equal("Assets/Scenes/AIDemo.unity", milestone.AssetManager.ScenePath);
        Assert.False(milestone.QaReserved);
    }

    [Theory]
    [InlineData("@@ASSET_MANAGER=1", "@@ASSET_MANAGER=2")]
    [InlineData("<SOURCE_POLICY>CC0_AUTO</>", "<SOURCE_POLICY>PAID_AUTO</>")]
    [InlineData("<SCENE>Assets/Scenes/AIDemo.unity</>", "<SCENE>../Outside.unity</>")]
    [InlineData("<REQUEST_ID>rock-001</>", "<REQUEST_ID>../../bad</>")]
    public void InvalidNumberPolicyOrPathIsRejected(string oldText, string newText)
    {
        var changed = AssetOnly.Replace(oldText, newText, StringComparison.Ordinal);
        Assert.False(HqTextProtocol.Parse(changed).IsValid);
    }

    [Fact]
    public void AssetManagerMustPrecedeOrdinaryWork()
    {
        const string work = """
            @@WORK=10
            <PATH>Assets/Scripts/Foo.cs</>
            <INSTRUCTIONS>코드를 구현한다.</>
            """;
        var valid = AssetOnly.Replace(
            "@@HIGH", work + "\n\n@@HIGH", StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(valid);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        var invalid = AssetOnly.Replace(
            "@@ASSET_MANAGER=1",
            work + "\n\n@@ASSET_MANAGER=1",
            StringComparison.Ordinal);
        Assert.False(HqTextProtocol.Parse(invalid).IsValid);
    }

    [Fact]
    public void AssetManagerAllowsAnyProjectWriteWithDeclaredScopeTracking()
    {
        var request = new AssetManagerDefinition("x", "goal",
            "Assets/Scenes/AIDemo.unity", "Assets/ThirdParty/Environment/Rocks",
            "CC0_AUTO", "Instructions");

        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "Assets/Scenes/AIDemo.unity", request, "ACQUIRE"));
        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "Assets/ThirdParty.meta", request, "ACQUIRE"));
        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "Assets/ThirdParty/Environment/Rocks/a.prefab.meta", request, "ACQUIRE"));
        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "Assets/Scenes/AIDemo.unity", request, "INTEGRATE"));
        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "Packages/manifest.json", request, "INTEGRATE"));
        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "ProjectSettings/URPProjectSettings.asset", request, "INTEGRATE"));
        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "Assets/Editor/SceneInstaller.cs", request, "DISCOVER"));
        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "Assets/Other.unity.meta", request, "ACQUIRE"));
        Assert.False(AssetManagerPathPolicy.IsAllowed(
            "../Outside/settings.asset", request, "INTEGRATE"));
        Assert.False(AssetManagerPathPolicy.IsAllowed(
            "/absolute/file.mat", request, "INTEGRATE"));
        Assert.False(AssetManagerPathPolicy.IsDeclaredScope(
            "ProjectSettings/URPProjectSettings.asset", request, "INTEGRATE"));
        Assert.True(AssetManagerPathPolicy.IsDeclaredScope(
            "Assets/Scenes/AIDemo.unity", request, "INTEGRATE"));
    }

    [Fact]
    public void ResourceCannotRaceAgainstManagerImportDirectory()
    {
        const string resource = """
            @@RESOURCE=0
            <TYPE>image</>
            <TARGET_PATH>Assets/ThirdParty/Environment/Rocks/generated.png</>
            <INSTRUCTIONS>작은 바위 텍스처를 생성한다.</>

            """;
        var parsed = HqTextProtocol.Parse(
            AssetOnly.Replace("@@HIGH", resource + "@@HIGH", StringComparison.Ordinal));
        Assert.False(parsed.IsValid);
        Assert.Contains("COMPACT_SECTION_FORBIDDEN", parsed.Errors);
    }

    [Fact]
    public void CheckpointUsesStableRequestAndRejectsChangedScope()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "asset-manager-checkpoint-" + Guid.NewGuid().ToString("N"));
        var definition = new AssetManagerDefinition("rocks-01",
            "place rocks", "Assets/Scenes/AIDemo.unity",
            "Assets/ThirdParty/Rocks", "PROJECT_ONLY", "reuse");
        var role = new WorkerAiRoleSettings();
        try
        {
            Directory.CreateDirectory(root);
            var initial = AssetManagerExecutionJournal.Open(root, "testjob", definition, role);
            Assert.Equal("READY", initial.State);
            AssetManagerExecutionJournal.Save(root, initial with { State = "RUNNING_INTEGRATE" });
            var resumed = AssetManagerExecutionJournal.Open(root, "testjob", definition, role);
            Assert.Equal("RUNNING_INTEGRATE", resumed.State);
            Assert.Throws<InvalidOperationException>(() =>
                AssetManagerExecutionJournal.Open(root, "testjob",
                    definition with { ScenePath = "Assets/Scenes/Another.unity" }, role));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HighReceivesAssetManagerPlanAndCompletionEvidence()
    {
        var parsed = HqTextProtocol.Parse(AssetOnly);
        Assert.True(parsed.IsValid);
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse,
            out var milestone, out var error), error);
        Assert.NotNull(milestone);
        var evidence = new[] { "ASSET_MANAGER #1: ASSET_MANAGER_COMPLETED: rock-001" };
        var highContext = MilestoneDefinitionContract.BuildHighContext(
            milestone!, new Dictionary<string, string>(), null, evidence);
        Assert.Contains("Assets/Scenes/AIDemo.unity", highContext);
        Assert.Contains("@@ASSET_MANAGER_REQUEST", highContext);
        Assert.Contains("@@ASSET_MANAGER_RESULTS", highContext);
        Assert.DoesNotContain("@@QA_REPORT", highContext);
        Assert.Contains("ASSET MANAGER", RoleContractLoader.LoadHighFooter());
    }

    [Fact]
    public void FileInventoryWorksWithoutAssetDatabaseOrRg()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "asset-discovery-" + Guid.NewGuid().ToString("N"));
        var assets = Path.Combine(root, "Assets");
        try
        {
            Directory.CreateDirectory(Path.Combine(assets, "Scenes"));
            File.WriteAllText(Path.Combine(assets, "Scenes", "AIDemo.unity"), "scene");
            Directory.CreateDirectory(Path.Combine(assets, "Models"));
            File.WriteAllText(Path.Combine(assets, "Models", "Oak.fbx"), "model");
            File.WriteAllText(Path.Combine(assets, "Models", "Oak.fbx.meta"), "guid");
            var result = AssetManagerLocalInventory.Scan(root,
                "Assets/Scenes/AIDemo.unity", "Assets/ThirdParty/Forest");
            Assert.True(result.SceneExists);
            Assert.False(result.TargetExists);
            Assert.Equal(1, result.Candidates);
            Assert.Null(result.Error);
            Assert.Contains("Assets/Models/Oak.fbx", result.Examples);
            var summary = AssetManagerLocalInventory.Format(result);
            Assert.Contains("SOURCE=WORKER_FILESYSTEM_READ_ONLY", summary);
            Assert.Contains("TARGET_EXISTS=False", summary);
            Assert.Contains("MODEL_CANDIDATES=1", summary);
            Assert.False(Directory.Exists(Path.Combine(assets, "ThirdParty")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DiscoveryContractSeparatesCandidateFromImportedAsset()
    {
        var contract = RoleContractLoader.LoadAssetManagerFooter();
        Assert.Contains("LOCAL_ASSET_INVENTORY", contract);
        Assert.Contains("Get-ChildItem", contract);
        Assert.Contains("단독 차단 사유가 아니다", contract);
        Assert.Contains("ACQUIRE에서 검증", contract);
        Assert.Contains("Importer", contract);
        Assert.Contains("Unity.exe -help/-batchmode", contract);
    }

    [Fact]
    public void EmptyLocalInventoryDoesNotRequireTargetDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "asset-discovery-empty-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            var result = AssetManagerLocalInventory.Scan(root,
                "Assets/Scenes/AIDemo.unity", "Assets/ThirdParty/Forest");
            Assert.Equal(0, result.Candidates);
            Assert.False(result.TargetExists);
            Assert.False(result.Truncated);
            Assert.Null(result.Error);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AssetManagerContractIsEmbedded()
    {
        Assert.Contains("DISCOVER", RoleContractLoader.LoadAssetManagerFooter());
        Assert.Contains("ACQUIRE", RoleContractLoader.LoadAssetManagerFooter());
        Assert.Contains("INTEGRATE", RoleContractLoader.LoadAssetManagerFooter());
        Assert.Contains("@@ASSET_MANAGER=1", RoleContractLoader.LoadHqFooter());
    }
}
