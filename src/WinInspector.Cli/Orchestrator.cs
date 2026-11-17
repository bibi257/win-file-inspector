using System.Globalization;
using System.Text;
using WinInspector.Core.Cli;
using WinInspector.Core.Output;
using WinInspector.Core.Scan;
using WinInspector.Windows.Registry;
using WinInspector.Windows.Fs;
using WinInspector.Core.Evidence;
using WinInspector.Core.Apps;
using WinInspector.Windows.Prefetch;

namespace WinInspector.Cli;

internal sealed class Orchestrator
{
 private static readonly string[] FileHeaders=["Path","Type","SizeBytes","CreationTime","LastWriteTime","LastAccessTime","LastAccessStatus","Attributes","Extension"];
 private static readonly string[] FolderHeaders=["Path","Depth","DirectSizeBytes","TotalSizeBytes","FileCount","HadErrors"];
 private static readonly string[] ErrorHeaders=["Path","Operation","ErrorType","Message"];
 private static readonly string[] AppHeaders=["Source","Name","Version","Publisher","InstallDate","InstallDateSource","SizeBytes","SizeSource","InstallLocation","LastRunTime","LastRunSource","RunCount","MatchMethod","Status","ExcludedReason"];
 public static string Run(CliOptions options,CancellationToken cancellationToken=default)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("WinInspector.exe は Windows で実行してください。");
  var started=DateTime.Now;var output=options.OutputDir??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WinInspector","Logs");
  var run=Path.Combine(output,started.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture));Directory.CreateDirectory(run);var stamp=Path.GetFileName(run);
  var targets=options.Targets.Length>0?options.Targets:DefaultTargets();var errors=new List<(string Path,string Operation,string Type,string Message)>();var files=new List<FileRow>();var folders=new List<FolderRow>();var interrupted=false;var unresolved=0;
  if(options.ConfigWarning is not null)errors.Add((Path.Combine(AppContext.BaseDirectory,"config","config.psd1"),"ReadConfig","ConfigurationWarning",options.ConfigWarning));
  var settingsLast=WindowsSettingsReader.LastAccess();var settingsTrack=WindowsSettingsReader.Tracking();
  if(options.Mode is "Files" or "All")
  {
   try{var scan=new FileScanner(new WindowsFileSystem()).Scan(new(targets,options.Excludes,options.FolderDepth,settingsLast.Enabled,output),cancellationToken);files.AddRange(scan.Files);folders.AddRange(scan.Folders);errors.AddRange(scan.Errors.Select(e=>(e,"Enumerate","IOException",e)));}catch(OperationCanceledException){interrupted=true;}
   Write(Path.Combine(run,$"files_{stamp}.csv"),FileHeaders,files.Select(f=>CsvWriter.Row([f.Path,f.Type,N(f.SizeBytes),F(f.CreationTime),F(f.LastWriteTime),f.LastAccessTime is null?null:F(f.LastAccessTime.Value),f.LastAccessStatus,f.Attributes,f.Extension])));
   Write(Path.Combine(run,$"folders_{stamp}.csv"),FolderHeaders,folders.Select(f=>CsvWriter.Row([f.Path,N(f.Depth),N(f.DirectSizeBytes),N(f.TotalSizeBytes),N(f.FileCount),f.HadErrors.ToString()])));
  }
  var appxStatus="未実行";
  if(options.Mode is "Apps" or "All")
  {
   var apps=new List<(string Source,IReadOnlyDictionary<string,object?> Values)>();
   foreach(var item in UninstallRegistryReader.ReadAll((path,ex)=>errors.Add((path,"ReadUninstall",ex.GetType().Name,ex.Message))))apps.Add((item.Source,item.Values));
   try{foreach(var x in global::WinInspector.Windows.Appx.AppxPackageReader.ReadCurrentUser())apps.Add(("Appx",new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase){{"DisplayName",x.Name},{"DisplayVersion",x.Version},{"Publisher",x.Publisher},{"InstallLocation",x.InstallLocation},{"SignatureKind",x.SignatureKind},{"IsFramework",x.IsFramework},{"NonRemovable",x.NonRemovable},{"IsResourcePackage",x.IsResourcePackage},{"PackageFamilyName",x.PackageFamilyName}}));appxStatus="取得済み（NonRemovable は false として扱います）";}
   catch(Exception ex){appxStatus="取得失敗（errors に記録）";errors.Add(("Appx","ReadPackages",ex.GetType().Name,ex.Message));}
   var known=new KnownFolderMap();var evidence=new List<UsageEvidence>();
   foreach(var raw in UserAssistRegistryReader.ReadAll()){var parsed=UserAssistDecoder.Parse(raw.Data);if(parsed is null)continue;var decoded=known.Resolve(Rot13.Decode(raw.ValueName));if(decoded.Contains('{')&&System.Text.RegularExpressions.Regex.IsMatch(decoded,@"\{[0-9A-Fa-f-]{36}\}"))unresolved++;evidence.Add(new("UserAssist",decoded,null,parsed.RunCount,parsed.LastRunTime));}
   if(WindowsSettingsReader.IsAdministrator())foreach(var pf in PrefetchDirectoryReader.List())if(PrefetchNameParser.Parse(pf.FileName) is { } exe)evidence.Add(new("Prefetch",null,exe,null,pf.LastWriteTime));
   var locations=apps.Select(a=>a.Values.TryGetValue("InstallLocation",out var x)?Convert.ToString(x,CultureInfo.InvariantCulture):null).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!).ToArray();var measurer=new InstallFolderMeasurer(new WindowsFileSystem());
   var rows=apps.Select(a=>BuildAppRow(a.Source,a.Values,options.UnusedDays,settingsTrack.Enabled,evidence,measurer,locations,errors)).ToList();
   Write(Path.Combine(run,$"apps_{stamp}.csv"),AppHeaders,rows.Select(x=>CsvWriter.Row(x,new HashSet<int>{1,2,3,4,5,8,10,12,13,14})));
   Write(Path.Combine(run,$"app_candidates_{stamp}.csv"),AppHeaders,rows.Where(r=>r[13] is "候補A" or "候補B").Select(x=>CsvWriter.Row(x,new HashSet<int>{1,2,3,4,5,8,10,12,13,14})));
  }
  Write(Path.Combine(run,$"errors_{stamp}.csv"),ErrorHeaders,errors.Select(e=>CsvWriter.Row([e.Path,e.Operation,e.Type,e.Message],new HashSet<int>{0,3})));
  var summary=SummaryBuilder.Build(new(started,options.Mode,targets,run,WindowsSettingsReader.IsAdministrator(),Environment.UserName,$"{settingsLast.Status} ({settingsLast.Value?.ToString(CultureInfo.InvariantCulture)??string.Empty}){(options.ConfigWarning is null?string.Empty:" / "+options.ConfigWarning)}",$"{settingsTrack.Status} / {settingsTrack.Value?.ToString(CultureInfo.InvariantCulture)??string.Empty}",TimeZoneInfo.Local.Id,options.Mode is "Files" or "All"?files.Count:0,options.Mode is "Files" or "All"?folders.Count:0,DateTime.Now-started,errors.Count,interrupted,files.Count,appxStatus,options.Mode is "Apps" or "All"?[]:null,options.Mode is "Apps" or "All"?[]:null,unresolved));
  WriteText(Path.Combine(run,$"summary_{stamp}.txt"),summary);
  return run;
 }
 private static string[] DefaultTargets()=>new[]{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}.Where(p=>!string.IsNullOrWhiteSpace(p)&&Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
 private static string[] BuildAppRow(string source,IReadOnlyDictionary<string,object?> v,int unusedDays,bool tracking,IEnumerable<UsageEvidence> evidence,InstallFolderMeasurer measurer,string[] locations,List<(string Path,string Operation,string Type,string Message)> errors)
 {
  string? Get(string key)=>v.TryGetValue(key,out var value)?Convert.ToString(value,CultureInfo.InvariantCulture):null;
  var name=Get("DisplayName")??string.Empty;var rawDate=Get("InstallDate");
  long? size=null;if(long.TryParse(Get("EstimatedSize"),NumberStyles.Integer,CultureInfo.InvariantCulture,out var kb))size=WinInspector.Core.Apps.InstallSizePolicy.EstimatedBytes(kb);
  var record=new WinInspector.Core.Apps.AppRecord(source,name,Get("DisplayVersion"),Get("Publisher"),rawDate,size is null?null:(long?)(size/1024),Get("InstallLocation"),Get("DisplayIcon"),Get("UninstallString"),Get("SignatureKind"),Get("IsFramework")=="True",Get("NonRemovable")=="True",Get("IsResourcePackage")=="True",Get("PackageFamilyName"),Get("SystemComponent"),Get("ParentKeyName"),Get("ReleaseType"));
  var match=EvidenceMatcher.Match(record,evidence);var result=AppClassifier.Classify(record,match.LastRunTime,match.RunCount,match.CountOnly,rawDate,unusedDays,tracking,DateTime.Now);
  var sizeSource=size is null?"":"EstimatedSize";if(size is null&&!string.IsNullOrWhiteSpace(record.InstallLocation)){var m=measurer.Measure(record.InstallLocation,locations);size=m.SizeBytes;sizeSource=m.Source??"";if(m.Reason is not null&&m.Reason!="計測対象外のパス"&&m.Reason!="InstallLocation 重複")errors.Add((record.InstallLocation,"MeasureInstallFolder","IOException",m.Reason));}
  return [source,name,Get("DisplayVersion")??"",Get("Publisher")??"",rawDate??"","Registry",N(size),sizeSource,Get("InstallLocation")??"",match.LastRunTime is null?"":F(match.LastRunTime.Value),match.LastRunTime is null?"なし":match.Method=="PrefetchName"?"Prefetch":"UserAssist",match.RunCount?.ToString(CultureInfo.InvariantCulture)??"",match.Method??"",result.Status,result.Reason??""];
 }
 private static string N(long? value)=>value?.ToString(CultureInfo.InvariantCulture)??"";
 private static string N(int value)=>value.ToString(CultureInfo.InvariantCulture);
 private static string F(DateTime value)=>value.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);
 private static void Write(string path,string[] headers,IEnumerable<string> rows){OutputGuard.AssertWithin(path,Path.GetDirectoryName(path)!);var content=string.Join("\r\n",new[]{CsvWriter.Row(headers)}.Concat(rows))+"\r\n";File.WriteAllText(path,content,new UTF8Encoding(true));}
 private static void WriteText(string path,string value){OutputGuard.AssertWithin(path,Path.GetDirectoryName(path)!);File.WriteAllText(path,value,new UTF8Encoding(true));}
}
