using System.Net;
using System.Text;
using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class JevContractTests
{
    [Fact]
    public void ParseNext_UsesFirstDirectiveAndRequiresReportForWeb()
    {
        var directive=JevContract.ParseNext("[NEXT : WEB]\n[REPORT]\n내용\n[NEXT : JEV]");
        Assert.Equal(NextRoute.Web,directive.Route);
        Assert.Null(JevContract.ValidateStructure(directive));
        Assert.Equal("REPORT_MISSING",JevContract.ValidateStructure(JevContract.ParseNext("[NEXT : WEB]\n내용")));
    }

    [Fact]
    public void CoordinatorFooter_UsesGotoAndKeepsJudgeTransportTyped()
    {
        var footer = JevContract.LoadCoordinatorFooter();
        Assert.DoesNotContain("[ACTION=HQ]", footer);
        Assert.Contains("[GOTO : JUDGE]", footer);
        Assert.Contains("Worker는 판단하지 않는다", footer);
        Assert.Contains("[GOTO : JUDGE]", footer);

        var report = JevContract.ParseNext("[NEXT : COORDINATOR]\n[REPORT]\n검증 완료", coordinatorMode: true);
        Assert.Equal(NextRoute.Coordinator, report.Route);
        Assert.Null(JevContract.ValidateCoordinatorStructure(report));

        var judge = JevContract.ParseNext("[NEXT : JEV]\n[VALIDATION REQUEST]\nNOUL | 검증 질문\nPASS: YES >= 0.8", coordinatorMode: true);
        Assert.Equal(NextRoute.Jev, judge.Route);
        Assert.Null(JevContract.ValidateCoordinatorStructure(judge));
        Assert.True(JevContract.TryParseValidation(JevContract.ExtractValidationRequest(judge.Body), out _, out var error), error);

        Assert.Equal("NEXT_WRONG_MODE", JevContract.ParseNext("[NEXT : WEB]\n[REPORT]\n내용", coordinatorMode: true).Error);
        Assert.Equal("NEXT_WRONG_MODE", JevContract.ParseNext("[NEXT : COORDINATOR]\n[REPORT]\n내용").Error);
        Assert.Equal("NEXT_DUPLICATE", JevContract.ParseNext("[NEXT : COORDINATOR]\n[REPORT]\n내용\n[NEXT : JEV]", coordinatorMode: true).Error);
        Assert.Equal("REPORT_PROTOCOL_ERROR", JevContract.ValidateStructure(JevContract.ParseNext("[NEXT : COORDINATOR]\n내용", coordinatorMode: true), reportOnly: true));
        Assert.Equal("COORDINATOR_REPORT_AMBIGUOUS", JevContract.ValidateCoordinatorStructure(JevContract.ParseNext("[NEXT : COORDINATOR]\n[REPORT]\n내용\n[VALIDATION REQUEST]", coordinatorMode: true)));
        Assert.Equal("JEV_REQUEST_AMBIGUOUS", JevContract.ValidateCoordinatorStructure(JevContract.ParseNext("[NEXT : JEV]\n서문\n[VALIDATION REQUEST]\n질문", coordinatorMode: true)));
        Assert.Equal("JEV_REPORT_NOT_COORDINATOR", JevContract.ValidateCoordinatorStructure(judge, reportOnly: true));
    }

    [Fact]
    public void ParseValidation_AcceptsNoulOneLineAndScoreNormalizesHumanThreshold()
    {
        Assert.True(JevContract.TryParseValidation("[VALIDATION REQUEST]\nNOUL | 완료율 | PASS: YES >= 0.8\nSCORE | 상태 점수\n1 = 낮음\n2 = 높음\nPASS: SCORE >= 2",out var request,out var error),error);
        Assert.Equal(2,request.Questions.Count);
        Assert.Equal(0.8,request.Questions[0].Rule.Number);
        Assert.Equal(2,request.Questions[1].Rule.Number);
    }

    [Fact]
    public void ParseValidation_PreservesAtomicQuestionEvidenceScopeAndCounterexample()
    {
        const string footer="""
            [NEXT : JEV]

            [VALIDATION REQUEST]

            - NOUL | [HIGH] ResetRound() keeps the current brick layout.
              EVIDENCE: GameWorld.cs / ResetRound() — does not call LoadStage().
              SCOPE: Only the existing stage layout after losing a life.
              COUNTEREXAMPLE: Any call clears or recreates the brick list.
              PASS: YES >= 0.80
            """;

        var directive=JevContract.ParseNext(footer);
        Assert.Equal(NextRoute.Jev,directive.Route);
        Assert.Null(JevContract.ValidateStructure(directive));
        Assert.True(JevContract.TryParseValidation(JevContract.ExtractValidationRequest(directive.Body),out var request,out var error),error);
        var instructions=request.Questions.Single().Instructions;
        Assert.Contains("[HIGH] ResetRound() keeps the current brick layout.",instructions);
        Assert.Contains("EVIDENCE: GameWorld.cs / ResetRound()",instructions);
        Assert.Contains("SCOPE: Only the existing stage layout",instructions);
        Assert.Contains("COUNTEREXAMPLE: Any call clears",instructions);
        Assert.DoesNotContain("PASS:",instructions);
        Assert.Equal(0.80,request.Questions.Single().Rule.Number);
    }

    [Fact]
    public void ParseValidation_PreservesQuestionIdForTargetedRecheck()
    {
        Assert.True(JevContract.TryParseValidation("NOUL | [QID:C2] [HIGH] one atomic claim\nEVIDENCE: E2 updated\nPASS: YES >= 0.80",out var request,out var error),error);
        Assert.Equal("C2",request.Questions.Single().Id);
        Assert.DoesNotContain("[QID:C2]",request.Questions.Single().Instructions);
    }

    [Fact]
    public void ParseValidation_RejectsDuplicateQuestionIds()
    {
        Assert.False(JevContract.TryParseValidation("NOUL | [QID:C1] first claim\nPASS: YES >= 0.60\nNOUL | [QID:C1] second claim\nPASS: YES >= 0.70",out _,out var error));
        Assert.Equal("QUESTION_ID_DUPLICATE",error);
    }

    [Fact]
    public void ParseValidation_RejectsNonContiguousScoreAndUndefinedChoice()
    {
        Assert.False(JevContract.TryParseValidation("SCORE | 점수\n1 = 낮음\n3 = 높음\nPASS: SCORE >= 2",out _,out var scoreError));
        Assert.Equal("SCORE_CRITERIA_NOT_CONTIGUOUS",scoreError);
        Assert.False(JevContract.TryParseValidation("CHOICE | 상태\nA = 승인\nPASS: 승인 또는 거절",out _,out var choiceError));
        Assert.Equal("CHOICE_ALLOWED_UNDEFINED",choiceError);
    }

    [Fact]
    public void EvidenceEnvelope_ProvidesStableReferencesDigestsAndSeparateSummaryProvenance()
    {
        var directory=Path.Combine(Path.GetTempPath(),"jev-evidence-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"ClaimTests.cs");
        File.WriteAllText(path,"// token=do-not-leak\nAssert.True(true);\n");
        try
        {
            var validation=JevContract.TryParseValidation("NOUL | claim\nEVIDENCE: ClaimTests.cs\nPASS: YES >= 0.8",out var parsed,out var error);
            Assert.True(validation,error);
            var request=new JudgeRequest("goal",1,directory,"the tests passed",JevContract.ExtractValidationRequest("[VALIDATION REQUEST]\nNOUL | claim\nEVIDENCE: ClaimTests.cs\nPASS: YES >= 0.8"),Array.Empty<CodexCliFile>(),"GIT","abc","job-1");
            var envelope=JevEvidenceEnvelope.Create(request,parsed);
            Assert.Equal("job-1",envelope.JobId);
            Assert.True(envelope.HasDirectEvidence("C1"));
            Assert.Contains("[REDACTED]",envelope.Evidence.Single(x=>x.Kind==EvidenceKind.Test).Excerpt);
            Assert.Equal(EvidenceProvenance.SummaryOnly,envelope.Evidence.Single(x=>x.Provenance==EvidenceProvenance.SummaryOnly).Provenance);
            Assert.StartsWith("sha256:",envelope.Evidence.Single(x=>x.Kind==EvidenceKind.Test).ContentDigest);
            Assert.Equal("BLOCKED_BY_TOOL",envelope.VerificationLayers["UI_BROWSER"].Status);
            Assert.Equal("NOT_RUN",envelope.VerificationLayers["ENGINE_HEADLESS"].Status);
            Assert.Equal("NOT_RECORDED",envelope.VerificationLayers["HUMAN_UX"].Status);
        }
        finally { Directory.Delete(directory,true); }
    }

    [Fact]
    public void EvidenceEnvelope_RecordsCliCommandExitSeparatelyFromImplementerClaims()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jev-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string command = "dotnet test ProjectHub.sln";
            Assert.True(JevContract.TryParseValidation("NOUL | Did dotnet test ProjectHub.sln succeed?\nPASS: YES >= 0.8", out var validation, out var error), error);
            var request = new JudgeRequest("goal", 1, directory, "tests passed", "NOUL | Did dotnet test ProjectHub.sln succeed?\nPASS: YES >= 0.8",
                Array.Empty<CodexCliFile>(), "GIT", null, "job-command", new[] { new CodexCommandExecution(command, 0, "token=do-not-leak\nPassed") });
            var envelope = JevEvidenceEnvelope.Create(request, validation);
            var executed = Assert.Single(envelope.Evidence, item => item.Provenance == EvidenceProvenance.Executed);
            Assert.Equal(command, executed.Command);
            Assert.Equal(0, executed.ExitCode);
            Assert.Contains("[REDACTED]", executed.Excerpt);
            Assert.True(envelope.HasDirectEvidence("C1"));
        }
        finally { Directory.Delete(directory, true); }
    }

}

