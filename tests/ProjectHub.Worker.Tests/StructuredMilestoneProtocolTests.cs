using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class StructuredMilestoneProtocolTests
{
    private const string Modern = """
        [ACTION=WORK]

        @@MILESTONE
        <ID>M3</>

        @@RESOURCE=0
        <TYPE>image</>
        <TARGET_PATH>assets/art/character.png</>
        <WIDTH>4096</>
        <HEIGHT>4096</>
        <COLUMNS>8</>
        <ROWS>8</>
        <ALPHA>required</>
        <INSTRUCTIONS>
        바탕색 없는 sprite sheet.
        1행: idle, 2행: run, 상세: https://example.com
        </>

        @@WORK=54
        <PATH>scripts/player.gd</>
        <INSTRUCTIONS>
        이미지가 적합하면 연결한다. base: './'
        </>

        @@QA
        <INSTRUCTIONS>
        게임 실행 후 확인한다.
        </>

        @@HIGH
        <INSTRUCTIONS>
        결함을 수정하고 다시 검증한다.
        </>

        [RESPONSE=OK]
        """;

    [Fact]
    public void StructuredHq_ReadsDeclaredFieldsButNotNestedProse()
    {
        var parsed = HqTextProtocol.Parse(Modern);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.True(MilestoneDefinitionContract.TryBuild(
            parsed.CompatibilityMessage, parsed.Parse, out var milestone, out var error), error);
        Assert.NotNull(milestone);
        Assert.Equal("M3", milestone!.Id);
        var resource = Assert.Single(milestone.Resources.Values);
        Assert.Equal(4096, resource.Width);
        Assert.Equal(4096, resource.Height);
        Assert.Equal(8, resource.Columns);
        Assert.Equal(8, resource.Rows);
        Assert.True(resource.RequireAlpha);
        Assert.Contains("1행: idle", resource.Body);
        Assert.Contains("base: './'", MilestoneDefinitionContract.BuildWorkContext(milestone.WorkItems["54"]));
    }

    [Fact]
    public void StructuredHq_RejectsUnclosedOrUnknownMiddleFields()
    {
        var malformed = HqTextProtocol.Parse(Modern.Replace(
            "<PATH>scripts/player.gd</>", "<PATH>scripts/player.gd</PATH>"));
        Assert.False(malformed.IsValid);
        var unknown = HqTextProtocol.Parse(Modern.Replace(
            "<WIDTH>4096</>", "<WIDTH>4096</>\n<SECRET>value</>"));
        Assert.False(unknown.IsValid);
    }

    [Fact]
    public void PartialHqRepair_PreservesPathsAndRejectsDroppedPaths()
    {
        var malformed = Modern.Replace(
            "<PATH>scripts/player.gd</>", "<PATH>scripts/player.gd</PATH>");
        Assert.True(HqResponseRecoveryContract.TryRepairClosingTags(
            malformed, out var repaired));
        Assert.True(HqTextProtocol.Parse(repaired).IsValid);
        Assert.True(HqResponseRecoveryContract.PreservesPaths(malformed, repaired));
        Assert.False(HqResponseRecoveryContract.PreservesPaths(
            malformed, repaired.Replace("<PATH>scripts/player.gd</>", "")));
    }

    [Fact]
    public void ReportProtocol_ParsesStatusOnlyAndKeepsProseOpaque()
    {
        var response = RoleTextProtocol.BuildResult(
            "issue", "원본: FAIL\n세부 검사: 실제 실패", issues: new[] { "배경 크기 불일치" });
        var parsed = RoleTextProtocol.ParseQa(response);
        Assert.True(parsed.IsValid, string.Join(", ", parsed.Errors));
        Assert.Equal("issue", parsed.Status);
        Assert.Contains("원본: FAIL", parsed.Summary);
        Assert.Contains("배경 크기 불일치", parsed.Issues);
    }

    [Fact]
    public void InvalidQaAndHighReports_ArePreservedWithoutSyntheticSuccess()
    {
        const string raw = "ACTION_RESULT: FAIL\nSTATUS: issue\nGodot smoke 실패";
        Assert.Equal(raw, MilestoneDefinitionContract.NormalizeQaReport(0, raw, null));
        Assert.Equal(raw, MilestoneDefinitionContract.NormalizeHighReport(0, raw, null));
        Assert.Null(MilestoneDefinitionContract.ReadQaStatus(raw));
    }

    [Fact]
    public void ResourceValidator_RejectsIncorrectDimensionsBeforeMoving()
    {
        var temp = Path.Combine(Path.GetTempPath(), "ph-artifact-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var header = new byte[26];
            new byte[] {137,80,78,71,13,10,26,10}.CopyTo(header, 0);
            System.Text.Encoding.ASCII.GetBytes("IHDR").CopyTo(header, 12);
            header[18] = 4; header[19] = 230; // 1254 width
            header[22] = 4; header[23] = 230; // 1254 height
            header[25] = 6; // RGBA
            File.WriteAllBytes(temp, header);

            var spec = new MilestoneResourceDefinition("0", "IMAGE", "assets/art/file.png", "{}", "{}")
            {
                Width = 4096, Height = 4096, Columns = 8, Rows = 8, RequireAlpha = true
            };
            Assert.False(ResourceArtifactValidator.TryValidate(spec, new[] { temp }, out var problem));
            Assert.Contains("RESOURCE_DIMENSIONS_MISMATCH", problem);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
