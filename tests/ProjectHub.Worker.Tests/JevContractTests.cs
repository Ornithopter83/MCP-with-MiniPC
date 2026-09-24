using System.Net;
using System.Text;
using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class RoleContractBoundaryTests
{
    [Fact]
    public void HqContractIsIsolatedFromOtherRoleOutputContracts()
    {
        var hq = RoleContractLoader.LoadHqFooter();
        Assert.Contains("[ACTION=CONTINUE]", hq);
        Assert.Contains("[GOTO : WORK]", hq);
        Assert.DoesNotContain("[GOTO : RESOURCE]", hq);
        Assert.Contains("Do not use [GOTO=WORK]", hq);
        Assert.DoesNotContain("GOTO : JUDGE", hq);
        Assert.DoesNotContain("GOTO : HQ", hq);
        Assert.DoesNotContain("You are WORK", hq);
        Assert.DoesNotContain("You are RESOURCE", hq);
        Assert.DoesNotContain("[INSTRUCTION]", hq);
        Assert.DoesNotContain("[REPORT]", hq);
    }

    [Fact]
    public void WorkContractOnlyOffersJudgeWhenAvailable()
    {
        var enabled = RoleContractLoader.LoadWorkFooter(true);
        var disabled = RoleContractLoader.LoadWorkFooter(false);
        Assert.Contains("[GOTO : HQ]", enabled);
        Assert.Contains("[GOTO : JUDGE]", enabled);
        Assert.Contains("first return to HQ", enabled);
        Assert.Contains("After you have read", enabled);
        Assert.Contains("NOUL | [QID:IMPLEMENTED]", enabled);
        Assert.Contains("SCORE | [QID:QUALITY]", enabled);
        Assert.Contains("CHOICE | [QID:FORMAT]", enabled);
        Assert.Contains("SCORE | [QID:ASSET_COUNT]", enabled);
        Assert.Contains("CHOICE | [QID:BLOCKER]", enabled);
        Assert.Contains("These examples are illustrative, not restrictions.", enabled);
        Assert.Contains("EVIDENCE:", enabled);
        Assert.DoesNotContain("[GOTO : JUDGE]", disabled);
        Assert.DoesNotContain("NOUL | [QID:IMPLEMENTED]", disabled);
        Assert.Contains("JUDGE is unavailable", disabled);
        Assert.DoesNotContain("[REPORT]", enabled);
        Assert.DoesNotContain("[VALIDATION REQUEST]", enabled);
    }

    [Fact]
    public void HqContractReviewsJudgePlanWithoutCreatingNewWorkerProtocol()
    {
        var hq = RoleContractLoader.LoadHqFooter();
        Assert.Contains("asks you to review a draft", hq);
        Assert.Contains("Return the reviewed judgment plan to WORK", hq);
        Assert.Contains("NOUL | [QID:RESTART_CLEAN]", hq);
        Assert.Contains("SCORE | [QID:PLAYBACK_COMPLETION]", hq);
        Assert.Contains("CHOICE | [QID:PROGRESSION_BLOCKER]", hq);
        Assert.Contains("not quotas or mandatory proportions", hq);
        Assert.DoesNotContain("[JUDGE PLAN]", hq);
        Assert.DoesNotContain("[VALIDATION REQUEST]", hq);
    }

    [Fact]
    public void WorkContractJevQuestionExamplesMatchTheTransportParser()
    {
        const string request = """
            NOUL | [QID:IMPLEMENTED] Is the requested behavior implemented?
            PASS: YES >= 0.90
            EVIDENCE: src/implementation.cs
            SCOPE: requested behavior only
            COUNTEREXAMPLE: a missing required case
            SCORE | [QID:QUALITY] Rate the required behavior.
            0 = absent
            1 = partial
            CHOICE | [QID:FORMAT] Is the response format valid?
            YES = valid
            NO = invalid
            """;

        Assert.True(JudgeTransportContract.TryParse(request, out var parsed, out var error), error);
        Assert.Equal(new[] { "IMPLEMENTED", "QUALITY", "FORMAT" }, parsed.Questions.Select(question => question.Id));
        Assert.Equal(3, parsed.Questions.Count);
        Assert.Contains("EVIDENCE: src/implementation.cs", parsed.Questions[0].Instructions);
    }

    [Fact]
    public void ResourceAndJudgeContractsKeepTheirFixedReturnSemantics()
    {
        var resource = RoleContractLoader.LoadResourceFooter();
        Assert.Contains("returns mechanically to the same WORK session", resource);
        Assert.DoesNotContain("[GOTO : HQ]", resource);
        Assert.DoesNotContain("[GOTO : JUDGE]", resource);
        var judge = RoleContractLoader.LoadJudgeFooter();
        Assert.Contains("[GOTO : WORK]", judge);
        Assert.DoesNotContain("[JUDGMENT]", judge);
    }

    [Fact]
    public void HqPromptSeparatesMechanicalHeaderFromOpaqueInboundAndOnlyAllowsWork()
    {
        var prompt = RoleContractLoader.BuildHqPrompt("WORK_REPORT", "opaque report");
        Assert.Contains("[ROLE : HQ]", prompt);
        Assert.Contains("[AVAILABLE GOTO]\n[GOTO : WORK]", prompt);
        Assert.DoesNotContain("[GOTO : RESOURCE]", prompt);
        Assert.DoesNotContain("[GOTO : JUDGE]", prompt);
        Assert.Contains("opaque report", prompt);
    }

    [Fact]
    public void LegacyWebExposesOnlyLegacyActionAndNextWire()
    {
        var prompt = LegacyWebActionContract.BuildInstructions(judgeEnabled: true);
        Assert.Contains("[ACTION=CONTINUE]", prompt);
        Assert.Contains("[ACTION=PAUSE]", prompt);
        Assert.Contains("[ACTION=END]", prompt);
        Assert.Contains("[NEXT : WEB]", prompt);
        Assert.Contains("[NEXT : JEV]", prompt);
        Assert.DoesNotContain("ACTION=HQ", prompt);
        Assert.DoesNotContain("NEXT : IMPLEMENTER", prompt);
        Assert.DoesNotContain("NEXT : HIGH_LEVEL", prompt);
        Assert.DoesNotContain("NEXT : COORDINATOR", prompt);
    }

    [Theory]
    [InlineData("[ACTION=CONTINUE]\ncontinue body", LegacyWebActionKind.Continue)]
    [InlineData("[ACTION=PAUSE]\nreason", LegacyWebActionKind.Pause)]
    [InlineData("[ACTION=END]\nreport", LegacyWebActionKind.End)]
    public void LegacyActionParserAcceptsOnlyPublicActions(string text, LegacyWebActionKind expected)
        => Assert.Equal(expected, LegacyWebActionContract.Parse(text, strict: true).Kind);

    [Theory]
    [InlineData("[ACTION=HQ]\nbody")]
    [InlineData("[ACTION=BEGIN]\nbody")]
    public void LegacyActionParserRejectsNewRoleRoutingControls(string text)
        => Assert.Equal(LegacyWebActionKind.ProtocolError, LegacyWebActionContract.Parse(text, strict: true).Kind);

    [Fact]
    public void LegacyNextContractAllowsOnlyWebAndJev()
    {
        var report = LegacyWebJevContract.ParseNext("[NEXT : WEB]\n[REPORT]\n내용");
        Assert.Equal(NextRoute.Web, report.Route);
        Assert.Null(LegacyWebJevContract.ValidateStructure(report));
        Assert.Equal("NEXT_INVALID", LegacyWebJevContract.ParseNext("[NEXT : COORDINATOR]\n[REPORT]\n내용").Error);
        Assert.Equal("NEXT_DUPLICATE", LegacyWebJevContract.ParseNext("[NEXT : WEB]\n[REPORT]\n내용\n[NEXT : JEV]").Error);
    }
}

