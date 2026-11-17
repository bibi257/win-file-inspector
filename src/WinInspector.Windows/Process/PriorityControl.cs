namespace WinInspector.Windows.Process;
public static class PriorityControl { public static void Lower(){System.Diagnostics.Process.GetCurrentProcess().PriorityClass=System.Diagnostics.ProcessPriorityClass.BelowNormal;} }
