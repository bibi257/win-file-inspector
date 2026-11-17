namespace WinInspector.Core.Evidence;

public interface IKnownFolderMap { string? PathOf(string knownFolderGuid); }

public sealed class KnownFolderMap : IKnownFolderMap
{
    public static IReadOnlyCollection<string> KnownGuids => Paths.Keys;
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{6D809377-6AF0-444B-8957-A3773F02200E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ["{F38BF404-1D43-42F2-9305-67DE0B28FC23}"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ["{F7F1ED05-9F6D-47A2-AAAE-29D317C6F066}"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
        ["{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"] = Environment.GetFolderPath(Environment.SpecialFolder.System),
        ["{DE974D24-D9C6-4D3E-BF91-F4455120B917}"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
        ["{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}"] = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)
    };
    public string? PathOf(string knownFolderGuid) => Paths.TryGetValue(knownFolderGuid.Trim('{', '}').Insert(0, "{").Insert(37, "}"), out var path) && path.Length > 0 ? path : null;
    public string Resolve(string path) => System.Text.RegularExpressions.Regex.Replace(path,@"\{([0-9A-Fa-f-]{36})\}",m=>PathOf(m.Value) is {Length:>0} value?value:m.Value);
}
