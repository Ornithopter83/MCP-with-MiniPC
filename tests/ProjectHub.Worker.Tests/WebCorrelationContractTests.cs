using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class WebCorrelationContractTests
{
    [Fact]
    public void CreateKey_ProducesThirteenAlphaNumericCharacters()
    {
        var key = WebCorrelationContract.CreateKey();

        Assert.Equal(13, key.Length);
        Assert.All(key, character => Assert.True(char.IsLetterOrDigit(character)));
    }

    [Fact]
    public void WrapPrompt_PutsKeyOnRequestAndExplainsResponseCorrelation()
    {
        const string key = "1234ABCDabcde";
        var prompt = WebCorrelationContract.WrapPrompt("원래 요청", key);

        Assert.StartsWith("[KEY=1234ABCDabcde]", prompt, StringComparison.Ordinal);
        Assert.Contains("원래 요청", prompt);
        Assert.Contains("HQ는 JSON이 아니라 고정 필드와 @@SECTION 평문 프로토콜", prompt);
        Assert.Contains(WebCorrelationContract.ResponseOkMarker, prompt);
        Assert.Contains("마지막 줄", prompt);
    }

    [Fact]
    public void PrioritizedHqUserTextRemainsFirstPayloadAfterTransportKey()
    {
        const string key = "1234ABCDabcde";
        var rawMessage = "  사용자 원문\r\n기존 지시보다 우선  ";
        var entry = new HqUserIntervention(
            "id", DateTimeOffset.UtcNow, rawMessage, false);
        var routed = HqUserInterventionQueue.AppendToHqBody(
            "역할: HQ\n입력 본문:\n기존 지시\n역할 계약",
            new[] { entry });
        var web = WebCorrelationContract.WrapPrompt(routed, key);
        Assert.StartsWith(WebCorrelationContract.Marker(key) +
            Environment.NewLine + rawMessage, web, StringComparison.Ordinal);
        Assert.True(web.IndexOf(rawMessage, StringComparison.Ordinal) <
            web.IndexOf("역할: HQ", StringComparison.Ordinal));
        Assert.True(web.IndexOf(
            HqUserInterventionQueue.LatestUserPriorityGuidance,
            StringComparison.Ordinal) <
            web.IndexOf("역할 계약", StringComparison.Ordinal));
        Assert.Contains("원문\r\n기존 지시보다 우선  ", web);
    }

    [Fact]
    public void ExtractResponse_SearchesWholeAnswerAndKeepsOnlyContentBeforeCompletionLine()
    {
        const string key = "1234ABCDabcde";
        const string raw = """
            도구 출력
            파일 생성 완료

            [KEY=1234ABCDabcde]
            [ACTION=END]

            @@MESSAGE
            완료
            [RESPONSE=OK]
            ChatGPT UI footer
            """;

        Assert.True(WebCorrelationContract.TryExtractResponse(raw, key, out var response));
        Assert.StartsWith("[ACTION=END]", response, StringComparison.Ordinal);
        Assert.Contains("@@MESSAGE", response);
        Assert.Contains("완료", response);
        Assert.DoesNotContain("도구 출력", response);
        Assert.DoesNotContain("[RESPONSE=OK]", response);
        Assert.DoesNotContain("ChatGPT UI footer", response);
    }

    [Fact]
    public void ExtractResponse_UsesLastMatchingKeyAndStopsAtFirstCompletionLineAfterIt()
    {
        const string key = "1234ABCDabcde";
        const string raw = """
            [KEY=1234ABCDabcde]
            요청 본문
            기타 대화
            [KEY=1234ABCDabcde]
            [ACTION=END]

            @@MESSAGE
            실제 응답
            [RESPONSE=OK]
            이후 UI 텍스트
            """;

        Assert.True(WebCorrelationContract.TryExtractResponse(raw, key, out var response));
        Assert.Equal(
            "[ACTION=END]" + Environment.NewLine +
            Environment.NewLine +
            "@@MESSAGE" + Environment.NewLine +
            "실제 응답",
            response);
    }

    [Fact]
    public void ExtractResponse_RejectsMatchingKeyUntilCompletionLineAppears()
    {
        Assert.False(WebCorrelationContract.TryExtractResponse(
            "[KEY=1234ABCDabcde]\n[ACTION=END]\n\n@@MESSAGE\n아직 작성 중",
            "1234ABCDabcde",
            out _));
    }

    [Fact]
    public void ExtractResponse_RequiresCompletionMarkerOnItsOwnLine()
    {
        Assert.False(WebCorrelationContract.TryExtractResponse(
            "[KEY=1234ABCDabcde]\n[ACTION=END]\n\n@@MESSAGE\n본문 [RESPONSE=OK] 계속",
            "1234ABCDabcde",
            out _));
    }

    [Fact]
    public void ExtractResponse_RejectsDifferentOrMissingKey()
    {
        Assert.False(WebCorrelationContract.TryExtractResponse(
            "[KEY=ZZZZZZZZZZZZZ]\n[ACTION=END]\n[RESPONSE=OK]",
            "1234ABCDabcde",
            out _));
    }
}
