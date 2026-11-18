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
        // With a config warning, config.psd1 values are not used (same as the CLI). Only the built-in defaults remain.
        var source = configured.ConfigWarning is null ? configured : new CliOptions("All", Array.Empty<string>(), Array.Empty<string>(), null);
        var loaded = GuiSettingsJson.Load(savedText, FromConfig(source), savedUnreadable);
        var warnings = new List<string>(loaded.Warnings);
        // A config value that the saved file overrides is not used, so its out-of-range value is not reported.
        var rounded = new List<string>();
        if (!loaded.SavedNumbers.Contains("unusedDays") && (source.UnusedDays < GuiLimits.MinUnusedDays || source.UnusedDays > GuiLimits.MaxUnusedDays)) rounded.Add("unusedDays");
        if (!loaded.SavedNumbers.Contains("folderDepth") && (source.FolderDepth < GuiLimits.MinFolderDepth || source.FolderDepth > GuiLimits.MaxFolderDepth)) rounded.Add("folderDepth");
        if (rounded.Count > 0) warnings.Add(GuiSettingsJson.RoundedMessage(rounded));
        if (configured.ConfigWarning is not null) warnings.Add(configured.ConfigWarning);
        return loaded with { Warnings = warnings };
    }
}
