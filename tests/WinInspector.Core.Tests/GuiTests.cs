using System.IO;
using WinInspector.Core.Cli;
using WinInspector.Core.Gui;
using WinInspector.Core.Paths;
using WinInspector.Core.Scan;
using WinInspector.Core.Settings;
using Xunit;

namespace WinInspector.Core.Tests;

public class GuiTests
{
    private static readonly string[] KeepTargets = new[] { @"D:\Keep" };
    private static readonly string[] ConfigTargets = new[] { @"C:\Config" };
    private static readonly string[] UsersTargets = new[] { @"C:\Users" };
    private static readonly string[] UserProfileTargets = new[] { @"C:\Users\me" };

    private static GuiSettings Baseline() => new("All", null, Array.Empty<string>(), null, 365, 3, false, null);

    private static void AssertSame(GuiSettings expected, GuiSettings actual)
    {
        Assert.Equal(expected.Mode, actual.Mode);
        Assert.Equal(expected.Targets, actual.Targets);
        Assert.Equal(expected.Excludes, actual.Excludes);
        Assert.Equal(expected.OutputDir, actual.OutputDir);
        Assert.Equal(expected.UnusedDays, actual.UnusedDays);
        Assert.Equal(expected.FolderDepth, actual.FolderDepth);
        Assert.Equal(expected.RunAsAdministrator, actual.RunAsAdministrator);
        Assert.Equal(expected.WindowBounds, actual.WindowBounds);
    }

    private static CliOptions Cli(string mode = "All", string[]? targets = null, string[]? excludes = null, string? output = null, int days = 365, int depth = 3, string? warning = null) =>
        new(mode, targets ?? Array.Empty<string>(), excludes ?? Array.Empty<string>(), output, days, depth, ConfigWarning: warning);

    // --- GuiSettingsJson ---

    [Fact]
    public void JsonRoundTripKeepsEveryValue()
    {
        var settings = new GuiSettings("Files", [@"C:\Users\me", @"D:\Data"], [@"C:\Users\me\Cache"], @"E:\Out", 90, 5, true, new WindowBounds(10, 20, 800, 600));
        var loaded = GuiSettingsJson.Load(GuiSettingsJson.Serialize(settings), Baseline());
        AssertSame(settings, loaded.Settings);
        Assert.Empty(loaded.Warnings);
        Assert.False(loaded.PreserveOnSave);
    }

    [Fact]
    public void NullTargetsAndOutputDirAreWrittenAndReadBack()
    {
        var settings = new GuiSettings("All", null, [], null, 365, 3, false, null);
        var text = GuiSettingsJson.Serialize(settings);
        Assert.Contains("\"targets\": null", text);
        Assert.Contains("\"outputDir\": null", text);
        AssertSame(settings, GuiSettingsJson.Load(text, Baseline()).Settings);
    }

    [Fact]
    public void ByteOrderMarkIsAccepted()
    {
        var text = "\uFEFF" + GuiSettingsJson.Serialize(Baseline() with { Mode = "Apps" });
        Assert.Equal("Apps", GuiSettingsJson.Load(text, Baseline()).Settings.Mode);
    }

