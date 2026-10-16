using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public static class WorkerHistoryCardFormatter
{
    private const int HardPreviewLimit = 512;

    public static string Preview(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(본문 없음)";
        var value = body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Trim();
        value = Regex.Replace(value, @"[^\S\n]+", " ", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, @"https?://\S+", "[주소]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = Regex.Replace(value, @"(?i)(api[_ -]?key|token|password|secret)\s*[:=]\s*\S+", "$1=[숨김]", RegexOptions.CultureInvariant);
        return value.Length <= HardPreviewLimit ? value : value[..(HardPreviewLimit - 1)] + "…";
    }

    /// <summary>
    /// Card-only preview: show the human-readable result instead of spending
    /// the limited visible lines on routing markers. Never alter FullMessage.
    /// </summary>
    public static string CardPreview(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(본문 없음)";
        var report = Regex.Match(body,
            @"(?is)<SUMMARY>\s*(.*?)(?:</(?:SUMMARY)?>|\z)",
            RegexOptions.CultureInvariant);
        if (report.Success && !string.IsNullOrWhiteSpace(report.Groups[1].Value))
            return Preview(report.Groups[1].Value);
        var withoutEnvelope = Regex.Replace(body,
            @"(?m)^\s*(?:\[GOTO=[^\]\r\n]+\]|\[ACTION=[^\]\r\n]+\]|\[RESPONSE=OK\]|@@REPORT)\s*(?:\r?\n|$)",
            string.Empty, RegexOptions.CultureInvariant);
        return Preview(withoutEnvelope);
    }

    public static string BuildHqReceiptBody(string action, string? response) =>
        "HQ ACTION: " + action + Environment.NewLine + (response ?? string.Empty);

    public static string ProgressPreview(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(본문 없음)";
        var lines = body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => Regex.Replace(line, @"\s+", " ", RegexOptions.CultureInvariant).Trim())
            .Where(line => line.Length > 0)
            .Select(line => Regex.Replace(line, @"https?://\S+", "[주소]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Select(line => Regex.Replace(line, @"(?i)(api[_ -]?key|token|password|secret)\s*[:=]\s*\S+", "$1=[숨김]", RegexOptions.CultureInvariant));
        var value = string.Join(Environment.NewLine, lines).Trim();
        if (value.Length == 0) return "(본문 없음)";
        return value.Length <= HardPreviewLimit ? value : value[..(HardPreviewLimit - 1)] + "…";
    }

    public static string TokenLine(CodexUsage? usage)
    {
        if (usage is null || !usage.UsageKnown) return "토큰 · 미제공";
        var total = usage.ProviderTotalTokens ?? usage.TotalTokens;
        var parts = new List<string>
        {
            "토큰",
            $"총 {total:N0}",
            $"입력 {usage.InputTokens:N0}",
            $"캐시 {usage.CachedInputTokens:N0}",
            $"출력 {usage.OutputTokens:N0}"
        };
        if (usage.ReasoningOutputTokens > 0) parts.Add($"추론 {usage.ReasoningOutputTokens:N0}");
        return string.Join(" · ", parts);
    }

    public static string FileLine(IReadOnlyList<CodexCliFile>? files)
    {
        if (files is null || files.Count == 0) return "파일 · 감지 없음";
        var distinct = files
            .GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var first = distinct[0].FileName;
        return distinct.Count == 1
            ? $"파일 · 1개 감지 · {first}"
            : $"파일 · {distinct.Count}개 감지 · {first} 외 {distinct.Count - 1}개";
    }
}
