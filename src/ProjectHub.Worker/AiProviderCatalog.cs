namespace ProjectHub.Worker;

public enum AiServiceProvider
{
    OpenAI,
    Claude,
    Muse
}

public sealed record AiModelDescriptor(
    string Id,
    string DisplayName,
    string DefaultReasoning,
    IReadOnlyList<string> ReasoningOptions)
{
    public bool SupportsReasoning(string? reasoning) =>
        !string.IsNullOrWhiteSpace(reasoning) &&
        ReasoningOptions.Contains(reasoning, StringComparer.OrdinalIgnoreCase);
}

public sealed record AiProviderDescriptor(
    AiServiceProvider Provider,
    string WireId,
    string DisplayName,
    bool ExecutionConfigured,
    IReadOnlyList<AiModelDescriptor> Models)
{
    public AiModelDescriptor? FindModel(string? modelId) =>
        Models.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
}

public static class AiProviderCatalog
{
    public static IReadOnlyList<AiProviderDescriptor> Current { get; } = new[]
    {
        CreateOpenAi(),
        new AiProviderDescriptor(AiServiceProvider.Claude, "claude", "Claude", false, Array.Empty<AiModelDescriptor>()),
        new AiProviderDescriptor(AiServiceProvider.Muse, "muse", "Muse", false, Array.Empty<AiModelDescriptor>())
    };

    public static AiProviderDescriptor Get(AiServiceProvider provider) =>
        Current.Single(item => item.Provider == provider);

    public static bool TryParse(string? value, out AiServiceProvider provider)
    {
        provider = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        switch (value.Trim().ToLowerInvariant())
        {
            case "openai":
                provider = AiServiceProvider.OpenAI;
                return true;
            case "claude":
                provider = AiServiceProvider.Claude;
                return true;
            case "muse":
                provider = AiServiceProvider.Muse;
                return true;
            default:
                return false;
        }
    }

    public static string ToWireId(AiServiceProvider provider) => Get(provider).WireId;

    private static AiProviderDescriptor CreateOpenAi()
    {
        var models = CodexServedModels.Current
            .Select(model => new AiModelDescriptor(
                model.Id,
                model.DisplayName,
                model.DefaultReasoning.ToString().ToLowerInvariant(),
                model.ReasoningDepths.Select(depth => depth.ToString().ToLowerInvariant()).ToArray()))
            .ToArray();

        return new AiProviderDescriptor(AiServiceProvider.OpenAI, "openai", "OpenAI", true, models);
    }
}
