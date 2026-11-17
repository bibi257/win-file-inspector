using System.Globalization;
using System.Text;
using WinInspector.Core.Cli;
using WinInspector.Core.Output;
using WinInspector.Core.Scan;
using WinInspector.Windows.Registry;
using WinInspector.Windows.Fs;
using WinInspector.Core.Evidence;
using WinInspector.Core.Apps;
using WinInspector.Core.Settings;
using WinInspector.Windows.Prefetch;

namespace WinInspector.Cli;

internal sealed class Orchestrator
{
 private static readonly string[] FileHeaders=["Path","Type","SizeBytes","CreationTime","LastWriteTime","LastAccessTime","LastAccessStatus","Attributes","Extension"];
 private static readonly string[] FolderHeaders=["Path","Depth","DirectSizeBytes","TotalSizeBytes","FileCount","HadErrors"];
 private static readonly string[] ErrorHeaders=["Operation","ErrorType","Path","Message"];
 private static readonly string[] AppHeaders=["Source","Name","Version","Publisher","InstallDate","InstallDateSource","SizeBytes","SizeSource","InstallLocation","LastRunTime","LastRunSource","RunCount","MatchMethod","Status","ExcludedReason"];
 private static readonly string[] CandidateHeaders=["Source","Name","Version","Publisher","InstallDate","SizeBytes","InstallLocation","LastRunTime","LastRunSource","RunCount","MatchMethod","Status","ExcludedReason"];
 public static string Run(CliOptions options,CancellationToken cancellationToken=default)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("WinInspector.exe は Windows で実行してください。");
  var started=DateTime.Now;var output=Path.GetFullPath(options.OutputDir??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WinInspector","Logs"));try{ValidateLocalPath(output,true);}catch(Exception ex){throw new OutputPrecheckException(ex.Message,ex);}var run=Path.Combine(output,started.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture));try{OutputFileWriter.EnsureDirectory(run,output);ValidateLocalPath(run,true);}catch(Exception ex){throw new OutputPrecheckException(ex.Message,ex);}var stamp=Path.GetFileName(run);
  var targets=options.Targets.Length>0?options.Targets:DefaultTargets();var errors=new List<(string Path,string Operation,string Type,string Message)>();var files=new List<FileRow>();var folders=new List<FolderRow>();var interrupted=false;var unresolved=0;IReadOnlyList<(string Name,long? Size)>? candidatesA=null;IReadOnlyList<(string Name,long? Size)>? candidatesB=null;
  if(options.ConfigWarning is not null)errors.Add((Path.Combine(AppContext.BaseDirectory,"config","config.psd1"),"ReadConfig","ConfigurationWarning",options.ConfigWarning));
  var safeTargets=new List<string>();foreach(var target in targets){try{ValidateLocalPath(target,false);safeTargets.Add(target);}catch(Exception ex){errors.Add((target,"ValidateTarget",ex.GetType().Name,ex.Message));}}targets=safeTargets.ToArray();
  LastAccessSetting settingsLast;TrackingSetting settingsTrack;try{settingsLast=WindowsSettingsReader.LastAccess();}catch(Exception ex){settingsLast=new(false,null,"設定不明");errors.Add(("NtfsDisableLastAccessUpdate","ReadSettings",ex.GetType().Name,ex.Message));}try{settingsTrack=WindowsSettingsReader.Tracking();}catch(Exception ex){settingsTrack=new(false,null,"設定不明");errors.Add(("Start_TrackProgs","ReadSettings",ex.GetType().Name,ex.Message));}bool isAdmin;try{isAdmin=WindowsSettingsReader.IsAdministrator();}catch(Exception ex){isAdmin=false;errors.Add(("WindowsIdentity","IsAdministrator",ex.GetType().Name,ex.Message));}var networkShortcutCount=0;var shortcutReadFailures=0;
  if(options.Mode is "Files" or "All")
  {
   try{var scan=new FileScanner(new WindowsFileSystem(),new VolumeInfoProvider().FormatOf).Scan(new(targets,options.Excludes,options.FolderDepth,settingsLast.Enabled,output,files,folders,options.IncludeRemovable,options.IncludeRawLastAccess,settingsLast.Status),cancellationToken);errors.AddRange(scan.Errors.Select(e=>{var split=e.IndexOf(": ",StringComparison.Ordinal);return(split<0?e:e[..split],"Enumerate","IOException",split<0?e:e[(split+2)..]);}));}catch(OperationCanceledException){interrupted=true;}
   var fileHeaders=options.IncludeRawLastAccess?FileHeaders.Concat(["LastAccessTime_Raw"]).ToArray():FileHeaders;Write(run,Path.Combine(run,$"files_{stamp}.csv"),fileHeaders,files.Select(f=>CsvWriter.Row(new string?[]{f.Path,f.Type,N(f.SizeBytes),F(f.CreationTime),F(f.LastWriteTime),f.LastAccessTime is null?null:F(f.LastAccessTime.Value),f.LastAccessStatus,f.Attributes,f.Extension}.Concat(options.IncludeRawLastAccess?[f.LastAccessTimeRaw is null?null:F(f.LastAccessTimeRaw.Value)]:[]))));
   Write(run,Path.Combine(run,$"folders_{stamp}.csv"),FolderHeaders,folders.Select(f=>CsvWriter.Row([f.Path,N(f.Depth),N(f.DirectSizeBytes),N(f.TotalSizeBytes),N(f.FileCount),f.HadErrors.ToString()])));
  }
  var appxStatus="未実行";
  if(!interrupted && (options.Mode is "Apps" or "All"))
  {
   var apps=new List<(string Source,IReadOnlyDictionary<string,object?> Values)>();
   foreach(var item in UninstallRegistryReader.ReadAll((path,ex)=>errors.Add((path,"ReadUninstall",ex.GetType().Name,ex.Message)))){if(cancellationToken.IsCancellationRequested){interrupted=true;break;}apps.Add((item.Source,item.Values));}
   if(!interrupted)try{foreach(var x in global::WinInspector.Windows.Appx.AppxPackageReader.ReadCurrentUser())apps.Add(("Appx",new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase){{"DisplayName",x.Name},{"DisplayVersion",x.Version},{"Publisher",x.Publisher},{"InstallLocation",x.InstallLocation},{"SignatureKind",x.SignatureKind},{"IsFramework",x.IsFramework},{"NonRemovable",x.NonRemovable},{"IsResourcePackage",x.IsResourcePackage},{"PackageFamilyName",x.PackageFamilyName}}));appxStatus="取得済み（NonRemovable は false として扱います）";}
   catch(Exception ex){appxStatus="取得失敗（errors に記録）";errors.Add(("Appx","ReadPackages",ex.GetType().Name,ex.Message));}
   var known=new KnownFolderMap();var evidence=new List<UsageEvidence>();
   if(!interrupted)try{foreach(var raw in UserAssistRegistryReader.ReadAll()){if(cancellationToken.IsCancellationRequested){interrupted=true;break;}var parsed=UserAssistDecoder.Parse(raw.Data);if(parsed is null)continue;var decoded=known.Resolve(Rot13.Decode(raw.ValueName));if(decoded.Contains('{')&&System.Text.RegularExpressions.Regex.IsMatch(decoded,@"\{[0-9A-Fa-f-]{36}\}"))unresolved++;evidence.Add(new("UserAssist",decoded,null,parsed.RunCount,parsed.LastRunTime));}}catch(Exception ex){errors.Add(("UserAssist","ReadRegistry",ex.GetType().Name,ex.Message));}
   if(!interrupted&&isAdmin)try{foreach(var pf in PrefetchDirectoryReader.List())if(PrefetchNameParser.Parse(pf.FileName) is { } exe)evidence.Add(new("Prefetch",null,exe,null,pf.LastWriteTime));}catch(Exception ex){errors.Add(("Prefetch","Enumerate",ex.GetType().Name,ex.Message));}
   for(var i=0;i<evidence.Count&&!cancellationToken.IsCancellationRequested;i++)if(evidence[i].Source=="UserAssist"&&evidence[i].Path?.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)==true){var link=evidence[i];var target=global::WinInspector.Windows.Shell.ShellLinkTargetReader.TryReadTarget(link.Path!,()=>{shortcutReadFailures++;errors.Add((link.Path!,"ReadShortcut","ShortcutReadFailure","ショートカットのリンク先を読み取れませんでした"));});if(target is not null){if(target.StartsWith(@"\\",StringComparison.Ordinal)||target.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase))networkShortcutCount++;evidence[i]=link with{ExeName=target};}}
   if(cancellationToken.IsCancellationRequested)interrupted=true;
   var locations=apps.Select(a=>a.Values.TryGetValue("InstallLocation",out var x)?Convert.ToString(x,CultureInfo.InvariantCulture):null).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!).ToArray();var measurer=new InstallFolderMeasurer(new WindowsFileSystem(),p=>new VolumeInfoProvider().KindOf(p)==DriveKind.Network);
   var patterns=ReadNamePatterns();var rows=new List<string[]>();foreach(var a in apps){if(cancellationToken.IsCancellationRequested){interrupted=true;break;}try{rows.Add(BuildAppRow(a.Source,a.Values,options.UnusedDays,settingsTrack.Enabled,evidence,measurer,locations,errors,patterns));}catch(Exception ex){errors.Add((Convert.ToString(a.Values.GetValueOrDefault("InstallLocation"),CultureInfo.InvariantCulture)??"","BuildAppRow",ex.GetType().Name,ex.Message));}}
   candidatesA=rows.Where(r=>r[13]=="候補A").Select(r=>(r[1],long.TryParse(r[6],out var value)?(long?)value:null)).ToArray();candidatesB=rows.Where(r=>r[13]=="候補B").Select(r=>(r[1],long.TryParse(r[6],out var value)?(long?)value:null)).ToArray();
   Write(run,Path.Combine(run,$"apps_{stamp}.csv"),AppHeaders,rows.Select(x=>CsvWriter.Row(x,new HashSet<int>{1,2,3,4,5,8,10,12,13,14})));
   Write(run,Path.Combine(run,$"app_candidates_{stamp}.csv"),CandidateHeaders,rows.Where(r=>r[13] is "候補A" or "候補B").Select(x=>CsvWriter.Row([x[0],x[1],x[2],x[3],x[4],x[6],x[8],x[9],x[10],x[11],x[12],x[13],x[14]],new HashSet<int>{1,2,3,4,6,10,11,12})));
  }
  Write(run,Path.Combine(run,$"errors_{stamp}.csv"),ErrorHeaders,errors.Select(e=>CsvWriter.Row([e.Operation,e.Type,e.Path,e.Message],new HashSet<int>{0,2,3})));
  string account;try{account=System.Security.Principal.WindowsIdentity.GetCurrent().Name;}catch(Exception ex){account="取得失敗";errors.Add(("WindowsIdentity","GetCurrent",ex.GetType().Name,ex.Message));}
  var summary=SummaryBuilder.Build(new(started,options.Mode,targets,run,isAdmin,account,$"{settingsLast.Status} ({Hex(settingsLast.Value)}){(options.ConfigWarning is null?string.Empty:" / "+options.ConfigWarning)}",$"{settingsTrack.Status} / {Hex(settingsTrack.Value)}",TimeZoneInfo.Local.Id,options.Mode is "Files" or "All"?files.Count:0,options.Mode is "Files" or "All"?folders.Count:0,DateTime.Now-started,errors.Count,interrupted,files.Count,appxStatus,candidatesA,candidatesB,unresolved,options.UnusedDays,options.FolderDepth,options.Excludes,isAdmin&&(options.Mode is "Apps" or "All"),WindowsSettingsReader.LastAccessRegistryStatus(),networkShortcutCount,shortcutReadFailures));
  WriteText(run,Path.Combine(run,$"summary_{stamp}.txt"),summary);
  return run;
 }
 private static string[] DefaultTargets()=>new[]{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}.Where(p=>!string.IsNullOrWhiteSpace(p)&&Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
 private static string[] BuildAppRow(string source,IReadOnlyDictionary<string,object?> v,int unusedDays,bool tracking,IEnumerable<UsageEvidence> evidence,InstallFolderMeasurer measurer,string[] locations,List<(string Path,string Operation,string Type,string Message)> errors,System.Text.RegularExpressions.Regex[] patterns)
 {
  string? Get(string key)=>v.TryGetValue(key,out var value)?Convert.ToString(value,CultureInfo.InvariantCulture):null;
  var name=Get("DisplayName")??string.Empty;var rawDate=Get("InstallDate");
  long? size=null;if(long.TryParse(Get("EstimatedSize"),NumberStyles.Integer,CultureInfo.InvariantCulture,out var kb))size=WinInspector.Core.Apps.InstallSizePolicy.EstimatedBytes(kb);
  var record=new WinInspector.Core.Apps.AppRecord(source,name,Get("DisplayVersion"),Get("Publisher"),rawDate,size is null?null:(long?)(size/1024),Get("InstallLocation"),Get("DisplayIcon"),Get("UninstallString"),Get("SignatureKind"),Get("IsFramework")=="True",Get("NonRemovable")=="True",Get("IsResourcePackage")=="True",Get("PackageFamilyName"),Get("SystemComponent"),Get("ParentKeyName"),Get("ReleaseType"));
  var match=EvidenceMatcher.Match(record,evidence);var result=AppClassifier.Classify(record,match.LastRunTime,match.RunCount,match.CountOnly,rawDate,unusedDays,tracking,DateTime.Now,patterns);
  var sizeSource=size is null?"":"EstimatedSize";if(size is null&&!string.IsNullOrWhiteSpace(record.InstallLocation)){var m=measurer.Measure(record.InstallLocation,locations);size=m.SizeBytes;sizeSource=m.Source??"";if(m.Reason is not null&&m.Reason!="計測対象外のパス"&&m.Reason!="InstallLocation 重複")errors.Add((record.InstallLocation,"MeasureInstallFolder","IOException",m.Reason));}
  return [source,name,Get("DisplayVersion")??"",Get("Publisher")??"",rawDate??"",rawDate is null||source=="Appx"?"":"Registry",N(size),sizeSource,Get("InstallLocation")??"",match.LastRunTime is null?"":F(match.LastRunTime.Value),match.LastRunSource??"なし",match.RunCount?.ToString(CultureInfo.InvariantCulture)??"",match.Method??"",result.Status,result.Reason??""];
 }
 internal sealed class OutputPrecheckException(string message,Exception inner):Exception(message,inner);
 private static void ValidateLocalPath(string path,bool output){var full=Path.GetFullPath(path);if(full.StartsWith('\\'))throw new InvalidOperationException("ネットワークパスは使用できません");var root=Path.GetPathRoot(full);if(root is not null&&new DriveInfo(root).DriveType==DriveType.Network)throw new InvalidOperationException("ネットワークパスは使用できません");for(var current=full;!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current)??string.Empty){if(Directory.Exists(current)||File.Exists(current)){if(File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))throw new InvalidOperationException(output?"出力先または祖先にリパースポイントを含められません":"入力先がリパースポイントです");}if(string.Equals(current,root,StringComparison.OrdinalIgnoreCase))break;}}
 private static System.Text.RegularExpressions.Regex[] ReadNamePatterns(){var p=Path.Combine(AppContext.BaseDirectory,"config","exclusions.psd1");if(!File.Exists(p))p=Path.Combine(Environment.CurrentDirectory,"config","exclusions.psd1");string t;if(File.Exists(p))t=File.ReadAllText(p);else{using var stream=typeof(Orchestrator).Assembly.GetManifestResourceStream("WinInspector.Cli.config.exclusions.psd1");if(stream is null)return [];using var reader=new StreamReader(stream);t=reader.ReadToEnd();}return System.Text.RegularExpressions.Regex.Matches(t,"'([^']*)'").Select(m=>new System.Text.RegularExpressions.Regex(m.Groups[1].Value,System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant)).ToArray();}
 private static string Hex(uint? value)=>value is null?string.Empty:$"0x{value.Value:X8}";
 private static string N(long? value)=>value?.ToString(CultureInfo.InvariantCulture)??"";
 private static string N(int value)=>value.ToString(CultureInfo.InvariantCulture);
 private static string F(DateTime value)=>value.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);
 private static void Write(string outputRoot,string path,string[] headers,IEnumerable<string> rows){var content=string.Join("\r\n",new[]{CsvWriter.Row(headers)}.Concat(rows))+"\r\n";OutputFileWriter.Write(path,outputRoot,content);}
 private static void WriteText(string outputRoot,string path,string value){OutputFileWriter.Write(path,outputRoot,value);}
}
