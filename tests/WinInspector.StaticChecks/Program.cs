using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

var root=FindRoot(Directory.GetCurrentDirectory());
var bannedSymbols=File.ReadAllLines(Path.Combine(root,"tools","BannedSymbols.txt")).Where(x=>x.Length>0&&!x.StartsWith('#')).ToArray();
if(args.Contains("--self-test")){if(!FindBanned("System.Net.Http.HttpClient").Contains("System.Net.Http")||!StripCommentsAndStrings("var x = Process.Start(info);").Contains("Process.Start(",StringComparison.Ordinal))throw new InvalidOperationException("Static checker failed to detect a deliberately banned sample.");Console.WriteLine("Static checker self-test passed.");return 0;}
var allowed=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tools","csharp-allowlist.json"))).RootElement.GetProperty("processStartAllowlist").EnumerateArray().Select(x=>x.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
var failures=new List<string>();
foreach(var file in Directory.EnumerateFiles(Path.Combine(root,"src"),"*.cs",SearchOption.AllDirectories).Where(p=>!p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")&&!p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
{
    var relative=Path.GetRelativePath(root,file).Replace('\\','/');var source=File.ReadAllText(file);
    var syntax=StripCommentsAndStrings(source);
    if(syntax.Contains("Process.Start(",StringComparison.Ordinal)&&!allowed.Contains(relative))failures.Add($"Process.Start outside allowlist: {relative}");
    if(syntax.Contains("Process.Start(",StringComparison.Ordinal)&&allowed.Contains(relative)&&(!source.Contains("Environment.ProcessPath",StringComparison.Ordinal)||!source.Contains("UseShellExecute=true",StringComparison.Ordinal)||!source.Contains("Verb=\"runas\"",StringComparison.Ordinal)))failures.Add($"Elevation contract missing: {relative}");
    foreach(var banned in bannedSymbols)if(syntax.Contains(banned,StringComparison.Ordinal)&&!(relative=="src/WinInspector.Windows/Process/Elevation.cs"&&banned=="File.Delete("))failures.Add($"Banned source symbol {banned}: {relative}");
}
var bins=Directory.EnumerateFiles(Path.Combine(root,"src"),"*.dll",SearchOption.AllDirectories).Where(p=>!p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")&&!p.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}")).ToArray();
foreach(var path in bins)try{using var stream=File.OpenRead(path);using var pe=new PEReader(stream);if(!pe.HasMetadata)continue;var md=pe.GetMetadataReader();foreach(var handle in md.MemberReferences){var member=md.GetMemberReference(handle);var name=md.GetString(member.Name);if((name is "Start" or "StartInfo")&&member.Parent.Kind==HandleKind.TypeReference&&md.GetString(md.GetTypeReference((TypeReferenceHandle)member.Parent).Name)=="Process"&&!path.Replace('\\','/').Contains("WinInspector.Windows.dll"))failures.Add($"Process start IL reference outside Windows layer: {path}");}}catch(BadImageFormatException){}
if(failures.Count>0){foreach(var failure in failures)Console.Error.WriteLine(failure);return 1;}
Console.WriteLine($"Static checks passed: {bins.Length} managed assemblies inspected; source bans and elevation allowlist verified.");return 0;
List<string> FindBanned(string source)=>bannedSymbols.Where(x=>source.Contains(x,StringComparison.Ordinal)).ToList();
static string StripCommentsAndStrings(string s)=>System.Text.RegularExpressions.Regex.Replace(s,"/\\*.*?\\*/|//[^\\r\\n]*|\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|'(?:\\\\.|[^'\\\\])*'"," ",System.Text.RegularExpressions.RegexOptions.Singleline);
static string FindRoot(string start){for(var d=new DirectoryInfo(start);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"WinInspector.sln")))return d.FullName;throw new DirectoryNotFoundException("Repository root not found.");}
