using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WorkerHistoryCardFormatterTests
{
    [Fact]
    public void PreviewPreservesControlMarkerNewlines()
    {
        const string response = "[GOTO=WORKER]\r\n[ACTION=WORK]\r\n\r\n@@MILESTONE\r\n<ID>M32</>";
        Assert.Equal("[GOTO=WORKER]\n[ACTION=WORK]\n\n@@MILESTONE\n<ID>M32</>",
            WorkerHistoryCardFormatter.Preview(response));
    }

    [Fact]
    public void ReceiptPreservesRawHqResponse()
    {
        const string response = "[GOTO=WORKER]\r\n[ACTION=WORK]\r\n@@MILESTONE\r\n<ID>M32</>";
        Assert.Equal("HQ ACTION: WORK" + Environment.NewLine + response,
            WorkerHistoryCardFormatter.BuildHqReceiptBody("WORK", response));
    }

    [Fact]
    public void PreviewStillRedactsAndTruncates()
    {
        var preview = WorkerHistoryCardFormatter.Preview(
            "token=private-value\n" + new string('x', 600));
        Assert.DoesNotContain("private-value", preview);
        Assert.Contains("\n", preview);
        Assert.True(preview.Length <= 512);
    }
}
