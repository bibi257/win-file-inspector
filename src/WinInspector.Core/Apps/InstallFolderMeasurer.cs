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
        if(full.StartsWith("\\\\",StringComparison.Ordinal)||ExclusionRules.IsBroadRoot(full))return cache[full]=new(null,null,"計測対象外のパス");
        if(allLocations?.Count(x=>string.Equals(x?.TrimEnd('\\','/'),full,StringComparison.OrdinalIgnoreCase))>1)return cache[full]=new(null,null,"InstallLocation 重複");
        for(var p=full;p.Length>2;p=WinPath.Normalize(p[..p.LastIndexOf('\\')])) if(fs.TryGetAttributes(p)?.HasFlag(FileAttributes.ReparsePoint)==true)return cache[full]=new(null,null,"祖先にリパースポイント");
        try { long sum=0;var stack=new Stack<string>();stack.Push(full);while(stack.Count>0)foreach(var e in fs.Enumerate(stack.Pop())){if(e.Attributes.HasFlag(FileAttributes.ReparsePoint))continue;if(e.IsDirectory)stack.Push(e.FullPath);else sum=checked(sum+e.Length);}return cache[full]=new(sum,"FolderSum",null); }
        catch(Exception ex){return cache[full]=new(null,null,ex.Message);}
    }
}
