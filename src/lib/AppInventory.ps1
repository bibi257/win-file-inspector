function Get-UninstallInventory {
    $results=[Collections.Generic.List[object]]::new()
    foreach($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)){
        try{$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$view);try{$root=$base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',$false);if($root){try{foreach($name in $root.GetSubKeyNames()){$key=$root.OpenSubKey($name,$false);if(!$key){continue};try{$results.Add((Convert-UninstallKey $key $(if($view -eq [Microsoft.Win32.RegistryView]::Registry64){'HKLM64'}else{'HKLM32'})))}finally{$key.Dispose()}}}finally{$root.Dispose()}}}finally{$base.Dispose()}}catch{}
    }
    try{$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Default);try{$root=$base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',$false);if($root){try{foreach($name in $root.GetSubKeyNames()){$key=$root.OpenSubKey($name,$false);if($key){try{$results.Add((Convert-UninstallKey $key 'HKCU'))}finally{$key.Dispose()}}}}finally{$root.Dispose()}}}finally{$base.Dispose()}}catch{}
    $results.ToArray()
}
function Convert-UninstallKey {param($Key,[string]$Source);$o=[ordered]@{Source=$Source};foreach($n in 'DisplayName','DisplayVersion','Publisher','InstallDate','EstimatedSize','InstallLocation','DisplayIcon','UninstallString','SystemComponent','ParentKeyName','ReleaseType','WindowsInstaller'){$o[$n]=$Key.GetValue($n,$null)};[pscustomobject]$o}
function Get-AppxInventory {
    try { if(-not (Get-Command Get-AppxPackage -ErrorAction SilentlyContinue)){throw 'Get-AppxPackage cmdlet unavailable'}; @(Get-AppxPackage -ErrorAction Stop | ForEach-Object {[pscustomobject]@{Source='Appx';Name=$_.Name;Version=[string]$_.Version;Publisher=$_.Publisher;InstallDate='';InstallDateSource='';EstimatedSize=$null;InstallLocation=$_.InstallLocation;SignatureKind=[string]$_.SignatureKind;IsFramework=[bool]$_.IsFramework;NonRemovable=[bool]$_.NonRemovable;IsResourcePackage=[bool]$_.IsResourcePackage;PackageFamilyName=$_.PackageFamilyName}}) } catch { [pscustomobject]@{Error=('Get-AppxPackage 取得失敗: '+$_.Exception.Message)} }
}

function Measure-InstallFolder {
    param([string]$Path)
    if(-not [IO.Directory]::Exists($Path)){throw "InstallLocation を読めません: $Path"}
    [long]$total=0;$stack=[Collections.Generic.Stack[string]]::new();$stack.Push($Path)
    while($stack.Count){$dir=$stack.Pop();foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($dir)){$attr=[IO.File]::GetAttributes($entry);if(($attr -band [IO.FileAttributes]::ReparsePoint) -ne 0){continue};if(($attr -band [IO.FileAttributes]::Directory) -ne 0){$stack.Push($entry)}else{$total += ([IO.FileInfo]::new($entry)).Length}}}
    $total
}
