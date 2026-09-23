using System.Net.Http;
using System.Net;
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
    private readonly HttpClient _client;

    public JevJudgeRunner(HttpMessageHandler? handler = null)
    {
        _client = handler is null ? new HttpClient { Timeout = Timeout.InfiniteTimeSpan } : new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public string? FindExecutable(string? value)=>string.IsNullOrWhiteSpace(value)?null:value;

    public async Task<JudgeResult> ReviewAsync(JudgeRequest request,JudgeSettings settings,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = string.IsNullOrWhiteSpace(settings.ManualExecutableOrEndpoint) ? DefaultEndpoint : settings.ManualExecutableOrEndpoint.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
            return Error("JEV_ENDPOINT_INVALID");
        var key=Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        if(string.IsNullOrWhiteSpace(key))return Error("JEV_API_KEY_MISSING");
        if(!JevContract.TryParseValidation(request.ValidationRequest,out var validation,out var parseError))return Error("JEV_VALIDATION_"+parseError);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds,10,600)));
        var questions=validation.Questions.ToDictionary(q=>q.Id,q=>BuildQuestion(q));
        using var message=new HttpRequestMessage(HttpMethod.Post,endpointUri);
        message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        message.Content=new StringContent(JsonSerializer.Serialize(new
        {
            model="jev-latest",
            state=new { task=request.Goal, codex_result=request.CodexResult, round=request.Round, working_directory=request.WorkingDirectory },
            questions
        }),Encoding.UTF8,"application/json");
        try
        {
            using var response=await _client.SendAsync(message,timeout.Token);
            var body=await response.Content.ReadAsStringAsync(timeout.Token);
            if(!response.IsSuccessStatusCode)return Error($"JEV_HTTP_{(int)response.StatusCode}");
            using var doc=JsonDocument.Parse(body);
            return Evaluate(validation,doc.RootElement);
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){return Error("JEV_TIMEOUT");}
        catch(JsonException){return Error("JEV_JSON_INVALID");}
        catch(HttpRequestException){return Error("JEV_CONNECTION");}
        catch(Exception){return Error("JEV_UNEXPECTED");}
    }

    private static object BuildQuestion(JevQuestion q)=>q.Type switch
    {
        JevQuestionType.Noul=>new { type="noul", instructions=q.Instructions },
        JevQuestionType.Score=>new { type="score", instructions=q.Instructions, criteria=q.Criteria },
        _=>new { type="choice", instructions=q.Instructions, criteria=q.ChoiceCriteria }
    };

    private static JudgeResult Evaluate(JevValidationRequest validation, JsonElement root)
    {
        if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("answers",out var answers) || answers.ValueKind!=JsonValueKind.Object)return Error("JEV_ANSWERS_MISSING");
        var expected=validation.Questions.ToDictionary(q=>q.Id,StringComparer.OrdinalIgnoreCase);
        foreach(var property in answers.EnumerateObject())if(!expected.ContainsKey(property.Name))return Error("JEV_UNKNOWN_QUESTION_ID");
        var failures=new List<string>();
        foreach(var q in validation.Questions)
        {
            if(!answers.TryGetProperty(q.Id,out var answer))return Error("JEV_QUESTION_ID_MISSING");
            if(answer.ValueKind!=JsonValueKind.Object || !answer.TryGetProperty("type",out var typeValue) || typeValue.ValueKind!=JsonValueKind.String || !string.Equals(typeValue.GetString(),q.Type.ToString(),StringComparison.OrdinalIgnoreCase))return Error("JEV_TYPE_MISMATCH_"+q.Id);
            switch(q.Type)
            {
                case JevQuestionType.Noul:
                    if(!TryNumber(answer,"noul",out var noul))return Error("JEV_VALUE_INVALID_"+q.Id);
                    if(!Compare(noul,q.Rule.Operator,q.Rule.Number!.Value))failures.Add(Failure(q,"YES "+q.Rule.Operator+" "+q.Rule.Number,noul.ToString(System.Globalization.CultureInfo.InvariantCulture),"NOUL"));
                    break;
                case JevQuestionType.Score:
                    if(!TryNumber(answer,"score",out var score) || score<0 || score>q.Criteria.Count-1)return Error("JEV_VALUE_INVALID_"+q.Id);
                    var expectedScore=q.Rule.Number!.Value-1;
                    if(!Compare(score,q.Rule.Operator,expectedScore))failures.Add(Failure(q,"SCORE "+q.Rule.Operator+" "+expectedScore.ToString(System.Globalization.CultureInfo.InvariantCulture),score.ToString(System.Globalization.CultureInfo.InvariantCulture),"SCORE"));
                    break;
                case JevQuestionType.Choice:
                    if(!answer.TryGetProperty("choice",out var choiceValue) || choiceValue.ValueKind!=JsonValueKind.String || string.IsNullOrWhiteSpace(choiceValue.GetString()))return Error("JEV_VALUE_INVALID_"+q.Id);
                    var choice=choiceValue.GetString()!;
                    if(!q.ChoiceCriteria.Keys.Any(x=>string.Equals(x,choice,StringComparison.OrdinalIgnoreCase)))return Error("JEV_CHOICE_UNDEFINED_"+q.Id);
                    if(!q.Rule.Allowed.Contains(choice))failures.Add(Failure(q,string.Join(" 또는 ",q.Rule.Allowed),choice,"CHOICE"));
                    break;
            }
        }
        return failures.Count==0?new(JudgeDecision.Pass,"ALL_PASS","jev"):new(JudgeDecision.Fail,"[JEV VALIDATION FAILED]"+Environment.NewLine+string.Join(Environment.NewLine+Environment.NewLine,failures),"jev");
    }

    private static bool TryNumber(JsonElement answer,string name,out double value)
    {
        value=0;
        return answer.TryGetProperty(name,out var item) && item.ValueKind==JsonValueKind.Number && item.TryGetDouble(out value) && double.IsFinite(value);
    }
    private static string Failure(JevQuestion q,string expected,string actual,string type)=>$"{q.Id} TYPE: {type} QUESTION: {q.Instructions} EXPECTED: {expected} ACTUAL: {actual} RESULT: FAIL";
    private static bool Compare(double actual,string op,double expected)=>op==">="?actual>=expected:actual<=expected;
    private static JudgeResult Error(string message)=>new(JudgeDecision.Error,message,"jev");
}