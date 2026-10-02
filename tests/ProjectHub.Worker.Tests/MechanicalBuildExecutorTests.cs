using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class MechanicalBuildExecutorTests
{
    [Fact]
    public void MalformedAuthorizationIsRejected()
    {
        var parsed = BuildRequestContract.TryParseAuthorization(
            "BUILD_AUTHORIZATION: not-json",
            out var authorization,
            out var errorCode);

        Assert.False(parsed);
        Assert.Null(authorization);
        Assert.Equal("BUILD_AUTHORIZATION_JSON_INVALID", errorCode);
    }

    [Fact]
    public void PartialTargetAuthorizationIsRejected()
    {
        var parsed = BuildRequestContract.TryParseAuthorization(
            """{"scope":"TARGET","configuration":"Release"}""",
            out var authorization,
            out var errorCode);

        Assert.False(parsed);
        Assert.Null(authorization);
        Assert.Equal("BUILD_AUTHORIZATION_TARGET_MISSING", errorCode);
    }

    [Fact]
    public void ValidTargetAuthorizationIsPreserved()
    {
        var parsed = BuildRequestContract.TryParseAuthorization(
            """{"scope":"TARGET","target":"src/App/App.csproj","configuration":"Release","noRestore":true}""",
            out var authorization,
            out var errorCode);

        Assert.True(parsed);
        Assert.Null(errorCode);
        Assert.NotNull(authorization);
        Assert.Equal("TARGET", authorization!.Scope);
        Assert.Equal("src/App/App.csproj", authorization.Target);
        Assert.Equal("Release", authorization.Configuration);
        Assert.True(authorization.NoRestore);
    }

    [Fact]
    public void BuildRequestMarkerIsDetectedInsideBlockedReport()
    {
        Assert.True(BuildRequestContract.ContainsRequest(
            "WORK_ITEM_STATUS: BLOCKED\nBUILD_REQUEST\n빌드가 필요합니다."));
    }

    [Fact]
    public void FullBuildSkipsIntegrationInputSnapshots()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-build-full-" + Guid.NewGuid().ToString("N"));
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
            var authorization = new BuildAuthorization(
                "FULL",
                null,
                "Debug",
                NoRestore: false);
            var resolved = MechanicalBuildExecutor.ResolveTarget(root, authorization);

            Assert.Equal(Path.GetFullPath(realProject), resolved);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExplicitIntegrationInputTargetIsRejectedWithoutFallback()
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
                NoRestore: false);
            var resolved = MechanicalBuildExecutor.ResolveTarget(root, authorization);

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
