namespace ProjectHub.Worker;

public readonly record struct PipelineCardVisualState(bool IsColored, double Opacity);

public static class PipelineCardVisualPolicy
{
    public static PipelineCardVisualState Resolve(bool initialInputIdle, bool isCurrent, bool disabled) =>
        new(IsColored: initialInputIdle || isCurrent, Opacity: initialInputIdle || !disabled ? 1.0 : 0.85);
}
