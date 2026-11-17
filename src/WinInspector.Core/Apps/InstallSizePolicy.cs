namespace WinInspector.Core.Apps;
public static class InstallSizePolicy
{
 public static long? EstimatedBytes(long? kilobytes)=>kilobytes is null or 0 or <0?null:checked(kilobytes.Value*1024);
 public static bool ShouldMeasure(string? installLocation,int matchingLocationCount)=>!string.IsNullOrWhiteSpace(installLocation)&&matchingLocationCount<=1&&!ExclusionRules.IsBroadRoot(installLocation);
}
