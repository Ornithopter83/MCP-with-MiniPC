namespace ProjectHub.Worker;

/// <summary>
/// Unity specialist input has one descriptive MESSAGE section. HQ command
/// sections and the REPORT template in role contracts are not rewritten.
/// </summary>
internal static class UnityRoleMessageComposer
{
    internal static string Compose(string prompt)
    {
        var value = (prompt ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        var lines = value.Split('\n');
        var hasLeadingMessage = lines.Length > 0 &&
            lines[0].Trim() == "@@MESSAGE";
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Trim() != "@@MESSAGE")
                continue;
            if (hasLeadingMessage && index == 0)
                lines[index] = "@@MESSAGE";
            else
            {
                // A MESSAGE marker inside a previous response is reference
                // data, not an additional top-level auxiliary section.
                lines[index] = "추가 작업 맥락:";
            }
        }

        var body = string.Join("\n", lines);
        return hasLeadingMessage ? body : "@@MESSAGE\n" + body;
    }
}
