using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public static class RoleContractLoader
{
    public static string LoadHqFooter(bool highPermitAvailable = true)
    {
        var value = Load("HQ-ROUTING-CONTRACT.md");
        if (!highPermitAvailable)
            value = Regex.Replace(value, @"(?s)\{\{HIGH_ON\}\}.*?\{\{/HIGH_ON\}\}", string.Empty);
        return value.Replace("{{HIGH_ON}}", string.Empty, StringComparison.Ordinal)
            .Replace("{{/HIGH_ON}}", string.Empty, StringComparison.Ordinal).Trim();
    }
    public static string LoadWorkFooter(bool judgeAvailable)
    {
        var value = Load("WORK-ROUTING-CONTRACT.md");
        value = Regex.Replace(value, judgeAvailable ? @"(?s)\{\{JUDGE_OFF\}\}.*?\{\{/JUDGE_OFF\}\}" : @"(?s)\{\{JUDGE_ON\}\}.*?\{\{/JUDGE_ON\}\}", string.Empty);
        return Regex.Replace(value, @"\{\{/?JUDGE_(?:ON|OFF)\}\}", string.Empty).Trim();
    }
    public static string LoadHighFooter() => Load("HIGH-ROUTING-CONTRACT.md");
    public static string LoadJudgeFooter() => Load("JUDGE-ROUTING-CONTRACT.md");
    public static string LoadUnknownEnvelopeContract() => Load("UNKNOWN-ENVELOPE-CONTRACT.md");

    public static string BuildHqPrompt(string inboundType, string body, bool highPermitAvailable)
    {
        var routes = highPermitAvailable ? "[GOTO : WORK]\n[GOTO : HIGH]" : "[GOTO : WORK]";
        var permit = highPermitAvailable ? "[HIGH PERMIT : ONE_SHOT]\nremaining=1\n\n" : string.Empty;
        var header = $"[ROLE : HQ]\n\n[INBOUND TYPE : {inboundType}]\n\n[AVAILABLE GOTO]\n{routes}\n\n{permit}";
        return header + "[INBOUND BODY : JSON]\n" + JsonSerializer.Serialize(new { body }) + "\n\n" + LoadHqFooter(highPermitAvailable);
    }

    public static string BuildWorkPrompt(string inboundType, string body, bool judgeAvailable)
    {
        var header = $"[ROLE : WORK]\n\n[INBOUND TYPE : {inboundType}]\n\n[JUDGE AVAILABLE : {judgeAvailable.ToString().ToLowerInvariant()}]\n\n";
        return header + "[OPAQUE INBOUND BODY]\n" + body + "\n\n" + LoadWorkFooter(judgeAvailable);
    }

    public static string BuildHighPrompt(string body) => "[ROLE : HIGH]\n\n[INVOCATION : ONE_SHOT]\n\n[OPAQUE INBOUND BODY]\n" + body + "\n\n" + LoadHighFooter();

    public static string BuildUnknownEnvelope(string sourceState, string code, string detail)
    {
        return "[ROLE : UNKNOWN]\n\n" + LoadUnknownEnvelopeContract() + "\n\n[ERROR ENVELOPE : JSON]\n" +
            JsonSerializer.Serialize(new { source_state = sourceState, code, detail });
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
