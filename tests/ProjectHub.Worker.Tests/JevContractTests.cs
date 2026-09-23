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
    public void EvidenceArchive_InvalidatesOnlyAtomicQuestionsReferencingChangedEvidence()
    {
        var directory=Path.Combine(Path.GetTempPath(),"jev-invalidation-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"source.cs");
        var unrelatedPath=Path.Combine(directory,"unrelated.cs");
        var job="invalidation-"+Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(path,"class Before {}\n");
            File.WriteAllText(unrelatedPath,"class Unchanged {}\n");
            var firstText="NOUL | [QID:C1] claim one\nEVIDENCE: source.cs\nPASS: YES >= 0.8\nNOUL | [QID:C2] unrelated claim\nEVIDENCE: unrelated.cs\nPASS: YES >= 0.8";
            var validation=JevContract.TryParseValidation(firstText,out var parsed,out var error);
            Assert.True(validation,error);
            var request=new JudgeRequest("goal",1,directory,"summary",firstText,Array.Empty<CodexCliFile>(),"GIT","abc",job);
            var first=JevEvidenceEnvelope.Create(request,parsed);
            Assert.Equal(JudgeDecision.Pass,JevEvidenceArchive.Save(first,new(JudgeDecision.Pass,"ALL_PASS","test"),1).Result.Decision);
            File.WriteAllText(path,"class After {}\n");
            const string q2="NOUL | [QID:C2] unrelated claim\nEVIDENCE: unrelated.cs\nPASS: YES >= 0.8";
            Assert.True(JevContract.TryParseValidation(q2,out var parsed2,out error),error);
            var second=JevEvidenceEnvelope.Create(request with { Round=2, ValidationRequest=q2 },parsed2);
            var saved=JevEvidenceArchive.Save(second,new(JudgeDecision.Pass,"ALL_PASS","test"),2);
            Assert.Equal(JudgeDecision.Partial,saved.Result.Decision);
            Assert.Contains("C1 RESULT: EVIDENCE_CHANGED",saved.Result.Message);
            Assert.Equal(new[]{"C1"},saved.InvalidatedQuestionIds);
            var archive=Path.Combine(WorkerPaths.State,"jev-evidence",job);
            using (var pending=JsonDocument.Parse(File.ReadAllText(Path.Combine(archive,"latest.json"))))
            {
                var statuses=pending.RootElement.GetProperty("questionResults");
                Assert.Equal("NEEDS_RECHECK",statuses.GetProperty("C1").GetString());
                Assert.Equal("PASS",statuses.GetProperty("C2").GetString());
            }

            const string q1="NOUL | [QID:C1] claim one\nEVIDENCE: source.cs\nPASS: YES >= 0.8";
            Assert.True(JevContract.TryParseValidation(q1,out var parsed1,out error),error);
            var third=JevEvidenceEnvelope.Create(request with { Round=3, ValidationRequest=q1 },parsed1);
            var rechecked=JevEvidenceArchive.Save(third,new(JudgeDecision.Pass,"ALL_PASS","test"),3);
            Assert.Equal(JudgeDecision.Pass,rechecked.Result.Decision);
            Assert.Empty(rechecked.InvalidatedQuestionIds);
            using var complete=JsonDocument.Parse(File.ReadAllText(Path.Combine(archive,"latest.json")));
            var completeStatuses=complete.RootElement.GetProperty("questionResults");
            Assert.Equal("PASS",completeStatuses.GetProperty("C1").GetString());
            Assert.Equal("PASS",completeStatuses.GetProperty("C2").GetString());
        }
        finally
        {
            Directory.Delete(directory,true);
            var archive=Path.Combine(WorkerPaths.State,"jev-evidence",job);
            if(Directory.Exists(archive))Directory.Delete(archive,true);
        }
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

        var incremental="""
            {"type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":10,"output_tokens":3,"total_tokens":13}}}}
            {"type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":20,"output_tokens":4,"total_tokens":24}}}}
            """;
        var incrementalUsage=CodexCliRunner.ExtractUsage(incremental);
        Assert.Equal(30,incrementalUsage.InputTokens);
        Assert.Equal(37,incrementalUsage.ProviderTotalTokens);
    }

    [Fact]
    public async Task ReviewAsync_ReturnsPassFromMockHttpResponse()
    {
        var prior=Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY","test-only");
        try
        {
            var handler=new StubHandler("{\"model\":\"jev-test\",\"usage\":{\"input_tokens\":120,\"output_tokens\":8},\"answers\":{\"C1\":{\"type\":\"Noul\",\"noul\":0.9}}}");
            var runner=new JevJudgeRunner(handler);
            var result=await runner.ReviewAsync(new("goal",1,"C:\\work","result","NOUL | 완료율 | PASS: YES >= 0.8",Array.Empty<CodexCliFile>(),"WEB",null),new(true,"jev","https://example.test/judge",30),CancellationToken.None);
            Assert.Equal(JudgeDecision.Pass,result.Decision);
            Assert.True(result.Telemetry!.UsageKnown);
            Assert.Equal(120,result.Telemetry.InputTokens);
            Assert.Null(result.Telemetry.ProviderTotalTokens);
            Assert.True(result.Telemetry.RequestBytes>0);
        }
        finally { Environment.SetEnvironmentVariable("TYPESAFE_API_KEY",prior); }
    }

    [Fact]
    public async Task ReviewAsync_ReturnsErrorForMissingAnswerAndPartialForThresholdMiss()
    {
        var prior=Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY","test-only");
        try
        {
            var missing=new JevJudgeRunner(new StubHandler("{\"answers\":{}}"));
            var missingResult=await missing.ReviewAsync(Request("NOUL | 완료율 | PASS: YES >= 0.8"),new(true,"jev","https://example.test/judge",30),CancellationToken.None);
            Assert.Equal(JudgeDecision.Error,missingResult.Decision);
            var failed=new JevJudgeRunner(new StubHandler("{\"answers\":{\"C1\":{\"type\":\"Noul\",\"noul\":0.2}}}"));
            var failedResult=await failed.ReviewAsync(Request("NOUL | 완료율 | PASS: YES >= 0.8"),new(true,"jev","https://example.test/judge",30),CancellationToken.None);
            Assert.Equal(JudgeDecision.Partial,failedResult.Decision);
            Assert.Contains("C1",failedResult.Message);
        }
        finally { Environment.SetEnvironmentVariable("TYPESAFE_API_KEY",prior); }
    }

    [Fact]
    public async Task ReviewAsync_UsesExplicitQidAsProviderAnswerKey()
    {
        var prior=Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY","test-only");
        try
        {
            var runner=new JevJudgeRunner(new StubHandler("{\"answers\":{\"C2\":{\"type\":\"Noul\",\"noul\":0.8}}}"));
            var result=await runner.ReviewAsync(Request("NOUL | [QID:C2] [HIGH] affected atomic claim\nPASS: YES >= 0.80"),new(true,"jev","https://example.test/judge",30),CancellationToken.None);
            Assert.Equal(JudgeDecision.Pass,result.Decision);
        }
        finally { Environment.SetEnvironmentVariable("TYPESAFE_API_KEY",prior); }
    }

    [Fact]
    public async Task ReviewAsync_UsesIndependentImportanceThresholdsForBatchedQuestions()
    {
        var prior=Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY","test-only");
        try
        {
            const string validation="NOUL | [LOW] copy claim\nPASS: YES >= 0.60\nNOUL | [MEDIUM] second claim\nPASS: YES >= 0.70\nNOUL | [HIGH] third claim\nPASS: YES >= 0.80\nNOUL | [CRITICAL] fourth claim\nPASS: YES >= 0.90";
            var pass=new JevJudgeRunner(new StubHandler("""{"answers":{"C1":{"type":"Noul","noul":0.60},"C2":{"type":"Noul","noul":0.70},"C3":{"type":"Noul","noul":0.80},"C4":{"type":"Noul","noul":0.90}}}"""));
            var passed=await pass.ReviewAsync(Request(validation),new(true,"jev","https://example.test/judge",30),CancellationToken.None);
            Assert.Equal(JudgeDecision.Pass,passed.Decision);

            var below=new JevJudgeRunner(new StubHandler("""{"answers":{"C1":{"type":"Noul","noul":0.60},"C2":{"type":"Noul","noul":0.69},"C3":{"type":"Noul","noul":0.80},"C4":{"type":"Noul","noul":0.90}}}"""));
            var partial=await below.ReviewAsync(Request(validation),new(true,"jev","https://example.test/judge",30),CancellationToken.None);
            Assert.Equal(JudgeDecision.Partial,partial.Decision);
            Assert.Contains("C2",partial.Message);
            Assert.DoesNotContain("C1 TYPE",partial.Message);
            Assert.DoesNotContain("C3 TYPE",partial.Message);
            Assert.DoesNotContain("C4 TYPE",partial.Message);
        }
        finally { Environment.SetEnvironmentVariable("TYPESAFE_API_KEY",prior); }
    }

    [Fact]
    public async Task ReviewAsync_DoesNotPassEvidenceClaimWhenOnlySummaryIsAvailable()
    {
        var prior=Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY","test-only");
        try
        {
            var runner=new JevJudgeRunner(new StubHandler("{\"answers\":{\"C1\":{\"type\":\"Noul\",\"noul\":0.99}}}"));
            var request=Request("NOUL | claim\nEVIDENCE: E-NOT-AVAILABLE\nPASS: YES >= 0.8");
            var result=await runner.ReviewAsync(request,new(true,"jev","https://example.test/judge",30),CancellationToken.None);
            Assert.Equal(JudgeDecision.Partial,result.Decision);
            Assert.Contains("MISSING_DIRECT_EVIDENCE",result.Message);
        }
        finally { Environment.SetEnvironmentVariable("TYPESAFE_API_KEY",prior); }
    }

    [Fact]
    public void JevRetryPrompt_ProtectsPassingQuestionsAndForbidsScoreChasing()
    {
        var prompt=JevRetryPromptBuilder.Build("C2 TYPE: NOUL QUESTION: changed assertion",2);
        Assert.Contains("[JEV PARTIAL REVIEW · 2/3]",prompt);
        Assert.Contains("threshold 미달은 그 자체로 구현 결함의 증거가 아니다",prompt);
        Assert.Contains("evidence 변경의 영향을 받는 원자 질문만",prompt);
        Assert.Contains("영향받지 않은 PASS 질문은 다시 보내지 않는다",prompt);
        Assert.Contains("threshold를 낮추지 않는다",prompt);
        Assert.Contains("최대 3회",prompt);
    }

    private static JudgeRequest Request(string validation)=>new("goal",1,"C:\\work","result",validation,Array.Empty<CodexCliFile>(),"WEB",null);
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});
    }
}
