using System.Text;
namespace WinInspector.Core.Output;
public static class OutputFileWriter
{
    public static void EnsureDirectory(string path,string outputRoot)
    {
        OutputGuard.AssertWithin(path,outputRoot);
#pragma warning disable RS0030
        Directory.CreateDirectory(path);
#pragma warning restore RS0030
    }
    public static void Write(string path,string outputDir,string content)
    {
        OutputGuard.AssertWithin(path,outputDir);
#pragma warning disable RS0030
        File.WriteAllText(path,content,new UTF8Encoding(true));
#pragma warning restore RS0030
    }
}
