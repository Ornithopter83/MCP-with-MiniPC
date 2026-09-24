namespace ProjectHub.Worker;

public sealed record AiProviderVisual(
    AiServiceProvider? Provider,
    string DisplayName,
    string ColorAsset,
    string GrayAsset,
    string FallbackSymbol);

public static class ProviderVisualCatalog
{
    private static readonly IReadOnlyDictionary<AiServiceProvider, AiProviderVisual> Visuals =
        new Dictionary<AiServiceProvider, AiProviderVisual>
        {
            [AiServiceProvider.OpenAI] = new(AiServiceProvider.OpenAI, "OpenAI", "current-openai.png", "current-openai-gray.png", "O"),
            [AiServiceProvider.Claude] = new(AiServiceProvider.Claude, "Claude", "current-console.png", "current-console-gray.png", "C"),
            [AiServiceProvider.Muse] = new(AiServiceProvider.Muse, "Muse", "current-console.png", "current-console-gray.png", "M")
        };

    public static AiProviderVisual Resolve(string? providerWireId)
    {
        if (AiProviderCatalog.TryParse(providerWireId, out var provider))
            return Visuals[provider];
        return new(null, string.IsNullOrWhiteSpace(providerWireId) ? "Unknown" : providerWireId.Trim(), "current-console.png", "current-console-gray.png", "?");
    }
}
