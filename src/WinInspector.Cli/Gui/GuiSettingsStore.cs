using System.Text;
using WinInspector.Core.Cli;
using WinInspector.Core.Gui;
using WinInspector.Core.Output;
using WinInspector.Windows.Fs;

namespace WinInspector.Cli.Gui;

/// <summary>Reads and writes %LOCALAPPDATA%\WinInspector\gui-settings.json through OutputFileWriter (the only write path).</summary>
internal static class GuiSettingsStore
{
    public static string RootFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinInspector");

    public static string FilePath => Path.Combine(RootFolder, "gui-settings.json");

    /// <summary>The default output folder, the same one Orchestrator uses when OutputDir is not given.</summary>
    public static string DefaultOutputDir => Path.Combine(RootFolder, "Logs");

    /// <summary>Loads the saved settings and merges them with the config-derived baseline.</summary>
    public static GuiLoadResult Load(CliOptions configured)
    {
        string? text = null;
        var unreadable = false;
        try
        {
            if (File.Exists(FilePath))
            {
                if (new FileInfo(FilePath).Length > GuiSettingsJson.MaxBytes) unreadable = true;
                else text = File.ReadAllText(FilePath, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            unreadable = true;
        }
        catch (UnauthorizedAccessException)
        {
            unreadable = true;
        }
        return GuiSettingsDefaults.Resolve(configured, text, unreadable);
    }

    /// <summary>Saves the settings. Throws when the folder is not writable or sits behind a reparse point.</summary>
    public static void Save(GuiSettings settings)
    {
        var root = RootFolder;
        if (new WindowsPathProbe().HasReparsePointOnPathChain(root))
            throw new IOException("保存先にリンク（ジャンクションなど）が含まれています。");
        OutputFileWriter.EnsureDirectory(root, root);
        OutputFileWriter.Write(FilePath, root, GuiSettingsJson.Serialize(settings));
    }
}
