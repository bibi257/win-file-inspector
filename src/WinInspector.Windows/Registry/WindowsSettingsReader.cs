using Microsoft.Win32;
using WinInspector.Core.Settings;
namespace WinInspector.Windows.Registry;
public sealed class WindowsSettingsReader
{
 public static object? ReadTracking(){using var baseKey=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Default);using var key=baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",false);return key?.GetValue("Start_TrackProgs");}
 public static object? ReadLastAccess(){using var baseKey=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var key=baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem",false);return key?.GetValue("NtfsDisableLastAccessUpdate");}
 public static bool IsAdministrator(){using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);}
 public static string LastAccessRegistryStatus(){try{using var baseKey=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var key=baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem",false);return "取得済み";}catch{return "取得失敗";}}
 public static TrackingSetting Tracking()=>TrackingRules.Interpret(ReadTracking());public static LastAccessSetting LastAccess()=>LastAccessRules.Interpret(ReadLastAccess());
}
