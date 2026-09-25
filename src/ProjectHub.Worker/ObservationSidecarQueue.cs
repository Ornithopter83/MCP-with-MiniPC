using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed class ObservationSidecarRequest
{
    public string Kind { get; init; } = "OBSERVATION";
    public string Id { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public List<string>? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public int? TimeoutSeconds { get; init; }
    public string CompletionMode { get; init; } = "WORK_RESULT_REQUIRED";
    public List<string>? ResultPaths { get; init; }
    public Dictionary<string, string>? Environment { get; init; }
}

public sealed record ObservationSidecarEvent(string Source, string Content, string? Status = null);

public sealed record ObservationResultFile(
    string Id,
    string Status,
    string CompletionMode,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? ErrorCode,
    string Message,
    IReadOnlyList<string> ResultPaths);

public static class ObservationRequestContract
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryParse(
        string? json,
        out ObservationSidecarRequest? request,
        out MechanicalWorkCompletionMode completionMode,
        out string? error)
    {
        request = null;
        completionMode = MechanicalWorkCompletionMode.WorkResultRequired;
        error = null;

        try
        {
            request = JsonSerializer.Deserialize<ObservationSidecarRequest>(json ?? string.Empty, JsonOptions);
        }
        catch (JsonException)
        {
            error = "OBSERVATION_REQUEST_JSON_INVALID";
            return false;
        }

        if (request is null)
        {
            error = "OBSERVATION_REQUEST_JSON_INVALID";
            return false;
        }

        if (!string.Equals(request.Kind?.Trim(), "OBSERVATION", StringComparison.OrdinalIgnoreCase))
        {
            error = "OBSERVATION_KIND_INVALID";
            return false;
        }

        if (!IsSafeId(request.Id))
        {
            error = "OBSERVATION_ID_INVALID";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            error = "OBSERVATION_COMMAND_MISSING";
            return false;
        }

        var timeout = request.TimeoutSeconds ?? 300;
        if (timeout is < 1 or > 86400)
        {
            error = "OBSERVATION_TIMEOUT_INVALID";
            return false;
        }

        if (!TryParseCompletionMode(request.CompletionMode, out completionMode))
        {
            error = "OBSERVATION_COMPLETION_MODE_INVALID";
            return false;
        }

        return true;
    }

    public static bool TryParseCompletionMode(string? value, out MechanicalWorkCompletionMode mode)
    {
        switch ((value ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "FINALIZE_ONLY":
                mode = MechanicalWorkCompletionMode.FinalizeOnly;
                return true;
            case "WORK_RESULT_REQUIRED":
                mode = MechanicalWorkCompletionMode.WorkResultRequired;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static bool IsSafeId(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= 96 &&
           value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
}

/// <summary>
/// Watches the current job's mechanical request folder, executes OBSERVATION requests as background processes,
/// and reports only mechanical facts back to the shared registry.
/// </summary>
public sealed class ObservationSidecarQueue : IAsyncDisposable
{
    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _workingDirectory;
    private readonly MechanicalWorkRegistry _registry;
    private readonly CancellationTokenSource _cts;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _pump;

    public ObservationSidecarQueue(
        string workingDirectory,
        string jobId,
        MechanicalWorkRegistry registry,
        CancellationToken jobCancellation)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _registry = registry;
        RequestDirectory = ProjectWorkspacePersistence.MechanicalRequestDirectory(workingDirectory, jobId);
        ActiveDirectory = ProjectWorkspacePersistence.MechanicalActiveDirectory(workingDirectory, jobId);
        ResultDirectory = ProjectWorkspacePersistence.MechanicalResultDirectory(workingDirectory, jobId);
        Directory.CreateDirectory(RequestDirectory);
        Directory.CreateDirectory(ActiveDirectory);
        Directory.CreateDirectory(ResultDirectory);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(jobCancellation);
        _pump = Task.Run(PumpAsync);
    }

    public string RequestDirectory { get; }
    public string ActiveDirectory { get; }
    public string ResultDirectory { get; }

    public event Action<ObservationSidecarEvent>? TransportEvent;
    public event Action<MechanicalWorkCompletion>? CompletionAvailable;

    public async Task ScanNowAsync(CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(RequestDirectory))
                return;

            foreach (var path in Directory.EnumerateFiles(RequestDirectory, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(File.GetCreationTimeUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await TryStartRequestAsync(path, cancellationToken);
            }
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await ScanNowAsync(_cts.Token);
                await Task.Delay(350, _cts.Token);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    private async Task TryStartRequestAsync(string requestPath, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(requestPath, Encoding.UTF8, cancellationToken);
        }
        catch (IOException)
        {
            return;
        }

        if (!ObservationRequestContract.TryParse(json, out var request, out var completionMode, out var error))
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(requestPath) < TimeSpan.FromSeconds(1))
                return;

            await CompleteInvalidRequestAsync(requestPath, error ?? "OBSERVATION_REQUEST_INVALID", cancellationToken);
            return;
        }

        var activePath = Path.Combine(ActiveDirectory, request!.Id + ".json");
        try
        {
            if (File.Exists(activePath))
            {
                File.Delete(requestPath);
                return;
            }
            File.Move(requestPath, activePath);
        }
        catch (IOException)
        {
            return;
        }

        try
        {
            _registry.Register(request.Id, "OBSERVATION", completionMode, request.Command.Trim());
        }
        catch (InvalidOperationException exception)
        {
            await WriteStandaloneFailureAsync(request.Id, completionMode, "OBSERVATION_REGISTER_FAILED", exception.Message, cancellationToken);
            TryDelete(activePath);
            return;
        }

        TransportEvent?.Invoke(new ObservationSidecarEvent(
            "OBSERVATION QUEUED",
            $"observation {request.Id} · mode {ToToken(completionMode)} · 기계 계측 접수",
            "QUEUED"));

        var running = ExecuteRequestAsync(request, completionMode, activePath, _cts.Token);
        _running[request.Id] = running;
        _ = running.ContinueWith(
            _ => _running.TryRemove(request.Id, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ExecuteRequestAsync(
        ObservationSidecarRequest request,
        MechanicalWorkCompletionMode completionMode,
        string activePath,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        Process? process = null;
        try
        {
            var processWorkingDirectory = ResolveAllowedDirectory(request.WorkingDirectory);
            if (processWorkingDirectory is null)
            {
                await FinishAsync(
                    request, completionMode, false, null, startedAt,
                    "OBSERVATION_WORKING_DIRECTORY_OUTSIDE_ALLOWED_ROOT",
                    "계측 실행 폴더가 현재 작업공간 또는 ProjectHub 실행 폴더 범위를 벗어났습니다.",
                    Array.Empty<string>(), cancellationToken);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = request.Command.Trim(),
                WorkingDirectory = processWorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in request.Arguments ?? Enumerable.Empty<string>())
                startInfo.ArgumentList.Add(argument);
            foreach (var pair in request.Environment ?? new Dictionary<string, string>())
                startInfo.Environment[pair.Key] = pair.Value;

            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
                throw new InvalidOperationException("OBSERVATION_PROCESS_START_FAILED");

            TransportEvent?.Invoke(new ObservationSidecarEvent(
                "OBSERVATION STARTED",
                $"observation {request.Id} · pid {process.Id} · {request.Command.Trim()}",
                "RUNNING"));

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var timedOut = false;
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds ?? 300));
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    timedOut = true;
                    TryKill(process);
                    try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                }
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            var requestedResults = ResolveResultPaths(request.ResultPaths, processWorkingDirectory, out var missingOrInvalid);
            var outputDirectory = Path.Combine(ResultDirectory, request.Id);
            Directory.CreateDirectory(outputDirectory);
            var stdoutPath = Path.Combine(outputDirectory, "stdout.txt");
            var stderrPath = Path.Combine(outputDirectory, "stderr.txt");
            await File.WriteAllTextAsync(stdoutPath, stdout, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(stderrPath, stderr, new UTF8Encoding(false), cancellationToken);

            var resultPaths = requestedResults.Concat(new[] { stdoutPath, stderrPath }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            int? exitCode = process.HasExited ? process.ExitCode : null;
            var success = !timedOut && exitCode == 0 && missingOrInvalid.Count == 0;
            var errorCode = timedOut
                ? "OBSERVATION_TIMEOUT"
                : exitCode != 0
                    ? "OBSERVATION_PROCESS_EXIT"
                    : missingOrInvalid.Count > 0
                        ? "OBSERVATION_RESULT_MISSING"
                        : null;
            var message = timedOut
                ? $"계측이 {request.TimeoutSeconds ?? 300}초 제한 시간을 초과해 종료되었습니다."
                : missingOrInvalid.Count > 0
                    ? "계측 프로세스는 종료됐지만 지정한 결과 경로 일부를 확인하지 못했습니다: " + string.Join(", ", missingOrInvalid)
                    : $"계측 프로세스 종료 · exitCode={exitCode}";

            await FinishAsync(request, completionMode, success, exitCode, startedAt, errorCode, message, resultPaths, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (process is not null)
                TryKill(process);
            var completion = _registry.Complete(
                request.Id, false,
                "현재 실행 구간 취소로 비동기 계측을 중단했습니다.",
                "OBSERVATION_CANCELED",
                Array.Empty<string>());
            if (completion is not null)
                CompletionAvailable?.Invoke(completion);
        }
        catch (Exception exception)
        {
            if (process is not null)
                TryKill(process);
            try
            {
                await FinishAsync(
                    request, completionMode, false,
                    process is { HasExited: true } ? process.ExitCode : null,
                    startedAt, "OBSERVATION_EXECUTION_ERROR", exception.Message,
                    Array.Empty<string>(), CancellationToken.None);
            }
            catch
            {
                var completion = _registry.Complete(
                    request.Id, false, exception.Message,
                    "OBSERVATION_EXECUTION_ERROR",
                    Array.Empty<string>());
                if (completion is not null)
                    CompletionAvailable?.Invoke(completion);
            }
        }
        finally
        {
            process?.Dispose();
            TryDelete(activePath);
        }
    }

    private async Task FinishAsync(
        ObservationSidecarRequest request,
        MechanicalWorkCompletionMode completionMode,
        bool success,
        int? exitCode,
        DateTimeOffset startedAt,
        string? errorCode,
        string message,
        IReadOnlyList<string> resultPaths,
        CancellationToken cancellationToken)
    {
        var finishedAt = DateTimeOffset.UtcNow;
        var outputDirectory = Path.Combine(ResultDirectory, request.Id);
        Directory.CreateDirectory(outputDirectory);
        var resultFilePath = Path.Combine(outputDirectory, "observation-result.json");
        var resultFile = new ObservationResultFile(
            request.Id,
            success ? "COMPLETED" : "FAILED",
            ToToken(completionMode),
            exitCode,
            startedAt,
            finishedAt,
            errorCode,
            message,
            resultPaths);
        await File.WriteAllTextAsync(
            resultFilePath,
            JsonSerializer.Serialize(resultFile, ResultJsonOptions),
            new UTF8Encoding(false),
            cancellationToken);

        var allPaths = resultPaths.Concat(new[] { resultFilePath }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var completion = _registry.Complete(request.Id, success, message, errorCode, allPaths, exitCode);
        if (completion is not null)
        {
            CompletionAvailable?.Invoke(completion);
            TransportEvent?.Invoke(new ObservationSidecarEvent(
                success ? "OBSERVATION COMPLETED" : "OBSERVATION FAILED",
                BuildCompletionMessage(completion),
                success ? "COMPLETED" : errorCode ?? "FAILED"));
        }
    }

    private async Task CompleteInvalidRequestAsync(string requestPath, string error, CancellationToken cancellationToken)
    {
        var id = Path.GetFileNameWithoutExtension(requestPath);
        if (!ObservationRequestContract.IsSafeId(id))
            id = "invalid-" + Guid.NewGuid().ToString("N");

        var activePath = Path.Combine(ActiveDirectory, id + ".json");
        try
        {
            File.Move(requestPath, activePath, true);
        }
        catch (IOException)
        {
            return;
        }

        const MechanicalWorkCompletionMode mode = MechanicalWorkCompletionMode.WorkResultRequired;
        try
        {
            _registry.Register(id, "OBSERVATION", mode, "invalid request");
            var request = new ObservationSidecarRequest { Id = id, Command = "invalid", CompletionMode = "WORK_RESULT_REQUIRED" };
            await FinishAsync(
                request, mode, false, null, DateTimeOffset.UtcNow,
                error, "비동기 계측 요청 형식이 올바르지 않습니다.",
                Array.Empty<string>(), cancellationToken);
        }
        finally
        {
            TryDelete(activePath);
        }
    }

    private async Task WriteStandaloneFailureAsync(
        string id,
        MechanicalWorkCompletionMode mode,
        string errorCode,
        string message,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.Combine(ResultDirectory, id);
        Directory.CreateDirectory(outputDirectory);
        var result = new ObservationResultFile(
            id, "FAILED", ToToken(mode), null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            errorCode, message, Array.Empty<string>());
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "observation-result.json"),
            JsonSerializer.Serialize(result, ResultJsonOptions),
            new UTF8Encoding(false),
            cancellationToken);
        TransportEvent?.Invoke(new ObservationSidecarEvent(
            "OBSERVATION FAILED",
            $"observation {id} · {errorCode} · {message}",
            errorCode));
    }

    private string? ResolveAllowedDirectory(string? requested)
    {
        string fullPath;
        try
        {
            fullPath = string.IsNullOrWhiteSpace(requested)
                ? _workingDirectory
                : Path.GetFullPath(Path.IsPathRooted(requested)
                    ? requested
                    : Path.Combine(_workingDirectory, requested));
        }
        catch
        {
            return null;
        }

        return Directory.Exists(fullPath) && IsAllowedPath(fullPath) ? fullPath : null;
    }

    private IReadOnlyList<string> ResolveResultPaths(
        IReadOnlyList<string>? requestedPaths,
        string processWorkingDirectory,
        out IReadOnlyList<string> missingOrInvalid)
    {
        var found = new List<string>();
        var missing = new List<string>();
        foreach (var requested in requestedPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(requested))
                continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.IsPathRooted(requested)
                    ? requested
                    : Path.Combine(processWorkingDirectory, requested));
            }
            catch
            {
                missing.Add(requested);
                continue;
            }

            if (!IsAllowedPath(fullPath))
            {
                missing.Add(requested);
                continue;
            }

            if (File.Exists(fullPath) || Directory.Exists(fullPath))
                found.Add(fullPath);
            else
                missing.Add(requested);
        }

        missingOrInvalid = missing;
        return found;
    }

    private bool IsAllowedPath(string path)
        => IsWithin(path, _workingDirectory) || IsWithin(path, Path.GetFullPath(AppContext.BaseDirectory));

    private static bool IsWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
            return true;
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCompletionMessage(MechanicalWorkCompletion completion)
    {
        var status = completion.Success ? "COMPLETED" : "FAILED";
        var exit = completion.ExitCode is null ? string.Empty : $" · exitCode={completion.ExitCode}";
        var paths = completion.ResultPaths.Count == 0
            ? string.Empty
            : Environment.NewLine + string.Join(Environment.NewLine, completion.ResultPaths.Select(path => "- " + path));
        return $"observation {completion.Id} · {status}{exit}{Environment.NewLine}{completion.Message}{paths}";
    }

    private static string ToToken(MechanicalWorkCompletionMode mode)
        => mode == MechanicalWorkCompletionMode.WorkResultRequired ? "WORK_RESULT_REQUIRED" : "FINALIZE_ONLY";

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _pump; }
        catch (OperationCanceledException) { }

        var running = _running.Values.ToArray();
        if (running.Length > 0)
        {
            try { await Task.WhenAll(running); }
            catch (OperationCanceledException) { }
        }

        _scanGate.Dispose();
        _cts.Dispose();
    }
}
