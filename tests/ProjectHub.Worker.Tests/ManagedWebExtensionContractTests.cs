using System.Reflection;
using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ManagedWebExtensionContractTests
{
    [Fact]
    public void EmbeddedManifest_RequiresTabsAndMatchesManagedWebVersion()
    {
        var assembly = typeof(BridgeServer).Assembly;
        using var stream = assembly.GetManifestResourceStream("ProjectHub.Worker.Extension.manifest.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);

        Assert.Equal("0.2.2", document.RootElement.GetProperty("version").GetString());
        var permissions = document.RootElement
            .GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => item is not null)
            .ToArray();
        Assert.Contains("storage", permissions);
        Assert.Contains("tabs", permissions);
    }

    [Fact]
    public void EmbeddedBackground_ContainsManagedSingleTabCommand()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.background.js");

        Assert.True(source.Contains("ensure-single-chatgpt-tab", StringComparison.Ordinal));
        Assert.True(source.Contains("chrome.tabs.query", StringComparison.Ordinal));
        Assert.True(source.Contains("chrome.tabs.remove", StringComparison.Ordinal));
        Assert.True(source.Contains("chrome.tabs.update", StringComparison.Ordinal));
        Assert.True(source.Contains("pathname.match(/\\/c\\/", StringComparison.Ordinal));
        Assert.True(source.Contains("isCanonicalConversationTab", StringComparison.Ordinal));
    }

    [Fact]
    public void EmbeddedContent_UsesManagedLaunchMarkerToRequestCleanup()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");

        Assert.True(source.Contains("projecthub-managed-role", StringComparison.Ordinal));
        Assert.True(source.Contains("ensureManagedSingleChatTab", StringComparison.Ordinal));
        Assert.True(source.Contains("ensure-single-chatgpt-tab", StringComparison.Ordinal));
        Assert.True(source.Contains("managedTabCleanupGenerations", StringComparison.Ordinal));
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
