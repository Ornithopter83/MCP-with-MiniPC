namespace ProjectHub.Worker;

public readonly record struct PipelineIdleCardVisual(string Background, string IconBackground, string Foreground, string Border);

public static class PipelineIdleCardVisualPolicy
{
    public static PipelineIdleCardVisual Resolve(bool active) => active
        ? new("#E0F2F4", "#0D7884", "#0F6B73", "#0D7884")
        : new("#B8C8DA", "#526477", "#FFFFFF", "Transparent");
}
