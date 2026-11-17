using System.Globalization;
using System.Text.RegularExpressions;
namespace WinInspector.Core.Apps;
public sealed record Classification(string Status,string? Reason);
public static class AppClassifier
{
 public static string Classify(DateTime? lastRun,DateTime? installDate,int unusedDays,bool trackingEnabled)=>Classify(null,lastRun,null,false,installDate?.ToString("yyyyMMdd",CultureInfo.InvariantCulture),unusedDays,trackingEnabled,DateTime.Now).Status;
 public static Classification Classify(AppRecord? app,DateTime? lastRun,uint? runCount,bool countOnly,string? installDate,int unusedDays,bool trackingEnabled,DateTime now,IEnumerable<Regex>? patterns=null)
 {
  if(app is not null){var reason=ExclusionRules.Reason(app,patterns);if(reason is not null)return new("対象外",reason);}
  if(countOnly)return new("判定不能","実行回数のみの記録あり");
  if(lastRun.HasValue)return lastRun.Value<=now.AddDays(-unusedDays)?new("候補A",null):new("判定不能","使用中（最終実行が閾値以内）");
  if(runCount.GetValueOrDefault()>0)return new("判定不能","証拠あり・時刻なし（実行回数の記録のみ）");
  if(!trackingEnabled)return new("判定不能","アプリ起動追跡が無効または不明");
  if(installDate is not null&&Regex.IsMatch(installDate,@"^\d{8}$")&&DateTime.TryParseExact(installDate,"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var installed)&&installed<=now.Date.AddDays(-unusedDays))return new("候補B",null);
  return new("判定不能","実行記録または有効なインストール日なし");
 }
}
