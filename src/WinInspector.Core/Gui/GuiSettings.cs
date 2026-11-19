namespace WinInspector.Core.Gui;

/// <summary>Input range the GUI accepts. The CLI accepts any integer and is unchanged.</summary>
public static class GuiLimits
{
    public const int MinUnusedDays = 1;
    public const int MaxUnusedDays = 3650;
    public const int MinFolderDepth = 0;
    public const int MaxFolderDepth = 10;
}

public sealed record WindowBounds(int X, int Y, int Width, int Height);

/// <summary>
/// Values shown on the GUI. Targets == null means "the environment's default folders";
/// OutputDir == null means "the default output folder". Mode is "All", "Files" or "Apps".
/// </summary>
public sealed record GuiSettings(
    string Mode,
    string[]? Targets,
    string[] Excludes,
    string? OutputDir,
    int UnusedDays,
    int FolderDepth,
    bool RunAsAdministrator,
    WindowBounds? WindowBounds);
