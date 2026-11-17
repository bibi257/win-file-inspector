using Microsoft.Win32;
namespace WinInspector.Windows.Registry;
public sealed class UserAssistRegistryReader
{
    private const string Root=@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";
    public static IEnumerable<(string ValueName,byte[] Data)> ReadAll()
    {
        using var baseKey=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Root,false);if(baseKey is null)yield break;
        foreach(var guid in baseKey.GetSubKeyNames()){using var key=baseKey.OpenSubKey(guid+@"\Count",false);if(key is null)continue;foreach(var name in key.GetValueNames())if(key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) is byte[] data)yield return(name,data);}
    }
}
