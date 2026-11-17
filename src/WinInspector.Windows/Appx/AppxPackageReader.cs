namespace WinInspector.Windows.Appx;
public sealed record AppxRecord(string Name,string Version,string Publisher,string InstallLocation,string SignatureKind,bool IsFramework,bool NonRemovable,bool IsResourcePackage,string PackageFamilyName);
// NonRemovable is deliberately false when the platform API does not expose a reliable value.
public sealed class AppxPackageReader
{
 public static IEnumerable<AppxRecord> ReadCurrentUser()
 {
  var manager=new global::Windows.Management.Deployment.PackageManager();
  foreach(var package in manager.FindPackagesForUser(string.Empty))
  {
   var id=package.Id;
   yield return new AppxRecord(id.Name,$"{id.Version.Major}.{id.Version.Minor}.{id.Version.Build}.{id.Version.Revision}",id.Publisher,package.InstalledLocation?.Path??string.Empty,package.SignatureKind.ToString(),package.IsFramework,false,package.IsResourcePackage,id.FamilyName);
  }
 }
}
