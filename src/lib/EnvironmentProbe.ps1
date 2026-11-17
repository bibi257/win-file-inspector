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
    $v=[uint32]([int64]$Value -band 0xffffffffL)
    switch($v){0 {[pscustomobject]@{Enabled=$true;Value='0x00000000';Status='有効'}} 1 {[pscustomobject]@{Enabled=$false;Value='0x00000001';Status='更新無効(NtfsDisableLastAccessUpdate=0x00000001)'}} 2147483650 {[pscustomobject]@{Enabled=$true;Value='0x80000002';Status='有効'}} 2147483651 {[pscustomobject]@{Enabled=$false;Value='0x80000003';Status='更新無効(NtfsDisableLastAccessUpdate=0x80000003)'}} default {[pscustomobject]@{Enabled=$false;Value=('0x{0:X8}' -f $v);Status='設定不明'}}}
}
function Get-InspectorEnvironment {
    param([scriptblock]$OnError)
    $last=$null; $registryStatus='設定不明'
    try {$key=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Default); try {$sub=$key.OpenSubKey('SYSTEM\CurrentControlSet\Control\FileSystem',$false); if($sub){try{$last=$sub.GetValue('NtfsDisableLastAccessUpdate',$null)}finally{$sub.Dispose()}}}finally{$key.Dispose()}; $registryStatus='取得済み'} catch {$registryStatus='取得失敗';if($OnError){$OnError.Invoke('NtfsDisableLastAccessUpdate','ReadRegistry',$_.Exception.GetType().Name,$_.Exception.Message)}}
    $setting=Get-LastAccessSetting $last
    $admin=$false;try{$principal=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent();$admin=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)}catch{}
    [pscustomobject]@{LanguageMode=[string]$ExecutionContext.SessionState.LanguageMode; IsAdministrator=$admin; LastAccess=$setting; LastAccessRegistryStatus=$registryStatus; TimeZone=[TimeZoneInfo]::Local.Id; PowerShellVersion=$PSVersionTable.PSVersion.ToString()}
}

function Get-AppTrackingSetting {
    param([scriptblock]$OnError,[object]$Value)
    if($PSBoundParameters.ContainsKey('Value')){if($null -eq $Value){return [pscustomobject]@{Enabled=$false;Value=$null;Status='値なし（既定で有効の可能性）'}};$normalized=[uint32]([int64]$Value -band 0xffffffffL);return [pscustomobject]@{Enabled=($normalized -ne 0);Value=('0x{0:X8}' -f $normalized);Status='取得済み'}}
    try {$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Default);try{$key=$base.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced',$false);if($key){try{$value=$key.GetValue('Start_TrackProgs',$null);if($null -ne $value){$normalized=[uint32]([int64]$value -band 0xffffffffL);return [pscustomobject]@{Enabled=($normalized -ne 0);Value=('0x{0:X8}' -f $normalized);Status='取得済み'}}}finally{$key.Dispose()}}}finally{$base.Dispose()}}catch{if($OnError){$OnError.Invoke('Start_TrackProgs','ReadRegistry',$_.Exception.GetType().Name,$_.Exception.Message)}}
    [pscustomobject]@{Enabled=$false;Value=$null;Status='値なし（既定で有効の可能性）'}
}
