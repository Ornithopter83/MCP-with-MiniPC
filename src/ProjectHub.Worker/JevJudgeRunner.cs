using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace ProjectHub.Worker;

public sealed record JudgeRequest(string Goal,int Round,string WorkingDirectory,string CodexResult,string ValidationRequest,IReadOnlyList<CodexCliFile> Files,string ReviewSource,string? ReviewCommitSha,string? JobId=null,IReadOnlyList<CodexCommandExecution>? CommandExecutions=null);
public sealed record JudgeTransportResult(string? RawResponse, string? ErrorCode, JevCallTelemetry? Telemetry);

public sealed class JevJudgeRunner
{
    public const string DefaultEndpoint="https://api.typesafe.ai/v1/systemone";
    private readonly HttpClient _client;

    public JevJudgeRunner(HttpMessageHandler? handler = null)
    {
        _client = handler is null ? new HttpClient { Timeout = Timeout.InfiniteTimeSpan } : new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public string? FindExecutable(string? value)=>string.IsNullOrWhiteSpace(value)?null:value;

    /// <summary>Coordinator-router adapter: performs transport and preserves the judge response without evaluating it.</summary>
    public async Task<JudgeTransportResult> ReviewRawAsync(JudgeRequest request, JudgeSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = string.IsNullOrWhiteSpace(settings.ManualExecutableOrEndpoint) ? DefaultEndpoint : settings.ManualExecutableOrEndpoint.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps) return new(null, "JEV_ENDPOINT_INVALID", null);
        var key = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return new(null, "JEV_API_KEY_MISSING", null);
        if (!JudgeTransportContract.TryParse(JudgeTransportContract.ExtractRequest(request.ValidationRequest), out var validation, out var parseError)) return new(null, "JEV_VALIDATION_" + parseError, null);
        var envelope = JevEvidenceEnvelope.Create(request, validation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 10, 600)));
        var questions = validation.Questions.ToDictionary(q => q.Id, q => BuildQuestion(q, envelope.EvidenceFor(q.Id)));
        var payload = JsonSerializer.Serialize(new { model = "jev-latest", state = new { task = request.Goal, codex_result = request.CodexResult, round = request.Round, working_directory = request.WorkingDirectory, evidence = envelope }, questions });
        var requestBytes = Encoding.UTF8.GetByteCount(payload);
        var evidenceBytes = Encoding.UTF8.GetByteCount(envelope.ToProviderState());
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpointUri);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            message.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(message, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            stopwatch.Stop();
            var telemetry = ReadTelemetry(body, requestBytes, evidenceBytes, Encoding.UTF8.GetByteCount(body), stopwatch.ElapsedMilliseconds, validation.Questions.Count, response.IsSuccessStatusCode ? null : $"JEV_HTTP_{(int)response.StatusCode}");
            return response.IsSuccessStatusCode ? new(body, null, telemetry) : new(null, $"JEV_HTTP_{(int)response.StatusCode}", telemetry);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { stopwatch.Stop(); return new(null, "JEV_TIMEOUT", EmptyTelemetry(requestBytes, evidenceBytes, 0, stopwatch.ElapsedMilliseconds, validation.Questions.Count, "JEV_TIMEOUT")); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { stopwatch.Stop(); return new(null, "JEV_CONNECTION", EmptyTelemetry(requestBytes, evidenceBytes, 0, stopwatch.ElapsedMilliseconds, validation.Questions.Count, "JEV_CONNECTION")); }
        catch (Exception) { stopwatch.Stop(); return new(null, "JEV_TRANSPORT_ERROR", EmptyTelemetry(requestBytes, evidenceBytes, 0, stopwatch.ElapsedMilliseconds, validation.Questions.Count, "JEV_TRANSPORT_ERROR")); }
    }

    private static JevCallTelemetry ReadTelemetry(string body,long requestBytes,long evidenceBytes,long responseBytes,long latency,int questionCount,string? errorCode)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(body); }
        catch (JsonException) { return new("jev-latest",requestBytes,evidenceBytes,responseBytes,latency,false,null,null,null,null,null,questionCount,errorCode); }
        using (document)
        {
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
    }
    private static object BuildQuestion(JudgeTransportQuestion question,string evidenceIds)=>question.Type switch
    {
        JudgeTransportQuestionType.Noul=>new { type="noul", instructions=question.Instructions, evidence_ids=evidenceIds },
        JudgeTransportQuestionType.Score=>new { type="score", instructions=question.Instructions, criteria=question.Criteria, evidence_ids=evidenceIds },
        _=>new { type="choice", instructions=question.Instructions, criteria=question.ChoiceCriteria, evidence_ids=evidenceIds }
    };
    private static JevCallTelemetry EmptyTelemetry(long requestBytes,long evidenceBytes,long responseBytes,long latency,int questionCount,string errorCode)=>new("jev-latest",requestBytes,evidenceBytes,responseBytes,latency,false,null,null,null,null,null,questionCount,errorCode);
}
