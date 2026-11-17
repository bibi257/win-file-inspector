using System.Diagnostics;
using System.Text.Json;
namespace WinInspector.Windows.Process;
public static class Elevation
{
 public static string[] ReadArguments(string path){var full=Path.GetFullPath(path);var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("wininspector-args-",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("ArgumentsFile は TEMP 配下の wininspector-args- ファイルに限ります");try{return JsonSerializer.Deserialize<string[]>(File.ReadAllText(full))??[];}finally{if(File.Exists(full))File.Delete(full);}}
 public static int Relaunch(string[] args)
 {
  var path=Path.Combine(Path.GetTempPath(),"wininspector-args-"+Guid.NewGuid().ToString("N")+".json");File.WriteAllText(path,JsonSerializer.Serialize(args.Where(a=>!a.Equals("-Elevate",StringComparison.OrdinalIgnoreCase)).ToArray()));
  if(args.Any(a=>a.Contains('\r')||a.Contains('\n')))throw new InvalidOperationException("改行を含む引数は指定できません");
  var info=new ProcessStartInfo{FileName=Environment.ProcessPath??throw new InvalidOperationException("実行ファイルのパスを取得できません"),Verb="runas",UseShellExecute=true};
  info.ArgumentList.Add("--elevated-args");info.ArgumentList.Add(path);
  try{using var child=System.Diagnostics.Process.Start(info)??throw new InvalidOperationException("昇格プロセスを起動できません");child.WaitForExit();return child.ExitCode;}catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223){throw;}catch(Exception ex){throw new InvalidOperationException("昇格プロセスを起動できません: "+ex.Message,ex);}finally{if(File.Exists(path))File.Delete(path);}
 }
}