    [Fact]
    public void AbsentFileUsesBaselineWithoutWarning()
    {
        var loaded = GuiSettingsJson.Load(null, Baseline());
        AssertSame(Baseline(), loaded.Settings);
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public void BrokenJsonFallsBackToBaselineAndCanBeOverwritten()
    {
        var loaded = GuiSettingsJson.Load("{ not json", Baseline());
        AssertSame(Baseline(), loaded.Settings);
        Assert.Single(loaded.Warnings);
        Assert.False(loaded.PreserveOnSave);
    }

    [Fact]
    public void EmptyFileFallsBackToBaseline()
    {
        var loaded = GuiSettingsJson.Load(string.Empty, Baseline());
        AssertSame(Baseline(), loaded.Settings);
        Assert.Single(loaded.Warnings);
    }

    [Fact]
    public void NonObjectRootFallsBackToBaseline()
    {
        var loaded = GuiSettingsJson.Load("[1,2,3]", Baseline());
        AssertSame(Baseline(), loaded.Settings);
        Assert.Single(loaded.Warnings);
    }

    [Fact]
    public void UnreadableOrOversizedFileFallsBackToBaseline()
    {
        var loaded = GuiSettingsJson.Load(null, Baseline(), unreadable: true);
        AssertSame(Baseline(), loaded.Settings);
        Assert.Single(loaded.Warnings);
    }

    [Fact]
    public void OversizedTextIsTreatedAsCorrupt()
    {
        var text = "{\"schemaVersion\":1,\"mode\":\"" + new string('a', GuiSettingsJson.MaxBytes) + "\"}";
        var loaded = GuiSettingsJson.Load(text, Baseline());
        AssertSame(Baseline(), loaded.Settings);
        Assert.Single(loaded.Warnings);
    }

    [Fact]
    public void MissingSchemaVersionIgnoresEverythingAndPreservesFile()
    {
        var loaded = GuiSettingsJson.Load("{\"mode\":\"Files\"}", Baseline());
        AssertSame(Baseline(), loaded.Settings);
        Assert.True(loaded.PreserveOnSave);
    }

    [Fact]
    public void NewerSchemaVersionIgnoresEverythingAndPreservesFile()
    {
        var loaded = GuiSettingsJson.Load("{\"schemaVersion\":2,\"mode\":\"Files\"}", Baseline());
        AssertSame(Baseline(), loaded.Settings);
        Assert.True(loaded.PreserveOnSave);
    }

    [Fact]
    public void InvalidItemFallsBackAloneAndIsNamedInWarning()
    {
        var loaded = GuiSettingsJson.Load("{\"schemaVersion\":1,\"mode\":\"Bogus\",\"unusedDays\":\"abc\",\"folderDepth\":7}", Baseline() with { Mode = "Apps" });
        Assert.Equal("Apps", loaded.Settings.Mode);
        Assert.Equal(365, loaded.Settings.UnusedDays);
        Assert.Equal(7, loaded.Settings.FolderDepth);
        Assert.Single(loaded.Warnings);
        Assert.Contains("mode", loaded.Warnings[0]);
        Assert.Contains("unusedDays", loaded.Warnings[0]);
    }

    [Fact]
    public void NumbersOutsideTheRangeAreRounded()
    {
        var loaded = GuiSettingsJson.Load("{\"schemaVersion\":1,\"unusedDays\":99999,\"folderDepth\":-4}", Baseline());
        Assert.Equal(GuiLimits.MaxUnusedDays, loaded.Settings.UnusedDays);
        Assert.Equal(GuiLimits.MinFolderDepth, loaded.Settings.FolderDepth);
        Assert.Single(loaded.Warnings);
        Assert.Contains("unusedDays", loaded.Warnings[0]);
        Assert.Contains("folderDepth", loaded.Warnings[0]);

        var inRange = GuiSettingsJson.Load("{\"schemaVersion\":1,\"unusedDays\":3650,\"folderDepth\":0}", Baseline());
        Assert.Empty(inRange.Warnings);
    }

    [Fact]
    public void ConfigNumbersOutsideTheRangeAreRoundedWithWarning()
    {
        var loaded = GuiSettingsDefaults.Resolve(Cli(days: 99999, depth: -4), null, false);
        Assert.Equal(GuiLimits.MaxUnusedDays, loaded.Settings.UnusedDays);
        Assert.Equal(GuiLimits.MinFolderDepth, loaded.Settings.FolderDepth);
        Assert.Single(loaded.Warnings);
        Assert.Contains("unusedDays", loaded.Warnings[0]);
        Assert.Contains("folderDepth", loaded.Warnings[0]);
    }

    [Fact]
    public void ConfigRoundingIsNotReportedWhenSavedValueWins()
    {
        // Config is out of range, but the saved file supplies both numbers: the config values are never used.
        var configured = Cli(days: 99999, depth: -4);
        var saved = "{\"schemaVersion\":1,\"unusedDays\":30,\"folderDepth\":2}";
        var loaded = GuiSettingsDefaults.Resolve(configured, saved, false);
        Assert.Equal(30, loaded.Settings.UnusedDays);
        Assert.Equal(2, loaded.Settings.FolderDepth);
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public void ConfigRoundingIsReportedOnlyForItemsWithoutSavedValue()
    {
        // Only unusedDays is saved, so the out-of-range folderDepth from config is still used and must be reported.
        var configured = Cli(days: 99999, depth: -4);
        var saved = "{\"schemaVersion\":1,\"unusedDays\":30}";
        var loaded = GuiSettingsDefaults.Resolve(configured, saved, false);
        Assert.Equal(30, loaded.Settings.UnusedDays);
        Assert.Equal(GuiLimits.MinFolderDepth, loaded.Settings.FolderDepth);
        Assert.Single(loaded.Warnings);
        Assert.Contains("folderDepth", loaded.Warnings[0]);
        Assert.DoesNotContain("unusedDays", loaded.Warnings[0]);
    }

    [Fact]
    public void ConfigRoundingIsReportedWhenSavedFileIsCorrupt()
    {
        // A corrupt file is ignored entirely, so the config values are used and their rounding is reported.
        var configured = Cli(days: 99999, depth: -4);
        var loaded = GuiSettingsDefaults.Resolve(configured, "{ broken", false);
        Assert.Equal(GuiLimits.MaxUnusedDays, loaded.Settings.UnusedDays);
        Assert.Equal(GuiLimits.MinFolderDepth, loaded.Settings.FolderDepth);
        Assert.Equal(2, loaded.Warnings.Count);
        Assert.Contains("unusedDays", loaded.Warnings[1]);
        Assert.Contains("folderDepth", loaded.Warnings[1]);
    }

    [Fact]
    public void BlankEntriesInListsAreRejectedPerItem()
    {
        var loaded = GuiSettingsJson.Load("{\"schemaVersion\":1,\"targets\":[\"C:\\\\\",\"\"],\"excludes\":[null]}", Baseline() with { Targets = [@"D:\Keep"] });
        Assert.Equal(KeepTargets, loaded.Settings.Targets);
        Assert.Empty(loaded.Settings.Excludes);
        Assert.Single(loaded.Warnings);
        Assert.Contains("targets", loaded.Warnings[0]);
        Assert.Contains("excludes", loaded.Warnings[0]);
    }

    [Fact]
    public void UnknownKeysAreIgnored()
    {
        var loaded = GuiSettingsJson.Load("{\"schemaVersion\":1,\"future\":{\"x\":1},\"mode\":\"Files\"}", Baseline());
        Assert.Equal("Files", loaded.Settings.Mode);
        Assert.Empty(loaded.Warnings);
    }

    // --- GuiSettingsDefaults and priority ---

    [Fact]
    public void FromConfigUsesDefaultTargetsWhenNoneConfigured()
    {
        var settings = GuiSettingsDefaults.FromConfig(Cli(days: 20, depth: 12));
        Assert.Null(settings.Targets);
        Assert.Equal(20, settings.UnusedDays);
        Assert.Equal(GuiLimits.MaxFolderDepth, settings.FolderDepth);
    }

    [Fact]
    public void SavedValueBeatsConfigAndConfigBeatsDefault()
    {
        var configured = Cli(mode: "Apps", targets: [@"C:\Config"], days: 42);
        var saved = "{\"schemaVersion\":1,\"mode\":\"Files\"}";
        var result = GuiSettingsDefaults.Resolve(configured, saved, false).Settings;
        Assert.Equal("Files", result.Mode);
        Assert.Equal(ConfigTargets, result.Targets);
        Assert.Equal(42, result.UnusedDays);

        var noSave = GuiSettingsDefaults.Resolve(configured, null, false).Settings;
        Assert.Equal("Apps", noSave.Mode);
    }

    [Fact]
    public void ConfigWarningIsPassedThroughAndConfigValuesAreNotUsed()
    {
        // Config values are present but must not leak into the GUI when the config has a warning.
        var configured = Cli(mode: "Apps", targets: [@"C:\Config"], excludes: [@"C:\Config\Skip"], output: @"E:\Cfg", days: 42, depth: 7, warning: "未対応の構文があります（行 3）");
        var loaded = GuiSettingsDefaults.Resolve(configured, null, false);
        Assert.Single(loaded.Warnings);
        Assert.Contains("未対応の構文があります（行 3）", loaded.Warnings[0]);
        AssertSame(Baseline(), loaded.Settings);
    }

    [Fact]
    public void SettingsCompositionReadsTheGivenConfigFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "wininspector-test-" + Guid.NewGuid().ToString("N") + ".psd1");
        try
        {
            File.WriteAllText(path, "@{ Mode='Files'; UnusedDays=20 }");
            var applied = SettingsComposition.Apply(Cli(), [], path);
            Assert.Equal("Files", applied.Mode);
            Assert.Equal(20, applied.UnusedDays);
            var explicitWins = SettingsComposition.Apply(Cli(mode: "Apps"), ["-Mode", "Apps"], path);
            Assert.Equal("Apps", explicitWins.Mode);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // --- Validation ---

    private sealed class FakeProbe : IPathProbe
    {
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Network { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Reparse { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Drives { get; } = new(StringComparer.OrdinalIgnoreCase) { @"C:\", @"D:\" };
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public bool IsNetworkPath(string path) => WinPath.IsUnc(path) || Network.Contains(path);
        public bool IsReparsePoint(string path) => Reparse.Contains(path);
        public bool HasReparsePointOnPathChain(string path) => Reparse.Any(r => WinPath.IsWithin(path, r));
        public bool DriveExists(string path) => Drives.Contains(WinPath.Root(path) ?? string.Empty);
    }

    private static GuiValidationResult Validate(GuiSettings settings, FakeProbe probe, params string[] targets) =>
        GuiSettingsValidator.Validate(settings, targets.Length > 0 ? targets : settings.Targets ?? Array.Empty<string>(), probe);

    private static FakeProbe ProbeWith(params string[] existing)
    {
        var probe = new FakeProbe();
        foreach (var path in existing) probe.Directories.Add(path);
        return probe;
    }

    [Fact]
    public void FilesModeNeedsAtLeastOneTarget()
    {
        var result = Validate(Baseline() with { Mode = "Files" }, ProbeWith());
        Assert.Contains(result.Errors, issue => issue.Field == "targets");
    }

    [Fact]
    public void AppsModeAllowsNoTargets()
    {
        var result = Validate(Baseline() with { Mode = "Apps" }, ProbeWith());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void RejectsRelativeNetworkAndMissingTargets()
    {
        var probe = ProbeWith(@"C:\Exists");
        probe.Network.Add(@"Z:\Share");
        var result = Validate(Baseline(), probe, @"Users\relative", @"\\server\share", @"Z:\Share", @"C:\Missing", @"C:\Exists");
        var messages = result.Errors.Select(issue => issue.Message).ToArray();
        Assert.Equal(4, messages.Length);
        Assert.Contains(messages, m => m.Contains("ドライブ名から始まる"));
        Assert.Contains(messages, m => m.Contains("ネットワーク上の場所"));
        Assert.Contains(messages, m => m.Contains("見つかりません"));
    }

    [Fact]
    public void RejectsJunctionTargets()
    {
        var probe = ProbeWith(@"C:\Link");
        probe.Reparse.Add(@"C:\Link");
        var result = Validate(Baseline(), probe, @"C:\Link");
        Assert.Contains(result.Errors, issue => issue.Message.Contains("ジャンクション"));
    }

    [Fact]
    public void DuplicatesIgnoreCaseAndTrailingSeparator()
    {
        var probe = ProbeWith(@"C:\Data", @"c:\data\");
        var result = Validate(Baseline(), probe, @"C:\Data", @"c:\data\");
        Assert.Contains(result.Errors, issue => issue.Message.Contains("既に追加されています"));
    }

    [Fact]
    public void NestedTargetsAreANoteNotAnError()
    {
        var probe = ProbeWith(@"C:\Users", @"C:\Users\me");
        var result = Validate(Baseline(), probe, @"C:\Users", @"C:\Users\me");
        Assert.True(result.IsValid);
        Assert.Single(result.Notes);
        Assert.Contains("二重に集計", result.Notes[0]);
    }

    [Fact]
    public void OutputFolderRules()
    {
        var probe = ProbeWith(@"C:\Users");
        Assert.True(Validate(Baseline() with { OutputDir = null }, probe, @"C:\Users").IsValid);
        Assert.Contains(Validate(Baseline() with { OutputDir = @"\\server\out" }, probe, @"C:\Users").Errors, i => i.Field == "outputDir");
        Assert.Contains(Validate(Baseline() with { OutputDir = @"Z:\out" }, probe, @"C:\Users").Errors, i => i.Field == "outputDir");
        Assert.Contains(Validate(Baseline() with { OutputDir = @"out\relative" }, probe, @"C:\Users").Errors, i => i.Field == "outputDir");
    }

    [Fact]
    public void RangesAreEnforcedForDaysAndDepth()
    {
        var probe = ProbeWith(@"C:\Users");
        Assert.Contains(Validate(Baseline() with { UnusedDays = 0 }, probe, @"C:\Users").Errors, i => i.Field == "unusedDays");
        Assert.Contains(Validate(Baseline() with { UnusedDays = 3651 }, probe, @"C:\Users").Errors, i => i.Field == "unusedDays");
        Assert.Contains(Validate(Baseline() with { FolderDepth = 11 }, probe, @"C:\Users").Errors, i => i.Field == "folderDepth");
        Assert.True(Validate(Baseline() with { UnusedDays = 3650, FolderDepth = 0 }, probe, @"C:\Users").IsValid);
    }

    // --- Mapping and elevated arguments ---

    [Fact]
    public void ToCliOptionsForcesGuiRunFlags()
    {
        var settings = Baseline() with { Mode = "Files", UnusedDays = 30 };
        var options = GuiOptionsMapper.ToCliOptions(settings, Cli(warning: null) with { IncludeRawLastAccess = true, LowerPriority = true, Elevate = true }, [@"C:\Users"]);
        Assert.True(options.NoPause);
        Assert.False(options.Elevate);
        Assert.False(options.LowerPriority);
        Assert.True(options.IncludeRawLastAccess);
        Assert.Equal(30, options.UnusedDays);
        Assert.Equal(UsersTargets, options.Targets);
    }

    [Fact]
    public void ElevatedArgumentsRoundTripThroughTheCliParser()
    {
        var settings = new GuiSettings("Apps", [@"C:\Program Files (x86)", @"C:\ユーザー\me"], [@"C:\Temp Cache", @"D:\a b"], @"D:\out put", 400, 2, true, null);
        var arguments = GuiOptionsMapper.ToArguments(settings, settings.Targets!, @"D:\out put");
        Assert.Equal("-Gui", arguments[0]);
        Assert.Contains("-GuiAutoRun", arguments);

        var stripped = GuiOptionsMapper.StripGuiFlags(arguments);
        Assert.DoesNotContain("-Gui", stripped);
        var parsed = WinInspector.Core.Cli.CliParser.Parse(stripped);
        Assert.Equal("Apps", parsed.Mode);
        Assert.Equal(settings.Targets, parsed.Targets);
        Assert.Equal(settings.Excludes, parsed.Excludes);
        Assert.Equal(@"D:\out put", parsed.OutputDir);
        Assert.Equal(400, parsed.UnusedDays);
        Assert.Equal(2, parsed.FolderDepth);
        Assert.True(parsed.Elevate);
        Assert.True(GuiOptionsMapper.IsAutoRun(arguments));
    }

    [Fact]
    public void EmptyExcludesAreNotWritten()
    {
        var arguments = GuiOptionsMapper.ToArguments(Baseline(), [@"C:\Users"], @"C:\out");
        Assert.DoesNotContain("-Exclude", arguments);
    }

    [Fact]
    public void ExplicitArgumentsReplaceOnlyTheirOwnValues()
    {
        var saved = Baseline() with { Mode = "Apps", UnusedDays = 99 };
        var args = new[] { "-Gui", "-Mode", "Files" };
        var given = WinInspector.Core.Cli.CliParser.Parse(GuiOptionsMapper.StripGuiFlags(args));
        var applied = GuiOptionsMapper.ApplyArguments(saved, GuiOptionsMapper.StripGuiFlags(args), given);
        Assert.Equal("Files", applied.Mode);
        Assert.Equal(99, applied.UnusedDays);
    }

    // --- Window placement ---

    private static readonly WindowBounds PrimaryWorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void SavedWindowInsideTheWorkAreaIsUnchanged()
    {
        var fitted = WindowPlacement.FitToWorkArea(new WindowBounds(100, 80, 780, 860), PrimaryWorkArea, 640, 560);
        Assert.Equal(new WindowBounds(100, 80, 780, 860), fitted);
    }

    [Fact]
    public void WindowPastTheRightAndBottomEdgesIsMovedBackIn()
    {
        var fitted = WindowPlacement.FitToWorkArea(new WindowBounds(1500, 900, 780, 860), PrimaryWorkArea, 640, 560);
        Assert.Equal(new WindowBounds(1920 - 780, 1040 - 860, 780, 860), fitted);
    }

    [Fact]
    public void WindowPastTheLeftAndTopEdgesIsMovedToTheEdge()
    {
        var fitted = WindowPlacement.FitToWorkArea(new WindowBounds(-300, -50, 780, 860), PrimaryWorkArea, 640, 560);
        Assert.Equal(new WindowBounds(0, 0, 780, 860), fitted);
    }

    [Fact]
    public void WindowLargerThanTheWorkAreaShrinksToIt()
    {
        var fitted = WindowPlacement.FitToWorkArea(new WindowBounds(10, 10, 3000, 2000), PrimaryWorkArea, 640, 560);
        Assert.Equal(new WindowBounds(0, 0, 1920, 1040), fitted);
    }

    [Fact]
    public void WorkAreaSmallerThanTheMinimumWinsOverTheMinimum()
    {
        var fitted = WindowPlacement.FitToWorkArea(new WindowBounds(0, 0, 780, 860), new WindowBounds(0, 0, 500, 400), 640, 560);
        Assert.Equal(new WindowBounds(0, 0, 500, 400), fitted);
    }

    [Fact]
    public void SizeSmallerThanTheMinimumIsRaisedToIt()
    {
        var fitted = WindowPlacement.FitToWorkArea(new WindowBounds(50, 50, 300, 200), PrimaryWorkArea, 640, 560);
        Assert.Equal(new WindowBounds(50, 50, 640, 560), fitted);
        var corrupt = WindowPlacement.FitToWorkArea(new WindowBounds(10, 10, -5, -5), PrimaryWorkArea, 640, 560);
        Assert.Equal(new WindowBounds(10, 10, 640, 560), corrupt);
    }

    [Fact]
    public void SecondScreenWithNegativeCoordinatesIsRespected()
    {
        var secondScreen = new WindowBounds(-1920, 0, 1920, 1040);
        Assert.Equal(new WindowBounds(-1000, 100, 780, 860), WindowPlacement.FitToWorkArea(new WindowBounds(-1000, 100, 780, 860), secondScreen, 640, 560));
        Assert.Equal(new WindowBounds(-780, 100, 780, 860), WindowPlacement.FitToWorkArea(new WindowBounds(-100, 100, 780, 860), secondScreen, 640, 560));
    }

    [Fact]
    public void ExtremeSavedValuesDoNotOverflow()
    {
        var fitted = WindowPlacement.FitToWorkArea(new WindowBounds(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue), PrimaryWorkArea, 640, 560);
        Assert.Equal(new WindowBounds(0, 0, 1920, 1040), fitted);
        var negative = WindowPlacement.FitToWorkArea(new WindowBounds(int.MinValue, int.MinValue, 780, 860), PrimaryWorkArea, 640, 560);
        Assert.Equal(new WindowBounds(0, 0, 780, 860), negative);
    }

    // --- Startup, progress, defaults, throttle ---

    [Theory]
    [InlineData(new string[0], true, false, false, false, StartupKind.Gui)]
    [InlineData(new string[0], true, true, false, false, StartupKind.Cli)]
    [InlineData(new string[0], true, false, true, false, StartupKind.Cli)]
    [InlineData(new string[0], false, false, false, false, StartupKind.Cli)]
    [InlineData(new[] { "-Gui" }, false, true, true, true, StartupKind.Gui)]
    [InlineData(new[] { "-Gui", "-Help" }, true, false, false, false, StartupKind.Cli)]
    [InlineData(new[] { "-Mode", "Files" }, true, false, false, false, StartupKind.Cli)]
    [InlineData(new[] { "-version" }, true, false, false, false, StartupKind.Cli)]
    public void StartupDecisionCoversEveryCombination(string[] args, bool interactive, bool input, bool output, bool error, StartupKind expected)
    {
        Assert.Equal(expected, StartupMode.Decide(args, interactive, input, output, error));
    }

    [Fact]
    public void EmptyArgumentsFileIsCliEvenWhenInteractive()
    {
        // -ArgumentsFile with an empty file (elevated -Elevate-only relaunch) must not open the GUI.
        Assert.Equal(StartupKind.Cli, StartupMode.Decide([], true, false, false, false, fromArgumentsFile: true));
        Assert.Equal(StartupKind.Gui, StartupMode.Decide([], true, false, false, false));
        Assert.Equal(StartupKind.Gui, StartupMode.Decide(["-Gui", "-GuiAutoRun"], true, false, false, false, fromArgumentsFile: true));
    }

    [Fact]
    public void AutoRunWithoutGuiIsAnArgumentError()
    {
        Assert.True(GuiOptionsMapper.IsAutoRunWithoutGui(["-GuiAutoRun"]));
        Assert.True(GuiOptionsMapper.IsAutoRunWithoutGui(["-GuiAutoRun", "-Mode", "Files"]));
        Assert.False(GuiOptionsMapper.IsAutoRunWithoutGui(["-Gui", "-GuiAutoRun"]));
        Assert.False(GuiOptionsMapper.IsAutoRunWithoutGui(["-Mode", "Files"]));
    }

    [Fact]
    public void HelpAndVersionWinOverAutoRunWithoutGui()
    {
        Assert.False(GuiOptionsMapper.IsAutoRunWithoutGui(["-GuiAutoRun", "-Help"]));
        Assert.False(GuiOptionsMapper.IsAutoRunWithoutGui(["-GuiAutoRun", "-Version"]));
        Assert.Equal(StartupKind.Cli, StartupMode.Decide(["-GuiAutoRun", "-Help"], true, false, false, false));
        Assert.Equal(StartupKind.Cli, StartupMode.Decide(["-GuiAutoRun", "-Version"], true, false, false, false));
    }

    [Fact]
    public void ProgressTextUsesThousandsSeparators()
    {
        var text = InspectionProgressText.Format(new InspectionProgress(InspectionPhase.ScanningFiles, 12345, 0, 0, null, TimeSpan.Zero));
        Assert.Equal("ファイルを走査しています… 12,345 件", text);
    }

    [Fact]
    public void RunOutcomeFollowsSummaryPriority()
    {
        Assert.Equal(RunState.Interrupted, RunOutcome.From(true, 5));
        Assert.Equal(RunState.PartialErrors, RunOutcome.From(false, 1));
        Assert.Equal(RunState.Completed, RunOutcome.From(false, 0));
    }

    [Fact]
    public void DefaultTargetsKeepsExistingFoldersOnce()
    {
        var folders = new Dictionary<Environment.SpecialFolder, string?>
        {
            [Environment.SpecialFolder.UserProfile] = @"C:\Users\me",
            [Environment.SpecialFolder.ProgramFiles] = null,
            [Environment.SpecialFolder.ProgramFilesX86] = @"c:\users\ME",
        };
        var resolved = DefaultTargets.Resolve(folder => folders.GetValueOrDefault(folder), path => true);
        Assert.Equal(UserProfileTargets, resolved);
    }

    [Fact]
    public void ProgressThrottleAllowsOncePerInterval()
    {
        var now = TimeSpan.Zero;
        var throttle = new ProgressThrottle(TimeSpan.FromMilliseconds(250), () => now);
        Assert.True(throttle.IsDue());
        now = TimeSpan.FromMilliseconds(100);
        Assert.False(throttle.IsDue());
        now = TimeSpan.FromMilliseconds(300);
        Assert.True(throttle.IsDue());
    }

    private sealed class SmallFileSystem : IFileSystem
    {
        public IEnumerable<FsEntry> Enumerate(string dir)
        {
            if (dir != @"C:\root") return [];
            return
            [
                new(@"C:\root\a.txt", "a.txt", ".txt", 0, false, 10, DateTime.UnixEpoch, DateTime.UnixEpoch, DateTime.UnixEpoch),
                new(@"C:\root\sub", "sub", string.Empty, System.IO.FileAttributes.Directory, true, 0, DateTime.UnixEpoch, DateTime.UnixEpoch, DateTime.UnixEpoch),
            ];
        }
        public System.IO.FileAttributes? TryGetAttributes(string path) => null;
        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => true;
    }

    [Fact]
    public void ScannerProgressDoesNotChangeTheResult()
    {
        var calls = 0;
        var options = new ScanOptions([@"C:\root"], []);
        var plain = new FileScanner(new SmallFileSystem(), null, () => TimeSpan.Zero).Scan(options);
        var withProgress = new FileScanner(new SmallFileSystem(), null, () => TimeSpan.Zero).Scan(options with { OnProgress = (count, path) => calls++ });
        Assert.Equal(plain.Files.Select(f => f.Path), withProgress.Files.Select(f => f.Path));
        Assert.Equal(plain.Folders.Select(f => f.Path), withProgress.Folders.Select(f => f.Path));
        Assert.True(calls >= 1);
    }
}
