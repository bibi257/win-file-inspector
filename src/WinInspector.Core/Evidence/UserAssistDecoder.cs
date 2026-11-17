namespace WinInspector.Core.Evidence;
public sealed record UserAssistData(uint RunCount,DateTime? LastRunTime);
public static class UserAssistDecoder { public static UserAssistData? Parse(byte[]? data){if(data is null||data.Length<68)return null;var count=BitConverter.ToUInt32(data,4);var ft=BitConverter.ToInt64(data,60);DateTime? time=null;try{if(ft>0)time=DateTime.FromFileTime(ft);}catch(ArgumentOutOfRangeException){}return new(count,time);} }
