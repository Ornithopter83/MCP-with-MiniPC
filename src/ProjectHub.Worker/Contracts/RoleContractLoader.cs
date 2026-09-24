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
        var header = $"Role: HQ\nInbound type: {inboundType}\nAllowed destination: WORK\n\nInbound body:\n";
        return header + body + "\n\n" + LoadHqFooter();
    }

    public static string BuildWorkPrompt(string inboundType, string body, bool judgeAvailable)
    {
        var header = $"Role: WORK\nInbound type: {inboundType}\nJudge available: {(judgeAvailable ? "yes" : "no")}\nResource available: yes\n\nInbound body:\n";
        return header + body + "\n\n" + LoadWorkFooter(judgeAvailable);
    }


    private static string Load(string fileName)
    {
        var name = $"ProjectHub.Worker.Contracts.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"Role contract resource not found: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
