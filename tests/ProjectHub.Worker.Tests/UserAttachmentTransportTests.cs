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
    public void StageForWorkspace_PreservesHashAndBuildsAiPrompt()
    {
        var root = CreateTempDirectory();
        var workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        var source = Path.Combine(root, "screen.png");
        File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4, 5, 6 });
        UserAttachmentInput? attachment = null;

        try
        {
            attachment = UserAttachmentTransport.CacheFile(
                source,
                "CLIPBOARD",
                "clipboard-test.png");

            var staged = UserAttachmentTransport.StageForWorkspace(
                new[] { attachment },
                workspace,
                "batch-1");

            var item = Assert.Single(staged);
            Assert.True(File.Exists(item.Path));
            Assert.Equal(attachment.Sha256, item.Sha256);
            Assert.True(item.RelativePath.Contains(
                Path.Combine(".projecthub", "attachments", "batch1"),
                StringComparison.OrdinalIgnoreCase));

            var prompt = UserAttachmentTransport.AppendPrompt(
                "Inspect this screenshot.",
                staged);
            Assert.Contains("[USER_ATTACHMENTS]", prompt);
            Assert.Contains(item.Path, prompt);
            Assert.Contains("viewing capability", prompt);
            Assert.Contains(attachment.Sha256, prompt);
        }
        finally
        {
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
            Assert.EndsWith(
                "/bridge/attachment/" + attachment.Id,
                bridge.DownloadUrl,
                StringComparison.Ordinal);
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
