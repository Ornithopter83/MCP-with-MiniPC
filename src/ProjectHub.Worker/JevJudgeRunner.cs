using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ProjectHub.Worker;

public enum JudgeDecision { Pass, Partial, Error }
public sealed record JudgeRequest(string Goal,int Round,string WorkingDirectory,string CodexResult,string ValidationRequest,IReadOnlyList<CodexCliFile> Files,string ReviewSource,string? ReviewCommitSha,string? JobId=null);
public sealed record JudgeResult(JudgeDecision Decision,string Message,string Provider,string? ExecutableOrEndpoint=null,JevCallTelemetry? Telemetry=null);

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
        var envelope=JevEvidenceEnvelope.Create(request,validation);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds,10,600)));
        var questions=validation.Questions.ToDictionary(q=>q.Id,q=>BuildQuestion(q,envelope.EvidenceFor(q.Id)));
        var payload=JsonSerializer.Serialize(new
        {
            model="jev-latest",
            state=new { task=request.Goal, codex_result=request.CodexResult, round=request.Round, working_directory=request.WorkingDirectory, evidence=envelope },
            questions
        });
        var requestBytes=Encoding.UTF8.GetByteCount(payload);
        var payloadDigest="sha256:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var evidenceBytes=Encoding.UTF8.GetByteCount(envelope.ToProviderState());
        var stopwatch=Stopwatch.StartNew();
        long responseBytes=0;
        try
        {
            using var message=new HttpRequestMessage(HttpMethod.Post,endpointUri);
            message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
            message.Content=new StringContent(payload,Encoding.UTF8,"application/json");
            using var response=await _client.SendAsync(message,timeout.Token);
            var body=await response.Content.ReadAsStringAsync(timeout.Token);
            responseBytes=Encoding.UTF8.GetByteCount(body);
            stopwatch.Stop();
            var telemetry=ReadTelemetry(body,requestBytes,evidenceBytes,responseBytes,stopwatch.ElapsedMilliseconds,validation.Questions.Count,response.IsSuccessStatusCode?null:$"JEV_HTTP_{(int)response.StatusCode}") with { PayloadDigest=payloadDigest };
            if(!response.IsSuccessStatusCode)return JevEvidenceArchive.Save(envelope,Error($"JEV_HTTP_{(int)response.StatusCode}",telemetry),request.Round).Result;
            using var doc=JsonDocument.Parse(body);
            var result=Evaluate(validation,doc.RootElement) with { Telemetry=telemetry };
            var missing=validation.Questions.Where(q=>q.Instructions.Contains("EVIDENCE:",StringComparison.OrdinalIgnoreCase) && !envelope.HasDirectEvidence(q.Id)).Select(q=>q.Id).ToArray();
            if(result.Decision==JudgeDecision.Pass && missing.Length>0)
                result = new(JudgeDecision.Partial,"[JEV PARTIAL]\n"+string.Join("\n",missing.Select(id=>$"{id} RESULT: MISSING_DIRECT_EVIDENCE; SUMMARY_ONLY is insufficient for SUPPORTED")),"jev",null,telemetry);
            return JevEvidenceArchive.Save(envelope,result,request.Round).Result;
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){stopwatch.Stop();return JevEvidenceArchive.Save(envelope,Error("JEV_TIMEOUT",EmptyTelemetry(requestBytes,evidenceBytes,responseBytes,stopwatch.ElapsedMilliseconds,validation.Questions.Count,"JEV_TIMEOUT") with { PayloadDigest=payloadDigest }),request.Round).Result;}
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
        catch(JsonException){stopwatch.Stop();return JevEvidenceArchive.Save(envelope,Error("JEV_JSON_INVALID",EmptyTelemetry(requestBytes,evidenceBytes,responseBytes,stopwatch.ElapsedMilliseconds,validation.Questions.Count,"JEV_JSON_INVALID") with { PayloadDigest=payloadDigest }),request.Round).Result;}
        catch(HttpRequestException){stopwatch.Stop();return JevEvidenceArchive.Save(envelope,Error("JEV_CONNECTION",EmptyTelemetry(requestBytes,evidenceBytes,responseBytes,stopwatch.ElapsedMilliseconds,validation.Questions.Count,"JEV_CONNECTION") with { PayloadDigest=payloadDigest }),request.Round).Result;}
        catch(Exception){stopwatch.Stop();return JevEvidenceArchive.Save(envelope,Error("JEV_UNEXPECTED",EmptyTelemetry(requestBytes,evidenceBytes,responseBytes,stopwatch.ElapsedMilliseconds,validation.Questions.Count,"JEV_UNEXPECTED") with { PayloadDigest=payloadDigest }),request.Round).Result;}
    }

    private static object BuildQuestion(JevQuestion q,string evidenceIds)=>q.Type switch
    {
        JevQuestionType.Noul=>new { type="noul", instructions=q.Instructions, evidence_ids=evidenceIds },
        JevQuestionType.Score=>new { type="score", instructions=q.Instructions, criteria=q.Criteria, evidence_ids=evidenceIds },
        _=>new { type="choice", instructions=q.Instructions, criteria=q.ChoiceCriteria, evidence_ids=evidenceIds }
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
        // A valid JEV answer below its fixed threshold is not, by itself, proof
        // that the implementation is wrong. Let the same Codex session triage
        // the evidence before changing code.
        return failures.Count==0?new(JudgeDecision.Pass,"ALL_PASS","jev"):new(JudgeDecision.Partial,"[JEV PARTIAL]"+Environment.NewLine+string.Join(Environment.NewLine+Environment.NewLine,failures),"jev");
    }

    private static bool TryNumber(JsonElement answer,string name,out double value)
    {
        value=0;
        return answer.TryGetProperty(name,out var item) && item.ValueKind==JsonValueKind.Number && item.TryGetDouble(out value) && double.IsFinite(value);
    }
    private static string Failure(JevQuestion q,string expected,string actual,string type)=>$"{q.Id} TYPE: {type} QUESTION: {q.Instructions} EXPECTED: {expected} ACTUAL: {actual} RESULT: FAIL";
    private static bool Compare(double actual,string op,double expected)=>op==">="?actual>=expected:actual<=expected;
    private static JevCallTelemetry ReadTelemetry(string body,long requestBytes,long evidenceBytes,long responseBytes,long latency,int questionCount,string? errorCode)
    {
        using var document=JsonDocument.Parse(body);
        if(!document.RootElement.TryGetProperty("usage",out var usage)||usage.ValueKind!=JsonValueKind.Object)
            return new("jev-latest",requestBytes,evidenceBytes,responseBytes,latency,false,null,null,null,null,null,questionCount,errorCode);
        long? Read(params string[] names)
        {
            foreach(var name in names)
                if(usage.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.Number && value.TryGetInt64(out var number))return number;
            return null;
        }
        var input=Read("input_tokens","inputTokens"); var cached=Read("cached_input_tokens","cachedInputTokens"); var output=Read("output_tokens","outputTokens"); var reasoning=Read("reasoning_output_tokens","reasoningOutputTokens"); var total=Read("total_tokens","totalTokens");
        var model=document.RootElement.TryGetProperty("model",out var modelValue)&&modelValue.ValueKind==JsonValueKind.String?modelValue.GetString()??"jev-latest":"jev-latest";
        return new(model,requestBytes,evidenceBytes,responseBytes,latency,input is not null||cached is not null||output is not null||reasoning is not null||total is not null,input,cached,output,reasoning,total,questionCount,errorCode);
    }
    private static JevCallTelemetry EmptyTelemetry(long requestBytes,long evidenceBytes,long responseBytes,long latency,int questionCount,string errorCode)=>new("jev-latest",requestBytes,evidenceBytes,responseBytes,latency,false,null,null,null,null,null,questionCount,errorCode);
    private static JudgeResult Error(string message,JevCallTelemetry? telemetry=null)=>new(JudgeDecision.Error,message,"jev",null,telemetry);
}
