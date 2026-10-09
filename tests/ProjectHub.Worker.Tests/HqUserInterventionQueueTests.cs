using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class HqUserInterventionQueueTests
{
    [Fact]
    public void NewUserMessages_StayOrderedAndSurviveRestartUntilAcknowledged()
    {
        var root = CreateRoot();
        try
        {
            var queue = new HqUserInterventionQueue(root, "job-a");
            var first = queue.Add("작업 목표 변경: 저장 안정성을 먼저 진행해줘.", false);
            var second = queue.Add("두 번째 메시지\n줄바꿈은 그대로 전달해.", false);
            var manual = queue.Add("웹에서 이미 전달한 메시지", true);

            var restored = new HqUserInterventionQueue(root, "job-a");
            var pending = restored.Pending();
            Assert.Equal(new[] { first.Id, second.Id },
                pending.Select(x => x.Id));
            Assert.Equal("웹에서 이미 전달한 메시지",
                Assert.Single(restored.ReadAll().Where(x => x.RecordOnly)).Message);

            var hqBody = HqUserInterventionQueue.AppendToHqBody(
                "MILESTONE_REPORT\nM2", pending);
            Assert.Contains("MILESTONE_REPORT\nM2", hqBody);
            Assert.Contains("저장 안정성을 먼저", hqBody);
            Assert.Contains("두 번째 메시지\n줄바꿈", hqBody);
            Assert.DoesNotContain(manual.Message, hqBody);
            Assert.True(hqBody.IndexOf(first.Id, StringComparison.Ordinal) <
                        hqBody.IndexOf(second.Id, StringComparison.Ordinal));

            restored.Acknowledge(pending);
            Assert.Empty(new HqUserInterventionQueue(root, "job-a").Pending());
            var stored = restored.ReadAll();
            Assert.Equal(3, stored.Count);
            Assert.All(stored.Where(x => !x.RecordOnly),
                item => Assert.NotNull(item.DeliveredAt));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MidFlightInput_IsNotSwallowedByEarlierHqAcknowledgment()
    {
        var root = CreateRoot();
        try
        {
            var queue = new HqUserInterventionQueue(root, "job-b");
            var first = queue.Add("이전 HQ 호출 직전 예약", false);
            var attachedToExistingCall = queue.Pending();
            var newer = queue.Add("HQ가 응답하는 동안 예약", false);
            queue.Acknowledge(attachedToExistingCall);
            Assert.Equal(newer.Id, Assert.Single(queue.Pending()).Id);

            var nextHqCall = new HqUserInterventionQueue(root, "job-b");
            Assert.Equal(newer.Id, Assert.Single(nextHqCall.Pending()).Id);
            Assert.DoesNotContain(first.Message,
                HqUserInterventionQueue.AppendToHqBody("REPORT", nextHqCall.Pending()));
            nextHqCall.Acknowledge(nextHqCall.Pending());
            Assert.Empty(queue.Pending());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidResponse_DoesNotConsumeMessages_AndRecoveryUsesSamePendingBatch()
    {
        var root = CreateRoot();
        try
        {
            var queue = new HqUserInterventionQueue(root, "job-c");
            queue.Add("검증 전에는 삭제하지 마", false);
            var firstAttempt = queue.Pending();
            var ignored = HqUserInterventionQueue.AppendToHqBody("REPORT", firstAttempt);
            Assert.Contains("삭제하지 마", ignored);
            // Failed or unparseable responses never invoke Acknowledge.
            Assert.Single(queue.Pending());
            var recovery = HqUserInterventionQueue.AppendToHqBody(
                "HQ_RESPONSE_RECOVERY", queue.Pending());
            Assert.Contains("삭제하지 마", recovery);
            Assert.Single(queue.Pending());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Acknowledge_RejectsUnknownOrAlreadyAcknowledgedBatch()
    {
        var root = CreateRoot();
        try
        {
            var queue = new HqUserInterventionQueue(root, "job-d");
            queue.Add("확인", false);
            var pending = queue.Pending();
            queue.Acknowledge(pending);
            Assert.Throws<IOException>(() => queue.Acknowledge(pending));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void EmptyBatch_PreservesOriginalHqPrompt()
    {
        Assert.Equal("ORIGINAL", HqUserInterventionQueue.AppendToHqBody(
            "ORIGINAL", Array.Empty<HqUserIntervention>()));
    }

    private static string CreateRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "ProjectHubInterventionTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
