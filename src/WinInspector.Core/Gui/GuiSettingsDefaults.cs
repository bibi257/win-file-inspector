using WinInspector.Core.Cli;

namespace WinInspector.Core.Gui;

/// <summary>Builds the starting values: saved GUI value &gt; config.psd1 &gt; built-in default (per item).</summary>
public static class GuiSettingsDefaults
{
    /// <summary>The config-derived values as GUI settings. Values out of the GUI range are clamped.</summary>
    public static GuiSettings FromConfig(CliOptions configured) => new(
        configured.Mode,
        configured.Targets.Length > 0 ? configured.Targets : null,
        configured.Excludes,
        configured.OutputDir,
        Math.Clamp(configured.UnusedDays, GuiLimits.MinUnusedDays, GuiLimits.MaxUnusedDays),
        Math.Clamp(configured.FolderDepth, GuiLimits.MinFolderDepth, GuiLimits.MaxFolderDepth),
        false,
        null);

    /// <param name="configured">CLI options after config.psd1 has been applied (SettingsComposition.Apply).</param>
    /// <param name="savedText">Text of gui-settings.json, or null when absent.</param>
    /// <param name="savedUnreadable">True when the saved file exists but cannot be read or is too large.</param>
    public static GuiLoadResult Resolve(CliOptions configured, string? savedText, bool savedUnreadable)
    {
        var loaded = GuiSettingsJson.Load(savedText, FromConfig(configured), savedUnreadable);
        if (configured.ConfigWarning is null) return loaded;
        var warnings = new List<string>(loaded.Warnings) { configured.ConfigWarning };
        return loaded with { Warnings = warnings };
    }
}
