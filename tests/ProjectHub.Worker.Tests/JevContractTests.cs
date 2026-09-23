using System.Net;
using System.Text;
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
    public void ParseValidation_RejectsNonContiguousScoreAndUndefinedChoice()
    {
        Assert.False(JevContract.TryParseValidation("SCORE | 점수\n1 = 낮음\n3 = 높음\nPASS: SCORE >= 2",out _,out var scoreError));
        Assert.Equal("SCORE_CRITERIA_NOT_CONTIGUOUS",scoreError);
        Assert.False(JevContract.TryParseValidation("CHOICE | 상태\nA = 승인\nPASS: 승인 또는 거절",out _,out var choiceError));
        Assert.Equal("CHOICE_ALLOWED_UNDEFINED",choiceError);
    }
}

public sealed class JevJudgeRunnerTests
{
    [Fact]
    public async Task ReviewAsync_ReturnsPassFromMockHttpResponse()
    {
        var prior=Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY","test-only");
        try
        {
            var handler=new StubHandler("{\"answers\":{\"C1\":{\"type\":\"Noul\",\"noul\":0.9}}}");
            var runner=new JevJudgeRunner(handler);
            var result=await runner.ReviewAsync(new("goal",1,"C:\\work","result","NOUL | 완료율 | PASS: YES >= 0.8",Array.Empty<CodexCliFile>(),"WEB",null),new(true,"jev","https://example.test/judge",30),CancellationToken.None);
            Assert.Equal(JudgeDecision.Pass,result.Decision);
        }
        finally { Environment.SetEnvironmentVariable("TYPESAFE_API_KEY",prior); }
    }

    [Fact]
    public async Task ReviewAsync_ReturnsErrorForMissingAnswerAndFailForThresholdMiss()
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
            Assert.Equal(JudgeDecision.Fail,failedResult.Decision);
            Assert.Contains("C1",failedResult.Message);
        }
        finally { Environment.SetEnvironmentVariable("TYPESAFE_API_KEY",prior); }
    }

    private static JudgeRequest Request(string validation)=>new("goal",1,"C:\\work","result",validation,Array.Empty<CodexCliFile>(),"WEB",null);
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});
    }
}