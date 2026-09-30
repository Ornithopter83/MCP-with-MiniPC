using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class MechanicalBuildExecutorTests
{
    [Fact]
    public void MalformedAuthorizationFallsBackToFullBuild()
    {
        var authorization = BuildRequestContract.ParseAuthorizationOrFullFallback(
            "BUILD_AUTHORIZATION: not-json");

        Assert.Equal("FULL", authorization.Scope);
        Assert.Null(authorization.Target);
        Assert.True(authorization.FallbackToFull);
        Assert.False(authorization.NoRestore);
    }

    [Fact]
    public void PartialAuthorizationFallsBackToFullBuild()
    {
        var authorization = BuildRequestContract.ParseAuthorizationOrFullFallback(
            """{"scope":"TARGET","configuration":"Release"}""");

        Assert.Equal("FULL", authorization.Scope);
        Assert.True(authorization.FallbackToFull);
    }

    [Fact]
    public void ValidTargetAuthorizationIsPreserved()
    {
        var authorization = BuildRequestContract.ParseAuthorizationOrFullFallback(
            """{"scope":"TARGET","target":"src/App/App.csproj","configuration":"Release","noRestore":true}""");

        Assert.Equal("TARGET", authorization.Scope);
        Assert.Equal("src/App/App.csproj", authorization.Target);
        Assert.Equal("Release", authorization.Configuration);
        Assert.True(authorization.NoRestore);
        Assert.False(authorization.FallbackToFull);
    }

    [Fact]
    public void BuildRequestMarkerIsDetectedInsideBlockedReport()
    {
        Assert.True(BuildRequestContract.ContainsRequest(
            "WORK_ITEM_STATUS: BLOCKED\nBUILD_REQUEST\n빌드가 필요합니다."));
    }
    [Fact]
    public void MalformedAuthorizationReportsFallbackReason()
    {
        var authorization = BuildRequestContract.ParseAuthorizationOrFullFallback(
            "BUILD_AUTHORIZATION: not-json",
            out var fallbackReason);

        Assert.True(authorization.FallbackToFull);
        Assert.Equal("BUILD_AUTHORIZATION_JSON_INVALID", fallbackReason);
    }

    [Fact]
    public void FullBuildFallbackSkipsIntegrationInputSnapshots()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-build-fallback-" + Guid.NewGuid().ToString("N"));
        var realProject = Path.Combine(root, "LocalLens.Studio", "LocalLens.Studio.csproj");
        var snapshotProject = Path.Combine(
            root,
            ".projecthub-integration-inputs",
            "11",
            "LocalLens.Studio",
            "LocalLens.Studio.csproj");

        Directory.CreateDirectory(Path.GetDirectoryName(realProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotProject)!);
        File.WriteAllText(realProject, "<Project />");
        File.WriteAllText(snapshotProject, "<Project />");

        try
        {
            var authorization = BuildRequestContract.ParseAuthorizationOrFullFallback(
                "BUILD_AUTHORIZATION: not-json");
            var resolved = MechanicalBuildExecutor.ResolveTarget(root, authorization);

            Assert.Equal(Path.GetFullPath(realProject), resolved.Target);
            Assert.False(resolved.Fallback);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExplicitIntegrationInputTargetFallsBackToRealProject()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-build-target-" + Guid.NewGuid().ToString("N"));
        var realProject = Path.Combine(root, "LocalLens.Studio", "LocalLens.Studio.csproj");
        var snapshotRelative = Path.Combine(
            ".projecthub-integration-inputs",
            "11",
            "LocalLens.Studio",
            "LocalLens.Studio.csproj");
        var snapshotProject = Path.Combine(root, snapshotRelative);

        Directory.CreateDirectory(Path.GetDirectoryName(realProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotProject)!);
        File.WriteAllText(realProject, "<Project />");
        File.WriteAllText(snapshotProject, "<Project />");

        try
        {
            var authorization = new BuildAuthorization(
                "TARGET",
                snapshotRelative,
                "Debug",
                NoRestore: false,
                FallbackToFull: false);
            var resolved = MechanicalBuildExecutor.ResolveTarget(root, authorization);

            Assert.Equal(Path.GetFullPath(realProject), resolved.Target);
            Assert.True(resolved.Fallback);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

}
