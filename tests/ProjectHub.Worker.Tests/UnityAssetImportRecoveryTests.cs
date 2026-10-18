using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityAssetImportRecoveryTests
{
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "unity-asset-resume-" + Guid.NewGuid().ToString("N"));
        public string Folder => Path.Combine(Root, "Assets", "ThirdParty", "Forest");
        public Workspace()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "LICENSES.md"), "Official Poly Haven CC0");
            File.WriteAllBytes(Path.Combine(Folder, "pine.fbx"), new byte[] { 1, 3, 5, 7 });
            File.WriteAllBytes(Path.Combine(Folder, "rock.png"), new byte[] { 2, 4, 6 });
        }
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void DownloadedFilesAreHashedAndMetaFilesNeverForceRedownload()
    {
        using var folder = new Workspace();
        var acquired = AssetManagerFileEvidence.Inspect(folder.Root,
            "Assets/ThirdParty/Forest", requireCc0Manifest: true);
        Assert.True(acquired.Ready, acquired.Detail);
        Assert.Equal(2, acquired.Files.Count);
        Assert.Equal(1, acquired.ModelCount);

        File.WriteAllText(Path.Combine(folder.Folder, "pine.fbx.meta"), "guid: abcdef");
        var resumed = AssetManagerFileEvidence.Inspect(folder.Root,
            "Assets/ThirdParty/Forest", requireCc0Manifest: true, acquired.Files);
        Assert.True(resumed.Ready, resumed.Detail);
        Assert.Equal(acquired.Files[0].Sha256, resumed.Files[0].Sha256);

        File.WriteAllBytes(Path.Combine(folder.Folder, "rock.png"), new byte[] { 8, 8, 8 });
        var changed = AssetManagerFileEvidence.Inspect(folder.Root,
            "Assets/ThirdParty/Forest", requireCc0Manifest: true, acquired.Files);
        Assert.False(changed.Ready);
        Assert.Equal("ASSET_FILES_CHANGED_SINCE_CHECKPOINT", changed.Detail);
    }

    [Fact]
    public void CC0AssetsRequireLicenseEvidenceAndRealModel()
    {
        using var folder = new Workspace();
        File.Delete(Path.Combine(folder.Folder, "LICENSES.md"));
        Assert.Equal("ASSET_CC0_MANIFEST_MISSING",
            AssetManagerFileEvidence.Inspect(folder.Root,
                "Assets/ThirdParty/Forest", true).Detail);

        File.WriteAllText(Path.Combine(folder.Folder, "LICENSES.md"), "CC0");
        File.Delete(Path.Combine(folder.Folder, "pine.fbx"));
        Assert.Equal("ASSET_MODEL_NOT_FOUND",
            AssetManagerFileEvidence.Inspect(folder.Root,
                "Assets/ThirdParty/Forest", true).Detail);
    }

    [Fact]
    public async Task ConnectedUnityCliImportsWithoutAnotherDownload()
    {
        using var folder = new Workspace();
        var assets = AssetManagerFileEvidence.Inspect(folder.Root,
            "Assets/ThirdParty/Forest", true);
        var calls = new List<IReadOnlyList<string>>();
        Task<UnityAssetCliResult> Fake(IReadOnlyList<string> args, CancellationToken _)
        {
            calls.Add(args);
            return Task.FromResult(calls.Count == 1
                ? new UnityAssetCliResult(0, """{"success":true,"data":{"commands":[{"name":"eval"}]}}""", "")
                : new UnityAssetCliResult(0, """{"success":true,"data":{"result":"PROJECTHUB_ASSET_IMPORT_OK|2|1"}}""", ""));
        }

        var result = await UnityAssetCliImporter.RunAsync(folder.Root,
            assets.Files, assets.ModelCount, CancellationToken.None, Fake);
        Assert.True(result.Success, result.Detail);
        Assert.Equal(2, result.VerifiedAssets);
        Assert.Equal(2, calls.Count);
        Assert.Contains("--project-path", calls[1]);
        Assert.Equal("eval", calls[1][1]);
        Assert.Contains("AssetDatabase.ImportAsset", calls[1][2]);
        Assert.Contains("AssetPathToGUID", calls[1][2]);
        Assert.Contains("AssetImporter.GetAtPath", calls[1][2]);
        Assert.Contains("pine.fbx", calls[1][2]);
    }

    [Theory]
    [InlineData(0, """{"success":true,"data":{"result":"PROJECTHUB_ASSET_IMPORT_OK|1|1"}}""", false)]
    [InlineData(0, """{"success":false,"data":{"result":"PROJECTHUB_ASSET_IMPORT_OK|2|1"}}""", false)]
    [InlineData(0, """{"success":true,"data":{"expression":"PROJECTHUB_ASSET_IMPORT_OK|2|1"}}""", false)]
    [InlineData(0, """{"success":true,"data":{"result":"PROJECTHUB_ASSET_IMPORT_OK|2|1"}}""", true)]
    [InlineData(7, """{"success":true,"data":{"result":"PROJECTHUB_ASSET_IMPORT_OK|2|1"}}""", false)]
    public void CliSuccessRequiresMatchingEditorEvidence(
        int exit, string response, bool expected)
    {
        var result = UnityAssetCliImporter.InterpretImportResult(
            new UnityAssetCliResult(exit, response, ""), 2, 1);
        Assert.Equal(expected, result.Success);
    }

    [Fact]
    public void NonImportableNotesDoNotBlockOtherwiseValidAssets()
    {
        using var folder = new Workspace();
        File.WriteAllText(Path.Combine(folder.Folder, "SOURCE.json"), "{}");
        File.WriteAllBytes(Path.Combine(folder.Folder, "original.zip"), new byte[] { 10, 20 });
        var found = AssetManagerFileEvidence.Inspect(folder.Root,
            "Assets/ThirdParty/Forest", requireCc0Manifest: true);
        Assert.True(found.Ready, found.Detail);
        Assert.Equal(2, found.Files.Count);
        Assert.Contains("ignored=2", found.Detail);
    }

    [Fact]
    public void ProjectOnlyPrefabsDoNotNeedDownloadedModelOrCc0Manifest()
    {
        using var folder = new Workspace();
        File.Delete(Path.Combine(folder.Folder, "pine.fbx"));
        File.Delete(Path.Combine(folder.Folder, "rock.png"));
        File.Delete(Path.Combine(folder.Folder, "LICENSES.md"));
        File.WriteAllText(Path.Combine(folder.Folder, "Tree.prefab"), "%YAML 1.1");
        var evidence = AssetManagerFileEvidence.Inspect(folder.Root,
            "Assets/ThirdParty/Forest", requireCc0Manifest: false,
            requireModel: false);
        Assert.True(evidence.Ready, evidence.Detail);
        Assert.Equal(0, evidence.ModelCount);
        Assert.Single(evidence.Files);
    }

    [Fact]
    public async Task EvalFileFallbackIsUsedOnlyAfterDefiniteUnavailableCommand()
    {
        using var folder = new Workspace();
        var assets = AssetManagerFileEvidence.Inspect(folder.Root,
            "Assets/ThirdParty/Forest", true);
        var calls = new List<IReadOnlyList<string>>();
        Task<UnityAssetCliResult> Fake(IReadOnlyList<string> args, CancellationToken _)
        {
            calls.Add(args);
            return Task.FromResult(calls.Count switch
            {
                1 => new UnityAssetCliResult(0, """{"success":true}""", ""),
                2 => new UnityAssetCliResult(6, "", "UNKNOWN_COMMAND: eval"),
                _ => new UnityAssetCliResult(0,
                    """{"success":true,"data":{"result":"PROJECTHUB_ASSET_IMPORT_OK|2|1"}}""", "")
            });
        }
        var result = await UnityAssetCliImporter.RunAsync(folder.Root,
            assets.Files, assets.ModelCount, CancellationToken.None, Fake);
        Assert.True(result.Success, result.Detail);
        Assert.Equal(3, calls.Count);
        Assert.Equal("eval_file", calls[2][1]);
        Assert.False(File.Exists(calls[2][2]));
    }

    [Fact]
    public async Task UncertainImportErrorIsNotReplayed()
    {
        using var folder = new Workspace();
        var assets = AssetManagerFileEvidence.Inspect(folder.Root,
            "Assets/ThirdParty/Forest", true);
        var calls = 0;
        Task<UnityAssetCliResult> Fake(IReadOnlyList<string> args, CancellationToken _)
        {
            calls++;
            return Task.FromResult(calls == 1
                ? new UnityAssetCliResult(0, """{"success":true}""", "")
                : new UnityAssetCliResult(7, "", "EDITOR_NOT_READY: timeout"));
        }
        var result = await UnityAssetCliImporter.RunAsync(folder.Root,
            assets.Files, assets.ModelCount, CancellationToken.None, Fake);
        Assert.False(result.Success);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ImportPendingCheckpointRetainsDownloadManifestAndIdentity()
    {
        using var folder = new Workspace();
        var assets = AssetManagerFileEvidence.Inspect(folder.Root,
            "Assets/ThirdParty/Forest", true);
        var definition = new AssetManagerDefinition("forest-01", "forest",
            "Assets/Scenes/AIDemo.unity", "Assets/ThirdParty/Forest",
            "CC0_AUTO", "Acquire");
        var role = new WorkerAiRoleSettings();
        var checkpoint = AssetManagerExecutionJournal.Open(
            folder.Root, "testjob", "M1", definition, role);
        AssetManagerExecutionJournal.Save(folder.Root, checkpoint with
        {
            State = "IMPORT_PENDING",
            AcquiredFiles = assets.Files,
            BlockedPhase = "ACQUIRE"
        });
        var recovered = AssetManagerExecutionJournal.Open(
            folder.Root, "testjob", "M1", definition, role);
        Assert.Equal("IMPORT_PENDING", recovered.State);
        Assert.Equal("testjob", recovered.JobId);
        Assert.Equal("forest-01", recovered.RequestId);
        Assert.Equal("ACQUIRE", recovered.BlockedPhase);
        Assert.Equal(2, recovered.AcquiredFiles!.Count);
    }
    [Theory]
    [InlineData(1, "UNITY_EDITOR_QUERY_TIMEOUT")]
    [InlineData(2, "UNITY_IMPORT_UNCERTAIN_TIMEOUT")]
    [InlineData(3, "UNITY_IMPORT_UNCERTAIN_TIMEOUT")]
    public async Task TimeoutsDoNotReplayPossiblyExecutedImports(
        int timedOutAt, string expectedCode)
    {
        using var folder = new Workspace();
        var assets = AssetManagerFileEvidence.Inspect(folder.Root,
            "Assets/ThirdParty/Forest", true);
        var calls = 0;
        Task<UnityAssetCliResult> Fake(IReadOnlyList<string> args, CancellationToken _)
        {
            calls++;
            if (calls == timedOutAt)
                return Task.FromResult(
                    new UnityAssetCliResult(-1, "", "UNITY_IMPORT_CLI_TIMEOUT"));
            if (calls == 2 && timedOutAt == 3)
                return Task.FromResult(
                    new UnityAssetCliResult(6, "", "UNKNOWN_COMMAND: eval"));
            return Task.FromResult(
                new UnityAssetCliResult(0, """{"success":true}""", ""));
        }

        var result = await UnityAssetCliImporter.RunAsync(folder.Root,
            assets.Files, assets.ModelCount, CancellationToken.None, Fake);
        Assert.False(result.Success);
        Assert.Contains(expectedCode, result.Detail);
        Assert.Equal(timedOutAt, calls);
    }

}
