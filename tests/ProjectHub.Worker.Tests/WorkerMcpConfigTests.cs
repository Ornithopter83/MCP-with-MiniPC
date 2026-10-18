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
    public void CodexOneShot_RequiresMcpReadyBeforeToolBinding()
    {
        var overrides = BridgeServer.CodexMcpOverrides();
        Assert.Contains("mcp_servers.projecthub.required=true", overrides);
        Assert.Contains("mcp_servers.projecthub.startup_timeout_sec=15", overrides);
        Assert.Contains("mcp_servers.projecthub.tool_timeout_sec=720", overrides);
        Assert.Contains(overrides, item => item.Contains(
            "bearer_token_env_var", StringComparison.Ordinal));
    }

    [Fact]
    public void McpCommandDefinitionsExposeNativeCaptureAndExplicitDesignOptions()
    {
        var json = JsonSerializer.Serialize(BridgeServer.ToolDefinitions());
        Assert.Contains("native_capture", json);
        Assert.Contains("command_catalog", json);
        Assert.Contains("visual_foundry", json);
        Assert.Contains("Immersive", json);
        Assert.Contains("playful", json);
    }

    [Fact]
    public void McpRegistration_ReportsUninitializedVsDiscoveredWithoutSecrets()
    {
        using var bridge = new BridgeServer();
        var activity = new List<string>();
        bridge.McpActivity = (role, detail) => activity.Add(role + " " + detail);
        var token = bridge.RegisterMcpSession(
            "DESIGN", "job", Path.GetTempPath(), CancellationToken.None);
        bridge.UnregisterMcpSession(token);
        Assert.Contains(activity, item => item.Contains("SESSION_REGISTERED"));
        Assert.Contains(activity, item => item.Contains(
            "SESSION_FINISHED initialized=False tools_listed=False tool_calls=0"));
        Assert.DoesNotContain(activity, item => item.Contains(token, StringComparison.Ordinal));
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
