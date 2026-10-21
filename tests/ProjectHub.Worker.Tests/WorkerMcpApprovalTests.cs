using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerMcpApprovalTests
{
    [Fact]
    public void ProjectHubMcp_PreapprovesOnlyScopedDelegationTool()
    {
        var options = BridgeServer.CodexMcpOverrides();
        Assert.Contains(
            "mcp_servers.projecthub.tools.delegate_work.approval_mode=\"approve\"",
            options);
        Assert.DoesNotContain(options, option =>
            option.Contains("default_tools_approval_mode", StringComparison.Ordinal));
        Assert.DoesNotContain(options, option =>
            option.Contains("approval_policy=\"never\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TemporaryWork_DoesNotGetDelegateWorkTool()
    {
        Assert.Empty(BridgeServer.ToolDefinitions(workOnly: true));
        Assert.Single(BridgeServer.ToolDefinitions(workOnly: false));
    }
}
