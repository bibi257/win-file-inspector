using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

var root=FindRoot(Directory.GetCurrentDirectory());
var bannedSymbols=File.ReadAllLines(Path.Combine(root,"tools","BannedSymbols.txt")).Where(x=>x.Length>0&&!x.StartsWith('#')).ToArray();
var allowDocument=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"tools","csharp-allowlist.json")));
var processContracts=ReadProcessContracts(allowDocument.RootElement);
var streamWriterAllowed=allowDocument.RootElement.GetProperty("streamWriterAllowlist").EnumerateArray().Select(x=>x.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
if(args.Contains("--self-test")){
 if(FindBanned("System.Net.Http.HttpClient").Count==0||FindBanned("System.IO.File.WriteAllText(\"x\",\"y\");").Count==0||!StripCommentsAndStrings("var x = Process.Start(info);").Contains("Process.Start(",StringComparison.Ordinal))throw new InvalidOperationException("Static checker failed to detect a deliberately banned sample.");
 var goodFolderOpener="var info = new ProcessStartInfo { FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), \"explorer.exe\"), UseShellExecute = false }; info.ArgumentList.Add(p); Process.Start(info);";
 var badFolderOpener="var info = new ProcessStartInfo { FileName = \"x.exe\", UseShellExecute=true, Verb=\"open\" }; Process.Start(info);";
 if(ProcessStartProblems("src/WinInspector.Windows/Process/FolderOpener.cs",goodFolderOpener,processContracts).Count!=0)throw new InvalidOperationException("Self-test: a valid FolderOpener sample was rejected.");
 if(ProcessStartProblems("src/WinInspector.Windows/Process/FolderOpener.cs",badFolderOpener,processContracts).Count==0)throw new InvalidOperationException("Self-test: a FolderOpener sample with UseShellExecute=true and Verb was accepted.");
 if(ProcessStartProblems("src/WinInspector.Cli/Other.cs",goodFolderOpener,processContracts).Count==0)throw new InvalidOperationException("Self-test: Process.Start outside the allowlist was accepted.");
 if(StreamWriterProblems("src/WinInspector.Core/Gui/Sample.cs",StripCommentsAndStrings("var w = new StreamWriter(path);"),streamWriterAllowed).Count==0)throw new InvalidOperationException("Self-test: new StreamWriter outside the allowlist was accepted.");
 if(StreamWriterProblems("src/WinInspector.Windows/Process/Elevation.cs",StripCommentsAndStrings("var w = new StreamWriter(path);"),streamWriterAllowed).Count!=0)throw new InvalidOperationException("Self-test: new StreamWriter in Elevation.cs was rejected.");
 Console.WriteLine("Static checker self-test passed.");return 0;}
var failures=new List<string>();
foreach(var file in Directory.EnumerateFiles(Path.Combine(root,"src"),"*.cs",SearchOption.AllDirectories).Where(p=>!p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")&&!p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
{
    var relative=Path.GetRelativePath(root,file).Replace('\\','/');var source=File.ReadAllText(file);
    var syntax=StripCommentsAndStrings(source);
    foreach(var literal in System.Text.RegularExpressions.Regex.Matches(source,"\\\"([^\\\"\\\\]|\\\\.)*\\\"").Select(x=>x.Value))if(literal.Contains("WinHttp")||literal.Contains("MSXML2")||literal.Contains("InternetExplorer.Application")||literal.Contains("WScript.Shell")||literal.Contains("Invoke-WebRequest")||literal.Contains("Win32_Product")||literal.Contains("Get-Package"))failures.Add($"Forbidden defensive string literal: {relative}: {literal}");
    failures.AddRange(ProcessStartProblems(relative,source,processContracts));
    failures.AddRange(StreamWriterProblems(relative,syntax,streamWriterAllowed));
    foreach(var banned in FindBanned(syntax))
    {
        var outputWriter=relative=="src/WinInspector.Core/Output/OutputFileWriter.cs";
        var approvedOutputWrite=outputWriter&&(banned.StartsWith("M:System.IO.File.",StringComparison.Ordinal)||banned=="M:System.IO.Directory.CreateDirectory(System.String)");
        if(!approvedOutputWrite)failures.Add($"Banned source symbol {banned}: {relative}");
    }
    if(relative=="src/WinInspector.Core/Output/OutputFileWriter.cs"&&(!source.Contains("OutputGuard.AssertWithin(path,outputDir)",StringComparison.Ordinal)||!source.Contains("OutputGuard.AssertWithin(path,outputRoot)",StringComparison.Ordinal)))failures.Add($"Output writer guard missing: {relative}");
}
var bins=Directory.EnumerateFiles(Path.Combine(root,"src"),"WinInspector*.dll",SearchOption.AllDirectories).Where(p=>!p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")&&!p.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}")).ToArray();
foreach(var path in bins)try{using var stream=File.OpenRead(path);using var pe=new PEReader(stream);if(!pe.HasMetadata)continue;var md=pe.GetMetadataReader();foreach(var handle in md.TypeReferences){var type=md.GetTypeReference(handle);var ns=md.GetString(type.Namespace);if(ns.StartsWith("System.Net",StringComparison.Ordinal)||ns.StartsWith("System.Management",StringComparison.Ordinal)||ns.StartsWith("System.Runtime.Loader",StringComparison.Ordinal)||ns.StartsWith("Microsoft.CSharp",StringComparison.Ordinal)||ns.StartsWith("System.Linq.Expressions",StringComparison.Ordinal)||ns.StartsWith("Windows.Networking",StringComparison.Ordinal)||ns.StartsWith("Windows.Web",StringComparison.Ordinal)||ns.StartsWith("Windows.Services",StringComparison.Ordinal))failures.Add($"Forbidden IL namespace {ns}: {path}");}foreach(var handle in md.MemberReferences){var member=md.GetMemberReference(handle);var name=md.GetString(member.Name);if(name=="GetType"&&member.Parent.Kind==HandleKind.TypeReference&&md.GetString(md.GetTypeReference((TypeReferenceHandle)member.Parent).Name)=="Type")failures.Add($"Forbidden IL member Type.GetType: {path}");if(name=="Start"&&member.Parent.Kind==HandleKind.TypeReference&&md.GetString(md.GetTypeReference((TypeReferenceHandle)member.Parent).Name)=="Process"&&!path.Replace('\\','/').EndsWith("WinInspector.Windows.dll",StringComparison.Ordinal))failures.Add($"Process.Start outside Windows layer: {path}");}foreach(var handle in md.MethodDefinitions){var method=md.GetMethodDefinition(handle);if((method.Attributes&System.Reflection.MethodAttributes.PinvokeImpl)!=0){var import=method.GetImport();var module=md.GetModuleReference(import.Module);var dll=md.GetString(module.Name);if(dll is not ("kernel32.dll" or "ole32.dll"))failures.Add($"Forbidden DllImport {dll}: {path}");}}}catch(BadImageFormatException){}
var cliAssembly=bins.FirstOrDefault(p=>Path.GetFileName(p)=="WinInspector.dll"&&!p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
if(cliAssembly is not null)try{var assembly=System.Reflection.Assembly.LoadFile(cliAssembly);using var resource=assembly.GetManifestResourceStream("WinInspector.Cli.config.exclusions.psd1");if(resource is null)failures.Add("Embedded exclusions resource is missing from WinInspector.dll.");else{using var reader=new StreamReader(resource);if(!reader.ReadToEnd().Contains("Visual C\\+\\+.*Redistributable",StringComparison.Ordinal))failures.Add("Embedded exclusions resource does not contain configured name patterns.");}}catch(Exception ex){failures.Add($"Embedded exclusions resource could not be read: {ex.GetType().Name}: {ex.Message}");}
else failures.Add("WinInspector.dll was not found for the embedded exclusions check.");
if(bins.Length==0)failures.Add("No managed assemblies found; build must run before static checks.");
if(failures.Count>0){foreach(var failure in failures)Console.Error.WriteLine(failure);return 1;}
Console.WriteLine($"Static checks passed: {bins.Length} managed assemblies inspected; source bans and elevation allowlist verified.");return 0;
static Dictionary<string,(string[] Requires,string[] Forbids)> ReadProcessContracts(JsonElement root)
{
    var contracts=new Dictionary<string,(string[] Requires,string[] Forbids)>(StringComparer.OrdinalIgnoreCase);
    foreach(var entry in root.GetProperty("processStartAllowlist").EnumerateArray())
    {
        var file=entry.GetProperty("file").GetString()!;
        var requires=entry.GetProperty("requires").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        var forbids=entry.GetProperty("forbids").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        contracts[file]=(requires,forbids);
    }
    return contracts;
}
static List<string> ProcessStartProblems(string relative,string source,IReadOnlyDictionary<string,(string[] Requires,string[] Forbids)> contracts)
{
    var problems=new List<string>();
    if(!StripCommentsAndStrings(source).Contains("Process.Start(",StringComparison.Ordinal))return problems;
    if(!contracts.TryGetValue(relative,out var contract)){problems.Add($"Process.Start outside allowlist: {relative}");return problems;}
    foreach(var required in contract.Requires)if(!source.Contains(required,StringComparison.Ordinal))problems.Add($"Process.Start contract missing '{required}': {relative}");
    foreach(var forbidden in contract.Forbids)if(source.Contains(forbidden,StringComparison.Ordinal))problems.Add($"Process.Start contract forbids '{forbidden}': {relative}");
    return problems;
}
static List<string> StreamWriterProblems(string relative,string syntax,HashSet<string> allowed)
{
    var problems=new List<string>();
    if(allowed.Contains(relative))return problems;
    if(syntax.Contains("new StreamWriter(",StringComparison.Ordinal))problems.Add($"new StreamWriter outside allowlist: {relative}");
    if(syntax.Contains("new FileStream(",StringComparison.Ordinal))problems.Add($"new FileStream outside allowlist: {relative}");
    return problems;
}
List<string> FindBanned(string source)=>bannedSymbols.Where(x=>
{
    var separator=x.IndexOf(':');var identifier=separator<0?x:x[(separator+1)..];
    var signature=identifier.IndexOf('(');if(signature>=0)identifier=identifier[..signature];
    if(x.StartsWith("T:",StringComparison.Ordinal))return source.Contains(identifier,StringComparison.Ordinal)||source.Contains(identifier[(identifier.LastIndexOf('.')+1)..],StringComparison.Ordinal);
    var split=identifier.LastIndexOf('.');var typeName=split<0?string.Empty:identifier[..split];var leaf=identifier[(split+1)..];
    var typeLeaf=typeName[(typeName.LastIndexOf('.')+1)..];
    return source.Contains(identifier,StringComparison.Ordinal)||source.Contains(typeLeaf+"."+leaf+"(",StringComparison.Ordinal);
}).ToList();
static string StripCommentsAndStrings(string s)=>System.Text.RegularExpressions.Regex.Replace(s,"/\\*.*?\\*/|//[^\\r\\n]*|\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|'(?:\\\\.|[^'\\\\])*'"," ",System.Text.RegularExpressions.RegexOptions.Singleline);
static string FindRoot(string start){for(var d=new DirectoryInfo(start);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"WinInspector.sln")))return d.FullName;throw new DirectoryNotFoundException("Repository root not found.");}
