using System.Text;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class UserAttachmentTransportTests
{
    [Fact]
    public void CacheFile_CopiesInputAndComputesSha256()
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "notes.md");
        File.WriteAllText(source, "# attachment\nhello", new UTF8Encoding(false));
        UserAttachmentInput? attachment = null;

        try
        {
            attachment = UserAttachmentTransport.CacheFile(
                source,
                "DROP");

            Assert.Equal("notes.md", attachment.FileName);
            Assert.Equal("text/markdown", attachment.MimeType);
            Assert.Equal("DROP", attachment.SourceKind);
            Assert.True(File.Exists(attachment.StoredPath));
            Assert.Equal(
                UserAttachmentTransport.ComputeSha256(attachment.StoredPath),
                attachment.Sha256);
            Assert.Equal(
                Path.GetFileNameWithoutExtension(attachment.StoredPath),
                attachment.Id);
        }
        finally
        {
            if (attachment is not null && File.Exists(attachment.StoredPath))
                File.Delete(attachment.StoredPath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CacheFile_BlocksExecutableBinaryButAllowsSourceScripts()
    {
        var root = CreateTempDirectory();
        var executable = Path.Combine(root, "tool.exe");
        var script = Path.Combine(root, "inspect.js");
        File.WriteAllText(executable, "not really executable");
        File.WriteAllText(script, "console.log('ok');");
        UserAttachmentInput? scriptAttachment = null;

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                UserAttachmentTransport.CacheFile(executable, "DROP"));

            scriptAttachment = UserAttachmentTransport.CacheFile(
                script,
                "DROP");
            Assert.Equal("inspect.js", scriptAttachment.FileName);
        }
        finally
        {
            if (scriptAttachment is not null &&
                File.Exists(scriptAttachment.StoredPath))
                File.Delete(scriptAttachment.StoredPath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StageForWorkerRuntimePreservesHashWithoutMutatingProjectGitState()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        var gitInfo = Path.Combine(workspace, ".git", "info");
        Directory.CreateDirectory(gitInfo);
        var excludePath = Path.Combine(gitInfo, "exclude");
        File.WriteAllText(excludePath, "existing-rule" + Environment.NewLine);
        var source = Path.Combine(root, "screen.png");
        File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4, 5, 6 });
        UserAttachmentInput? attachment = null;
        AiInputAttachment? stagedItem = null;

        try
        {
            attachment = UserAttachmentTransport.CacheFile(
                source,
                "CLIPBOARD",
                "clipboard-test.png");

            var staged = UserAttachmentTransport.StageForWorkerRuntime(
                new[] { attachment },
                "batch-" + Guid.NewGuid().ToString("N"));

            stagedItem = Assert.Single(staged);
            Assert.True(File.Exists(stagedItem.Path));
            Assert.Equal(attachment.Sha256, stagedItem.Sha256);
            Assert.StartsWith(
                Path.GetFullPath(WorkerPaths.Attachments) + Path.DirectorySeparatorChar,
                Path.GetFullPath(stagedItem.Path),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Assert.False(
                Path.GetFullPath(stagedItem.Path).StartsWith(
                    Path.GetFullPath(workspace) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            Assert.Equal(
                "existing-rule" + Environment.NewLine,
                File.ReadAllText(excludePath));

            var prompt = UserAttachmentTransport.AppendPrompt(
                "Inspect this screenshot.",
                staged);
            Assert.Contains("[USER_ATTACHMENTS]", prompt);
            Assert.Contains(stagedItem.Path, prompt);
            Assert.Contains("viewing capability", prompt);
            Assert.Contains(attachment.Sha256, prompt);
        }
        finally
        {
            if (stagedItem is not null)
            {
                var stagedDirectory = Path.GetDirectoryName(stagedItem.Path);
                if (!string.IsNullOrWhiteSpace(stagedDirectory) &&
                    Directory.Exists(stagedDirectory))
                {
                    Directory.Delete(stagedDirectory, recursive: true);
                }
            }
            if (attachment is not null && File.Exists(attachment.StoredPath))
                File.Delete(attachment.StoredPath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateBridgeAttachment_UsesCachedIdAndHash()
    {
        var root = CreateTempDirectory();
        var source = Path.Combine(root, "document.txt");
        File.WriteAllText(source, "bridge attachment");
        UserAttachmentInput? attachment = null;

        try
        {
            attachment = UserAttachmentTransport.CacheFile(
                source,
                "DROP");

            var bridge = UserAttachmentTransport.CreateBridgeAttachment(
                attachment);

            Assert.Equal(attachment.Id, bridge.Id);
            Assert.Equal(attachment.FileName, bridge.FileName);
            Assert.Equal(attachment.Sha256, bridge.Sha256);
            Assert.Equal(attachment.Size, bridge.Size);
            Assert.True(
                bridge.DownloadUrl?.EndsWith(
                    "/bridge/attachment/" + attachment.Id,
                    StringComparison.Ordinal) == true);
        }
        finally
        {
            if (attachment is not null && File.Exists(attachment.StoredPath))
                File.Delete(attachment.StoredPath);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "projecthub-attachment-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
