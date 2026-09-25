using System.Reflection;
using System.IO;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public static class RoleContractLoader
{
    public static string LoadHqFooter() => Load("HQ-ROUTING-CONTRACT.md");

    public static string LoadWorkFooter(bool judgeAvailable)
    {
        var value = Load("WORK-ROUTING-CONTRACT.md");
        value = Regex.Replace(value, judgeAvailable ? @"(?s)\{\{JUDGE_OFF\}\}.*?\{\{/JUDGE_OFF\}\}" : @"(?s)\{\{JUDGE_ON\}\}.*?\{\{/JUDGE_ON\}\}", string.Empty);
        return Regex.Replace(value, @"\{\{/?JUDGE_(?:ON|OFF)\}\}", string.Empty).Trim();
    }

    public static string LoadJudgeFooter() => Load("JUDGE-ROUTING-CONTRACT.md");

    public static string BuildHqPrompt(string inboundType, string body)
    {
        var header = $"역할: HQ\n입력 유형: {inboundType}\n허용 목적지: WORK\n\n입력 본문:\n";
        return header + body + "\n\n" + LoadHqFooter();
    }

    public static string BuildWorkPrompt(string inboundType, string body, bool judgeAvailable)
    {
        var header = $"역할: WORK\n입력 유형: {inboundType}\n판정 사용 가능: {(judgeAvailable ? "예" : "아니오")}\n리소스 사용 가능: 예\n\n입력 본문:\n";
        return header + body + "\n\n" + LoadWorkFooter(judgeAvailable);
    }


    private static string Load(string fileName)
    {
        var name = $"ProjectHub.Worker.Contracts.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"역할 계약 리소스를 찾을 수 없습니다: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
