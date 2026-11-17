using Microsoft.Win32;
namespace WinInspector.Windows.Registry;
public sealed record RawUninstallEntry(string Source,IReadOnlyDictionary<string,object?> Values);
public sealed class UninstallRegistryReader
{
    private static readonly string[] ValueNames=["DisplayName","DisplayVersion","Publisher","InstallDate","EstimatedSize","InstallLocation","DisplayIcon","UninstallString","SystemComponent","ParentKeyName","ReleaseType","WindowsInstaller"];
    public static IEnumerable<RawUninstallEntry> ReadAll(Action<string,Exception>? onError=null)
    {
        foreach(var (hive,view,source) in new[]{(RegistryHive.LocalMachine,RegistryView.Registry64,"HKLM64"),(RegistryHive.LocalMachine,RegistryView.Registry32,"HKLM32"),(RegistryHive.CurrentUser,RegistryView.Default,"HKCU")})
        {
            using var baseKey=RegistryKey.OpenBaseKey(hive,view);using var root=baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall",false);if(root is null)continue;
            foreach(var name in root.GetSubKeyNames())
            {
                RawUninstallEntry? entry=null;
                try{using var key=root.OpenSubKey(name,false);if(key is not null){var values=ValueNames.ToDictionary(n=>n,n=>key.GetValue(n,null,RegistryValueOptions.DoNotExpandEnvironmentNames),StringComparer.OrdinalIgnoreCase);entry=new(source,values);}}
                catch(Exception ex){onError?.Invoke(name,ex);}
                if(entry is not null)yield return entry;
            }
        }
    }
}
