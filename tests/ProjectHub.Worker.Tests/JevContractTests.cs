using System.Net;
using System.Text;
using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class RoleContractBoundaryTests
{
    [Fact]
    public void HqContractExposesOnlyDurableHqControls()
    {
        var hq = RoleContractLoader.LoadHqFooter();
        Assert.Contains("[ACTION=CONTINUE]", hq);
        Assert.Contains("[ACTION=PAUSE]", hq);
        Assert.Contains("[ACTION=END]", hq);
        Assert.Contains("[GOTO : WORK]", hq);
        Assert.Contains("사용자의 요청에서 설계 기획에 관련된 부분은 반드시 HQ가 작업 수행한 뒤 구체화하여 WORK에 전달한다", hq);
        Assert.DoesNotContain("[GOTO : RESOURCE]", hq);
        Assert.DoesNotContain("[GOTO : JUDGE]", hq);
        Assert.Contains("Worker의 기계적 사실은 관측값이며 의미 판단이 아니다.", hq);
        Assert.Contains("기계적 대기 작업 때문에 END 판단을 미루지 않는다.", hq);
        Assert.Contains("JUDGE용 Form", hq);
        Assert.Contains("NOUL | QID:<id>", hq);
        Assert.Contains("SCORE | QID:<id>", hq);
        Assert.Contains("CHOICE | QID:<id>", hq);
        Assert.Contains("CHOICE의 선택지 키는 영문자로 시작", hq);
        Assert.Contains("한글 선택지 키는 사용하지 않는다.", hq);
        Assert.Contains("A=<기준>", hq);
        Assert.Contains("B=<기준>", hq);
        Assert.Contains("이미 관측 사실로 확정된 항목은 다시 JUDGE 문항으로 만들지 않는다.", hq);
        Assert.Contains("이전 판정 뒤 근거가 의미 있게 바뀌면", hq);
        Assert.DoesNotContain("WORK가 의미 판정 질문을 올리면", hq);
    }

    [Fact]
    public void WorkContractKeepsStableRoutingAndJudgeBoundary()
    {
        var work = RoleContractLoader.LoadWorkFooter();

        Assert.Contains("[GOTO : HQ]", work);
        Assert.Contains("[GOTO : JUDGE]", work);
        Assert.Contains("[GOTO : RESOURCE]", work);
        Assert.Contains("관측 사실 확인이 아니라", work);
        Assert.Contains("JUDGE용 Form", work);
        Assert.DoesNotContain("JUDGE_ON", work);
        Assert.DoesNotContain("JUDGE_OFF", work);
        Assert.DoesNotContain("사용 가능", work);
    }

    [Fact]
    public void ActiveRoleContractsStayStructuralAndExampleFree()
    {
        var contracts = new[]
        {
            RoleContractLoader.LoadHqFooter(),
            RoleContractLoader.LoadWorkFooter()
        };

        foreach (var contract in contracts)
        {
            Assert.DoesNotContain("Example:", contract, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Illustrative", contract, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("src/", contract, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tests/", contract, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".cs", contract, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".log", contract, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WorkResourceContractStatesGeneralTransportBoundary()
    {
        var work = RoleContractLoader.LoadWorkFooter();
        Assert.Contains("한 요청에는 한 종류의 새로운 생성 리소스만 포함한다.", work);
        Assert.Contains("상태 조회·저장 지시·Worker 운영 지시는 넣지 않는다.", work);
        Assert.Contains("유효한 GOTO 제어행만 라우팅을 변경", work);
        Assert.DoesNotContain("예시:", work, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HqPromptUsesOnlyRequiredWorkGraphMetadata()
    {
        var prompt = RoleContractLoader.BuildHqPrompt(
            "WORK_REPORT",
            "불투명 보고",
            new WorkGraphPromptContext(3, 1, "abc123"));

        Assert.Contains("역할: HQ", prompt);
        Assert.Contains("입력 유형: WORK_REPORT", prompt);
        Assert.Contains("WorkGraph revision: 3", prompt);
        Assert.Contains("최대 동시 WORK: 1", prompt);
        Assert.DoesNotContain("허용 목적지:", prompt);
        Assert.DoesNotContain("병렬 WorkGraph 사용:", prompt);
        Assert.DoesNotContain("[ROLE :", prompt);
        Assert.Contains("불투명 보고", prompt);
    }

    [Fact]
    public void WorkPromptUsesOnlyRequiredWorkItemMetadata()
    {
        var prompt = RoleContractLoader.BuildWorkPrompt(
            "RESOURCE_QUEUED",
            "기계적 상태",
            new WorkItemPromptContext(
                "W1",
                WorkItemKind.Normal,
                "작업",
                Array.Empty<string>(),
                "abc123",
                "branch",
                "worktree"));

        Assert.Contains("역할: WORK", prompt);
        Assert.Contains("입력 유형: RESOURCE_QUEUED", prompt);
        Assert.Contains("workItemId: W1", prompt);
        Assert.DoesNotContain("판정 사용 가능:", prompt);
        Assert.DoesNotContain("리소스 사용 가능:", prompt);
        Assert.DoesNotContain("병렬 WorkItem 사용:", prompt);
        Assert.DoesNotContain("[ROLE :", prompt);
    }

    [Fact]
    public void JudgeTransportAcceptsPlainAndLegacyQidSyntax()
    {
        const string plain = "NOUL | QID:PLAIN_ID Is the behavior present?\nPASS: YES >= 0.9";
        Assert.True(JudgeTransportContract.TryParse(plain, out var plainParsed, out var plainError), plainError);
        Assert.Equal("PLAIN_ID", Assert.Single(plainParsed.Questions).Id);

        const string legacy = "NOUL | [QID:LEGACY_ID] Is the behavior present?\nPASS: YES >= 0.9";
        Assert.True(JudgeTransportContract.TryParse(legacy, out var legacyParsed, out var legacyError), legacyError);
        Assert.Equal("LEGACY_ID", Assert.Single(legacyParsed.Questions).Id);
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
    public void ChoiceParserRejectsNonAsciiChoiceKeys()
    {
        const string input = "CHOICE | QID:STATE 상태를 선택하라.\n성공=완료\n실패=미완료";
        Assert.False(JudgeTransportContract.TryParse(input, out _, out var error));
        Assert.Equal("CHOICE_CRITERIA_MISSING", error);
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
