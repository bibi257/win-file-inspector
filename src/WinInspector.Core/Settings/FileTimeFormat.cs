namespace WinInspector.Core.Settings;
public static class FileTimeFormat
{
    public static DateTime? Convert(object? value)
    {
        try { var ticks = System.Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture); return ticks <= 0 ? null : DateTime.FromFileTime(ticks); }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException or InvalidCastException or OverflowException) { return null; }
    }
}
