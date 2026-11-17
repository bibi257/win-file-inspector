using System.Globalization;
namespace WinInspector.Core.Output;
public sealed record SummaryData(DateTime Started,string Mode,string[] Targets,string OutputDir,bool IsAdministrator,string Account,string LastAccess,string Tracking,string TimeZone,int FileCount,int FolderCount,TimeSpan Elapsed,int ErrorCount,bool Interrupted=false,int InterruptedCount=0,string AppxStatus="未実行",IReadOnlyList<(string Name,long? Size)>? CandidatesA=null,IReadOnlyList<(string Name,long? Size)>? CandidatesB=null,int UnresolvedGuidCount=0);
public static class SummaryBuilder
{
    public static string Build(SummaryData d)
    {
        static string Top(IReadOnlyList<(string Name,long? Size)>? items)=>items is null?"未実行":string.Join("; ",items.OrderByDescending(x=>x.Size).Take(10).Select(x=>$"{x.Name} ({x.Size?.ToString(CultureInfo.InvariantCulture)??""} bytes)"));
        var appsOnly=d.Mode=="Apps";
        var lines=new[]{"WinInspector summary",$"実行日時: {d.Started:yyyy-MM-dd HH:mm:ss}",$"実行形式: WinInspector.exe（.NET {Environment.Version}）","モード: "+d.Mode,"対象: "+string.Join("; ",d.Targets),"出力先: "+d.OutputDir,"管理者: "+d.IsAdministrator,"実行アカウント: "+d.Account,"言語モード: 該当なし（exe 版）","最終アクセス設定: "+d.LastAccess,"タイムゾーン: "+d.TimeZone,"アプリ起動追跡(Start_TrackProgs): "+d.Tracking,$"ファイル件数: {(appsOnly?"未実行":d.FileCount.ToString(CultureInfo.InvariantCulture))}",$"フォルダ件数: {(appsOnly?"未実行":d.FolderCount.ToString(CultureInfo.InvariantCulture))}","所要時間: "+d.Elapsed,"候補A 上位: "+Top(d.CandidatesA),"候補B 上位: "+Top(d.CandidatesB),$"エラー件数: {d.ErrorCount}","Appx取得状態: "+(d.AppxStatus.StartsWith("取得済み",StringComparison.Ordinal)?"取得済み":d.AppxStatus),$"状態: {(d.Interrupted?$"中断（{d.InterruptedCount} 件まで）":d.ErrorCount>0?"一部エラー":"完了")}","",$"設定: モード={d.Mode}","制約: UserAssist は現在のユーザーの Explorer 経由の起動のみ記録します。追跡設定が無効なら履歴は残りません。OS 再インストールで履歴は消えます。Prefetch 名は29文字で切り詰められる場合があります。ネットワーク先またはリンク経由のショートカットは読み取りません。未変換の GUID 付きパスやハードリンクは個別に判定できない場合があります。",$"未変換の GUID 付きパス: {d.UnresolvedGuidCount}","最終アクセス時刻は有効な NTFS 設定の場合のみ記録します。"};
        return string.Join("\r\n",lines);
    }
}
