using System.IO;
namespace WinInspector.Core.Scan;
public sealed record FsEntry(string FullPath,string Name,string Extension,FileAttributes Attributes,bool IsDirectory,long Length,DateTime CreationTime,DateTime LastWriteTime,DateTime LastAccessTime);
public interface IFileSystem { IEnumerable<FsEntry> Enumerate(string dir); FileAttributes? TryGetAttributes(string path); bool DirectoryExists(string path); bool FileExists(string path); }
public sealed record ScanOptions(IReadOnlyList<string> Targets,IReadOnlyList<string> Exclude,int FolderDepth=3,bool LastAccessEnabled=false,string? OutputDir=null);
public sealed record FileRow(string Path,string Type,long? SizeBytes,DateTime CreationTime,DateTime LastWriteTime,DateTime? LastAccessTime,string LastAccessStatus,string Attributes,string Extension);
public sealed record FolderRow(string Path,int Depth,long DirectSizeBytes,long TotalSizeBytes,long FileCount,bool HadErrors);
public sealed record ScanResult(IReadOnlyList<FileRow> Files,IReadOnlyList<FolderRow> Folders,IReadOnlyList<string> Errors);
public sealed class FileScanner(IFileSystem fs)
{
 private sealed class Frame(string path,int depth){public string Path{get;}=path;public int Depth{get;}=depth;public List<FsEntry> Entries{get;set;}=[];public int Index{get;set;}public long Direct{get;set;}public long Total{get;set;}public long Count{get;set;}public bool HadErrors{get;set;}}
 public ScanResult Scan(ScanOptions options,CancellationToken cancellationToken=default){var files=new List<FileRow>();var folders=new List<FolderRow>();var errors=new List<string>();var excludes=options.Exclude.Concat(options.OutputDir is null?[]:[options.OutputDir]).ToArray();
 foreach(var target in options.Targets){if(excludes.Any(x=>WinInspector.Core.Paths.WinPath.IsWithin(target,x)))continue;var stack=new Stack<Frame>();stack.Push(new(target,0));
 while(stack.Count>0){cancellationToken.ThrowIfCancellationRequested();var f=stack.Peek();if(f.Index==0&&f.Entries.Count==0){try{f.Entries=fs.Enumerate(f.Path).ToList();}catch(Exception ex){errors.Add($"{f.Path}: {ex.Message}");stack.Pop();if(stack.TryPeek(out var p))p.HadErrors=true;continue;}}
 if(f.Index<f.Entries.Count){var e=f.Entries[f.Index++];if(excludes.Any(x=>WinInspector.Core.Paths.WinPath.IsWithin(e.FullPath,x)))continue;if((e.Attributes&FileAttributes.ReparsePoint)!=0){files.Add(new(e.FullPath,"ReparsePoint",null,e.CreationTime,e.LastWriteTime,null,"リパースポイント",e.Attributes.ToString(),e.Extension));continue;}if(e.IsDirectory){stack.Push(new(e.FullPath,f.Depth+1));continue;}f.Direct+=e.Length;f.Total+=e.Length;f.Count++;files.Add(new(e.FullPath,"File",e.Length,e.CreationTime,e.LastWriteTime,options.LastAccessEnabled?e.LastAccessTime:null,options.LastAccessEnabled?"有効":"設定無効または不明",e.Attributes.ToString(),e.Extension));continue;}
 stack.Pop();if(f.Depth<=options.FolderDepth)folders.Add(new(f.Path,f.Depth,f.Direct,f.Total,f.Count,f.HadErrors));if(stack.TryPeek(out var parent)){parent.Total+=f.Total;parent.Count+=f.Count;parent.HadErrors|=f.HadErrors;}
 }
 }return new(files,folders,errors);}
}
public sealed class PhysicalFileSystem : IFileSystem
{
 public IEnumerable<FsEntry> Enumerate(string dir){var o=new EnumerationOptions{AttributesToSkip=0,IgnoreInaccessible=false,RecurseSubdirectories=false,ReturnSpecialDirectories=false};foreach(var x in new DirectoryInfo(dir).EnumerateFileSystemInfos("*",o)){var a=x.Attributes;var d=(a&FileAttributes.Directory)!=0;var fi=x as FileInfo;yield return new(x.FullName,x.Name,x.Extension,a,d,fi?.Length??0,x.CreationTime,x.LastWriteTime,x.LastAccessTime);}}
 public FileAttributes? TryGetAttributes(string path){try{return File.GetAttributes(path);}catch{return null;}} public bool DirectoryExists(string path)=>Directory.Exists(path);public bool FileExists(string path)=>File.Exists(path);
}
