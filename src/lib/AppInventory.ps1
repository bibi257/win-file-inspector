function Get-UninstallInventory {
    param([scriptblock]$OnError)
    $results=[Collections.Generic.List[object]]::new()
    foreach($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)){
        try{$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$view);try{$root=$base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',$false);if($root){try{foreach($name in $root.GetSubKeyNames()){$key=$root.OpenSubKey($name,$false);if(!$key){continue};try{$results.Add((Convert-UninstallKey $key $(if($view -eq [Microsoft.Win32.RegistryView]::Registry64){'HKLM64'}else{'HKLM32'})))}finally{$key.Dispose()}}}finally{$root.Dispose()}}}finally{$base.Dispose()}}catch{if($OnError){$OnError.Invoke('HKLM','ReadUninstallRegistry',$_.Exception.GetType().Name,$_.Exception.Message)}}
    }
    try{$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Default);try{$root=$base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',$false);if($root){try{foreach($name in $root.GetSubKeyNames()){$key=$root.OpenSubKey($name,$false);if($key){try{$results.Add((Convert-UninstallKey $key 'HKCU'))}finally{$key.Dispose()}}}}finally{$root.Dispose()}}}finally{$base.Dispose()}}catch{if($OnError){$OnError.Invoke('HKCU','ReadUninstallRegistry',$_.Exception.GetType().Name,$_.Exception.Message)}}
    $results.ToArray()
}
function Convert-UninstallKey {param($Key,[string]$Source);$o=[ordered]@{Source=$Source};foreach($n in 'DisplayName','DisplayVersion','Publisher','InstallDate','EstimatedSize','InstallLocation','DisplayIcon','UninstallString','SystemComponent','ParentKeyName','ReleaseType','WindowsInstaller'){$o[$n]=$Key.GetValue($n,$null)};[pscustomobject]$o}
function Get-AppxInventory {
    param([scriptblock]$OnError)
    try { if(-not (Get-Command Get-AppxPackage -ErrorAction SilentlyContinue)){throw 'Get-AppxPackage cmdlet unavailable'}; @(Get-AppxPackage -ErrorAction Stop | ForEach-Object {[pscustomobject]@{Source='Appx';Name=$_.Name;Version=[string]$_.Version;Publisher=$_.Publisher;InstallDate='';InstallDateSource='';EstimatedSize=$null;InstallLocation=$_.InstallLocation;SignatureKind=[string]$_.SignatureKind;IsFramework=[bool]$_.IsFramework;NonRemovable=[bool]$_.NonRemovable;IsResourcePackage=[bool]$_.IsResourcePackage;PackageFamilyName=$_.PackageFamilyName}}) } catch { if($OnError){$OnError.Invoke('Appx','Get-AppxPackage',$_.Exception.GetType().Name,$_.Exception.Message)};[pscustomobject]@{Error=('Get-AppxPackage 取得失敗: '+$_.Exception.Message)} }
}

$script:InstallFolderSizeCache=[Collections.Generic.Dictionary[string,long]]::new([StringComparer]::OrdinalIgnoreCase)
function Measure-InstallFolder {
    param([string]$Path,[scriptblock]$OnError)
    $full=[IO.Path]::GetFullPath($Path)
    if($full.StartsWith('\\')){if($OnError){$OnError.Invoke($full,'MeasureInstallFolder','NetworkPathRejected','UNC は列挙しません')};return}
    $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($full));if($drive.DriveType -eq [IO.DriveType]::Network){if($OnError){$OnError.Invoke($full,'MeasureInstallFolder','NetworkDriveRejected','ネットワークドライブは列挙しません')};return}
    $reparse=Get-ReparsePointAncestor $full
    if($reparse){if($OnError){$OnError.Invoke($reparse,'MeasureInstallFolder','ReparsePointRejected','対象または祖先がリパースポイントのため列挙しません')};return}
    if($script:InstallFolderSizeCache.ContainsKey($full)){return $script:InstallFolderSizeCache[$full]}
    if(-not [IO.Directory]::Exists($full)){throw "InstallLocation を読めません: $full"}
    [long]$total=0;$stack=[Collections.Generic.Stack[string]]::new();$stack.Push($full)
    while($stack.Count){$dir=$stack.Pop();try{$entries=[IO.Directory]::EnumerateFileSystemEntries($dir);foreach($entry in $entries){try{$attr=[IO.File]::GetAttributes($entry);if(($attr -band [IO.FileAttributes]::ReparsePoint) -ne 0){continue};if(($attr -band [IO.FileAttributes]::Directory) -ne 0){$stack.Push($entry)}else{$total += ([IO.FileInfo]::new($entry)).Length}}catch{if($OnError){$OnError.Invoke($entry,'MeasureInstallFolderEntry',$_.Exception.GetType().Name,$_.Exception.Message)}}}}catch{if($OnError){$OnError.Invoke($dir,'MeasureInstallFolderDirectory',$_.Exception.GetType().Name,$_.Exception.Message)}}}
    $script:InstallFolderSizeCache[$full]=$total;$total
}
function Get-ReparsePointAncestor {
    param([Parameter(Mandatory)][string]$Path)
    $current=[IO.Path]::GetFullPath($Path)
    while($current){try{if([IO.Directory]::Exists($current) -or [IO.File]::Exists($current)){if(([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0){return $current}}}catch{};$parent=[IO.Directory]::GetParent($current);if(!$parent){break};$current=$parent.FullName}
    $null
}
