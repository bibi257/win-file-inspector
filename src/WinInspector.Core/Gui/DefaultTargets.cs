namespace WinInspector.Core.Gui;

/// <summary>The default target folders: user profile, Program Files, Program Files (x86), keeping those that exist.</summary>
public static class DefaultTargets
{
    private static readonly Environment.SpecialFolder[] Order =
    [
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolder.ProgramFiles,
        Environment.SpecialFolder.ProgramFilesX86,
    ];

    public static string[] Resolve(Func<Environment.SpecialFolder, string?> folderOf, Func<string, bool> directoryExists)
    {
        var result = new List<string>();
        foreach (var folder in Order)
        {
            var path = folderOf(folder);
            if (string.IsNullOrWhiteSpace(path) || !directoryExists(path)) continue;
            if (result.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(path);
        }
        return result.ToArray();
    }
}
