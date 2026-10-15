using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UnityDesignCardActivationPolicyTests
{
    [Theory]
    [InlineData(true, false, true, false)]   // HIGH handoff, no initial DESIGN
    [InlineData(true, false, false, true)]   // HIGH returned and no initial DESIGN
    [InlineData(true, true, true, false)]    // HQ initial DESIGN
    [InlineData(true, true, false, false)]   // HQ design is still a reserved stage
    [InlineData(true, null, true, false)]    // Unspecified reservation during run
    [InlineData(false, false, false, false)] // No active coordinator milestone
    public void DesignCard_ActiveHandoffOverridesInitialReservation(
        bool coordinator, bool? reserved, bool designRunning, bool expectedDisabled)
    {
        Assert.Equal(expectedDisabled,
            UnityDesignCardActivationPolicy.IsDisabled(
                coordinator, reserved, designRunning));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)] // Retired RESOURCE is still outstanding.
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void DesignTurnHighlight_FollowsCurrentTurnNotLegacyResource(
        bool designRunning, bool legacyResourceOutstanding, bool expectedActive)
    {
        var highlighted = UnityDesignCardActivationPolicy.ShouldHighlight(
            designRunning, legacyResourceOutstanding);
        Assert.Equal(expectedActive, highlighted);
        var visual = PipelineCardVisualPolicy.Resolve(
            initialInputIdle: false, isCurrent: highlighted, disabled: false);
        Assert.Equal(expectedActive, visual.IsColored);
    }
}
