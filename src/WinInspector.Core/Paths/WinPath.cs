namespace WinInspector.Core.Paths;
public static class WinPath
{
    public static string Normalize(string path)
    {
        if (path.Length == 0) return path;
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/') && path[3..].All(c => c == '\\' || c == '/')) return path[..3];
        var normalized=path.TrimEnd('\\','/');
        return normalized.Length==0 && (path[0]=='\\'||path[0]=='/') ? path[..1] : normalized;
    }
    public static bool IsWithin(string path, string parent)
    { var p=Normalize(path); var b=Normalize(parent); return string.Equals(p,b,StringComparison.OrdinalIgnoreCase)||p.StartsWith(b.EndsWith('\\')||b.EndsWith('/')?b:b+"\\",StringComparison.OrdinalIgnoreCase)||p.StartsWith(b.EndsWith('\\')||b.EndsWith('/')?b:b+"/",StringComparison.OrdinalIgnoreCase); }
}
