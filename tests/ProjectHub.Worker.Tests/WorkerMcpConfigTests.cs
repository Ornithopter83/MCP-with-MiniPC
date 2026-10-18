using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerMcpConfigTests
{
    private static AiRoleRunRequest BuildRequest(
        IReadOnlyDictionary<string, string>? variables = null) =>
        new("bounded specialist work", new WorkerAiRoleSettings(
                Provider: "muse", Model: "test-model", Reasoning: "low",
                Transport: "opencode_cli"),
            Path.GetTempPath(), null, CodexSandboxMode.WorkspaceWrite,
            CancellationToken.None, EnvironmentVariables: variables);

    [Fact]
    public void OpenCodeConfig_ExposesWorkerMcpOnlyForAuthorizedParent()
    {
        using var standalone = JsonDocument.Parse(
            OpenCodeCliRunner.BuildInlineConfig(BuildRequest()));
        Assert.False(standalone.RootElement.TryGetProperty("mcp", out _));

        var request = BuildRequest(new Dictionary<string, string>
        {
            ["PROJECTHUB_MCP_BEARER"] = "unit-test-token"
        });
        using var specialist = JsonDocument.Parse(
            OpenCodeCliRunner.BuildInlineConfig(request));
        var server = specialist.RootElement
            .GetProperty("mcp").GetProperty("projecthub");
        Assert.Equal(BridgeServer.WorkerMcpUrl,
            server.GetProperty("url").GetString());
        Assert.Equal("Bearer unit-test-token", server.GetProperty("headers")
            .GetProperty("Authorization").GetString());
    }

    [Fact]
    public void McpRegistration_RefusesHqAndUsesUniqueTokens()
    {
        using var bridge = new BridgeServer();
        Assert.Throws<ArgumentException>(() => bridge.RegisterMcpSession(
            "HQ", "job", Path.GetTempPath(), CancellationToken.None));
        var first = bridge.RegisterMcpSession(
            "DESIGN", "job", Path.GetTempPath(), CancellationToken.None);
        var second = bridge.RegisterMcpSession(
            "HIGH", "job", Path.GetTempPath(), CancellationToken.None);
        Assert.NotEqual(first, second);
        bridge.UnregisterMcpSession(first);
        bridge.UnregisterMcpSession(second);
    }
}
