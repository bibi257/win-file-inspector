function ConvertFrom-UserAssistData {
    param([byte[]]$Data)
    if($null -eq $Data -or $Data.Length -lt 68){return $null}
    $count=0;if($Data.Length -ge 8){$count=[BitConverter]::ToUInt32($Data,4)}
    $ft=[BitConverter]::ToInt64($Data,60);$time=ConvertFrom-FileTimeValue $ft
    [pscustomobject]@{RunCount=$count;LastRunTime=$time}
}
function Get-UserAssistEvidence {
    param([scriptblock]$OnError)
    $list=[Collections.Generic.List[object]]::new();$basePath='Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist'
    try{$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Default);try{$ua=$base.OpenSubKey($basePath,$false);if($ua){try{foreach($guid in $ua.GetSubKeyNames()){$countKey=$ua.OpenSubKey(($guid+'\Count'),$false);if(!$countKey){continue};try{foreach($n in $countKey.GetValueNames()){$d=$countKey.GetValue($n,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);if($d -is [byte[]]){$parsed=ConvertFrom-UserAssistData $d;if($parsed){$list.Add([pscustomobject]@{Path=(Resolve-UserAssistPath (ConvertFrom-Rot13 $n));RunCount=$parsed.RunCount;LastRunTime=$parsed.LastRunTime;Source='UserAssist'})}}}}finally{$countKey.Dispose()}}}finally{$ua.Dispose()}}}finally{$base.Dispose()}}catch{if($OnError){$OnError.Invoke('UserAssist','ReadRegistry',$_.Exception.GetType().Name,$_.Exception.Message)}}
    $list.ToArray()
}
function Resolve-UserAssistPath {
    param([string]$Path)
    $map=@{'6D809377-6AF0-444B-8957-A3773F02200E'=[Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles);'7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E'=[Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86);'1AC14E77-02E7-4E5D-B744-2EB1AE5198B7'=[Environment]::GetFolderPath([Environment+SpecialFolder]::System);'F38BF404-1D43-42F2-9305-67DE0B28FC23'=[Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)}
    [regex]::Replace($Path,'\{([0-9A-Fa-f-]{36})\}',{param($m) $id=$m.Groups[1].Value.ToUpperInvariant();if($map.ContainsKey($id) -and $map[$id]){$map[$id]}else{$m.Value}})
}
function Get-PrefetchEvidence {
    param([switch]$Enabled,[scriptblock]$OnError)
    if(-not $Enabled){return @()};$root=Join-Path $env:SystemRoot 'Prefetch';try{@( [IO.Directory]::EnumerateFiles($root,'*.pf') | ForEach-Object {$i=[IO.FileInfo]::new($_);if($i.Name -match '^(.+)-[0-9A-Fa-f]{8}\.pf$'){[pscustomobject]@{Path=$i.Name;ExeName=$matches[1];RunCount=$null;LastRunTime=$i.LastWriteTime;Source='Prefetch'}}})}catch{if($OnError){$OnError.Invoke($root,'EnumeratePrefetch',$_.Exception.GetType().Name,$_.Exception.Message)};@()}
}
function Resolve-AppEvidence {
    param($App,[object[]]$Evidence)
    $Evidence=@($Evidence | Where-Object {$_.Source -ne 'UserAssist' -or $_.RunCount -ne 0})
    $exePaths=[Collections.Generic.List[string]]::new()
    foreach($raw in @($App.DisplayIcon,$App.UninstallString)){if(!$raw){continue};$m=[regex]::Match([string]$raw,'^\s*"?([^",]+?\.exe)(?:"|,|\s|$)',[Text.RegularExpressions.RegexOptions]::IgnoreCase);if($m.Success){$exePaths.Add($m.Groups[1].Value)}}
    $found=[Collections.Generic.List[object]]::new();$loc=[string]$App.InstallLocation;$excludedRoots=@('C:\','C:\Program Files','C:\Program Files (x86)','C:\Windows','C:\Windows\System32') | ForEach-Object {$_.TrimEnd('\')};if($loc -and $excludedRoots -contains $loc.TrimEnd('\','/')){$loc=''}
    if($loc){foreach($e in $Evidence){if($e.Source -eq 'UserAssist' -and $e.RunCount -ne 0 -and $e.Path -and $e.Path.StartsWith($loc.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){$found.Add([pscustomobject]@{Evidence=$e;Method='InstallLocation'})}}}
    if($exePaths.Count){foreach($e in $Evidence){if($e.Source -eq 'UserAssist' -and $e.Path){foreach($exe in $exePaths){$exeFolder=(Split-Path $exe -Parent).TrimEnd('\','/');if($excludedRoots -contains $exeFolder){continue};if($e.Path.StartsWith($exeFolder+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){$found.Add([pscustomobject]@{Evidence=$e;Method='IconOrUninstaller'});break}}}}}
    foreach($e in $Evidence){if($e.Source -eq 'UserAssist' -and $e.Path -match '\.lnk$' -and $e.TargetPath -and ($exePaths | Where-Object {[string]::Equals($_,[string]$e.TargetPath,[StringComparison]::OrdinalIgnoreCase)})){$found.Add([pscustomobject]@{Evidence=$e;Method='ShortcutTarget'})}}
    if($App.PackageFamilyName){foreach($e in $Evidence){if($e.Source -eq 'UserAssist' -and $e.Path -like ($App.PackageFamilyName+'!*')){$found.Add([pscustomobject]@{Evidence=$e;Method='AppxAumid'})}}}
    if($exePaths.Count){$names=@($exePaths|ForEach-Object {[IO.Path]::GetFileName($_)});foreach($e in $Evidence){if($e.Source -eq 'Prefetch' -and $e.ExeName -and ($names | Where-Object {[string]::Equals($_,$e.ExeName,[StringComparison]::OrdinalIgnoreCase)})){$found.Add([pscustomobject]@{Evidence=$e;Method='PrefetchName'})}}}
    $found|Sort-Object {$_.Evidence.LastRunTime} -Descending|Select-Object -First 1
}
