using System.Text;
using System.Text.Json;

namespace WinInspector.Core.Gui;

/// <summary>
/// Result of reading the saved settings. PreserveOnSave == true means the file was written by
/// another schema version: it must not be overwritten when the window closes.
/// </summary>
public sealed record GuiLoadResult(GuiSettings Settings, IReadOnlyList<string> Warnings, bool PreserveOnSave);

/// <summary>
/// Reads and writes gui-settings.json. Parsing is done by hand on JsonDocument (no reflection) so that
/// each item can be checked on its own: a bad item falls back alone, and an absent item uses the baseline.
/// </summary>
public static class GuiSettingsJson
{
    public const int SchemaVersion = 1;
    public const int MaxBytes = 64 * 1024;

    private const string CorruptMessage = "保存された設定を読み込めなかったため、初期設定で開きました。次に保存するときに上書きします。";
    private const string OtherVersionMessage = "保存された設定は別の版で作られているため、初期設定で開きました。終了時には上書きしません（「検査を開始」を押すと上書きします）。";
    private static readonly string[] ModeNames = ["All", "Files", "Apps"];

    /// <param name="text">File text, or null when there is no saved file.</param>
    /// <param name="baseline">Values used for every item that is absent or invalid.</param>
    /// <param name="unreadable">True when the file exists but could not be read or is too large.</param>
    public static GuiLoadResult Load(string? text, GuiSettings baseline, bool unreadable = false)
    {
        if (unreadable) return Corrupt(baseline);
        if (text is null) return new GuiLoadResult(baseline, Array.Empty<string>(), false);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) return Corrupt(baseline);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException)
        {
            return Corrupt(baseline);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Corrupt(baseline);
            if (!root.TryGetProperty("schemaVersion", out var version) || !version.TryGetInt32(out var number) || number != SchemaVersion)
                return new GuiLoadResult(baseline, [OtherVersionMessage], true);

            var reset = new List<string>();
            var rounded = new List<string>();
            var mode = ReadMode(root, baseline.Mode, reset);
            var targets = ReadTargets(root, baseline.Targets, reset);
            var excludes = ReadExcludes(root, baseline.Excludes, reset);
            var outputDir = ReadOutputDir(root, baseline.OutputDir, reset);
            var unusedDays = ReadInt(root, "unusedDays", baseline.UnusedDays, GuiLimits.MinUnusedDays, GuiLimits.MaxUnusedDays, reset, rounded);
            var folderDepth = ReadInt(root, "folderDepth", baseline.FolderDepth, GuiLimits.MinFolderDepth, GuiLimits.MaxFolderDepth, reset, rounded);
            var runAsAdministrator = ReadBool(root, "runAsAdministrator", baseline.RunAsAdministrator, reset);
            var bounds = ReadBounds(root, baseline.WindowBounds, reset);

            var settings = new GuiSettings(mode, targets, excludes, outputDir, unusedDays, folderDepth, runAsAdministrator, bounds);
            var warnings = new List<string>();
            if (reset.Count > 0) warnings.Add("一部の設定を初期値に戻しました（" + string.Join("、", reset) + "）。");
            if (rounded.Count > 0) warnings.Add(RoundedMessage(rounded));
            return new GuiLoadResult(settings, warnings, false);
        }
    }

    public static string Serialize(GuiSettings settings)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("mode", settings.Mode);
            if (settings.Targets is null)
            {
                writer.WriteNull("targets");
            }
            else
            {
                writer.WriteStartArray("targets");
                foreach (var target in settings.Targets) writer.WriteStringValue(target);
                writer.WriteEndArray();
            }
            writer.WriteStartArray("excludes");
            foreach (var exclude in settings.Excludes) writer.WriteStringValue(exclude);
            writer.WriteEndArray();
            if (settings.OutputDir is null) writer.WriteNull("outputDir");
            else writer.WriteString("outputDir", settings.OutputDir);
            writer.WriteNumber("unusedDays", settings.UnusedDays);
            writer.WriteNumber("folderDepth", settings.FolderDepth);
            writer.WriteBoolean("runAsAdministrator", settings.RunAsAdministrator);
            if (settings.WindowBounds is null)
            {
                writer.WriteNull("windowBounds");
            }
            else
            {
                writer.WriteStartObject("windowBounds");
                writer.WriteNumber("x", settings.WindowBounds.X);
                writer.WriteNumber("y", settings.WindowBounds.Y);
                writer.WriteNumber("width", settings.WindowBounds.Width);
                writer.WriteNumber("height", settings.WindowBounds.Height);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Warning for numbers that were moved into the GUI range. Also used for config.psd1 values (GuiSettingsDefaults).</summary>
    public static string RoundedMessage(IReadOnlyList<string> names) =>
        "一部の数値を範囲内に丸めました（" + string.Join("、", names) + "）。";

    private static GuiLoadResult Corrupt(GuiSettings baseline) => new(baseline, [CorruptMessage], false);

    private static string ReadMode(JsonElement root, string fallback, List<string> reset)
    {
        if (!root.TryGetProperty("mode", out var value)) return fallback;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var match = text is null ? null : ModeNames.FirstOrDefault(name => name.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;
        reset.Add("mode");
        return fallback;
    }

    private static string[]? ReadTargets(JsonElement root, string[]? fallback, List<string> reset)
    {
        if (!root.TryGetProperty("targets", out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Null) return null;
        var list = ReadStringArray(value);
        if (list is not null) return list;
        reset.Add("targets");
        return fallback;
    }

    private static string[] ReadExcludes(JsonElement root, string[] fallback, List<string> reset)
    {
        if (!root.TryGetProperty("excludes", out var value)) return fallback;
        var list = ReadStringArray(value);
        if (list is not null) return list;
        reset.Add("excludes");
        return fallback;
    }

    private static string? ReadOutputDir(JsonElement root, string? fallback, List<string> reset)
    {
        if (!root.TryGetProperty("outputDir", out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (text is null || string.IsNullOrWhiteSpace(text)) return null;
            return text.Trim();
        }
        reset.Add("outputDir");
        return fallback;
    }

    private static int ReadInt(JsonElement root, string key, int fallback, int min, int max, List<string> reset, List<string> rounded)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            var clamped = Math.Clamp(number, min, max);
            if (clamped != number) rounded.Add(key);
            return clamped;
        }
        reset.Add(key);
        return fallback;
    }

    private static bool ReadBool(JsonElement root, string key, bool fallback, List<string> reset)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        reset.Add(key);
        return fallback;
    }

    private static WindowBounds? ReadBounds(JsonElement root, WindowBounds? fallback, List<string> reset)
    {
        if (!root.TryGetProperty("windowBounds", out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Object
            && TryInt(value, "x", out var x) && TryInt(value, "y", out var y)
            && TryInt(value, "width", out var width) && TryInt(value, "height", out var height)
            && width > 0 && height > 0)
        {
            return new WindowBounds(x, y, width, height);
        }
        reset.Add("windowBounds");
        return fallback;
    }

    private static bool TryInt(JsonElement obj, string key, out int number)
    {
        number = 0;
        return obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number);
    }

    /// <summary>A JSON array of non-blank strings, or null when the value has any other shape.</summary>
    private static string[]? ReadStringArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return null;
            var text = item.GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            list.Add(text.Trim());
        }
        return list.ToArray();
    }
}
