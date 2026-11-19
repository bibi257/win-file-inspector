using WinInspector.Core.Cli;

namespace WinInspector.Core.Settings;

public static class SettingsComposition
{
    public static CliOptions Apply(CliOptions cli, string[] args) =>
        Apply(cli, args, Path.Combine(AppContext.BaseDirectory, "config", "config.psd1"));

    /// <summary>Same as Apply(cli, args) but reads the config file at configPath (for tests and the GUI).</summary>
    public static CliOptions Apply(CliOptions cli, string[] args, string configPath)
    {
        if (!File.Exists(configPath)) return Validate(cli);
        PsdResult parsed;
        try { parsed = PsdReader.Parse(File.ReadAllText(configPath)); }
        catch (IOException ex) { return cli with { ConfigWarning = "設定ファイルを読み込めません: " + ex.Message }; }
        if (parsed.Warning is not null) return cli with { ConfigWarning = parsed.Warning };
        var v = parsed.Values;
        bool Has(string key) => args.Any(a => a.Equals("-" + key, StringComparison.OrdinalIgnoreCase));
        T Get<T>(string key, T fallback) => v.TryGetValue(key, out var value) && value is T typed ? typed : fallback;
        var mode = Has("Mode") ? cli.Mode : Get("Mode", cli.Mode);
        string[] Strings(string key, string[] fallback) => !v.TryGetValue(key, out var value) || value is not object?[] values ? fallback : values.OfType<string>().ToArray();
        var targets = Has("Target") ? cli.Targets : v.TryGetValue("Target", out _) ? Strings("Target", cli.Targets) : cli.Targets;
        var excludes = Has("Exclude") ? cli.Excludes : v.TryGetValue("Exclude", out _) ? Strings("Exclude", cli.Excludes) : cli.Excludes;
        var output = Has("OutputDir") ? cli.OutputDir : Get("OutputDir", cli.OutputDir);
        var days = Has("UnusedDays") ? cli.UnusedDays : Get("UnusedDays", cli.UnusedDays);
        var depth = Has("FolderDepth") ? cli.FolderDepth : Get("FolderDepth", cli.FolderDepth);
        var raw = Has("IncludeRawLastAccess") ? cli.IncludeRawLastAccess : Get("IncludeRawLastAccess", cli.IncludeRawLastAccess);
        var removable = Has("IncludeRemovable") ? cli.IncludeRemovable : Get("IncludeRemovable", cli.IncludeRemovable);
        return Validate(cli with { Mode = mode, Targets = targets, Excludes = excludes, OutputDir = output, UnusedDays = days, FolderDepth = depth, IncludeRawLastAccess = raw, IncludeRemovable = removable });
    }
    private static CliOptions Validate(CliOptions value)
    {
        if(value.Mode is not ("Files" or "Apps" or "All")) throw new ArgumentException("パラメーター 'Mode' の引数を検証できません。引数 \""+value.Mode+"\" は ValidateSet 属性で指定されたセット \"Files,Apps,All\" に属していません。検証セットで指定された引数を指定してから、コマンドを再試行してください。");
        if(value.Targets.Any(string.IsNullOrWhiteSpace)||value.Excludes.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Target と Exclude に空の値は指定できません。");
        return value;
    }
}
