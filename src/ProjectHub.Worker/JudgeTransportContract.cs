using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public enum JudgeTransportQuestionType { Noul, Score, Choice }
public sealed record JudgeTransportQuestion(string Id, JudgeTransportQuestionType Type, string Instructions, IReadOnlyList<string> Criteria, IReadOnlyDictionary<string, string> ChoiceCriteria);
public sealed record JudgeTransportRequest(IReadOnlyList<JudgeTransportQuestion> Questions);

/// <summary>Parses only the structure required to serialize a provider request; PASS rules remain opaque instructions.</summary>
public static class JudgeTransportContract
{
    private static readonly Regex QuestionLine = new(@"^-?\s*(NOUL|SCORE|CHOICE)\s*\|\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string ExtractRequest(string body) => body.Trim();

    public static bool TryParse(string text, out JudgeTransportRequest request, out string error)
    {
        var lines = Normalize(text).Split('\n');
        var questions = new List<JudgeTransportQuestion>();
        for (var i = 0; i < lines.Length; i++)
        {
            var match = QuestionLine.Match(lines[i].Trim());
            if (!match.Success) continue;
            var type = Enum.Parse<JudgeTransportQuestionType>(match.Groups[1].Value, true);
            var prompt = match.Groups[2].Value.Trim();
            var qid = Regex.Match(
                prompt,
                @"^(?:\[QID:(?<id>[A-Z][A-Z0-9_-]{0,31})\]|QID:\s*(?<id>[A-Z][A-Z0-9_-]{0,31}))\s*",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var explicitId = qid.Success;
            var generatedId = questions.Count + 1;
            var id = explicitId ? qid.Groups["id"].Value.ToUpperInvariant() : $"C{generatedId}";
            while (!explicitId && questions.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) id = $"C{++generatedId}";
            if (qid.Success) prompt = prompt[qid.Length..].Trim();
            if (prompt.Length == 0) return Fail("QUESTION_EMPTY", out request, out error);

            var instructions = new List<string>();
            var criteria = new SortedDictionary<int, string>();
            var choices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var end = i;
            for (var j = i + 1; j < lines.Length; j++)
            {
                var line = lines[j].Trim();
                if (QuestionLine.IsMatch(line)) break;
                if (line.Length == 0) continue;
                var criterion = type switch
                {
                    JudgeTransportQuestionType.Score => Regex.Match(line, @"^(\d+)\s*=\s*(.+)$"),
                    JudgeTransportQuestionType.Choice => Regex.Match(line, @"^([A-Za-z][A-Za-z0-9_-]*)\s*=\s*(.+)$"),
                    _ => Match.Empty
                };
                if (criterion.Success && type == JudgeTransportQuestionType.Score)
                    criteria[int.Parse(criterion.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)] = criterion.Groups[2].Value.Trim();
                else if (criterion.Success && type == JudgeTransportQuestionType.Choice)
                    choices[criterion.Groups[1].Value] = criterion.Groups[2].Value.Trim();
                else instructions.Add(line);
                end = j;
            }
            if (type == JudgeTransportQuestionType.Score && criteria.Count == 0)
                return Fail("SCORE_CRITERIA_INVALID", out request, out error);
            if (type == JudgeTransportQuestionType.Choice && choices.Count == 0)
                return Fail("CHOICE_CRITERIA_MISSING", out request, out error);
            if (questions.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                return Fail("QUESTION_ID_DUPLICATE", out request, out error);

            var fullInstructions = prompt + (instructions.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, instructions));
            questions.Add(new(id, type, fullInstructions, criteria.Values.ToArray(), choices));
            i = end;
        }
        if (questions.Count == 0) return Fail("VALIDATION_EMPTY", out request, out error);
        request = new(questions);
        error = string.Empty;
        return true;
    }

    private static bool Fail(string code, out JudgeTransportRequest request, out string error)
    {
        request = new(Array.Empty<JudgeTransportQuestion>());
        error = code;
        return false;
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');
}
