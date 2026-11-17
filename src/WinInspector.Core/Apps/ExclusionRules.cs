using System.Text.RegularExpressions;
namespace WinInspector.Core.Apps;
public static partial class ExclusionRules
{
    private static readonly string[] BroadRoots=[@"C:\",@"C:\Program Files",@"C:\Program Files (x86)",@"C:\Windows",@"C:\Windows\System32"];
    [GeneratedRegex("KB\\d+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)] private static partial Regex KbRegex();
    [GeneratedRegex("^(?:Update|Hotfix|Security Update)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)] private static partial Regex ReleaseRegex();
    public static string? Reason(AppRecord app,IEnumerable<Regex>? namePatterns=null)
    {
        var reasons=new List<string>();
        if(Convert.ToString(app.SystemComponent,System.Globalization.CultureInfo.InvariantCulture)=="1") reasons.Add("SystemComponent=1");
        if(!string.IsNullOrWhiteSpace(app.ParentKeyName)) reasons.Add("ParentKeyNameあり");
        if(app.ReleaseType is not null&&ReleaseRegex().IsMatch(app.ReleaseType)) reasons.Add($"ReleaseType={app.ReleaseType}");
        if(app.Name is not null&&KbRegex().IsMatch(app.Name)) reasons.Add("KB 更新プログラム");
        if(string.IsNullOrWhiteSpace(app.Name)) reasons.Add("名称なし");
        else if(namePatterns is not null){var matched=namePatterns.FirstOrDefault(r=>r.IsMatch(app.Name));if(matched is not null)reasons.Add($"名前パターン: {matched}");}
        if(app.Source.Equals("Appx",StringComparison.OrdinalIgnoreCase) && (app.SignatureKind?.Equals("System",StringComparison.OrdinalIgnoreCase)==true||app.IsFramework||app.NonRemovable||app.IsResourcePackage)) reasons.Add("システム/Framework/NonRemovable/Resource Appx");
        return reasons.Count==0?null:string.Join("; ",reasons);
    }
    public static bool IsBroadRoot(string? path) => path is not null && BroadRoots.Any(x=>string.Equals(path.TrimEnd('\\','/'),x.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase));
}
