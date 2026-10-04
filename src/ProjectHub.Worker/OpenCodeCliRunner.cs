using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record OpenCodeCliResult(
    string ExecutablePath,
    string Model,
    string Reasoning,
    string? SessionId,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string FinalMessage,
    IReadOnlyList<CodexCliFile> Files,
    CodexUsage Usage,
    IReadOnlyList<CodexCommandExecution> CommandExecutions);

public sealed class OpenCodeCliRunner : IDisposable
{
    public const string MuseContributorFreeModel = "muse-spark-1.3-contributor-free";
    public const string MuseContributorFreeModelSpecifier =
        "opencode/" + MuseContributorFreeModel;

    private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();
    private readonly ConcurrentDictionary<int, WorkerChildProcessJob> _activeProcessJobs = new();
    private int _disposed;

    public string? FindExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in new[] { "opencode.exe", "opencode.cmd", "opencode.bat" })
            {
                try
                {
                    var candidate = Path.Combine(directory, name);
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch (Exception exception) when (
                    exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                }
            }
        }

        return null;
    }

    public async Task<OpenCodeCliResult> RunAsync(
        AiRoleRunRequest request,
        string effectivePrompt)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(OpenCodeCliRunner));
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory) ||
            !Directory.Exists(request.WorkingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"OpenCode 작업 폴더를 찾을 수 없습니다: {request.WorkingDirectory}");
        }

        var executable = FindExecutable()
            ?? throw new FileNotFoundException(
                "OpenCode CLI를 찾을 수 없습니다. Windows PATH에서 opencode.exe/opencode.cmd를 확인하세요.");

        var sessionId = CodexCliRunner.NormalizeSessionId(request.SessionId);
        var arguments = new List<string>
        {
            "--pure",
            "run",
            "--auto",
            "--format",
            "json",
            "--model",
            MuseContributorFreeModelSpecifier
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            arguments.Add("--session");
            arguments.Add(sessionId);
        }

        using var processJob = new WorkerChildProcessJob("OpenCode CLI run");
        var startInfo = CreateStartInfo(
            executable,
            request.WorkingDirectory,
            arguments);
        ApplyEnvironment(startInfo, request);

        int? activeProcessId = null;
        var accumulator = new OpenCodeEventAccumulator(sessionId);

        try
        {
            using var launched = processJob.Start(
                startInfo,
                request.CancellationToken);
            var process = launched.Process;
            process.EnableRaisingEvents = true;

            activeProcessId = process.Id;
            _activeProcesses[process.Id] = process;
            _activeProcessJobs[process.Id] = processJob;

            var stdoutBuilder = new StringBuilder();
            var stdoutTask = Task.Run(async () =>
            {
                while (true)
                {
                    var line = await launched.StandardOutput!
                        .ReadLineAsync(request.CancellationToken);
                    if (line is null)
                        break;

                    stdoutBuilder.AppendLine(line);
                    accumulator.Process(
                        line,
                        request.Progress,
                        request.SessionStarted);
                }
            }, CancellationToken.None);
            var stderrTask = launched.StandardError!
                .ReadToEndAsync(request.CancellationToken);

            try
            {
                await launched.StandardInput!
                    .WriteAsync(
                        (effectivePrompt + Environment.NewLine).AsMemory(),
                        request.CancellationToken)
                    .ConfigureAwait(false);
                await launched.StandardInput!
                    .FlushAsync(request.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                processJob.Dispose();
                await TerminateProcessTreeAsync(process).ConfigureAwait(false);
                throw;
            }
            finally
            {
                launched.StandardInput!.Close();
            }

            try
            {
                await process.WaitForExitAsync(request.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                processJob.Dispose();
                await TerminateProcessTreeAsync(process).ConfigureAwait(false);
                throw;
            }

            var exitCode = process.ExitCode;

            // OpenCode가 만든 후손이 pipe를 상속한 경우에도 drain이 끝나도록
            // launcher 종료 뒤 이 실행 전용 Job을 먼저 닫는다.
            processJob.Dispose();

            await stdoutTask.ConfigureAwait(false);
            var stdout = stdoutBuilder.ToString();
            var stderr = await stderrTask.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(accumulator.ErrorText))
            {
                stderr = string.IsNullOrWhiteSpace(stderr)
                    ? accumulator.ErrorText
                    : stderr.TrimEnd() + Environment.NewLine + accumulator.ErrorText;
            }

            return new OpenCodeCliResult(
                executable,
                MuseContributorFreeModel,
                request.Role.Reasoning,
                accumulator.SessionId,
                exitCode,
                stdout,
                stderr,
                accumulator.FinalMessage,
                Array.Empty<CodexCliFile>(),
                accumulator.Usage,
                accumulator.CommandExecutions);
        }
        finally
        {
            if (activeProcessId is { } processId)
            {
                _activeProcessJobs.TryRemove(processId, out _);
                _activeProcesses.TryRemove(processId, out _);
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var extension = Path.GetExtension(executable);
        var isCommandScript =
            string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);

        var startInfo = new ProcessStartInfo
        {
            FileName = isCommandScript
                ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe"
                : executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (isCommandScript)
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(BuildCommandScriptInvocation(executable, arguments));
        }
        else
        {
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string BuildCommandScriptInvocation(
        string executable,
        IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append('"')
            .Append(executable.Replace("\"", "\"\"", StringComparison.Ordinal))
            .Append('"');
        foreach (var argument in arguments)
        {
            builder.Append(' ')
                .Append(QuoteCommandArgument(argument));
        }

        return builder.ToString();
    }

    private static string QuoteCommandArgument(string value)
    {
        if (value.Length > 0 &&
            value.All(character =>
                char.IsLetterOrDigit(character) ||
                character is '-' or '_' or '.' or '/' or ':' or '='))
        {
            return value;
        }

        return "\"" +
               value.Replace("\"", "\"\"", StringComparison.Ordinal) +
               "\"";
    }

    private static void ApplyEnvironment(
        ProcessStartInfo startInfo,
        AiRoleRunRequest request)
    {
        if (request.EnvironmentVariables is not null)
        {
            foreach (var pair in request.EnvironmentVariables)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        startInfo.Environment["OPENCODE_AUTO_SHARE"] = "false";
        startInfo.Environment["OPENCODE_DISABLE_AUTOUPDATE"] = "true";
        startInfo.Environment["OPENCODE_DISABLE_TERMINAL_TITLE"] = "true";
        startInfo.Environment["OPENCODE_DISABLE_DEFAULT_PLUGINS"] = "true";
        startInfo.Environment["OPENCODE_DISABLE_LSP_DOWNLOAD"] = "true";
        startInfo.Environment["OPENCODE_CONFIG_CONTENT"] =
            BuildInlineConfig(request);
        startInfo.Environment["NO_COLOR"] = "1";
    }

    internal static string BuildInlineConfig(AiRoleRunRequest request)
    {
        var permission = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["external_directory"] = BuildExternalDirectoryRules(request)
        };

        switch (request.Sandbox)
        {
            case CodexSandboxMode.ReadOnly:
                permission["edit"] = "deny";
                permission["bash"] = BuildReadOnlyShellRules();
                break;

            case CodexSandboxMode.WorkspaceWrite:
                permission["edit"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["*"] = "allow",
                    [".git"] = "deny",
                    [".git/**"] = "deny",
                    ["**/.git"] = "deny",
                    ["**/.git/**"] = "deny"
                };
                permission["bash"] = BuildWorkspaceShellRules(
                    request.BuildExecutionAllowed);
                break;

            default:
                permission["edit"] = "allow";
                permission["bash"] = "allow";
                break;
        }

        var config = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["$schema"] = "https://opencode.ai/config.json",
            ["permission"] = permission
        };

        return JsonSerializer.Serialize(config);
    }

    private static Dictionary<string, string> BuildExternalDirectoryRules(
        AiRoleRunRequest request)
    {
        var rules = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["*"] = "deny"
        };

        var directories = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

        foreach (var directory in request.AdditionalWritableDirectories ??
                     Array.Empty<string>())
        {
            AddDirectory(directories, directory);
        }

        foreach (var attachment in request.InputAttachments ??
                     Array.Empty<AiInputAttachment>())
        {
            AddDirectory(directories, Path.GetDirectoryName(attachment.Path));
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (var key in new[]
                     {
                         "PROJECTHUB_RESOURCE_TEMP",
                         "PROJECTHUB_WORK_TEMP",
                         "PROJECTHUB_PUBLISH_ROOT",
                         "PROJECTHUB_TARGET_WORKSPACE"
                     })
            {
                if (request.EnvironmentVariables.TryGetValue(key, out var directory))
                    AddDirectory(directories, directory);
            }
        }

        foreach (var directory in directories)
        {
            var normalized = directory
                .Replace('\\', '/')
                .TrimEnd('/');
            rules[normalized] = "allow";
            rules[normalized + "/**"] = "allow";
        }

        return rules;
    }

    private static void AddDirectory(
        ISet<string> directories,
        string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            if (Directory.Exists(directory))
                directories.Add(Path.GetFullPath(directory));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }

    private static Dictionary<string, string> BuildReadOnlyShellRules()
    {
        var rules = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["*"] = "deny"
        };

        foreach (var pattern in new[]
                 {
                     "git status*",
                     "git diff*",
                     "git show*",
                     "git log*",
                     "git rev-parse*",
                     "git ls-remote*",
                     "git cat-file*",
                     "git merge-base*",
                     "git for-each-ref*",
                     "git branch --show-current*",
                     "git branch --contains*",
                     "git remote*",
                     "dir*",
                     "type *",
                     "where *",
                     "findstr *",
                     "rg *",
                     "grep *",
                     "cat *",
                     "ls*",
                     "pwd*",
                     "*Get-Content*",
                     "*Get-ChildItem*",
                     "*Select-String*"
                 })
        {
            rules[pattern] = "allow";
        }

        return rules;
    }

    private static Dictionary<string, string> BuildWorkspaceShellRules(
        bool buildExecutionAllowed)
    {
        var rules = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["*"] = "allow"
        };

        foreach (var pattern in GitMutationDeniedPatterns)
            rules[pattern] = "deny";

        if (!buildExecutionAllowed)
        {
            foreach (var pattern in BuildExecutionPolicy.OpenCodeDeniedCommandPatterns)
                rules[pattern] = "deny";
        }

        return rules;
    }

    private static IReadOnlyList<string> GitMutationDeniedPatterns { get; } =
        new[]
        {
            "*git add*",
            "*git commit*",
            "*git push*",
            "*git reset*",
            "*git checkout*",
            "*git switch*",
            "*git merge*",
            "*git rebase*",
            "*git cherry-pick*",
            "*git revert*",
            "*git clean*",
            "*git rm*",
            "*git mv*",
            "*git tag*",
            "*git update-ref*",
            "*git worktree add*",
            "*git worktree remove*",
            "*git init*",
            "*git branch -d*",
            "*git branch -D*"
        };

    private static async Task TerminateProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        try
        {
            if (!process.HasExited)
                await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var job in _activeProcessJobs.Values.ToArray())
        {
            try { job.Dispose(); }
            catch { }
        }

        foreach (var process in _activeProcesses.Values.ToArray())
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

        _activeProcessJobs.Clear();
        _activeProcesses.Clear();
    }

    private sealed class OpenCodeEventAccumulator
    {
        private readonly Dictionary<string, List<TextPart>> _messages =
            new(StringComparer.Ordinal);
        private readonly List<string> _messageOrder = new();
        private readonly List<string> _fallbackText = new();
        private readonly List<CodexCommandExecution> _commands = new();
        private readonly List<string> _errors = new();
        private string? _lastTextMessageId;

        public OpenCodeEventAccumulator(string? sessionId)
        {
            SessionId = sessionId;
        }

        public string? SessionId { get; private set; }
        public CodexUsage Usage { get; private set; } = CodexUsage.Empty;

        public IReadOnlyList<CodexCommandExecution> CommandExecutions =>
            _commands
                .DistinctBy(item => (item.Command, item.ExitCode, item.Output))
                .Take(250)
                .ToArray();

        public string ErrorText => string.Join(
            Environment.NewLine,
            _errors.Where(value => !string.IsNullOrWhiteSpace(value)));

        public string FinalMessage
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_lastTextMessageId) &&
                    _messages.TryGetValue(_lastTextMessageId, out var parts))
                {
                    return string.Join(
                        Environment.NewLine,
                        parts.Select(part => part.Text)
                            .Where(value => !string.IsNullOrWhiteSpace(value)))
                        .Trim();
                }

                return string.Join(
                    Environment.NewLine,
                    _fallbackText.Where(value => !string.IsNullOrWhiteSpace(value)))
                    .Trim();
            }
        }

        public void Process(
            string line,
            Action<string>? progress,
            Action<string>? sessionStarted)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return;

                if (TryGetString(root, "sessionID", out var eventSessionId) &&
                    !string.IsNullOrWhiteSpace(eventSessionId) &&
                    !string.Equals(
                        SessionId,
                        eventSessionId,
                        StringComparison.Ordinal))
                {
                    var firstSession = string.IsNullOrWhiteSpace(SessionId);
                    SessionId = eventSessionId.Trim();
                    if (firstSession)
                    {
                        try { sessionStarted?.Invoke(SessionId); }
                        catch { }
                    }
                }

                var type = GetString(root, "type");
                switch (type)
                {
                    case "text":
                        CollectText(root);
                        break;
                    case "tool_use":
                        CollectTool(root, progress);
                        break;
                    case "step_finish":
                        CollectUsage(root);
                        break;
                    case "error":
                        CollectError(root);
                        break;
                }
            }
            catch (JsonException)
            {
            }
        }

        private void CollectText(JsonElement root)
        {
            if (!TryGetProperty(root, "part", out var part) ||
                !TryGetString(part, "text", out var text) ||
                string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (!TryGetString(part, "messageID", out var messageId) ||
                string.IsNullOrWhiteSpace(messageId))
            {
                _fallbackText.Add(text.Trim());
                return;
            }

            if (!_messages.TryGetValue(messageId, out var parts))
            {
                parts = new List<TextPart>();
                _messages[messageId] = parts;
                _messageOrder.Add(messageId);
            }

            var partId = GetString(part, "id") ??
                         (parts.Count + 1).ToString(
                             System.Globalization.CultureInfo.InvariantCulture);
            var existingIndex = parts.FindIndex(item =>
                string.Equals(item.Id, partId, StringComparison.Ordinal));
            var entry = new TextPart(partId, text.Trim());
            if (existingIndex >= 0)
                parts[existingIndex] = entry;
            else
                parts.Add(entry);

            _lastTextMessageId = messageId;
        }

        private void CollectTool(
            JsonElement root,
            Action<string>? progress)
        {
            if (!TryGetProperty(root, "part", out var part))
                return;

            var tool = GetString(part, "tool") ?? "tool";
            if (!TryGetProperty(part, "state", out var state))
                return;

            var status = GetString(state, "status") ?? "completed";
            string? command = null;
            if (TryGetProperty(state, "input", out var input))
            {
                command = GetString(input, "command") ??
                          GetString(input, "cmd");
            }

            if (string.Equals(tool, "bash", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool, "shell", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(command))
                {
                    var exitCode = string.Equals(
                        status,
                        "completed",
                        StringComparison.OrdinalIgnoreCase)
                        ? FindInt32(state, "exit") ?? 0
                        : FindInt32(state, "exit") ?? 1;
                    var output = GetString(state, "output") ??
                                 GetString(state, "error");
                    if (output is { Length: > 4000 })
                        output = output[..4000];

                    _commands.Add(new CodexCommandExecution(
                        command.Trim().Length <= 2000
                            ? command.Trim()
                            : command.Trim()[..2000],
                        exitCode,
                        output));

                    EmitProgress(
                        progress,
                        "OpenCode shell · " + SingleLine(command, 500));
                    return;
                }
            }

            var title = GetString(state, "title") ??
                        GetString(part, "title") ??
                        tool;
            EmitProgress(
                progress,
                $"OpenCode {tool} · {SingleLine(title, 500)}");
        }

        private void CollectUsage(JsonElement root)
        {
            if (!TryGetProperty(root, "part", out var part) ||
                !TryGetProperty(part, "tokens", out var tokens))
            {
                return;
            }

            var input = ReadLong(tokens, "input");
            var output = ReadLong(tokens, "output");
            var reasoning = ReadLong(tokens, "reasoning");
            var cached = 0L;
            if (TryGetProperty(tokens, "cache", out var cache))
                cached = ReadLong(cache, "read");

            if (input == 0 &&
                output == 0 &&
                reasoning == 0 &&
                cached == 0)
            {
                return;
            }

            var total = input + output + reasoning;
            Usage = Usage.Add(new CodexUsage(
                input,
                cached,
                output,
                reasoning,
                total,
                total,
                true));
        }

        private void CollectError(JsonElement root)
        {
            if (!TryGetProperty(root, "error", out var error))
                return;

            var message = FindString(error, "message") ??
                          FindString(error, "name") ??
                          error.GetRawText();
            if (!string.IsNullOrWhiteSpace(message))
                _errors.Add(message.Trim());
        }

        private static void EmitProgress(
            Action<string>? progress,
            string message)
        {
            if (progress is null || string.IsNullOrWhiteSpace(message))
                return;
            try { progress(message); }
            catch { }
        }

        private static string SingleLine(string value, int maxLength)
        {
            var normalized = value
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return normalized.Length <= maxLength
                ? normalized
                : normalized[..maxLength];
        }

        private sealed record TextPart(string Id, string Text);
    }

    private static long ReadLong(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        if (property.TryGetInt64(out var integer))
            return integer;
        if (property.TryGetDouble(out var number) &&
            number >= 0 &&
            number <= long.MaxValue)
        {
            return (long)number;
        }

        return 0;
    }

    private static int? FindInt32(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out var number))
                {
                    return number;
                }

                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    var nested = FindInt32(property.Value, propertyName);
                    if (nested.HasValue)
                        return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindInt32(item, propertyName);
                if (nested.HasValue)
                    return nested;
            }
        }

        return null;
    }

    private static string? FindString(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    var nested = FindString(property.Value, propertyName);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(
        JsonElement element,
        string propertyName)
        => TryGetString(element, propertyName, out var value)
            ? value
            : null;

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        if (TryGetProperty(element, propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
