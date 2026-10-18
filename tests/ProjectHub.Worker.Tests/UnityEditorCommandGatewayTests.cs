using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityEditorCommandGatewayTests
{
    [Fact]
    public async Task UnconfirmedCapabilityNeverExecutesAnExpression()
    {
        var probe = new UnityToolchainPreflight.Result(
            true, "UNITY_EDITOR_CONNECTED_NO_CATALOG", "connection only");
        var snapshot = new UnityEditorCommandGateway.Capabilities(
            probe, null, false, "catalog and detail unverified");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => UnityEditorCommandGateway.ExecuteEvalAsync(
                Path.GetTempPath(), snapshot, "return 1;", "1",
                CancellationToken.None));
        Assert.Contains("EVAL_UNVERIFIED", error.Message);
    }
}
