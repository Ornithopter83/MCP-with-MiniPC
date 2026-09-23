using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public enum NextRoute { Invalid, Web, Jev }
public sealed record NextDirective(NextRoute Route, string Body, string? Error = null);
public enum JevQuestionType { Noul, Score, Choice }
public sealed record JevPassRule(string Operator, double? Number, IReadOnlySet<string> Allowed);
public sealed record JevQuestion(string Id, JevQuestionType Type, string Instructions, IReadOnlyList<string> Criteria, IReadOnlyDictionary<string,string> ChoiceCriteria, JevPassRule Rule);
public sealed record JevValidationRequest(IReadOnlyList<JevQuestion> Questions);

public static class JevContract
{
    private static readonly Regex NextPattern = new(@"^\[NEXT\s*:\s*(WEB|JEV)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NumericPass = new(@"^PASS\s*:\s*(?:YES|SCORE)\s*(>=|<=)\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ChoicePass = new(@"^PASS\s*:\s*(?:CHOICE\s+IN\s+)?(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static NextDirective ParseNext(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(NextRoute.Invalid, string.Empty, "CODEX_EMPTY");
        var lines = Normalize(text).Split('\n');
        var first = lines.Select((value,index)=>(value.Trim(),index)).FirstOrDefault(x=>x.Item1.Length>0);
        if (string.IsNullOrWhiteSpace(first.Item1)) return new(NextRoute.Invalid,string.Empty,"NEXT_MISSING");
        var match=NextPattern.Match(first.Item1);
        if(!match.Success) return new(NextRoute.Invalid,string.Empty,"NEXT_INVALID");
        var body=string.Join(Environment.NewLine,lines.Skip(first.index+1)).Trim();
        return match.Groups[1].Value.Equals("WEB",StringComparison.OrdinalIgnoreCase)?new(NextRoute.Web,body):new(NextRoute.Jev,body);
    }

    public static string? ValidateStructure(NextDirective directive, bool reportOnly = false)
    {
        if (directive.Route == NextRoute.Invalid) return directive.Error ?? "NEXT_INVALID";
        var body = Normalize(directive.Body);
        if (directive.Route == NextRoute.Web && !FirstContentLine(body).Equals("[REPORT]", StringComparison.OrdinalIgnoreCase)) return reportOnly ? "REPORT_PROTOCOL_ERROR" : "REPORT_MISSING";
        if (directive.Route == NextRoute.Jev && !body.Split('\n').Any(line=>line.Trim().Equals("[VALIDATION REQUEST]",StringComparison.OrdinalIgnoreCase))) return "VALIDATION_REQUEST_MISSING";
        return null;
    }

    public static string ExtractValidationRequest(string body)
    {
        var lines=Normalize(body).Split('\n'); var index=Array.FindIndex(lines,line=>line.Trim().Equals("[VALIDATION REQUEST]",StringComparison.OrdinalIgnoreCase));
        return index<0?string.Empty:string.Join(Environment.NewLine,lines.Skip(index+1)).Trim();
    }

    public static bool TryParseValidation(string text, out JevValidationRequest request, out string error)
    {
        var lines=Normalize(text).Split('\n'); var parsed=new List<JevQuestion>();
        for(var i=0;i<lines.Length;i++)
        {
            var match=Regex.Match(lines[i].Trim(),@"^-?\s*(NOUL|SCORE|CHOICE)\s*\|\s*(.*)$",RegexOptions.IgnoreCase);
            if(!match.Success) continue;
            var type=match.Groups[1].Value.ToUpperInvariant(); var rawQuestion=match.Groups[2].Value.Trim();
            var inlinePass=rawQuestion.IndexOf("|",StringComparison.Ordinal); string question=inlinePass>=0?rawQuestion[..inlinePass].Trim():rawQuestion;
            var passLine=inlinePass>=0?rawQuestion[(inlinePass+1)..].Trim():null;
            if(question.Length==0){error=$"{type}_QUESTION_EMPTY";request=new(Array.Empty<JevQuestion>());return false;}
            var criteriaByNumber=new SortedDictionary<int,string>(); var choices=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); string? op=null; double? threshold=null; var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var end=i;
            if(passLine is not null) { if(!TryReadPass(type,passLine,ref op,ref threshold,allowed)){error=$"{type}_PASS_INVALID";request=new(Array.Empty<JevQuestion>());return false;} }
            for(var j=i+1;j<lines.Length;j++)
            {
                var line=lines[j].Trim(); if(Regex.IsMatch(line,@"^-?\s*(NOUL|SCORE|CHOICE)\s*\|",RegexOptions.IgnoreCase))break; if(line.Length==0)continue;
                if(TryReadPass(type,line,ref op,ref threshold,allowed)){end=j;break;}
                if(type=="SCORE"){var c=Regex.Match(line,@"^(\d+)\s*=\s*(.+)$");if(c.Success)criteriaByNumber[int.Parse(c.Groups[1].Value,CultureInfo.InvariantCulture)]=c.Groups[2].Value.Trim();}
                else if(type=="CHOICE"){var c=Regex.Match(line,@"^([A-Za-z][A-Za-z0-9_-]*)\s*=\s*(.+)$");if(c.Success)choices[c.Groups[1].Value]=c.Groups[2].Value.Trim();}
                end=j;
            }
            if(type!="CHOICE" && (op is null || threshold is null)){error=$"{type}_PASS_MISSING";request=new(Array.Empty<JevQuestion>());return false;}
            if(type!="CHOICE" && (!double.IsFinite(threshold!.Value) || (type=="NOUL" && (threshold<0 || threshold>1)))){error=$"{type}_THRESHOLD_RANGE";request=new(Array.Empty<JevQuestion>());return false;}
            if(type=="SCORE")
            {
                if(criteriaByNumber.Count==0 || !criteriaByNumber.Keys.SequenceEqual(Enumerable.Range(1,criteriaByNumber.Count))){error="SCORE_CRITERIA_NOT_CONTIGUOUS";request=new(Array.Empty<JevQuestion>());return false;}
                if(threshold!.Value<1 || threshold.Value>criteriaByNumber.Count){error="SCORE_THRESHOLD_RANGE";request=new(Array.Empty<JevQuestion>());return false;}
            }
            if(type=="CHOICE")
            {
                if(choices.Count==0 || allowed.Count==0 || allowed.Any(value=>!choices.ContainsKey(value))){error="CHOICE_ALLOWED_UNDEFINED";request=new(Array.Empty<JevQuestion>());return false;}
            }
            var criteria=criteriaByNumber.OrderBy(x=>x.Key).Select(x=>x.Value).ToList(); var qType=type=="NOUL"?JevQuestionType.Noul:type=="SCORE"?JevQuestionType.Score:JevQuestionType.Choice;
            parsed.Add(new($"C{parsed.Count+1}",qType,question,criteria,choices,new(op??"IN",threshold,allowed))); i=end;
        }
        if(parsed.Count==0){error="VALIDATION_EMPTY";request=new(Array.Empty<JevQuestion>());return false;} request=new(parsed);error=string.Empty;return true;
    }

