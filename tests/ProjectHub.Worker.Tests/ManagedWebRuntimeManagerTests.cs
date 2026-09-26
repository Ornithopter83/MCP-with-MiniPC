using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ManagedWebRuntimeManagerTests
{
    [Fact]
    public void RoleProfiles_AreDistinct()
    {
        var hq = ManagedWebRuntimeManager.ProfilePathFor(ManagedWebRole.Hq);
        var resource = ManagedWebRuntimeManager.ProfilePathFor(ManagedWebRole.Resource);

        Assert.NotEqual(hq, resource);
        Assert.EndsWith("HQ", hq, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("RESOURCE", resource, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ManagedWebRole.Hq, "HQ")]
    [InlineData(ManagedWebRole.Resource, "RESOURCE")]
    public void ResolveLaunchUrl_CarriesManagedRole(ManagedWebRole role, string expectedRole)
    {
        var url = ManagedWebRuntimeManager.ResolveLaunchUrl(role, null);

        Assert.StartsWith("https://chatgpt.com/", url, StringComparison.Ordinal);
        Assert.Contains("projecthub-managed-role=" + expectedRole, url, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveLaunchUrl_UsesBoundConversationWhenAvailable()
    {
        var url = ManagedWebRuntimeManager.ResolveLaunchUrl(
            ManagedWebRole.Hq,
            "abc-123");

        Assert.StartsWith(
            "https://chatgpt.com/c/abc-123?",
            url,
            StringComparison.Ordinal);
        Assert.Contains("projecthub-managed-role=HQ", url, StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenLaunchArguments_UseDedicatedProfileAndExtension()
    {
        var extension = Path.Combine(Path.GetTempPath(), "projecthub-extension");
        var profile = Path.Combine(Path.GetTempPath(), "projecthub-profile");

        var arguments = ManagedWebRuntimeManager.BuildLaunchArguments(
            ManagedWebRole.Resource,
            hidden: true,
            extension,
            profile,
            "resource-conversation");

        Assert.Contains(arguments, item => item.StartsWith("--user-data-dir=", StringComparison.Ordinal));
        Assert.Contains(arguments, item => item.StartsWith("--load-extension=", StringComparison.Ordinal));
        Assert.Contains(arguments, item => item.StartsWith("--disable-extensions-except=", StringComparison.Ordinal));
        Assert.Contains("--window-position=-32000,-32000", arguments);
        Assert.Contains("--start-minimized", arguments);
        Assert.Contains(
            arguments,
            item => item.Contains("projecthub-managed-role=RESOURCE", StringComparison.Ordinal));
    }

    [Fact]
    public void VisibleLaunchArguments_DoNotForceHiddenWindow()
    {
        var arguments = ManagedWebRuntimeManager.BuildLaunchArguments(
            ManagedWebRole.Hq,
            hidden: false,
            Path.Combine(Path.GetTempPath(), "projecthub-extension"),
            Path.Combine(Path.GetTempPath(), "projecthub-profile"),
            null);

        Assert.DoesNotContain("--window-position=-32000,-32000", arguments);
        Assert.DoesNotContain("--start-minimized", arguments);
    }
}
