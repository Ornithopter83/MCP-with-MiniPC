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
            Assert.Equal(first.Message + "\n\n" + second.Message +
                "\n\nMILESTONE_REPORT\nM2", hqBody);
            Assert.DoesNotContain(manual.Message, hqBody);
            Assert.DoesNotContain(first.Id, hqBody);
            Assert.DoesNotContain(second.Id, hqBody);
            Assert.DoesNotContain("USER_MESSAGE_BEGIN", hqBody);
            Assert.DoesNotContain("다음 역할 AI", hqBody);

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
    public void FailedAiTurn_DoesNotConsumeMessages_AndRecoveryUsesSamePendingBatch()
    {
        var root = CreateRoot();
        try
        {
            var queue = new HqUserInterventionQueue(root, "job-c");
            queue.Add("검증 전에는 삭제하지 마", false);
            var firstAttempt = queue.Pending();
            var ignored = HqUserInterventionQueue.AppendToHqBody("REPORT", firstAttempt);
            Assert.Contains("삭제하지 마", ignored);
            // Failed calls do not acknowledge, so the next AI can receive them.
            Assert.False(HqUserInterventionQueue.CanAcknowledgeAiDelivery(
                1, "error"));
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
    public void ParallelRoleClaims_DoNotDuplicateAndFailuresRelease()
    {
        var root = CreateRoot();
        try
        {
            var queue = new HqUserInterventionQueue(root, "parallel");
            var first = queue.Add("안전 중단 요청", false);
            var second = queue.Add("그래픽을 밝게 수정", false);
            var designBatch = queue.ClaimPending();
            Assert.Equal(new[] { first.Id, second.Id }, designBatch.Select(x => x.Id));
            Assert.Empty(queue.ClaimPending());
            queue.Release(designBatch);
            var highBatch = queue.ClaimPending();
            Assert.Equal(designBatch.Select(x => x.Id), highBatch.Select(x => x.Id));
            queue.Acknowledge(highBatch);
            Assert.Empty(queue.Pending());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Restart_ReleasesUnacknowledgedReservations()
    {
        var root = CreateRoot();
        try
        {
            var original = new HqUserInterventionQueue(root, "restart");
            original.Add("원문 메시지", false);
            Assert.Single(original.ClaimPending());
            Assert.Single(new HqUserInterventionQueue(root, "restart").ClaimPending());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void UserMessage_PreservesWhitespaceLineEndingsAndControlLikeText()
    {
        var root = CreateRoot();
        try
        {
            var queue = new HqUserInterventionQueue(root, "verbatim");
            const string message = "  중단하지 마세요.\r\n@@MESSAGE\n[GOTO=HIGH]\t \r\n";
            var entry = queue.Add(message, false);
            var restored = new HqUserInterventionQueue(root, "verbatim");
            Assert.Equal(message, Assert.Single(restored.Pending()).Message);
            var body = HqUserInterventionQueue.AppendToHqBody(
                "@@MESSAGE\n기존 역할 지시", restored.Pending());
            Assert.Equal("@@MESSAGE\n" + message +
                "\n\n기존 역할 지시", body);
            Assert.Contains(message, body);
            Assert.DoesNotContain(entry.Id, body);
            restored.Acknowledge(restored.Pending());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void UserMessage_WithoutBasePromptIsExactlyTheUsersText()
    {
        var root = CreateRoot();
        try
        {
            var queue = new HqUserInterventionQueue(root, "raw-only");
            const string message = "\n  UI 색상은 파란색으로  \n\n";
            queue.Add(message, false);
            Assert.Equal(message,
                HqUserInterventionQueue.AppendToHqBody("", queue.Pending()));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void HqNextAiInput_IsInsertedBeforeOriginalBodyAndContract()
    {
        var root = CreateRoot();
        try
        {
            var queue = new HqUserInterventionQueue(root, "hq-input");
            const string message = "  UI 조정: 이 값을 우선 적용\r\n";
            var entry = queue.Add(message, false);
            const string hqPrompt =
                "역할: HQ\r\n입력 유형: MILESTONE\r\n입력 본문:\r\n" +
                "기존 마일스톤 보고\r\n\r\nHQ 계약";
            var composed = HqUserInterventionQueue.AppendToHqBody(
                hqPrompt, queue.ClaimPending());
            Assert.Equal("역할: HQ\r\n입력 유형: MILESTONE\r\n입력 본문:\r\n" +
                message + "\n\n기존 마일스톤 보고\r\n\r\nHQ 계약", composed);
            Assert.DoesNotContain(entry.Id, composed);
            Assert.DoesNotContain("USER_MESSAGE_BEGIN", composed);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0, "완료", true)]
    [InlineData(0, "형식 없는 응답", true)]
    [InlineData(0, "", false)]
    [InlineData(0, "  ", false)]
    [InlineData(1, "형식 있는 응답", false)]
    public void AiDeliveryAcknowledgment_DoesNotDependOnReportGrammar(
        int exitCode, string response, bool expected)
    {
        Assert.Equal(expected,
            HqUserInterventionQueue.CanAcknowledgeAiDelivery(exitCode, response));
    }

    [Fact]
    public void SuccessfulRecipientReply_ConsumesOnlyClaimedMessages()
    {
        var root = CreateRoot();
        try
        {
            var queue = new HqUserInterventionQueue(root, "recipient");
            var first = queue.Add("먼저 보낸 사용자 지시", false);
            var sent = queue.ClaimPending();
            var second = queue.Add("호출 도중 보낸 다음 지시", false);
            Assert.True(HqUserInterventionQueue.CanAcknowledgeAiDelivery(
                0, "형식이 잘못된 보고라도 AI 응답이 도착함"));
            queue.Acknowledge(sent);
            Assert.Equal(first.Id, Assert.Single(queue.ReadAll()
                .Where(item => item.DeliveredAt is not null)).Id);
            Assert.Equal(second.Id, Assert.Single(queue.Pending()).Id);
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