    private static bool TryReadPass(string type,string line,ref string? op,ref double? threshold,HashSet<string> allowed)
    {
        var numeric=NumericPass.Match(line); if(type!="CHOICE" && numeric.Success && double.TryParse(numeric.Groups[2].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)){op=numeric.Groups[1].Value;threshold=number;return true;}
        if(type=="CHOICE"){var choice=ChoicePass.Match(line);if(choice.Success){foreach(var value in Regex.Split(choice.Groups[1].Value,@"\s*(?:또는|,|\||\bor\b)\s*",RegexOptions.IgnoreCase).Select(x=>x.Trim()).Where(x=>x.Length>0))allowed.Add(value);return allowed.Count>0;}}
        return false;
    }
    private static bool Fail(out JevValidationRequest request,out string error){request=new(Array.Empty<JevQuestion>());error="VALIDATION_INVALID";return false;}
    private static string Normalize(string text)=>text.Replace("\r\n","\n").Replace('\r','\n');
    private static string FirstContentLine(string text)=>text.Split('\n').Select(x=>x.Trim()).FirstOrDefault(x=>x.Length>0)??string.Empty;
    public static string LoadFooter(){using var stream=typeof(JevContract).Assembly.GetManifestResourceStream("ProjectHub.Worker.JEV-FOOTER-CONTRACT.md")??throw new FileNotFoundException("Embedded JEV footer contract was not found.");using var reader=new StreamReader(stream);return reader.ReadToEnd().Trim();}
}
