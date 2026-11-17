using WinInspector.Core.Scan;
using WinInspector.Core.Paths;
namespace WinInspector.Core.Apps;
public sealed record InstallMeasurement(long? SizeBytes,string? Source,string? Reason);
public sealed class InstallFolderMeasurer(IFileSystem fs)
{
    private readonly Dictionary<string,InstallMeasurement> cache=new(StringComparer.OrdinalIgnoreCase);
    public InstallMeasurement Measure(string? path,IEnumerable<string>? allLocations=null)
    {
        if(string.IsNullOrWhiteSpace(path))return new(null,null,"InstallLocation なし");
        var full=path.TrimEnd('\\','/'); if(cache.TryGetValue(full,out var found))return found;
        string[] excluded=[@"C:\",@"C:\Program Files",@"C:\Program Files (x86)"];
        if(full.StartsWith(@"\\",StringComparison.Ordinal)||excluded.Any(x=>string.Equals(full,x.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)))return cache[full]=new(null,null,"計測対象外のパス");
        if(allLocations?.Count(x=>string.Equals(x?.TrimEnd('\\','/'),full,StringComparison.OrdinalIgnoreCase))>1)return cache[full]=new(null,null,"InstallLocation 重複");
        for(var p=full;p.Length>2;){var index=p.LastIndexOf('\\');if(index<0)break;p=WinPath.Normalize(p[..index]);if(fs.TryGetAttributes(p)?.HasFlag(FileAttributes.ReparsePoint)==true)return cache[full]=new(null,null,"祖先にリパースポイント");}
        long sum=0;string? rootFailure=null;var stack=new Stack<string>();stack.Push(full);
        while(stack.Count>0){var dir=stack.Pop();try{foreach(var e in fs.Enumerate(dir)){try{if(e.Attributes.HasFlag(FileAttributes.ReparsePoint))continue;if(e.IsDirectory)stack.Push(e.FullPath);else sum=checked(sum+e.Length);}catch{}}}catch(Exception ex){if(string.Equals(dir,full,StringComparison.OrdinalIgnoreCase))rootFailure=ex.Message;}}
        return cache[full]=rootFailure is null?new(sum,"FolderSum",null):new(null,null,rootFailure);
    }
}
