namespace WinInspector.Core.Gui;

/// <summary>
/// File-system questions the GUI validator needs. The Windows implementation lives in WinInspector.Windows;
/// tests inject a fake so that validation can run on Linux.
/// </summary>
public interface IPathProbe
{
    bool DirectoryExists(string path);

    /// <summary>True for UNC paths and for drives whose type is network.</summary>
    bool IsNetworkPath(string path);

    /// <summary>True when the path itself is a reparse point (junction, symbolic link).</summary>
    bool IsReparsePoint(string path);

    /// <summary>True when the path or any existing ancestor is a reparse point.</summary>
    bool HasReparsePointOnPathChain(string path);

    bool DriveExists(string path);
}
