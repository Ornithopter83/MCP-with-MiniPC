using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class GeneratedArtifactPolicyTests
{
    [Fact]
    public void DotNetProjectBinAndObjAreGeneratedButRootToolBinIsNot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubGeneratedArtifactTests",
            Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "src", "App");
        Directory.CreateDirectory(Path.Combine(project, "bin", "Debug"));
        Directory.CreateDirectory(Path.Combine(project, "obj"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        File.WriteAllText(Path.Combine(project, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(root, "bin", "tool.ps1"), "Write-Host tool");

        try
        {
            Assert.True(GeneratedArtifactPolicy.IsGeneratedArtifactPath(root, "src/App/bin/Debug/App.dll"));
            Assert.True(GeneratedArtifactPolicy.IsGeneratedArtifactPath(root, "src/App/obj/project.assets.json"));
            Assert.False(GeneratedArtifactPolicy.IsGeneratedArtifactPath(root, "bin/tool.ps1"));

            var generated = GeneratedArtifactPolicy.EnumerateGeneratedDirectories(root);
            Assert.Contains(
                Path.GetFullPath(Path.Combine(project, "bin")),
                generated,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Assert.Contains(
                Path.GetFullPath(Path.Combine(project, "obj")),
                generated,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Assert.DoesNotContain(
                Path.GetFullPath(Path.Combine(root, "bin")),
                generated,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void NestedRegenerableDirectoriesAreGenerated()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "ProjectHubGeneratedArtifactTests",
            Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "tools", "dist-temp");
        Directory.CreateDirectory(nested);

        try
        {
            Assert.True(GeneratedArtifactPolicy.IsGeneratedArtifactPath(root, "tools/dist-temp/output.tmp"));
            Assert.Contains(
                Path.GetFullPath(nested),
                GeneratedArtifactPolicy.EnumerateGeneratedDirectories(root),
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
