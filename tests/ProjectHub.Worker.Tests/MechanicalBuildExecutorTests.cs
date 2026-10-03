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
    public void FullBuildResolvesProjectInsideClone()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-build-full-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "LocalLens.Studio", "LocalLens.Studio.csproj");

        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "<Project />");

        try
        {
            var authorization = new BuildAuthorization(
                "FULL",
                null,
                "Debug",
                NoRestore: false);
            var resolved = MechanicalBuildExecutor.ResolveTarget(root, authorization);

            Assert.Equal(Path.GetFullPath(project), resolved);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExplicitTargetOutsideCloneIsRejectedWithoutFallback()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "projecthub-build-target-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "clone");
        var external = Path.Combine(parent, "inputs", "External.csproj");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(external)!);
        File.WriteAllText(external, "<Project />");

        try
        {
            var relativeOutside = Path.GetRelativePath(root, external);
            var authorization = new BuildAuthorization(
                "TARGET",
                relativeOutside,
                "Debug",
                NoRestore: false);
            var resolved = MechanicalBuildExecutor.ResolveTarget(root, authorization);

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

}