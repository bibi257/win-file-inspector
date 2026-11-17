using System.Text.RegularExpressions;
namespace WinInspector.Core.Settings;
public sealed record PsdResult(IReadOnlyDictionary<string,object?> Values,string? Warning);
public static partial class PsdReader
{
    [GeneratedRegex("(?m)#.*$")] private static partial Regex Comments();
    [GeneratedRegex("(?s)^\\s*@\\s*\\{(.*)\\}\\s*$")] private static partial Regex Root();
    [GeneratedRegex("(?s)([A-Za-z][A-Za-z0-9]*)[ \\t\\r\\n]*=[ \\t\\r\\n]*('(?:''|[^'])*'|\\$true|\\$false|-?[0-9]+|@\\s*\\((.*?)\\))[ \\t]*(?:[;,][ \\t]*|\\r?\\n|$)",RegexOptions.IgnoreCase)] private static partial Regex Entry();
    public static PsdResult Parse(string text)
    {
        var root=Root().Match(Comments().Replace(text,""));if(!root.Success)return new(new Dictionary<string,object?>(),"設定ファイルに未対応の構文があります（行 1）");
        var values=new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase);var body=root.Groups[1].Value;var consumed=new bool[body.Length];
        foreach(Match m in Entry().Matches(body)){values[m.Groups[1].Value]=Value(m.Groups[2].Value,m.Groups[3].Value);for(var i=m.Index;i<m.Index+m.Length;i++)consumed[i]=true;}
        for(var i=0;i<body.Length;i++)if(!consumed[i]&&!char.IsWhiteSpace(body[i]))return new(new Dictionary<string,object?>(),$"設定ファイルに未対応の構文があります（行 {1+body[..i].Count(c=>c=='\n')}）");
        return new(values,null);
    }
    private static object? Value(string token,string array){if(token.StartsWith('@')){return Regex.Matches(array,"'(?:''|[^'])*'|\\$true|\\$false|-?[0-9]+",RegexOptions.IgnoreCase).Select(m=>Value(m.Value,"")).ToArray();}if(token.StartsWith('\''))return token[1..^1].Replace("''","'");if(token.Equals("$true",StringComparison.OrdinalIgnoreCase))return true;if(token.Equals("$false",StringComparison.OrdinalIgnoreCase))return false;if(int.TryParse(token,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var n))return n;return null;}
}
