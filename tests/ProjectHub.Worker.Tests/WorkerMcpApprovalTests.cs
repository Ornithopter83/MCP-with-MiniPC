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
    public void HighAndSupervisor_CanDelegate_ButChildCannot()
    {
        Assert.True(BridgeServer.CanRegisterRole("SV"));
        Assert.True(BridgeServer.CanRegisterRole("HIGH"));
        Assert.True(BridgeServer.CanRegisterRole("WORK"));
        Assert.False(BridgeServer.CanRegisterRole("QA"));
        Assert.False(BridgeServer.CanRegisterRole("HQ"));
    }

    [Fact]
    public void ReadOnlyChildInstructions_NeverGrantWriteAccess()
    {
        var prompt = RoleContractLoader.BuildDirectWorkPrompt(
            "TEMP", "조사", new[] { "README.md" }, @"C:\test", readOnly: true);
        Assert.Contains("읽기 전용 WORK", prompt);
        Assert.DoesNotContain("작업에 필요한 모든 권한은 승인한다", prompt);
    }

    [Fact]
    public void TemporaryWork_DoesNotGetDelegateWorkTool()
    {
        Assert.Empty(BridgeServer.ToolDefinitions(workOnly: true));
        Assert.Single(BridgeServer.ToolDefinitions(workOnly: false));
    }
}
