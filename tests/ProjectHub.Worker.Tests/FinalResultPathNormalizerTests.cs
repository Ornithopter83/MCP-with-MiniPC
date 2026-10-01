using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class FinalResultPathNormalizerTests
{
    [Fact]
    public void NormalizesExistingLandedArtifactToTargetWorkspace()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-final-path-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var worktree = Path.Combine(root, ".projecthub", "runtime", "worktrees", "job", "11");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(worktree);

        var targetFile = Path.Combine(workspace, "ImagePackStudio.exe");
        File.WriteAllText(targetFile, "artifact");

        try
        {
            var sourceFile = Path.Combine(worktree, "ImagePackStudio.exe");
            var text =
                "최종 실행파일:" +
                Environment.NewLine +
                sourceFile;

            var normalized = FinalResultPathNormalizer.NormalizeLandedPaths(
                text,
                workspace,
                new[] { worktree });

            Assert.Contains(targetFile, normalized);
            Assert.DoesNotContain(sourceFile, normalized);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void KeepsRuntimePathWhenEquivalentTargetArtifactDoesNotExist()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-final-path-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var worktree = Path.Combine(root, ".projecthub", "runtime", "worktrees", "job", "11");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(worktree);

        try
        {
            var sourceFile = Path.Combine(worktree, "only-in-runtime.txt");
            var normalized = FinalResultPathNormalizer.NormalizeLandedPaths(
                sourceFile,
                workspace,
                new[] { worktree });

            Assert.Equal(sourceFile, normalized);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void NormalizesForwardSlashMarkdownPath()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-final-path-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var worktree = Path.Combine(root, ".projecthub", "runtime", "worktrees", "job", "11");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(worktree);

        var targetFile = Path.Combine(workspace, "ImagePackStudio.exe");
        File.WriteAllText(targetFile, "artifact");

        try
        {
            var sourceFile = Path.Combine(worktree, "ImagePackStudio.exe")
                .Replace('\\', '/');
            var text = $"[ImagePackStudio.exe]({sourceFile})";

            var normalized = FinalResultPathNormalizer.NormalizeLandedPaths(
                text,
                workspace,
                new[] { worktree });

            Assert.Contains(targetFile, normalized);
            Assert.DoesNotContain(sourceFile, normalized);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