public sealed class JudgeTransportContractTests
{
    [Fact]
    public void ParserPreservesPassThresholdAsOpaqueTextWithoutRangeJudgment()
    {
        const string input = "NOUL | claim\nPASS: YES >= 99\nSCORE | status\n1 = low\n3 = high\nPASS: SCORE >= 999";
        Assert.Equal(input, JudgeTransportContract.ExtractRequest(input));
        Assert.True(JudgeTransportContract.TryParse(JudgeTransportContract.ExtractRequest(input), out var request, out var error), error);
        Assert.Equal(2, request.Questions.Count);
        Assert.Contains("PASS: YES >= 99", request.Questions[0].Instructions);
        Assert.Contains("PASS: SCORE >= 999", request.Questions[1].Instructions);
        Assert.Equal(new[] { "low", "high" }, request.Questions[1].Criteria);
    }

    [Fact]
    public void ParserExtractsProviderFieldsAndKeepsEvidenceInstructions()
    {
        const string input = "NOUL | [QID:C2] atomic claim\nEVIDENCE: source.cs\nSCOPE: this method only\nPASS: YES >= 0.8\nCHOICE | deploy state\nA = ready\nB = pending\nPASS: A or B";
        Assert.True(JudgeTransportContract.TryParse(input, out var parsed, out var error), error);
        Assert.Equal("C2", parsed.Questions[0].Id);
        Assert.Contains("EVIDENCE: source.cs", parsed.Questions[0].Instructions);
        Assert.Contains("PASS: YES >= 0.8", parsed.Questions[0].Instructions);
        Assert.Equal(new Dictionary<string, string> { ["A"] = "ready", ["B"] = "pending" }, parsed.Questions[1].ChoiceCriteria);
        Assert.Contains("PASS: A or B", parsed.Questions[1].Instructions);
    }

