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
        Assert.Contains("KEY → ACTION → GOTO", prompt);
    }

    [Fact]
    public void ExtractResponse_SearchesWholeAnswerAndKeepsOnlyContentAfterMatchingKey()
    {
        const string key = "1234ABCDabcde";
        const string raw = """
            도구 출력
            파일 생성 완료

            [KEY=1234ABCDabcde]
            설명이 먼저 있어도 된다.
            [ACTION=END]
            완료
            """;

        Assert.True(WebCorrelationContract.TryExtractResponse(raw, key, out var response));
        Assert.StartsWith("설명이 먼저 있어도 된다.", response, StringComparison.Ordinal);
        Assert.Contains("[ACTION=END]", response);
        Assert.DoesNotContain("도구 출력", response);
    }

    [Fact]
    public void ExtractResponse_UsesLastMatchingKeyWhenBroadScopeContainsRequestAndResponse()
    {
        const string key = "1234ABCDabcde";
        const string raw = """
            [KEY=1234ABCDabcde]
            요청 본문
            기타 대화
            [KEY=1234ABCDabcde]
            [ACTION=END]
            실제 응답
            """;

        Assert.True(WebCorrelationContract.TryExtractResponse(raw, key, out var response));
        Assert.Equal("[ACTION=END]" + Environment.NewLine + "실제 응답", response);
    }

    [Fact]
    public void ExtractResponse_RejectsDifferentOrMissingKey()
    {
        Assert.False(WebCorrelationContract.TryExtractResponse(
            "[KEY=ZZZZZZZZZZZZZ]\n[ACTION=END]",
            "1234ABCDabcde",
            out _));
    }
}
