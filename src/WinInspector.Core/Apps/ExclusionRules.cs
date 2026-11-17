using System.Text.RegularExpressions;
namespace WinInspector.Core.Apps;
public static partial class ExclusionRules
{
    private static readonly string[] BroadRoots=[@"C:\",@"C:\Program Files",@"C:\Program Files (x86)",@"C:\Windows",@"C:\Windows\System32"];
    [GeneratedRegex("^KB\\d+$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)] private static partial Regex KbRegex();
    [GeneratedRegex("^(?:Update|Hotfix|Security Update)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)] private static partial Regex ReleaseRegex();
    public static string? Reason(AppRecord app,IEnumerable<Regex>? namePatterns=null)
    {
        if(Convert.ToString(app.SystemComponent,System.Globalization.CultureInfo.InvariantCulture)=="1") return "SystemComponent";
        if(!string.IsNullOrWhiteSpace(app.ParentKeyName)) return "ParentKeyName";
        if(app.ReleaseType is not null&&ReleaseRegex().IsMatch(app.ReleaseType)) return "ReleaseType";
        if(app.Name is not null&&KbRegex().IsMatch(app.Name)) return "KB";
        if(string.IsNullOrWhiteSpace(app.Name)) return "名称なし";
        if(namePatterns?.FirstOrDefault(r=>r.IsMatch(app.Name)) is { } pattern) return $"名前パターン: {pattern}";
        if(app.Source.Equals("Appx",StringComparison.OrdinalIgnoreCase) && (app.SignatureKind?.Equals("System",StringComparison.OrdinalIgnoreCase)==true||app.IsFramework||app.NonRemovable||app.IsResourcePackage)) return "Appx システムパッケージ";
        return null;
    }
    public static bool IsBroadRoot(string? path) => path is not null && BroadRoots.Any(x=>string.Equals(path.TrimEnd('\\','/'),x.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase));
}
