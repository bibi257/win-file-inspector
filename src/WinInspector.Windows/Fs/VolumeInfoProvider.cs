using System.IO;
namespace WinInspector.Windows.Fs;
public enum DriveKind { Unknown,Network,Removable,Fixed }
public interface IVolumeInfo { DriveKind KindOf(string path);string? FormatOf(string path); }
public sealed class VolumeInfoProvider : IVolumeInfo
{
 public DriveKind KindOf(string path){try{return new DriveInfo(Path.GetPathRoot(path)!).DriveType switch{DriveType.Network=>DriveKind.Network,DriveType.Removable=>DriveKind.Removable,DriveType.Fixed=>DriveKind.Fixed,_=>DriveKind.Unknown};}catch{return DriveKind.Unknown;}}
 public string? FormatOf(string path){try{return new DriveInfo(Path.GetPathRoot(path)!).DriveFormat;}catch{return null;}}
}
