function ConvertFrom-Rot13 {
    param([Parameter(Mandatory)][string]$Text)
    -join ($Text.ToCharArray() | ForEach-Object { $c=[int][char]$_; if($c -ge 65 -and $c -le 90){[char](65+(($c-65+13)%26))}elseif($c -ge 97 -and $c -le 122){[char](97+(($c-97+13)%26))}else{$_} })
}
function ConvertFrom-FileTimeValue {
    param([long]$Value)
    if($Value -le 0){return $null}; try {[DateTime]::FromFileTime($Value)} catch {$null}
}
function Get-LastAccessSetting {
    param([object]$Value)
    if($null -eq $Value){return [pscustomobject]@{Enabled=$false;Value=$null;Status='設定不明'}}
    $v=[long]$Value
    switch($v){0 {[pscustomobject]@{Enabled=$true;Value='0x00000000';Status='有効'}} 1 {[pscustomobject]@{Enabled=$false;Value='0x00000001';Status='更新無効(NtfsDisableLastAccessUpdate=0x00000001)'}} 2147483650 {[pscustomobject]@{Enabled=$true;Value='0x80000002';Status='有効'}} 2147483651 {[pscustomobject]@{Enabled=$false;Value='0x80000003';Status='更新無効(NtfsDisableLastAccessUpdate=0x80000003)'}} default {[pscustomobject]@{Enabled=$false;Value=('0x{0:X8}' -f $v);Status='設定不明'}}}
}
function Get-InspectorEnvironment {
    $last=$null; $registryStatus='設定不明'
    try {$key=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Default); try {$sub=$key.OpenSubKey('SYSTEM\CurrentControlSet\Control\FileSystem',$false); if($sub){try{$last=$sub.GetValue('NtfsDisableLastAccessUpdate',$null)}finally{$sub.Dispose()}}}finally{$key.Dispose()}; $registryStatus='取得済み'} catch {$registryStatus='取得失敗'}
    $setting=Get-LastAccessSetting $last
    $principal=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent(); $admin=$false; try{$admin=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)}catch{}
    [pscustomobject]@{LanguageMode=[string]$ExecutionContext.SessionState.LanguageMode; IsAdministrator=$admin; LastAccess=$setting; LastAccessRegistryStatus=$registryStatus; TimeZone=[TimeZoneInfo]::Local.Id; PowerShellVersion=$PSVersionTable.PSVersion.ToString()}
}

function Get-AppTrackingSetting {
    try {$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Default);try{$key=$base.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced',$false);if($key){try{$value=$key.GetValue('Start_TrackProgs',$null);if($null -ne $value){return [pscustomobject]@{Enabled=([int]$value -ne 0);Value=[int]$value;Status='取得済み'}}}finally{$key.Dispose()}}}finally{$base.Dispose()}}catch{}
    [pscustomobject]@{Enabled=$false;Value=$null;Status='設定不明'}
}
