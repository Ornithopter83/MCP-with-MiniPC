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
}
