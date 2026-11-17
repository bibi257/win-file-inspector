namespace WinInspector.Core.Output;
public static class CsvWriter
{
 public static string Cell(string? value,bool protect=false){var v=value??"";if(protect&&v.Length>0&&"=+-@\t\r\n".Contains(v[0]))v="'"+v;return "\""+v.Replace("\"","\"\"")+"\"";}
 public static string Row(IEnumerable<string?> values,IReadOnlySet<int>? protectedColumns=null)=>string.Join(',',values.Select((v,i)=>Cell(v,protectedColumns?.Contains(i)==true)));
}
