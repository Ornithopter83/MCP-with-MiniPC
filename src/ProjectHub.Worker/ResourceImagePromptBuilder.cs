using System.Text;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

/// <summary>
/// Web RESOURCE receives an image request, never the machine-readable
/// RESOURCE definition, JSON, target path, or WORK scheduling policy.
/// </summary>
internal static class ResourceImagePromptBuilder
{
    public static string Build(MilestoneResourceDefinition resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!resource.Type.Equals("IMAGE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("RESOURCE_TYPE_UNSUPPORTED");

        var instructions = (resource.Instructions ?? string.Empty).Trim();
        if (instructions.Length == 0)
            throw new InvalidOperationException("RESOURCE_IMAGE_INSTRUCTIONS_EMPTY");

        // HQ prose can inadvertently include Worker scheduling instructions.
        // Preserve the artistic content; drop only sentences whose purpose is
        // RESOURCE/WORK control. Control must live outside the image request.
        var segments = Regex.Split(instructions, @"(?<=[.!?])\s+");
        var imageOnly = segments
            .Where(segment => !IsWorkerControl(segment))
            .Select(segment => Regex.Replace(segment,
                @"[\w./\\-]+\.(?:png|jpe?g|webp|psd)\b",
                "이전 이미지", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        if (imageOnly.Length == 0)
            throw new InvalidOperationException("RESOURCE_IMAGE_INSTRUCTIONS_EMPTY");

        var result = new StringBuilder();
        result.AppendLine("아래 조건을 충족하는 이미지 한 장을 생성해 주세요.");
        if (resource.Width.HasValue && resource.Height.HasValue)
            result.AppendLine($"이미지 해상도: {resource.Width.Value}×{resource.Height.Value}픽셀.");
        if (resource.Columns.HasValue && resource.Rows.HasValue)
            result.AppendLine($"시트 구성: {resource.Columns.Value}열 × {resource.Rows.Value}행.");
        if (resource.RequireAlpha)
            result.AppendLine("투명한 배경의 PNG 이미지로 생성해 주세요.");
        result.AppendLine();
        result.Append(string.Join(Environment.NewLine, imageOnly).Trim());
        return result.ToString();
    }

    private static bool IsWorkerControl(string sentence) =>
        Regex.IsMatch(sentence,
            @"(?:RESOURCE|WORKITEM|WORK|HQ|QA|HIGH)", RegexOptions.IgnoreCase) &&
        Regex.IsMatch(sentence,
            @"(?:시작|진입|대기|실패|중단|완료|처리|재배정|재실행|보고|기다리)",
            RegexOptions.IgnoreCase);
}
