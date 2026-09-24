using System.Reflection;
using System.IO;
using System.Text.Json;
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
        var header = $"[ROLE : HQ]\n\n[INBOUND TYPE : {inboundType}]\n\n[AVAILABLE GOTO]\n[GOTO : WORK]\n\n";
        return header + "[INBOUND BODY : JSON]\n" + JsonSerializer.Serialize(new { body }) + "\n\n" + LoadHqFooter();
    }

    public static string BuildWorkPrompt(string inboundType, string body, bool judgeAvailable)
    {
        var header = $"[ROLE : WORK]\n\n[INBOUND TYPE : {inboundType}]\n\n[JUDGE AVAILABLE : {judgeAvailable.ToString().ToLowerInvariant()}]\n[RESOURCE AVAILABLE : true]\n\n";
        return header + "[OPAQUE INBOUND BODY]\n" + body + "\n\n" + LoadWorkFooter(judgeAvailable);
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
