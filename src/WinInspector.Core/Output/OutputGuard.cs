using WinInspector.Core.Paths;
namespace WinInspector.Core.Output;
public static class OutputGuard { public static void AssertWithin(string path,string outputDir){if(!WinPath.IsWithin(path,outputDir))throw new InvalidOperationException("出力先フォルダ外への書き込みを拒否しました");} }
