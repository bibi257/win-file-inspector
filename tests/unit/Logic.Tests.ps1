BeforeAll {
    $root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    foreach($name in 'EnvironmentProbe','UsageEvidence','Classifier','LogWriter','FileScanner'){. (Join-Path $root "src/lib/$name.ps1")}
}
Describe 'Pure logic' {
    It 'decodes ROT13' { ConvertFrom-Rot13 'GurDhVPX' | Should -Be 'TheQuick' }
    It 'parses UserAssist count and FILETIME' {
        $bytes=[byte[]]::new(72);[BitConverter]::GetBytes([uint32]7).CopyTo($bytes,4);$ft=[datetime]::new(2020,1,2).ToFileTime();[BitConverter]::GetBytes([long]$ft).CopyTo($bytes,60)
        $r=ConvertFrom-UserAssistData $bytes;$r.RunCount | Should -Be 7;$r.LastRunTime.Year | Should -Be 2020
    }
    It 'converts FILETIME' { (ConvertFrom-FileTimeValue ([datetime]::new(2024,1,1).ToFileTime())).Year | Should -Be 2024 }
    It 'interprets last access settings' {
        (Get-LastAccessSetting 0).Enabled | Should -BeTrue
        (Get-LastAccessSetting 1).Enabled | Should -BeFalse
        (Get-LastAccessSetting 2147483650).Enabled | Should -BeTrue
        (Get-LastAccessSetting 2147483651).Status | Should -Match '更新無効'
        (Get-LastAccessSetting $null).Status | Should -Be '設定不明'
    }
    It 'classifies candidates and exclusions' {
        $app=[pscustomobject]@{Name='Old app';SystemComponent=0;ParentKeyName='';ReleaseType='';Source='HKCU';InstallDate='20200101';LastRunTime=$null}
        (Get-AppClassification $app ([datetime]::new(2025,1,1)) 365 $true @()).Status | Should -Be '候補B'
        $app.Name='Microsoft Edge';(Get-AppClassification $app ([datetime]::new(2025,1,1)) 365 $true @('(?i)Microsoft Edge')).Status | Should -Be '対象外'
    }
    It 'escapes CSV and prevents formula injection' {
        (ConvertTo-CsvCell 'a,"b') | Should -Be '"a,""b"'
        (ConvertTo-CsvCell '=1+1' -ProtectFormula) | Should -Be """'=1+1"""
    }
    It 'resolves each application evidence method' {
        $now=[datetime]::new(2024,1,1)
        $app=[pscustomobject]@{InstallLocation='/opt/app';DisplayIcon='/opt/app/tool.exe';UninstallString='';PackageFamilyName=''}
        $r=Resolve-AppEvidence $app @([pscustomobject]@{Path='/opt/app/tool.exe';Source='UserAssist';LastRunTime=$now;RunCount=2});$r.Method | Should -Be 'InstallLocation'
        $app.InstallLocation='';$r=Resolve-AppEvidence $app @([pscustomobject]@{Path='/opt/app/tool.exe';Source='UserAssist';LastRunTime=$now;RunCount=2});$r.Method | Should -Be 'IconOrUninstaller'
        $r=Resolve-AppEvidence $app @([pscustomobject]@{Path='/home/user/start/tool.lnk';TargetPath='/opt/app/tool.exe';Source='UserAssist';LastRunTime=$now;RunCount=2});$r.Method | Should -Be 'ShortcutTarget'
        $app.PackageFamilyName='Example_family';$r=Resolve-AppEvidence $app @([pscustomobject]@{Path='Example_family!App';Source='UserAssist';LastRunTime=$now;RunCount=2});$r.Method | Should -Be 'AppxAumid'
        $app.DisplayIcon='/opt/app/tool.exe';$r=Resolve-AppEvidence $app @([pscustomobject]@{Path='TOOL-12345678.pf';ExeName='TOOL';Source='Prefetch';LastRunTime=$now;RunCount=$null});$r.Method | Should -Be 'PrefetchName'
        $newer=$now.AddDays(2);$r=Resolve-AppEvidence $app @([pscustomobject]@{Path='/opt/app/tool.exe';Source='UserAssist';LastRunTime=$now;RunCount=2},[pscustomobject]@{Path='TOOL-12345678.pf';ExeName='TOOL';Source='Prefetch';LastRunTime=$newer;RunCount=$null});$r.Evidence.LastRunTime | Should -Be $newer
    }
    It 'aggregates nested folder totals' {
        $root=Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString());$null=[IO.Directory]::CreateDirectory((Join-Path $root 'child/deep'));[IO.File]::WriteAllBytes((Join-Path $root 'a.bin'),[byte[]](1,2));[IO.File]::WriteAllBytes((Join-Path $root 'child/b.bin'),[byte[]](1,2,3));[IO.File]::WriteAllBytes((Join-Path $root 'child/deep/c.bin'),[byte[]](1,2,3,4))
        $rows=[Collections.Generic.List[object]]::new();Invoke-FileScan -Targets @($root) -Exclude @() -FolderDepth 3 -LastAccessSetting $null -OnFile {} -OnFolder {$rows.Add($args[0])} -OnError {} -OnProgress {} | Out-Null
        ($rows | Where-Object Path -eq $root).TotalSizeBytes | Should -Be 9;($rows | Where-Object Path -eq $root).FileCount | Should -Be 3;[IO.Directory]::Delete($root,$true)
    }
    It 'normalizes and checks paths' { (ConvertTo-NormalizedPath 'x/../y') | Should -Not -BeNullOrEmpty; (Test-PathWithin 'C:\a\b' 'C:\a') | Should -BeTrue }
}
