using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public static class WorkerHistoryCardFormatter
{
    private const int HardPreviewLimit = 512;

    public static string Preview(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(본문 없음)";
        var value = Regex.Replace(body, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        value = Regex.Replace(value, @"https?://\S+", "[주소]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = Regex.Replace(value, @"(?i)(api[_ -]?key|token|password|secret)\s*[:=]\s*\S+", "$1=[숨김]", RegexOptions.CultureInvariant);
        return value.Length <= HardPreviewLimit ? value : value[..(HardPreviewLimit - 1)] + "…";
    }

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

    public static string TokenLine(JevCallTelemetry? telemetry)
    {
        if (telemetry is null || !telemetry.UsageKnown) return "토큰 · 미제공";
        var total = telemetry.ProviderTotalTokens
            ?? (telemetry.InputTokens.GetValueOrDefault() + telemetry.OutputTokens.GetValueOrDefault() + telemetry.ReasoningTokens.GetValueOrDefault());
        var parts = new List<string> { "토큰", $"총 {total:N0}" };
        if (telemetry.InputTokens.HasValue) parts.Add($"입력 {telemetry.InputTokens.Value:N0}");
        if (telemetry.CachedInputTokens.HasValue) parts.Add($"캐시 {telemetry.CachedInputTokens.Value:N0}");
        if (telemetry.OutputTokens.HasValue) parts.Add($"출력 {telemetry.OutputTokens.Value:N0}");
        if (telemetry.ReasoningTokens.HasValue && telemetry.ReasoningTokens.Value > 0) parts.Add($"추론 {telemetry.ReasoningTokens.Value:N0}");
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
