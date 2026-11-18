using System.Globalization;
using WinInspector.Core.Cli;

namespace WinInspector.Core.Gui;

/// <summary>Converts between GUI settings and CliOptions, and builds the arguments for an elevated relaunch.</summary>
public static class GuiOptionsMapper
{
    private static readonly string[] GuiOnlyFlags = ["-Gui", "-GuiAutoRun"];

    /// <summary>
    /// CliOptions for one run. NoPause is set (the GUI never waits for Enter), Elevate is off, and LowerPriority
    /// is off (not offered in the GUI). IncludeRawLastAccess and IncludeRemovable come from config.psd1.
    /// </summary>
    public static CliOptions ToCliOptions(GuiSettings settings, CliOptions configured, IReadOnlyList<string> effectiveTargets) => new(
        settings.Mode,
        effectiveTargets.ToArray(),
        settings.Excludes,
        settings.OutputDir,
        settings.UnusedDays,
        settings.FolderDepth,
        NoPause: true,
        Elevate: false,
        ConfigWarning: configured.ConfigWarning,
        IncludeRawLastAccess: configured.IncludeRawLastAccess,
        IncludeRemovable: configured.IncludeRemovable,
        LowerPriority: false,
        ShowHelp: false,
        ShowVersion: false);

    /// <summary>
    /// Arguments for the elevated GUI. Targets and the output folder are written as absolute paths already expanded,
    /// so the elevated process (a different account in some setups) uses the same folders.
    /// Each value is one argument; the relaunch passes them through an argument file, so no quoting is needed.
    /// </summary>
    public static string[] ToArguments(GuiSettings settings, IReadOnlyList<string> effectiveTargets, string expandedOutputDir)
    {
        var arguments = new List<string> { "-Gui", "-GuiAutoRun", "-Mode", settings.Mode };
        if (effectiveTargets.Count > 0)
        {
            arguments.Add("-Target");
            arguments.AddRange(effectiveTargets);
        }
        if (settings.Excludes.Length > 0)
        {
            arguments.Add("-Exclude");
            arguments.AddRange(settings.Excludes);
        }
        arguments.Add("-OutputDir");
        arguments.Add(expandedOutputDir);
        arguments.Add("-UnusedDays");
        arguments.Add(settings.UnusedDays.ToString(CultureInfo.InvariantCulture));
        arguments.Add("-FolderDepth");
        arguments.Add(settings.FolderDepth.ToString(CultureInfo.InvariantCulture));
        arguments.Add("-Elevate");
        return arguments.ToArray();
    }

    /// <summary>Removes the GUI-only flags so that the rest can go through CliParser unchanged.</summary>
    public static string[] StripGuiFlags(string[] args) =>
        args.Where(arg => !GuiOnlyFlags.Any(flag => flag.Equals(arg, StringComparison.OrdinalIgnoreCase))).ToArray();

    public static bool IsAutoRun(string[] args) => args.Any(arg => arg.Equals("-GuiAutoRun", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Starts from the baseline and replaces only the values whose flag is present in args. Used for
    /// "-Gui -Mode Files" (explicit values win over the saved ones) and for the elevated relaunch.
    /// </summary>
    /// <param name="given">args parsed by CliParser (with GUI flags stripped).</param>
    public static GuiSettings ApplyArguments(GuiSettings baseline, string[] args, CliOptions given)
    {
        bool Has(string flag) => args.Any(arg => arg.Equals("-" + flag, StringComparison.OrdinalIgnoreCase));
        return baseline with
        {
            Mode = Has("Mode") ? given.Mode : baseline.Mode,
            Targets = Has("Target") ? (given.Targets.Length > 0 ? given.Targets : null) : baseline.Targets,
            Excludes = Has("Exclude") ? given.Excludes : baseline.Excludes,
            OutputDir = Has("OutputDir") ? given.OutputDir : baseline.OutputDir,
            UnusedDays = Has("UnusedDays") ? given.UnusedDays : baseline.UnusedDays,
            FolderDepth = Has("FolderDepth") ? given.FolderDepth : baseline.FolderDepth,
            RunAsAdministrator = Has("Elevate") || baseline.RunAsAdministrator,
        };
    }
}
