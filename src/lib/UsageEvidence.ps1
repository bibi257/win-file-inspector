function ConvertFrom-UserAssistData {
    param([byte[]]$Data)
    if($null -eq $Data -or $Data.Length -lt 68){return $null}
    $count=0;if($Data.Length -ge 8){$count=[BitConverter]::ToUInt32($Data,4)}
    $ft=[BitConverter]::ToInt64($Data,60);$time=ConvertFrom-FileTimeValue $ft
    [pscustomobject]@{RunCount=$count;LastRunTime=$time}
}
function Get-UserAssistEvidence {
    $list=[Collections.Generic.List[object]]::new();$basePath='Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist'
    try{$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Default);try{$ua=$base.OpenSubKey($basePath,$false);if($ua){try{foreach($guid in $ua.GetSubKeyNames()){$countKey=$ua.OpenSubKey(($guid+'\Count'),$false);if(!$countKey){continue};try{foreach($n in $countKey.GetValueNames()){$d=$countKey.GetValue($n,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);if($d -is [byte[]]){$parsed=ConvertFrom-UserAssistData $d;if($parsed){$list.Add([pscustomobject]@{Path=(ConvertFrom-Rot13 $n);RunCount=$parsed.RunCount;LastRunTime=$parsed.LastRunTime;Source='UserAssist'})}}}}finally{$countKey.Dispose()}}}finally{$ua.Dispose()}}}finally{$base.Dispose()}}catch{}
    $list.ToArray()
}
function Get-PrefetchEvidence {
    param([switch]$Enabled)
    if(-not $Enabled){return @()};$root=Join-Path $env:SystemRoot 'Prefetch';try{@( [IO.Directory]::EnumerateFiles($root,'*.pf') | ForEach-Object {$i=[IO.FileInfo]::new($_);if($i.Name -match '^(.+)-[0-9A-Fa-f]{8}\.pf$'){[pscustomobject]@{Path=$i.Name;ExeName=$matches[1];RunCount=$null;LastRunTime=$i.LastWriteTime;Source='Prefetch'}}})}catch{@()}
}
function Resolve-AppEvidence {
    param($App,[object[]]$Evidence)
    $exePaths=[Collections.Generic.List[string]]::new()
    foreach($raw in @($App.DisplayIcon,$App.UninstallString)){if(!$raw){continue};$m=[regex]::Match([string]$raw,'^\s*"?([^",]+?\.exe)(?:"|,|\s|$)',[Text.RegularExpressions.RegexOptions]::IgnoreCase);if($m.Success){$exePaths.Add($m.Groups[1].Value)}}
    $found=[Collections.Generic.List[object]]::new();$loc=[string]$App.InstallLocation
    if($loc){foreach($e in $Evidence){if($e.Source -eq 'UserAssist' -and $e.Path -and $e.Path.StartsWith($loc.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){$found.Add([pscustomobject]@{Evidence=$e;Method='InstallLocation'})}}}
    if($exePaths.Count){foreach($e in $Evidence){if($e.Source -eq 'UserAssist' -and $e.Path){foreach($exe in $exePaths){if($e.Path.StartsWith((Split-Path $exe -Parent).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){$found.Add([pscustomobject]@{Evidence=$e;Method='IconOrUninstaller'});break}}}}}
    foreach($e in $Evidence){if($e.Source -eq 'UserAssist' -and $e.Path -match '\.lnk$' -and $e.TargetPath -and $exePaths.Contains([string]$e.TargetPath)){$found.Add([pscustomobject]@{Evidence=$e;Method='ShortcutTarget'})}}
    if($App.PackageFamilyName){foreach($e in $Evidence){if($e.Source -eq 'UserAssist' -and $e.Path -like ($App.PackageFamilyName+'!*')){$found.Add([pscustomobject]@{Evidence=$e;Method='AppxAumid'})}}}
    if($exePaths.Count){$names=@($exePaths|ForEach-Object {[IO.Path]::GetFileName($_)});foreach($e in $Evidence){if($e.Source -eq 'Prefetch' -and $e.ExeName -and ($names -contains ($e.ExeName+'.exe'))){$found.Add([pscustomobject]@{Evidence=$e;Method='PrefetchName'})}}}
    $found|Sort-Object {$_.Evidence.LastRunTime} -Descending|Select-Object -First 1
}
