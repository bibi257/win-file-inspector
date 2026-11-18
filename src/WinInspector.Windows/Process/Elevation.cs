using System.Diagnostics;
namespace WinInspector.Windows.Process;
public static class Elevation
{
 public static string[] ReadArguments(string path){var full=Path.GetFullPath(path);var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("wininspector-args-",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("ArgumentsFile は TEMP 配下の wininspector-args- ファイルに限ります");try{return File.ReadAllLines(full);}finally{if(File.Exists(full))new FileInfo(full).Delete();}}
 /// <summary>
 /// Starts this executable elevated with the arguments passed through an argument file.
 /// waitForExit=true (the CLI path): waits, returns the child's exit code, and drops -Elevate from the arguments.
 /// waitForExit=false (the GUI path): returns 0 once the child has started. The argument file is kept, because the
 /// child reads and deletes it; it is deleted here only when the start fails.
 /// </summary>
 public static int Relaunch(string[] args,bool waitForExit=true)
 {
  if(args.Any(a=>a.Contains('\r')||a.Contains('\n')))throw new InvalidOperationException("改行を含む引数は指定できません");
  var path=Path.Combine(Path.GetTempPath(),"wininspector-args-"+Guid.NewGuid().ToString("N")+".txt");
  var forwarded=waitForExit?args.Where(a=>!a.Equals("-Elevate",StringComparison.OrdinalIgnoreCase)).ToArray():args;
  var info=new ProcessStartInfo{FileName=Environment.ProcessPath??throw new InvalidOperationException("実行ファイルのパスを取得できません"),Verb="runas",UseShellExecute=true};
  using(var writer=new StreamWriter(path,false,new System.Text.UTF8Encoding(false)))foreach(var argument in forwarded)writer.WriteLine(argument);
  info.Arguments="-ArgumentsFile \""+path.Replace("\"","\\\"",StringComparison.Ordinal)+"\"";
  var keepArgumentsFile=false;
  try
  {
   using var child=System.Diagnostics.Process.Start(info)??throw new InvalidOperationException("昇格プロセスを起動できません");
   if(!waitForExit){keepArgumentsFile=true;return 0;}
   child.WaitForExit();return child.ExitCode;
  }
  catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223){throw;}
  catch(Exception ex){throw new InvalidOperationException("昇格プロセスを起動できません: "+ex.Message,ex);}
  finally{if(!keepArgumentsFile&&File.Exists(path))new FileInfo(path).Delete();}
 }
}
