namespace WinInspector.Core.Settings;
public sealed record LastAccessSetting(bool Enabled,uint? Value,string Status);
public static class LastAccessRules { public static LastAccessSetting Interpret(object? value){if(value is null)return new(false,null,"設定不明");var n=unchecked((uint)Convert.ToInt64(value,System.Globalization.CultureInfo.InvariantCulture));return n switch{0=>new(true,n,"有効"),1=>new(false,n,"更新無効(NtfsDisableLastAccessUpdate=0x00000001)"),0x80000002=>new(true,n,"有効"),0x80000003=>new(false,n,"更新無効(NtfsDisableLastAccessUpdate=0x80000003)"),_=>new(false,n,"設定不明")};} }
