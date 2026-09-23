using System.Diagnostics;
using System.Text.Json;

namespace ProjectHub.Worker;

public enum CodexServedModel
{
    Gpt6Astra,
    Gpt6Sol,
    Gpt6Luna,
    Gpt56Sol,
    Gpt56Terra,
    Gpt56Luna,
    Gpt55
}

public enum CodexReasoningDepth
{
    Low,
    Medium,
    High,
    XHigh,
    Max,
    Ultra
}

public sealed record CodexServedModelOption(
    CodexServedModel Value,
    string Id,
    string DisplayName,
    CodexReasoningDepth DefaultReasoning,
    IReadOnlyList<CodexReasoningDepth> ReasoningDepths);

public sealed record CodexModelRequest(CodexServedModelOption Model, CodexReasoningDepth Reasoning)
{
    public string ReasoningId => Reasoning.ToString().ToLowerInvariant();

    public string ToQueryString() =>
        $"model={Uri.EscapeDataString(Model.Id)}&reasoning={Uri.EscapeDataString(ReasoningId)}";

    public IReadOnlyList<string> ToCliArguments() =>
        new[] { "--model", Model.Id, "-c", $"model_reasoning_effort=\"{ReasoningId}\"" };

    public static bool TryCreate(string? modelId, string? reasoningId, out CodexModelRequest request)
    {
        request = null!;
        var model = CodexServedModels.Find(modelId);
        if (model is null || !Enum.TryParse<CodexReasoningDepth>(reasoningId, true, out var reasoning) ||
            !model.ReasoningDepths.Contains(reasoning))
            return false;

        request = new CodexModelRequest(model, reasoning);
        return true;
    }
}

public static class CodexServedModels
{
    public static IReadOnlyList<CodexServedModelOption> Current { get; } = new[]
    {
        Create(CodexServedModel.Gpt6Astra, "gpt-6-astra", "GPT-6 Astra", CodexReasoningDepth.Low, CodexReasoningDepth.Low, CodexReasoningDepth.Medium, CodexReasoningDepth.High, CodexReasoningDepth.XHigh, CodexReasoningDepth.Max, CodexReasoningDepth.Ultra),
        Create(CodexServedModel.Gpt6Sol, "gpt-6-sol", "GPT-6 Sol", CodexReasoningDepth.Medium, CodexReasoningDepth.Low, CodexReasoningDepth.Medium, CodexReasoningDepth.High, CodexReasoningDepth.XHigh, CodexReasoningDepth.Max, CodexReasoningDepth.Ultra),
        Create(CodexServedModel.Gpt6Luna, "gpt-6-luna", "GPT-6 Luna", CodexReasoningDepth.Medium, CodexReasoningDepth.Low, CodexReasoningDepth.Medium, CodexReasoningDepth.High, CodexReasoningDepth.XHigh, CodexReasoningDepth.Max),
        Create(CodexServedModel.Gpt56Sol, "gpt-5.6-sol", "GPT-5.6 Sol", CodexReasoningDepth.Low, CodexReasoningDepth.Low, CodexReasoningDepth.Medium, CodexReasoningDepth.High, CodexReasoningDepth.XHigh, CodexReasoningDepth.Max, CodexReasoningDepth.Ultra),
        Create(CodexServedModel.Gpt56Terra, "gpt-5.6-terra", "GPT-5.6 Terra", CodexReasoningDepth.Medium, CodexReasoningDepth.Low, CodexReasoningDepth.Medium, CodexReasoningDepth.High, CodexReasoningDepth.XHigh, CodexReasoningDepth.Max, CodexReasoningDepth.Ultra),
        Create(CodexServedModel.Gpt56Luna, "gpt-5.6-luna", "GPT-5.6 Luna", CodexReasoningDepth.Medium, CodexReasoningDepth.Low, CodexReasoningDepth.Medium, CodexReasoningDepth.High, CodexReasoningDepth.XHigh, CodexReasoningDepth.Max),
        Create(CodexServedModel.Gpt55, "gpt-5.5", "GPT-5.5", CodexReasoningDepth.Medium, CodexReasoningDepth.Low, CodexReasoningDepth.Medium, CodexReasoningDepth.High, CodexReasoningDepth.XHigh)
    };

    public static CodexServedModelOption? Find(string? id) => Current.FirstOrDefault(option =>
        string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase));

    private static CodexServedModelOption Create(
        CodexServedModel value,
        string id,
        string displayName,
        CodexReasoningDepth defaultReasoning,
        params CodexReasoningDepth[] reasoningDepths) =>
        new(value, id, displayName, defaultReasoning, reasoningDepths);
}

public sealed record CodexModelCapability(
    string Id,
    string DisplayName,
    string DefaultReasoning,
    IReadOnlyList<string> ReasoningEfforts,
    bool SupportedInApi);

public sealed record CodexModelCatalogResult(
    IReadOnlyList<CodexModelCapability> Models,
    string Status)
{
    public CodexModelCapability? Find(string? modelId) => Models.FirstOrDefault(model =>
        string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));

    public bool Supports(string? modelId, string? reasoning)
    {
        var model = Find(modelId);
        return model is not null && model.SupportedInApi &&
            model.ReasoningEfforts.Contains(reasoning ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }
}

public static class CodexModelCatalog
{
    public static CodexModelCatalogResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                return new(Array.Empty<CodexModelCapability>(), "MODEL_CATALOG_INVALID");

            var result = new List<CodexModelCapability>();
            foreach (var model in models.EnumerateArray())
            {
                var id = ReadString(model, "slug");
                var displayName = ReadString(model, "display_name");
                var visibility = ReadString(model, "visibility");
                var supportedInApi = model.TryGetProperty("supported_in_api", out var supported) && supported.ValueKind == JsonValueKind.True;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName) || visibility != "list" || !supportedInApi)
                    continue;

                var defaultReasoning = ReadString(model, "default_reasoning_level");
                var efforts = new List<string>();
                if (model.TryGetProperty("supported_reasoning_levels", out var levels) && levels.ValueKind == JsonValueKind.Array)
                {
                    foreach (var level in levels.EnumerateArray())
                    {
                        var effort = ReadString(level, "effort");
                        if (!string.IsNullOrWhiteSpace(effort) && !efforts.Contains(effort, StringComparer.OrdinalIgnoreCase))
                            efforts.Add(effort);
                    }
                }
                if (efforts.Count == 0 || !efforts.Contains(defaultReasoning, StringComparer.OrdinalIgnoreCase))
                    continue;
                result.Add(new(id, displayName, defaultReasoning, efforts, supportedInApi));
            }

            return new(result.OrderBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
                result.Count == 0 ? "MODEL_CATALOG_EMPTY" : "READY");
        }
        catch (JsonException)
        {
            return new(Array.Empty<CodexModelCapability>(), "MODEL_CATALOG_INVALID");
        }
    }

    public static async Task<CodexModelCatalogResult> LoadAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("debug");
        process.StartInfo.ArgumentList.Add("models");

        try
        {
            if (!process.Start()) return new(Array.Empty<CodexModelCapability>(), "MODEL_CATALOG_UNAVAILABLE");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            _ = await stderrTask;
            if (process.ExitCode != 0) return new(Array.Empty<CodexModelCapability>(), "MODEL_CATALOG_UNAVAILABLE");
            return Parse(stdout);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new(Array.Empty<CodexModelCapability>(), "MODEL_CATALOG_TIMEOUT");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(Array.Empty<CodexModelCapability>(), "MODEL_CATALOG_UNAVAILABLE");
        }
        finally
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
        }
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
}
