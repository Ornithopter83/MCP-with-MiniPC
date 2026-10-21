using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class CompactMilestoneRoutingTests
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
    public void CompactHq_DelegatesSingleSupervisorWithoutNumberedWork()
    {
        var parsed = HqTextProtocol.Parse(Valid);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone, out var error), error);
        Assert.NotNull(milestone);
        Assert.Equal("M1", milestone.Id);
        Assert.Empty(milestone.WorkItems);
        Assert.Contains("새 구현", milestone.SvInstructions);
        Assert.Contains("검토", milestone.HighInstructions);
    }

    [Theory]
    [InlineData("@@SV", "@@WORK=10")]
    [InlineData("@@SV", "@@QA")]
    [InlineData("@@HIGH", "@@QA")]
    public void CompactHq_RejectsDeprecatedAndMissingRoleSections(string from, string to)
    {
        var parsed = HqTextProtocol.Parse(Valid.Replace(from, to, StringComparison.Ordinal));
        Assert.False(parsed.IsValid);
    }

    [Fact]
    public void CompactHq_RejectsDuplicateSupervisor()
    {
        var body = "\n@@SV\n<INSTRUCTIONS>중복 구현</>\n";
        var parsed = HqTextProtocol.Parse(Valid.Replace("@@HIGH", body + "@@HIGH"));
        Assert.False(parsed.IsValid);
    }

    [Fact]
    public void CompactHq_ResourceRemainsOptionalImageSidecar()
    {
        var withImage = Valid.Replace("@@SV",
            "@@RESOURCE=0\n<TYPE>image</>\n<TARGET_PATH>assets/a.png</>\n" +
            "<INSTRUCTIONS>바다 위 다리를 그려라.</>\n\n@@SV");
        var parsed = HqTextProtocol.Parse(withImage);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone, out var error), error);
        Assert.Single(milestone!.Resources);
        Assert.Equal("assets/a.png", milestone.Resources["0"].TargetPath);
    }
}
