using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WebResultFileStorageTests
{
    [Fact]
    public void SaveWebResults_WritesNonImageFileUnderTaskDirectory()
    {
        WorkerPaths.EnsureCreated();
        var taskId = Guid.NewGuid().ToString("N");
        var bytes = Encoding.UTF8.GetBytes("generated markdown file");
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        var request = new ResultRequest(
            Success: true,
            ResultFiles: new List<ResourceResultFile>
            {
                new(
                    Convert.ToBase64String(bytes),
                    "text/markdown",
                    "report.md",
                    sha256)
            });

        var method = typeof(BridgeServer).GetMethod(
            "SaveWebResults",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var directory = Path.Combine(WorkerPaths.WebResults, taskId);
        try
        {
            var result = Assert.IsType<List<string>>(
                method!.Invoke(null, new object[] { taskId, request }));

            var path = Assert.Single(result);
            Assert.True(path.StartsWith(
                Path.GetFullPath(directory),
                StringComparison.OrdinalIgnoreCase));
            Assert.Equal("report.md", Path.GetFileName(path));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(
                sha256,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveWebResults_RejectsShaMismatch()
    {
        WorkerPaths.EnsureCreated();
        var taskId = Guid.NewGuid().ToString("N");
        var bytes = Encoding.UTF8.GetBytes("generated file");
        var request = new ResultRequest(
            Success: true,
            ResultFiles: new List<ResourceResultFile>
            {
                new(
                    Convert.ToBase64String(bytes),
                    "application/pdf",
                    "answer.pdf",
                    new string('0', 64))
            });

        var method = typeof(BridgeServer).GetMethod(
            "SaveWebResults",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var exception = Assert.Throws<TargetInvocationException>(
            () => method!.Invoke(null, new object[] { taskId, request }));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains(
            "WEB_RESULT_HASH_MISMATCH",
            exception.InnerException!.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WebResultsPath_IsCreatedByWorkerPaths()
    {
        WorkerPaths.EnsureCreated();

        Assert.True(Directory.Exists(WorkerPaths.WebResults));
    }
}
