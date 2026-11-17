using Microsoft.Win32;
using WinInspector.Core.Settings;
namespace WinInspector.Windows.Registry;
public sealed class WindowsSettingsReader
{
 public static object? ReadTracking(){using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",false);return key?.GetValue("Start_TrackProgs");}
 public static object? ReadLastAccess(){using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem",false);return key?.GetValue("NtfsDisableLastAccessUpdate");}
 public static bool IsAdministrator(){using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);}
 public static TrackingSetting Tracking()=>TrackingRules.Interpret(ReadTracking());public static LastAccessSetting LastAccess()=>LastAccessRules.Interpret(ReadLastAccess());
}
