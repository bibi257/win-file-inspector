using WinInspector.Core.Cli;
using WinInspector.Core.Settings;
using WinInspector.Windows.Process;
namespace WinInspector.Cli;
internal static class Program
{
 private static int Main(string[] args)
 {
  using var cancellation=new CancellationTokenSource();var cancelCount=0;
  Console.CancelKeyPress+=(sender,eventArgs)=>{if(Interlocked.Increment(ref cancelCount)==1){eventArgs.Cancel=true;cancellation.Cancel();}};
  try
  {
   if(args.Length==2&&args[0].Equals("-ArgumentsFile",StringComparison.OrdinalIgnoreCase))args=Elevation.ReadArguments(args[1]);
   var options=CliParser.Parse(args);
   options=SettingsComposition.Apply(options,args);
   if(options.ShowHelp){Console.WriteLine("WinInspector.exe [-Mode Files|Apps|All] [-Target path] [-Exclude path] [-OutputDir path] [-UnusedDays n] [-FolderDepth n] [-NoPause] [-LowerPriority]");return 0;}
   if(options.ShowVersion){Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString()??"0.0.0");return 0;}
   if(options.LowerPriority)System.Diagnostics.Process.GetCurrentProcess().PriorityClass=System.Diagnostics.ProcessPriorityClass.BelowNormal;
   if(options.Elevate){try{return Elevation.Relaunch(args);}catch(InvalidOperationException ex){Console.Error.WriteLine(ex.Message);return 2;}catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223){Console.Error.WriteLine("ユーザーが昇格をキャンセルしました。");return 2;}}
   var run=Orchestrator.Run(options,cancellation.Token);
   Console.WriteLine($"出力先: {run}");
   if(!options.NoPause&&!Console.IsInputRedirected&&args.Length==0){Console.WriteLine("Enter キーで終了");Console.ReadLine();}
   return 0;
  }
  catch(ArgumentException ex){Console.Error.WriteLine(ex.Message);return 1;}
  catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
 }
}
