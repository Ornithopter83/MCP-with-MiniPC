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

        @@QA
        <INSTRUCTIONS>씬 구성과 참조를 실제 검증한다.</>

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
        Assert.True(milestone.QaReserved);
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
        var misplaced = AssetOnly.Replace(
            "@@ASSET_MANAGER=1", work + "\n@@ASSET_MANAGER=1",
            StringComparison.Ordinal);
        var parsed = HqTextProtocol.Parse(misplaced);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        var invalid = AssetOnly.Replace(
            "@@MILESTONE\n<ID>M1</>",
            "@@MILESTONE\n<ID>M1</>\n\n" + work,
            StringComparison.Ordinal);
        Assert.False(HqTextProtocol.Parse(invalid).IsValid);
    }

    [Fact]
    public void AssetManagerAllowsOnlyExpectedUnityWriteScopes()
    {
        var request = new AssetManagerDefinition("x", "goal",
            "Assets/Scenes/AIDemo.unity", "Assets/ThirdParty/Environment/Rocks",
            "CC0_AUTO", "Instructions");

        Assert.False(AssetManagerPathPolicy.IsAllowed(
            "Assets/Scenes/AIDemo.unity", request, "ACQUIRE"));
        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "Assets/ThirdParty.meta", request, "ACQUIRE"));
        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "Assets/ThirdParty/Environment/Rocks/a.prefab.meta", request, "ACQUIRE"));
        Assert.True(AssetManagerPathPolicy.IsAllowed(
            "Assets/Scenes/AIDemo.unity", request, "INTEGRATE"));
        Assert.False(AssetManagerPathPolicy.IsAllowed(
            "Packages/manifest.json", request, "INTEGRATE"));
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
    public void AssetManagerContractIsEmbedded()
    {
        Assert.Contains("DISCOVER", RoleContractLoader.LoadAssetManagerFooter());
        Assert.Contains("ACQUIRE", RoleContractLoader.LoadAssetManagerFooter());
        Assert.Contains("INTEGRATE", RoleContractLoader.LoadAssetManagerFooter());
        Assert.Contains("@@ASSET_MANAGER=1", RoleContractLoader.LoadHqFooter());
    }
}
