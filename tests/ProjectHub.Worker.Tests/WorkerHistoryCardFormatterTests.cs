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
    public void CardPreviewPrefersHumanSummaryOverProtocolEnvelope()
    {
        const string report = "[GOTO=WORKER]\n@@REPORT\n<STATUS>completed</>\n" +
            "<SUMMARY>O/X 그래픽 정리 완료\n메시 생성 변경</>\n" +
            "<CHANGED_PATH>Assets/Quiz/Art/O-mark.png.meta</>\n[RESPONSE=OK]";
        Assert.Equal("O/X 그래픽 정리 완료\n메시 생성 변경",
            WorkerHistoryCardFormatter.CardPreview(report));
        Assert.Contains("[GOTO=WORKER]", WorkerHistoryCardFormatter.Preview(report));
    }

    [Fact]
    public void CardPreviewKeepsUsefulTextWithoutSummaryField()
    {
        const string report = "[GOTO=WORKER]\n@@REPORT\n작업 완료\n[RESPONSE=OK]";
        Assert.Equal("작업 완료", WorkerHistoryCardFormatter.CardPreview(report));
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
