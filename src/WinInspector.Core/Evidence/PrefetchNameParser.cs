using System.Text.RegularExpressions;
namespace WinInspector.Core.Evidence;
public static partial class PrefetchNameParser
{
    [GeneratedRegex("^(.+)-[0-9A-Fa-f]{8}\\.pf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex NameRegex();
    [GeneratedRegex("^(?:uninstall|unins|install).*\\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex GenericRegex();
    public static string? Parse(string fileName)
    {
        var match = NameRegex().Match(Path.GetFileName(fileName)); if (!match.Success) return null;
        var name = match.Groups[1].Value + ".exe";
        return GenericRegex().IsMatch(name) || name.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("update.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("setup.exe", StringComparison.OrdinalIgnoreCase) ? null : name;
    }
}
