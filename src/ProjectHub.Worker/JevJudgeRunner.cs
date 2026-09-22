using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public enum JudgeDecision { Pass, Fail, Error }
public sealed record JudgeRequest(string Goal,int Round,string WorkingDirectory,string CodexResult,string ValidationRequest,IReadOnlyList<CodexCliFile> Files,string ReviewSource,string? ReviewCommitSha);
public sealed record JudgeResult(JudgeDecision Decision,string Message,string Provider,string? ExecutableOrEndpoint=null);

public sealed class JevJudgeRunner
{
    public const string DefaultEndpoint="https://api.typesafe.ai/v1/systemone";
    private static readonly HttpClient Client=new(){Timeout=Timeout.InfiniteTimeSpan};
    public string? FindExecutable(string? value)=>string.IsNullOrWhiteSpace(value)?null:value;

    public async Task<JudgeResult> ReviewAsync(JudgeRequest request,JudgeSettings settings,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = string.IsNullOrWhiteSpace(settings.ManualExecutableOrEndpoint) ? DefaultEndpoint : settings.ManualExecutableOrEndpoint.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
            return Error("JEV Endpoint는 유효한 HTTPS URL이어야 합니다.", endpoint);
        var key=Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        if(string.IsNullOrWhiteSpace(key))return Error("TYPESAFE_API_KEY가 없어 JEV를 호출하지 않고 GPT Web fallback을 사용합니다.", endpoint);
        if(!JevContract.TryParseValidation(request.ValidationRequest,out var validation,out var parseError))return Error("VALIDATION REQUEST 오류: "+parseError, endpoint);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds,10,600)));
        var questions=validation.Questions.ToDictionary(q=>q.Id,q=>q.Type switch
        {
            JevQuestionType.Noul=>new object?[]{new{type="noul",instructions=q.Instructions,criteria=new{}}},
            JevQuestionType.Score=>new object?[]{new{type="score",instructions=q.Instructions,criteria=q.Criteria}},
            _=>new object?[]{new{type="choice",instructions=q.Instructions,criteria=q.ChoiceCriteria}}
        });
        var questionMap=questions.ToDictionary(x=>x.Key,x=>x.Value[0]);
        using var message=new HttpRequestMessage(HttpMethod.Post,endpointUri);message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        message.Content=new StringContent(JsonSerializer.Serialize(new{model="jev-latest",state=new{task=request.Goal,codex_result=request.CodexResult,round=request.Round,working_directory=request.WorkingDirectory},questions=questionMap}),Encoding.UTF8,"application/json");
        try
        {
            using var response=await Client.SendAsync(message,timeout.Token);var body=await response.Content.ReadAsStringAsync(timeout.Token);
            if(!response.IsSuccessStatusCode)return Error($"JEV HTTP {(int)response.StatusCode} 응답입니다.", endpoint);
            using var doc=JsonDocument.Parse(body);if(!doc.RootElement.TryGetProperty("answers",out var answers))return Error("JEV 응답에 answers가 없습니다.", endpoint);
            var failures=new List<string>();
            foreach(var q in validation.Questions)
            {
                if(!answers.TryGetProperty(q.Id,out var answer)){failures.Add($"{q.Id} RESULT: question ID missing");continue;}
                var type=answer.TryGetProperty("type",out var typeValue)?typeValue.GetString():null;var expected=q.Type.ToString().ToLowerInvariant();if(!string.Equals(type,expected,StringComparison.OrdinalIgnoreCase)){failures.Add($"{q.Id} RESULT: type mismatch");continue;}
                if(q.Type==JevQuestionType.Noul){if(!answer.TryGetProperty("noul",out var value)||value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var actual)||actual<0||actual>1||!Compare(actual,q.Rule.Operator,q.Rule.Number!.Value))failures.Add($"{q.Id} TYPE: NOUL EXPECTED: YES {q.Rule.Operator} {q.Rule.Number} ACTUAL: {(answer.TryGetProperty("noul",out var a)?a.ToString():"missing")}");}
                else if(q.Type==JevQuestionType.Score){if(!answer.TryGetProperty("score",out var value)||value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var actual)||!Compare(actual,q.Rule.Operator,q.Rule.Number!.Value-1))failures.Add($"{q.Id} TYPE: SCORE EXPECTED: SCORE {q.Rule.Operator} {q.Rule.Number!.Value-1} ACTUAL: {(answer.TryGetProperty("score",out var a)?a.ToString():"missing")}");}
                else {var actual=answer.TryGetProperty("choice",out var value)?value.GetString():null;if(actual is null||!q.Rule.Allowed.Contains(actual))failures.Add($"{q.Id} TYPE: CHOICE EXPECTED: {string.Join(" 또는 ",q.Rule.Allowed)} ACTUAL: {actual??"missing"}");}
            }
            return failures.Count==0?new(JudgeDecision.Pass,"ALL PASS",settings.Provider,endpoint):new(JudgeDecision.Fail,"[JEV VALIDATION FAILED]"+Environment.NewLine+string.Join(Environment.NewLine+Environment.NewLine,failures),settings.Provider,endpoint);
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){return Error("JEV timeout으로 GPT Web fallback을 사용합니다.", endpoint);}
        catch(JsonException){return Error("JEV 응답 JSON을 해석할 수 없어 GPT Web fallback을 사용합니다.", endpoint);}
        catch(Exception ex){return Error($"JEV 연결 오류: {ex.GetType().Name}", endpoint);}
    }
    private static bool Compare(double actual,string op,double expected)=>op==">="?actual>=expected:actual<=expected;
    private static JudgeResult Error(string message, string endpoint)=>new(JudgeDecision.Error,message,"jev",endpoint);
}
