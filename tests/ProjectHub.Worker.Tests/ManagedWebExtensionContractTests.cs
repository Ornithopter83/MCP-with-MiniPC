using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ManagedWebExtensionContractTests
{
    [Fact]
    public void EmbeddedManifest_IsManagedBridgeWithoutTabsPermission()
    {
        var assembly = typeof(BridgeServer).Assembly;
        using var stream = assembly.GetManifestResourceStream("ProjectHub.Worker.Extension.manifest.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);

        Assert.Equal("0.3.0", document.RootElement.GetProperty("version").GetString());
        var permissions = document.RootElement
            .GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => item is not null)
            .ToArray();

        Assert.Contains("storage", permissions);
        Assert.DoesNotContain("tabs", permissions);
    }

    [Fact]
    public void EmbeddedBackground_HasNoBrowserTabManagement()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.background.js");

        Assert.False(source.Contains("chrome.tabs.", StringComparison.Ordinal));
        Assert.False(source.Contains("ensure-single-chatgpt-tab", StringComparison.Ordinal));
        Assert.True(source.Contains("fetch-resource-file", StringComparison.Ordinal));
    }

    [Fact]
    public void EmbeddedContent_RequiresManagedRoleAndRuntimeToken()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");

        Assert.True(source.Contains("projecthub-managed-role", StringComparison.Ordinal));
        Assert.True(source.Contains("projecthub-runtime-token", StringComparison.Ordinal));
        Assert.True(source.Contains("X-ProjectHub-Managed-Token", StringComparison.Ordinal));
        Assert.True(source.Contains("if(!preflightManagedRole||!preflightRuntimeToken)return;", StringComparison.Ordinal));
        Assert.False(source.Contains("ensureManagedSingleChatTab", StringComparison.Ordinal));
    }

    [Fact]
    public void Bridge_CreatesManagedRuntimeToken()
    {
        using var bridge = new BridgeServer();

        Assert.False(string.IsNullOrWhiteSpace(bridge.ManagedRuntimeToken));
        Assert.True(bridge.ManagedRuntimeToken.Length >= 32);
    }

    private static string ReadEmbeddedText(string name)
    {
        var assembly = typeof(BridgeServer).Assembly;
        using var stream = assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
