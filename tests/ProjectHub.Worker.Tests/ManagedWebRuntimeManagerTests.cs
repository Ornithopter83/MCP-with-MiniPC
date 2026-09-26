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
        Assert.True(hq.EndsWith("HQ", StringComparison.OrdinalIgnoreCase));
        Assert.True(resource.EndsWith("RESOURCE", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(ManagedWebRole.Hq, "HQ")]
    [InlineData(ManagedWebRole.Resource, "RESOURCE")]
    public void ResolveLaunchUrl_CarriesRoleAndRuntimeToken(ManagedWebRole role, string expectedRole)
    {
        var url = ManagedWebRuntimeManager.ResolveLaunchUrl(
            role,
            null,
            "runtime-token");

        Assert.True(url.StartsWith("https://chatgpt.com/", StringComparison.Ordinal));
        Assert.True(url.Contains("projecthub-managed-role=" + expectedRole, StringComparison.Ordinal));
        Assert.True(url.Contains("projecthub-runtime-token=runtime-token", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveLaunchUrl_UsesBoundConversationWhenAvailable()
    {
        var url = ManagedWebRuntimeManager.ResolveLaunchUrl(
            ManagedWebRole.Hq,
            "abc-123",
            "runtime-token");

        Assert.True(url.StartsWith(
            "https://chatgpt.com/c/abc-123?",
            StringComparison.Ordinal));
        Assert.True(url.Contains("projecthub-managed-role=HQ", StringComparison.Ordinal));
        Assert.True(url.Contains("projecthub-runtime-token=runtime-token", StringComparison.Ordinal));
    }

    [Fact]
    public void WorkerOwnedBrowserExecutable_AcceptsManagedRuntimeAndRejectsArbitraryChrome()
    {
        var managed = Path.Combine(
            WorkerPaths.ManagedWebBrowserRuntime,
            "chrome-win64",
            "chrome.exe");
        var arbitrary = Path.Combine(
            Path.GetTempPath(),
            "external-chrome",
            "chrome.exe");

        Assert.True(ManagedWebRuntimeManager.IsWorkerOwnedBrowserExecutable(managed));
        Assert.False(ManagedWebRuntimeManager.IsWorkerOwnedBrowserExecutable(arbitrary));
        Assert.False(ManagedWebRuntimeManager.IsWorkerOwnedBrowserExecutable(null));
    }

    [Fact]
    public void HiddenLaunchArguments_UseAppModeDedicatedProfileAndExtension()
    {
        var extension = Path.Combine(Path.GetTempPath(), "projecthub-extension");
        var profile = Path.Combine(Path.GetTempPath(), "projecthub-profile");

        var arguments = ManagedWebRuntimeManager.BuildLaunchArguments(
            ManagedWebRole.Resource,
            hidden: true,
            extension,
            profile,
            "resource-conversation",
            "runtime-token");

        Assert.True(arguments.Any(item => item.StartsWith("--user-data-dir=", StringComparison.Ordinal)));
        Assert.True(arguments.Any(item => item.StartsWith("--load-extension=", StringComparison.Ordinal)));
        Assert.True(arguments.Any(item => item.StartsWith("--disable-extensions-except=", StringComparison.Ordinal)));
        Assert.Contains("--window-position=-32000,-32000", arguments);
        Assert.Contains("--start-minimized", arguments);
        Assert.True(arguments.Any(item =>
            item.StartsWith("--app=https://chatgpt.com/c/resource-conversation?", StringComparison.Ordinal)));
        Assert.True(arguments.Any(item =>
            item.Contains("projecthub-runtime-token=runtime-token", StringComparison.Ordinal)));
        Assert.False(arguments.Any(item =>
            item.StartsWith("https://chatgpt.com/", StringComparison.Ordinal)));
    }

    [Fact]
    public void VisibleLaunchArguments_CreateOnlyAppWindowWithoutHiddenFlags()
    {
        var arguments = ManagedWebRuntimeManager.BuildLaunchArguments(
            ManagedWebRole.Hq,
            hidden: false,
            Path.Combine(Path.GetTempPath(), "projecthub-extension"),
            Path.Combine(Path.GetTempPath(), "projecthub-profile"),
            null,
            "runtime-token");

        Assert.DoesNotContain("--window-position=-32000,-32000", arguments);
        Assert.DoesNotContain("--start-minimized", arguments);
        Assert.Single(arguments.Where(item => item.StartsWith("--app=", StringComparison.Ordinal)));
    }

    [Fact]
    public void ClearBrowserSessionState_RemovesTabRestoreStateButKeepsLoginData()
    {
        var root = Path.Combine(Path.GetTempPath(), "projecthub-managed-web-test-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "HQ");
        var defaultProfile = Path.Combine(profile, "Default");
        var sessions = Path.Combine(defaultProfile, "Sessions");
        Directory.CreateDirectory(sessions);
        File.WriteAllText(Path.Combine(sessions, "Session_1"), "session");
        File.WriteAllText(Path.Combine(defaultProfile, "Last Tabs"), "tabs");
        File.WriteAllText(Path.Combine(defaultProfile, "Cookies"), "keep");

        try
        {
            ManagedWebRuntimeManager.ClearBrowserSessionState(profile);

            Assert.False(Directory.Exists(sessions));
            Assert.False(File.Exists(Path.Combine(defaultProfile, "Last Tabs")));
            Assert.True(File.Exists(Path.Combine(defaultProfile, "Cookies")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
