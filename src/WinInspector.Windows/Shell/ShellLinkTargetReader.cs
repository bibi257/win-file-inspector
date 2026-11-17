using System.Runtime.InteropServices;
namespace WinInspector.Windows.Shell;
[ComImport,Guid("00021401-0000-0000-C000-000000000046")] internal class ShellLinkCom {}
[ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
 void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file,int max,IntPtr data,uint flags);
 void GetIDList(out IntPtr item);void SetIDList(IntPtr item);void GetDescription(System.Text.StringBuilder name,int max);void SetDescription(string name);void GetWorkingDirectory(System.Text.StringBuilder dir,int max);void SetWorkingDirectory(string dir);void GetArguments(System.Text.StringBuilder args,int max);void SetArguments(string args);void GetHotkey(out short hotkey);void SetHotkey(short hotkey);void GetShowCmd(out int command);void SetShowCmd(int command);void GetIconLocation(System.Text.StringBuilder icon,int max,out int index);void SetIconLocation(string icon,int index);void SetRelativePath(string path,uint reserved);void Resolve(IntPtr window,uint flags);void SetPath(string path);
}
public sealed class ShellLinkTargetReader
{
 public static string? TryReadTarget(string lnkPath)
 {
  object? instance=null;
  try{instance=new ShellLinkCom();var shell=(IShellLinkW)instance;((IPersistFile)instance).Load(lnkPath,0);var path=new System.Text.StringBuilder(32768);shell.GetPath(path,path.Capacity,IntPtr.Zero,4);return path.Length==0?null:path.ToString();}
  catch{return null;}finally{if(instance is not null&&Marshal.IsComObject(instance))Marshal.ReleaseComObject(instance);}
 }
}
[ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("0000010B-0000-0000-C000-000000000046")]
internal interface IPersistFile{void GetClassID(out Guid classId);void IsDirty();void Load([MarshalAs(UnmanagedType.LPWStr)]string file,uint mode);void Save([MarshalAs(UnmanagedType.LPWStr)]string file,bool remember);void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)]string file);void GetCurFile([MarshalAs(UnmanagedType.LPWStr)]out string file);}
