using WinInspector.Core.Gui;
using WinInspector.Core.Paths;

namespace WinInspector.Windows.Fs;

/// <summary>Windows implementation of IPathProbe. Read-only: it only inspects attributes and drive types.</summary>
public sealed class WindowsPathProbe : IPathProbe
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool IsNetworkPath(string path)
    {
        if (WinPath.IsUnc(path)) return true;
        try
        {
            var root = WinPath.Root(path);
            return root is not null && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch
        {
            // When the drive type cannot be read, treat the path as network (the safe answer for validation).
            return true;
        }
    }

    public bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return false;
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }

    public bool HasReparsePointOnPathChain(string path)
    {
        try
        {
            var current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (IsReparsePoint(current)) return true;
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                current = parent;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    public bool DriveExists(string path)
    {
        var root = WinPath.Root(path);
        return root is not null && DriveInfo.GetDrives().Any(drive => string.Equals(drive.Name, root, StringComparison.OrdinalIgnoreCase));
    }
}
