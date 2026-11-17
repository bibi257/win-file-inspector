namespace WinInspector.Core.Apps;
public sealed record UsageEvidence(string Source,string? Path,string? ExeName,uint? RunCount,DateTime? LastRunTime,string? PackageFamilyName=null);
public sealed record EvidenceMatch(DateTime? LastRunTime,string? LastRunSource,uint? RunCount,bool CountOnly,string? Method);
public static class EvidenceMatcher
{
    public static EvidenceMatch Match(AppRecord app,IEnumerable<UsageEvidence> evidence)
    {
        var candidates=new List<(UsageEvidence Evidence,string Method)>();
        var location=ExclusionRules.IsBroadRoot(app.InstallLocation)?null:app.InstallLocation?.TrimEnd('\\','/');
        var exeFolders=new[]{app.DisplayIcon,app.UninstallString}.Select(ExtractExeFolder).Where(x=>x is not null&&!ExclusionRules.IsBroadRoot(x)).ToArray();
        foreach(var item in evidence)
        {
            if(item.Source=="UserAssist" && item.RunCount>0)
            {
                if(location is not null&&item.Path?.StartsWith(location+"\\",StringComparison.OrdinalIgnoreCase)==true)candidates.Add((item,"InstallLocation"));
                else if(item.Path is not null&&exeFolders.Any(f=>item.Path.StartsWith(f+"\\",StringComparison.OrdinalIgnoreCase)))candidates.Add((item,"IconOrUninstaller"));
                else if(app.PackageFamilyName is not null&&item.Path?.StartsWith(app.PackageFamilyName+"!",StringComparison.OrdinalIgnoreCase)==true)candidates.Add((item,"AppxAumid"));
            }
            if(item.Source=="Prefetch"&&item.ExeName is not null&&new[]{app.DisplayIcon,app.UninstallString}.Select(ExtractExeName).Any(n=>n is not null&&n.Equals(item.ExeName,StringComparison.OrdinalIgnoreCase)))candidates.Add((item,"PrefetchName"));
        }
        var timed=candidates.Where(x=>x.Evidence.LastRunTime.HasValue).OrderByDescending(x=>x.Evidence.LastRunTime).FirstOrDefault();
        var count=candidates.Where(x=>x.Evidence.Source=="UserAssist").Select(x=>x.Evidence.RunCount).Where(x=>x.HasValue).Select(x=>x!.Value).DefaultIfEmpty().Max();
        return new(timed.Evidence?.LastRunTime,timed.Evidence?.Source??(count>0?"UserAssist":"なし"),count>0?count:null,candidates.Any(x=>x.Evidence.Source=="UserAssist"&&x.Evidence.RunCount>0&&!x.Evidence.LastRunTime.HasValue),candidates.FirstOrDefault().Method);
    }
    private static string? ExtractExeName(string? text){if(string.IsNullOrWhiteSpace(text))return null;var m=System.Text.RegularExpressions.Regex.Match(text,"(?i)([^\\\\/\\\"\\s,]+\\.exe)");return m.Success?m.Groups[1].Value:null;}
    private static string? ExtractExeFolder(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        var match=System.Text.RegularExpressions.Regex.Match(text,@"^\s*""?([^"",]+?\.exe)(""|,|\s|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if(!match.Success)return null;var exe=match.Groups[1].Value;var separator=exe.LastIndexOfAny(['\\','/']);return separator<0?null:exe[..separator];
    }
}
