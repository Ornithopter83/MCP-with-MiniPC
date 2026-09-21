using System.IO;
using System.Globalization;
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
    private static readonly Regex PassNumber = new(@"PASS\s*:\s*(?:YES|SCORE)\s*(>=|<=)\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static NextDirective ParseNext(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(NextRoute.Invalid, string.Empty, "Codex 결과가 비어 있습니다.");
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = lines.Select((value,index)=>(value.Trim(),index)).FirstOrDefault(x=>x.Item1.Length>0);
        if (string.IsNullOrWhiteSpace(first.Item1)) return new(NextRoute.Invalid,string.Empty,"Codex 결과에 유효한 NEXT 행이 없습니다.");
        var match=NextPattern.Match(first.Item1);
        if(!match.Success) return new(NextRoute.Invalid,string.Empty,"Codex 결과 첫 유효행이 [NEXT : WEB] 또는 [NEXT : JEV]가 아닙니다.");
        var body=string.Join(Environment.NewLine,lines.Skip(first.index+1)).Trim();
        return match.Groups[1].Value.Equals("WEB",StringComparison.OrdinalIgnoreCase)?new(NextRoute.Web,body):new(NextRoute.Jev,body);
    }

    public static string ExtractValidationRequest(string body)
    {
        var marker=body.IndexOf("[VALIDATION REQUEST]",StringComparison.OrdinalIgnoreCase);
        return marker<0?string.Empty:body[(marker+"[VALIDATION REQUEST]".Length)..].Trim();
    }

    public static bool TryParseValidation(string text, out JevValidationRequest request, out string error)
    {
        var lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        var parsed=new List<JevQuestion>();
        for(var i=0;i<lines.Length;i++)
        {
            var match=Regex.Match(lines[i].Trim(),@"^-?\s*(NOUL|SCORE|CHOICE)\s*\|\s*(.+)$",RegexOptions.IgnoreCase);
            if(!match.Success) continue;
            var type=match.Groups[1].Value.ToUpperInvariant(); var question=match.Groups[2].Value.Trim();
            var criteria=new List<string>(); var choices=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); string? op=null; double? number=null; var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var passIndex=-1;
            for(var j=i+1;j<lines.Length;j++)
            {
                var line=lines[j].Trim();
                if(Regex.IsMatch(line,@"^-?\s*(NOUL|SCORE|CHOICE)\s*\|",RegexOptions.IgnoreCase)) break;
                if(line.Length==0) continue;
                var pass=PassNumber.Match(line); if(pass.Success){op=pass.Groups[1].Value; number=double.Parse(pass.Groups[2].Value,CultureInfo.InvariantCulture);passIndex=j;break;}
                var choicePass=Regex.Match(line,@"^PASS\s*:\s*(?:CHOICE\s+IN\s+)?(.+)$",RegexOptions.IgnoreCase);
                if(type=="CHOICE" && choicePass.Success){foreach(var value in Regex.Split(choicePass.Groups[1].Value,@"\s*(?:또는|,|\||\bor\b)\s*",RegexOptions.IgnoreCase).Select(x=>x.Trim()).Where(x=>x.Length>0))allowed.Add(value);passIndex=j;break;}
                if(type=="SCORE") {var c=Regex.Match(line,@"^\d+\s*=\s*(.+)$");if(c.Success)criteria.Add(c.Groups[1].Value.Trim());}
                if(type=="CHOICE") {var c=Regex.Match(line,@"^([A-Za-z][A-Za-z0-9_-]*)\s*=\s*(.+)$");if(c.Success)choices[c.Groups[1].Value]=c.Groups[2].Value.Trim();}
            }
            if(type=="CHOICE" && allowed.Count==0){error=$"{type} 검증의 PASS 허용값이 없습니다.";request=new(Array.Empty<JevQuestion>());return false;}
            if(type!="CHOICE" && (op is null || number is null)){error=$"{type} 검증의 PASS threshold가 없습니다.";request=new(Array.Empty<JevQuestion>());return false;}
            if(type=="SCORE" && criteria.Count==0){error="SCORE criteria가 없습니다.";request=new(Array.Empty<JevQuestion>());return false;}
            var rule=new JevPassRule(op??"IN",number,allowed); parsed.Add(new($"C{parsed.Count+1}",type switch{"NOUL"=>JevQuestionType.Noul,"SCORE"=>JevQuestionType.Score,_=>JevQuestionType.Choice},question,criteria,choices,rule));
            if(passIndex>i)i=passIndex;
        }
        if(parsed.Count==0){error="NOUL/SCORE/CHOICE 검증 항목이 없습니다.";request=new(Array.Empty<JevQuestion>());return false;}
        request=new(parsed);error=string.Empty;return true;
    }

    public static string LoadFooter()
    {
        using var stream=typeof(JevContract).Assembly.GetManifestResourceStream("ProjectHub.Worker.JEV-FOOTER-CONTRACT.md")??throw new FileNotFoundException("Embedded JEV footer contract was not found.");
        using var reader=new StreamReader(stream);return reader.ReadToEnd().Trim();
    }
}
