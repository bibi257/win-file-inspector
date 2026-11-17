using WinInspector.Core.Scan;
namespace WinInspector.Windows.Fs;
public sealed class WindowsFileSystem : IFileSystem
{
 public IEnumerable<FsEntry> Enumerate(string dir){var options=new EnumerationOptions{AttributesToSkip=0,IgnoreInaccessible=false,RecurseSubdirectories=false,ReturnSpecialDirectories=false};foreach(var item in new DirectoryInfo(dir).EnumerateFileSystemInfos("*",options)){var attributes=item.Attributes;var file=item as FileInfo;yield return new(item.FullName,item.Name,item.Extension,attributes,(attributes&FileAttributes.Directory)!=0,file?.Length??0,item.CreationTime,item.LastWriteTime,item.LastAccessTime);}}
 public FileAttributes? TryGetAttributes(string path){try{return File.GetAttributes(path);}catch{return null;}}
 public bool DirectoryExists(string path)=>Directory.Exists(path);
 public bool FileExists(string path)=>File.Exists(path);
}
