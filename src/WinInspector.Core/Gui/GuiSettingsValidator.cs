using WinInspector.Core.Paths;

namespace WinInspector.Core.Gui;

public sealed record GuiIssue(string Field, string Message);

/// <summary>Errors block the run. Notes are shown but do not block (for example, nested target folders).</summary>
public sealed record GuiValidationResult(IReadOnlyList<GuiIssue> Errors, IReadOnlyList<string> Notes)
{
    public bool IsValid => Errors.Count == 0;
}

public static class GuiSettingsValidator
{
    /// <param name="settings">Values from the screen.</param>
    /// <param name="effectiveTargets">Targets to check: the screen's list, or the default folders when the list is default.</param>
    /// <param name="probe">File-system checks, injected.</param>
    public static GuiValidationResult Validate(GuiSettings settings, IReadOnlyList<string> effectiveTargets, IPathProbe probe)
    {
        var errors = new List<GuiIssue>();
        var notes = new List<string>();

        if (settings.Mode is not ("All" or "Files" or "Apps"))
            errors.Add(new GuiIssue("mode", "モードは「すべて」「ファイルのみ」「アプリのみ」のいずれかです。"));
        if (settings.Mode is not "Apps" && effectiveTargets.Count == 0)
            errors.Add(new GuiIssue("targets", "対象フォルダを 1 つ以上追加してください。"));

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var valid = new List<string>();
        foreach (var target in effectiveTargets)
        {
            var problem = FolderProblem(target, probe);
            if (problem is null && !probe.DirectoryExists(target))
                problem = $"「{target}」が見つかりません。削除するか、別のフォルダを選んでください。";
            if (problem is null && probe.IsReparsePoint(target))
                problem = $"「{target}」はリンク（ジャンクションなど）のため検査できません。リンク先のフォルダを直接追加してください。";
            if (problem is not null)
            {
                errors.Add(new GuiIssue("targets", problem));
                continue;
            }
            if (!keys.Add(PathRules.Key(target)))
            {
                errors.Add(new GuiIssue("targets", $"「{target}」は既に追加されています。"));
                continue;
            }
            valid.Add(target);
        }

        foreach (var child in valid)
        {
            foreach (var parent in valid)
            {
                if (!PathRules.SameFolder(child, parent) && WinPath.IsWithin(child, parent))
                    notes.Add($"「{child}」は「{parent}」に含まれるため、二重に集計されます。");
            }
        }

        foreach (var exclude in settings.Excludes)
        {
            var problem = FolderProblem(exclude, probe);
            if (problem is not null) errors.Add(new GuiIssue("excludes", problem));
        }

        if (!string.IsNullOrWhiteSpace(settings.OutputDir))
        {
            var output = settings.OutputDir;
            if (WinPath.IsUnc(output) || probe.IsNetworkPath(output))
                errors.Add(new GuiIssue("outputDir", "出力先にネットワーク上の場所は使えません。"));
            else if (!PathRules.IsDriveAbsolute(output))
                errors.Add(new GuiIssue("outputDir", "出力先はドライブ名から始まるパスで指定してください。"));
            else if (!probe.DriveExists(output))
                errors.Add(new GuiIssue("outputDir", "出力先のドライブが見つかりません。"));
            else if (probe.HasReparsePointOnPathChain(output))
                errors.Add(new GuiIssue("outputDir", "出力先または上位のフォルダにリンク（ジャンクションなど）があるため使えません。"));
        }

        if (settings.UnusedDays < GuiLimits.MinUnusedDays || settings.UnusedDays > GuiLimits.MaxUnusedDays)
            errors.Add(new GuiIssue("unusedDays", $"未使用と判定する日数は {GuiLimits.MinUnusedDays}〜{GuiLimits.MaxUnusedDays} の整数で指定してください。"));
        if (settings.FolderDepth < GuiLimits.MinFolderDepth || settings.FolderDepth > GuiLimits.MaxFolderDepth)
            errors.Add(new GuiIssue("folderDepth", $"フォルダ集計の深さは {GuiLimits.MinFolderDepth}〜{GuiLimits.MaxFolderDepth} の整数で指定してください。"));

        return new GuiValidationResult(errors, notes);
    }

    /// <summary>Checks that apply to every folder path: non-blank, drive-letter absolute, not a network location.</summary>
    private static string? FolderProblem(string path, IPathProbe probe)
    {
        if (string.IsNullOrWhiteSpace(path)) return "空の値は指定できません。";
        if (WinPath.IsUnc(path) || probe.IsNetworkPath(path)) return $"「{path}」はネットワーク上の場所のため指定できません。";
        if (!PathRules.IsDriveAbsolute(path)) return $"「{path}」はドライブ名から始まるフォルダを指定してください。";
        return null;
    }
}
