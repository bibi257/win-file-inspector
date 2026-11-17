namespace WinInspector.Windows.Prefetch;
public sealed class PrefetchDirectoryReader
{
 public static IEnumerable<(string FileName,DateTime LastWriteTime)> List(){var root=Environment.GetEnvironmentVariable("SystemRoot");if(string.IsNullOrEmpty(root))yield break;var dir=Path.Combine(root,"Prefetch");if(!Directory.Exists(dir))yield break;foreach(var file in Directory.EnumerateFiles(dir,"*.pf")){DateTime time;try{time=File.GetLastWriteTime(file);}catch{continue;}yield return(Path.GetFileName(file),time);}}
}
