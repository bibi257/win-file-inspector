namespace WinInspector.Core.Settings;
public sealed record TrackingSetting(bool Enabled,uint? Value,string Status);
public static class TrackingRules { public static TrackingSetting Interpret(object? value){if(value is null)return new(false,null,"値なし（既定で有効の可能性）");try{var n=unchecked((uint)Convert.ToInt64(value,System.Globalization.CultureInfo.InvariantCulture));return new(n!=0,n,"取得済み");}catch(FormatException){return new(false,null,"値なし（既定で有効の可能性）");}catch(InvalidCastException){return new(false,null,"値なし（既定で有効の可能性）");}} }
