using WinInspector.Core.Paths;

namespace WinInspector.Core.Gui;

/// <summary>String-only path rules for values typed into the GUI. No file-system access.</summary>
public static class PathRules
{
    /// <summary>True for a path that starts with a drive letter and a separator, e.g. C:\ or C:/.</summary>
    public static bool IsDriveAbsolute(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    /// <summary>Key for duplicate checks: case-insensitive, trailing separators ignored.</summary>
    public static string Key(string path) => WinPath.Normalize(path.Trim()).ToUpperInvariant();

    public static bool SameFolder(string a, string b) => string.Equals(Key(a), Key(b), StringComparison.Ordinal);
}
