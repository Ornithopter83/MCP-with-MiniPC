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
