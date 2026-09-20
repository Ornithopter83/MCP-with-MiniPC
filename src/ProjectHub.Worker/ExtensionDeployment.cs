using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record ExtensionDeploymentResult(string Directory, string Version, bool Updated, string? Error);

public static class ExtensionDeployment
{
    private const string ManifestResource = "ProjectHub.Worker.Extension.manifest.json";
    private const string ContentResource = "ProjectHub.Worker.Extension.content.js";
    private const string BackgroundResource = "ProjectHub.Worker.Extension.background.js";

    public static ExtensionDeploymentResult EnsureDeployed()
    {
        WorkerPaths.EnsureCreated();
        try
        {
            var manifest = ReadResource(ManifestResource);
            var content = ReadResource(ContentResource);
            var background = ReadResource(BackgroundResource);
            var version = ReadVersion(manifest);
            var existingManifestPath = Path.Combine(WorkerPaths.Extension, "manifest.json");
            var existingContentPath = Path.Combine(WorkerPaths.Extension, "content.js");
            var existingBackgroundPath = Path.Combine(WorkerPaths.Extension, "background.js");
            var currentVersion = File.Exists(existingManifestPath) ? ReadVersion(File.ReadAllText(existingManifestPath)) : null;
            var updated = !string.Equals(version, currentVersion, StringComparison.Ordinal) ||
                         !File.Exists(existingContentPath) ||
                         !string.Equals(File.ReadAllText(existingContentPath), content, StringComparison.Ordinal) ||
                         !File.Exists(existingBackgroundPath) ||
                         !string.Equals(File.ReadAllText(existingBackgroundPath), background, StringComparison.Ordinal);

            if (updated)
            {
                WriteAtomically(existingManifestPath, manifest);
                WriteAtomically(existingContentPath, content);
                WriteAtomically(existingBackgroundPath, background);
            }

            return new ExtensionDeploymentResult(WorkerPaths.Extension, version, updated, null);
        }
        catch (Exception exception)
        {
            return new ExtensionDeploymentResult(WorkerPaths.Extension, "unknown", false, exception.Message);
        }
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("내장 Extension 리소스를 찾을 수 없습니다: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ReadVersion(string manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        return document.RootElement.TryGetProperty("version", out var version)
            ? version.GetString() ?? "unknown"
            : "unknown";
    }

    private static void WriteAtomically(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
}

public static class RuntimeDiagnostics
{
    public static string? FindChrome()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}