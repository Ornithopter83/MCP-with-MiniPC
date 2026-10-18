using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityToolchainPreflightTests
{
    [Fact]
    public void MissingUnityProjectFailsClosed()
    {
        var result = UnityToolchainPreflight.ValidateProject(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.False(result.Ready);
        Assert.Equal("UNITY_PROJECT_NOT_FOUND", result.Code);
    }

    [Theory]
    [InlineData("{}", "UNITY_PIPELINE_NOT_DECLARED", false)]
    [InlineData("{\"dependencies\":{\"com.unity.pipeline\":\"0.8.0-exp.1\"}}", "UNITY_PROJECT_READY", true)]
    [InlineData("{broken-json", "UNITY_MANIFEST_UNREADABLE", false)]
    public void PipelineManifestIsValidated(string manifestText, string expectedCode, bool ready)
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-preflight-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            Directory.CreateDirectory(Path.Combine(root, "Packages"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(root, "Packages", "manifest.json"), manifestText);
            File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"),
                "m_EditorVersion: 6000.6.5f1");
            var result = UnityToolchainPreflight.ValidateProject(root);
            Assert.Equal(expectedCode, result.Code);
            Assert.Equal(ready, result.Ready);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EditorProbeTargetsExplicitUnityRoot()
    {
        var args = UnityToolchainPreflight.EditorProbeArguments(Path.GetTempPath());
        Assert.Equal("command", args[0]);
        Assert.Equal("--project-path", args[1]);
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), args[2]);
        Assert.Equal(new[] { "--format", "json" }, args[3..]);
    }

    [Fact]
    public void LiveCommandSchemaIsSharedExactlyAndInvalidatedOnFailedProbe()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-catalog-" +
            Guid.NewGuid().ToString("N"));
        const string jobId = "test-job";
        const string commands =
            "[{\"name\":\"open_scene\",\"parameters\":[{\"name\":\"path\",\"type\":\"String\"}]}]";
        try
        {
            var probe = UnityToolchainPreflight.InterpretEditorProbe(
                0, "{\"success\":true,\"data\":{\"commands\":" +
                    commands + "}}", "");
            Assert.True(probe.Ready);
            Assert.Equal(commands, probe.CommandCatalogJson);
            var catalogPath = UnityToolchainPreflight.SaveCatalogForJob(
                root, jobId, probe);
            Assert.Equal(UnityToolchainPreflight.CatalogPath(root, jobId), catalogPath);
            Assert.Equal(commands, File.ReadAllText(catalogPath!));
            Assert.Null(UnityToolchainPreflight.SaveCatalogForJob(
                root, jobId, new(false, "EDITOR_UNAVAILABLE", "offline")));
            Assert.False(File.Exists(catalogPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("""{"success":true,"data":[{"name":"eval"}]}""", "UNITY_EDITOR_CONNECTED", true)]
    [InlineData("""{"success":true,"data":{"commands":[{"name":"eval"}]}}""", "UNITY_EDITOR_CONNECTED", true)]
    [InlineData("""{"success":true,"commands":[{"name":"eval"}]}""", "UNITY_EDITOR_CONNECTED", true)]
    [InlineData("""{"success":true,"data":{"count":1}}""", "UNITY_EDITOR_CONNECTED_NO_CATALOG", false)]
    public void ProbeSeparatesConnectionAndLiveCommandCapability(
        string output, string code, bool hasEval)
    {
        var probe = UnityToolchainPreflight.InterpretEditorProbe(0, output, "");
        Assert.True(probe.Ready);
        Assert.Equal(code, probe.Code);
        Assert.Equal(hasEval,
            UnityToolchainPreflight.CatalogContainsCommand(probe.CommandCatalogJson, "eval"));
    }

    [Theory]
    [InlineData("""{"success":true,"data":{"name":"eval","parameters":[]}}""", true)]
    [InlineData("""{"success":true,"data":[{"name":"eval"}]}""", true)]
    [InlineData("""{"success":true,"command":"eval","data":{"count":1}}""", false)]
    [InlineData("""{"success":true,"data":{"name":"other"}}""", false)]
    [InlineData("""{"success":false,"data":{"name":"eval"}}""", false)]
    public void DetailedProbeDoesNotAcceptEchoOrSuccessfulStatus(
        string stdout, bool allowed)
    {
        Assert.Equal(allowed,
            UnityToolchainPreflight.DetailedCommandConfirmed(0, stdout, "eval"));
    }

    [Fact]
    public void CatalogRejectsUnsupportedAndEchoedCommands()
    {
        const string catalog = """[{"name":"console"},{"name":"open_scene"}]""";
        Assert.True(UnityToolchainPreflight.CatalogContainsCommand(catalog, "console"));
        Assert.False(UnityToolchainPreflight.CatalogContainsCommand(catalog, "editor_state"));
        Assert.False(UnityToolchainPreflight.CatalogContainsCommand(null, "console"));
        Assert.False(UnityToolchainPreflight.CatalogContainsCommand("{bad", "console"));
    }

    [Theory]
    [InlineData(0, "{\"success\":true,\"data\":[]}", true, "UNITY_EDITOR_CONNECTED")]
    [InlineData(0, "{\"success\":false,\"errors\":[{\"code\":\"NOT_CONNECTED\"}]}", false, "UNITY_EDITOR_UNVERIFIED")]
    [InlineData(0, "not json", false, "UNITY_EDITOR_INVALID_RESPONSE")]
    [InlineData(7, "", false, "UNITY_EDITOR_UNAVAILABLE")]
    public void ProbeNeverTreatsMissingOrFailedJsonAsSuccess(
        int exit, string stdout, bool ready, string expectedCode)
    {
        var result = UnityToolchainPreflight.InterpretEditorProbe(exit, stdout, "");
        Assert.Equal(ready, result.Ready);
        Assert.Equal(expectedCode, result.Code);
    }
}
