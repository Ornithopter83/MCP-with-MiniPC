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
        var seen = false;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Trim() != "@@MESSAGE")
                continue;
            if (!seen)
            {
                lines[index] = "@@MESSAGE";
                seen = true;
            }
            else
            {
                // Information remains readable; duplicate transport headings
                // do not create additional MESSAGE sections.
                lines[index] = "추가 작업 맥락:";
            }
        }

        var body = string.Join("\n", lines);
        return seen ? body : "@@MESSAGE\n" + body;
    }
}