public sealed class JevNegativeControlFixtureTests
{
    [Fact]
    public void NegativeControlFixture_ContainsIsolatedCasesAndSeparateExpectedLayers()
    {
        var assembly=typeof(JevNegativeControlFixtureTests).Assembly;
        var resource=assembly.GetManifestResourceNames().Single(x=>x.EndsWith("JevNegativeControls.json",StringComparison.Ordinal));
        using var stream=assembly.GetManifestResourceStream(resource)!;
        using var document=JsonDocument.Parse(stream);
        var cases=document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(6,cases.Length);
        Assert.Equal(cases.Length,cases.Select(x=>x.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.All(cases,item=>
        {
            Assert.Equal("synthetic-ministore-fixture",item.GetProperty("isolation").GetString());
            Assert.True(item.GetProperty("requiresDirectEvidence").GetBoolean());
            Assert.NotEqual(JsonValueKind.Null,item.GetProperty("engineExpected").ValueKind);
            Assert.NotEqual(JsonValueKind.Null,item.GetProperty("jevExpectedDisposition").ValueKind);
        });
        Assert.Contains(cases,item=>item.GetProperty("jevExpectedDisposition").GetString()=="CONTRADICTORY");
        Assert.Contains(cases,item=>item.GetProperty("jevExpectedDisposition").GetString()=="INSUFFICIENT");
    }
}

public sealed class JevJudgeRunnerTests
{
    [Fact]
    public void CodexUsageParser_DoesNotDoubleCountCumulativeSnapshotsAndSumsIncrementalEvents()
    {
        var snapshots="""
            {"type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":20,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}
            {"type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":110,"cached_input_tokens":22,"output_tokens":25,"total_tokens":135},"last_token_usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}}}
            """;
        var snapshotUsage=CodexCliRunner.ExtractUsage(snapshots);
        Assert.Equal(110,snapshotUsage.InputTokens);
        Assert.Equal(22,snapshotUsage.CachedInputTokens);
        Assert.Equal(135,snapshotUsage.ProviderTotalTokens);
    }

    [Fact]
    public async Task ReviewRawAsync_ReturnsProviderBodyWithoutJudgingIt()
    {
        var prior=Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY","test-only");
        try
        {
            const string body="{\"model\":\"jev-test\",\"usage\":{\"input_tokens\":120},\"answers\":{\"C1\":{\"type\":\"Noul\",\"noul\":0.1}}}";
            var runner=new JevJudgeRunner(new StubHandler(body));
            var request=new JudgeRequest("goal",1,"C:\\work","result","NOUL | 완료율 | PASS: YES >= 0.8",Array.Empty<CodexCliFile>(),"WEB",null);
            var result=await runner.ReviewRawAsync(request,new(true,"jev","https://example.test/judge",30),CancellationToken.None);
            Assert.Null(result.ErrorCode);
            Assert.Equal(body,result.RawResponse);
            Assert.True(result.Telemetry!.UsageKnown);
        }
        finally { Environment.SetEnvironmentVariable("TYPESAFE_API_KEY",prior); }
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});
    }
}