    [Fact]
    public void CodexUsageParserDoesNotDoubleCountCumulativeSnapshots()
    {
        var snapshots = """
            {"type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":20,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}
            {"type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":110,"cached_input_tokens":22,"output_tokens":25,"total_tokens":135},"last_token_usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}}}
            """;
        var usage = CodexCliRunner.ExtractUsage(snapshots);
        Assert.Equal(110, usage.InputTokens);
        Assert.Equal(22, usage.CachedInputTokens);
        Assert.Equal(135, usage.ProviderTotalTokens);
    }

    [Fact]
    public void HistoryCardFormatter_UsesMechanicalPreviewUsageAndDetectedFiles()
    {
        Assert.Equal("첫 줄 둘째 줄", WorkerHistoryCardFormatter.Preview("첫 줄\n\t둘째 줄"));

        var usage = new CodexUsage(920, 210, 164, 80, 1164, 1284, true);
        Assert.Equal("토큰 · 총 1,284 · 입력 920 · 캐시 210 · 출력 164 · 추론 80", WorkerHistoryCardFormatter.TokenLine(usage));
        Assert.Equal("토큰 · 미제공", WorkerHistoryCardFormatter.TokenLine(CodexUsage.Empty));

        var files = new[]
        {
            new CodexCliFile("C:\\work\\projecthub-smoke.txt", "projecthub-smoke.txt", "text/plain", 19),
            new CodexCliFile("C:\\work\\other.txt", "other.txt", "text/plain", 4)
        };
        Assert.Equal("파일 · 2개 감지 · projecthub-smoke.txt 외 1개", WorkerHistoryCardFormatter.FileLine(files));
        Assert.Equal("파일 · 감지 없음", WorkerHistoryCardFormatter.FileLine(Array.Empty<CodexCliFile>()));
    }

    [Fact]
    public async Task NativeJevAdapterReturnsRawProviderResponseWithoutJudging()
    {
        var prior = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "test-only");
        try
        {
            const string body = "{\"model\":\"jev-test\",\"usage\":{\"input_tokens\":120},\"answers\":{\"C1\":{\"type\":\"Noul\",\"noul\":0.1}}}";
            var request = new JudgeRequest("goal", 1, "C:\\work", "result", "NOUL | claim\nPASS: YES >= 0.8", Array.Empty<CodexCliFile>(), "WEB", null);
            var result = await new JevJudgeRunner(new StubHandler(body)).ReviewRawAsync(request, new(true, "jev", "https://example.test/judge", 30), CancellationToken.None);
            Assert.Null(result.ErrorCode);
            Assert.Equal(body, result.RawResponse);
            Assert.True(result.Telemetry!.UsageKnown);
        }
        finally { Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", prior); }
    }

    [Fact]
    public void EvidenceEnvelopePackagesEvidenceWithoutJudgingThresholds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jev-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ClaimTests.cs");
        File.WriteAllText(path, "// token=do-not-leak\nAssert.True(true);\n");
        try
        {
            Assert.True(JudgeTransportContract.TryParse("NOUL | claim\nEVIDENCE: ClaimTests.cs\nPASS: YES >= 0.8", out var parsed, out var error), error);
            var request = new JudgeRequest("goal", 1, directory, "summary", "", Array.Empty<CodexCliFile>(), "GIT", null, "job-1");
            var envelope = JevEvidenceEnvelope.Create(request, parsed);
            Assert.Equal("job-1", envelope.JobId);
            Assert.Contains(envelope.QuestionEvidence.Single().EvidenceIds, id => envelope.Evidence.Any(item => item.EvidenceId == id && item.Kind == EvidenceKind.Test));
            Assert.Contains("[REDACTED]", envelope.Evidence.Single(item => item.Kind == EvidenceKind.Test).Excerpt);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

public sealed class JevNegativeControlFixtureTests
{
    [Fact]
    public void NegativeControlFixtureContainsIsolatedCasesAndSeparateExpectedLayers()
    {
        var assembly = typeof(JevNegativeControlFixtureTests).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith("JevNegativeControls.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var document = JsonDocument.Parse(stream);
        var cases = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(6, cases.Length);
        Assert.Equal(cases.Length, cases.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.All(cases, item => Assert.Equal("synthetic-ministore-fixture", item.GetProperty("isolation").GetString()));
    }
}
