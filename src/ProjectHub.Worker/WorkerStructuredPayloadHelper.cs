namespace ProjectHub.Worker;

public delegate bool StructuredPayloadParser<T>(
    string? payload,
    out T? value,
    out string? error)
    where T : class;

public delegate bool StructuredPayloadDeterministicRepair(
    string rawPayload,
    out string repairedPayload,
    out string? repairSummary);

public sealed record StructuredPayloadRequest(
    string ContractType,
    string RawPayload,
    WorkerAiRoleSettings RepairRole,
    string WorkingDirectory,
    string RepairInstruction,
    string? OutputSchemaJson = null);

public sealed record StructuredPayloadResult<T>(
    bool Success,
    T? Value,
    string FinalPayload,
    bool RepairAttempted,
    bool Repaired,
    string? InitialErrorCode,
    string? FinalErrorCode,
    string? RepairRawOutput = null,
    AiRoleRunResult? RepairRunResult = null,
    string? RepairSummary = null,
    string? ErrorDetail = null)
    where T : class;

public sealed class WorkerStructuredPayloadHelper
{
    private readonly AiRoleRunnerRegistry _runners;

    public WorkerStructuredPayloadHelper(AiRoleRunnerRegistry runners)
    {
        _runners = runners ?? throw new ArgumentNullException(nameof(runners));
    }

    public static StructuredPayloadResult<T> ProcessDeterministically<T>(
        string rawPayload,
        StructuredPayloadParser<T> parser)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(parser);
        var payload = rawPayload ?? string.Empty;
        if (parser(payload, out var value, out var error))
        {
            return new(
                true,
                value,
                payload,
                false,
                false,
                null,
                null);
        }

        return new(
            false,
            null,
            payload,
            false,
            false,
            error,
            error);
    }

    public async Task<StructuredPayloadResult<T>> ProcessAsync<T>(
        StructuredPayloadRequest request,
        StructuredPayloadParser<T> initialParser,
        StructuredPayloadParser<T>? repairedParser = null,
        CancellationToken cancellationToken = default,
        StructuredPayloadDeterministicRepair? deterministicRepair = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(initialParser);

        var rawPayload = request.RawPayload ?? string.Empty;
        if (initialParser(rawPayload, out var initialValue, out var initialError))
        {
            return new(
                true,
                initialValue,
                rawPayload,
                false,
                false,
                null,
                null);
        }

        var repairInputPayload = rawPayload;
        var repairInputError = initialError;
        string? deterministicRepairSummary = null;

        if (deterministicRepair is not null &&
            deterministicRepair(
                rawPayload,
                out var deterministicPayload,
                out deterministicRepairSummary) &&
            !string.IsNullOrWhiteSpace(deterministicPayload))
        {
            var deterministicParser = repairedParser ?? initialParser;
            if (deterministicParser(
                    deterministicPayload,
                    out var deterministicValue,
                    out var deterministicError))
            {
                return new(
                    true,
                    deterministicValue,
                    deterministicPayload,
                    true,
                    true,
                    initialError,
                    null,
                    deterministicPayload,
                    null,
                    deterministicRepairSummary);
            }

            repairInputPayload = deterministicPayload;
            repairInputError = deterministicError ?? initialError;
        }

        var runner = _runners.Resolve(request.RepairRole);
        if (runner is null)
        {
            return new(
                false,
                null,
                repairInputPayload,
                true,
                false,
                initialError,
                "STRUCTURED_REPAIR_RUNNER_UNAVAILABLE",
                RepairSummary: deterministicRepairSummary);
        }

        var repairRequest = request with { RawPayload = repairInputPayload };
        var prompt = BuildRepairPrompt(repairRequest, repairInputError);
        AiRoleRunResult repairRun;
        try
        {
            repairRun = await runner.RunAsync(new AiRoleRunRequest(
                Prompt: prompt,
                Role: request.RepairRole,
                WorkingDirectory: request.WorkingDirectory,
                SessionId: null,
                Sandbox: CodexSandboxMode.ReadOnly,
                CancellationToken: cancellationToken,
                OutputSchemaJson: request.OutputSchemaJson,
                IgnoreProjectInstructions: true)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(
                false,
                null,
                repairInputPayload,
                true,
                false,
                initialError,
                "STRUCTURED_REPAIR_EXECUTION_FAILED",
                exception.GetType().Name + ": " + exception.Message,
                RepairSummary: deterministicRepairSummary);
        }

        if (repairRun.ExitCode != 0)
        {
            return new(
                false,
                null,
                repairInputPayload,
                true,
                false,
                initialError,
                "STRUCTURED_REPAIR_PROCESS_EXIT",
                repairRun.FinalMessage,
                repairRun,
                deterministicRepairSummary);
        }

        var repairedPayload = repairRun.FinalMessage?.Trim() ?? string.Empty;
        if (repairedPayload.Length == 0)
        {
            return new(
                false,
                null,
                repairInputPayload,
                true,
                false,
                initialError,
                "STRUCTURED_REPAIR_EMPTY",
                repairedPayload,
                repairRun,
                deterministicRepairSummary);
        }

        var finalParser = repairedParser ?? initialParser;
        if (!finalParser(repairedPayload, out var repairedValue, out var repairedError))
        {
            return new(
                false,
                null,
                repairedPayload,
                true,
                false,
                initialError,
                repairedError ?? "STRUCTURED_REPAIR_INVALID",
                repairedPayload,
                repairRun,
                deterministicRepairSummary);
        }

        return new(
            true,
            repairedValue,
            repairedPayload,
            true,
            true,
            initialError,
            null,
            repairedPayload,
            repairRun,
            deterministicRepairSummary);
    }

    private static string BuildRepairPrompt(
        StructuredPayloadRequest request,
        string? initialError)
        => $$"""
           입력 유형: STRUCTURED_PAYLOAD_REPAIR
           계약 유형: {{request.ContractType}}
           최초 기계 파싱 오류: {{initialError ?? "UNKNOWN"}}

           다음 원문의 의미를 추가·삭제·재설계하지 말고 구조 형식만 복구하라.
           누락된 작업 의미나 값을 추측해 새로 만들지 않는다.
           설명, Markdown 코드펜스, 사족을 붙이지 않는다.
           최종 출력은 완성된 구조 데이터 하나만 반환한다.

           추가 형식 규칙:
           {{request.RepairInstruction}}

           원문 시작
           -----BEGIN RAW PAYLOAD-----
           {{request.RawPayload}}
           -----END RAW PAYLOAD-----
           """;
}
