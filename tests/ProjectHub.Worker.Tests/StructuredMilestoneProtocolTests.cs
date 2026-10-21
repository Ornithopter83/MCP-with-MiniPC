using ProjectHub.Worker;
using System.Text.Json;

namespace ProjectHub.Worker.Tests;

public sealed class StructuredMilestoneProtocolTests
{
    private const string Valid = """
[ACTION=WORK]

@@MILESTONE
<ID>M1</>

@@SV
<INSTRUCTIONS>src 폴더에 새 구현을 추가하고 테스트하라.</>

@@HIGH
<INSTRUCTIONS>코드·동작·검증 근거를 검토하라.</>

[RESPONSE=OK]
""";

    [Fact]
    public void StructuredHq_ProducesSvOnlyMilestone()
    {
        var parsed = StructuredHqProtocol.Parse(Valid, false);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(parsed.CompatibilityMessage,
            parsed.Parse, out var milestone, out var error), error);
        Assert.Empty(milestone!.WorkItems);
        Assert.NotEmpty(milestone.SvInstructions);
    }

    [Fact]
    public void StructuredHq_RejectsLegacyDirectWorkAndQa()
    {
        var extra = "\n@@WORK=10\n<PATH>src/a.cs</>\n<INSTRUCTIONS>직접 배정</>\n";
        Assert.False(StructuredHqProtocol.Parse(Valid.Replace("@@HIGH",extra+"@@HIGH"),false).IsValid);
        Assert.False(StructuredHqProtocol.Parse(Valid.Replace("@@HIGH",
            "\n@@QA\n<INSTRUCTIONS>별도 QA</>\n@@HIGH"),false).IsValid);
    }

    [Fact]
    public void TemporaryWorkPaths_RejectTraversalAndSensitiveTargets()
    {
        foreach (var path in new[] { "../outside.cs", "Master-Polish.md",
            ".git/config", ".github/workflows/build.yml", "temp/work.cs",
            ".projecthub/state.json", "src/../private.cs" })
            Assert.False(TemporaryWorkScopeRegistry.IsAllowedPath(path), path);
        Assert.True(TemporaryWorkScopeRegistry.IsAllowedPath("src/MyProject/Program.cs"));
    }

    [Fact]
    public void TemporaryWorkPaths_RejectOverlapsButPermitParallelDistinctFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProjectHubScope", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scopes = new TemporaryWorkScopeRegistry();
            Assert.True(scopes.TryAcquire(root, new[] { "src/a.cs" }, out var first));
            using (first!)
            {
                Assert.False(scopes.TryAcquire(root, new[] { "src" }, out _));
                Assert.False(scopes.TryAcquire(root, new[] { "src/a.cs" }, out _));
                Assert.True(scopes.TryAcquire(root, new[] { "src/b.cs" }, out var second));
                second!.Dispose();
            }
            Assert.True(scopes.TryAcquire(root, new[] { "src/a.cs" }, out var third));
            third!.Dispose();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void McpRoles_ExposeDelegationOnlyToSupervisor()
    {
        var parent = JsonSerializer.Serialize(BridgeServer.ToolDefinitions());
        Assert.Contains("delegate_work", parent);
        Assert.Empty(BridgeServer.ToolDefinitions(workOnly: true));
        Assert.Contains("bearer_token_env_var",
            string.Join("\n", BridgeServer.CodexMcpOverrides()));
    }
}
