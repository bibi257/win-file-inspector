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
        (Get-LastAccessSetting -2147483646).Enabled | Should -BeTrue
        (Get-LastAccessSetting -2147483645).Status | Should -Match '更新無効'
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
        $app.DisplayIcon='/opt/app/tool.exe';$r=Resolve-AppEvidence $app @([pscustomobject]@{Path='TOOL.EXE-12345678.pf';ExeName='TOOL.EXE';Source='Prefetch';LastRunTime=$now;RunCount=$null});$r.Method | Should -Be 'PrefetchName'
        $newer=$now.AddDays(2);$r=Resolve-AppEvidence $app @([pscustomobject]@{Path='/opt/app/tool.exe';Source='UserAssist';LastRunTime=$now;RunCount=2},[pscustomobject]@{Path='TOOL.EXE-12345678.pf';ExeName='TOOL.EXE';Source='Prefetch';LastRunTime=$newer;RunCount=$null});$r.Evidence.LastRunTime | Should -Be $newer
    }
    It 'aggregates nested folder totals' {
        $root=Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString());$null=[IO.Directory]::CreateDirectory((Join-Path $root 'child/deep'));[IO.File]::WriteAllBytes((Join-Path $root 'a.bin'),[byte[]](1,2));[IO.File]::WriteAllBytes((Join-Path $root 'child/b.bin'),[byte[]](1,2,3));[IO.File]::WriteAllBytes((Join-Path $root 'child/deep/c.bin'),[byte[]](1,2,3,4))
        $rows=[Collections.Generic.List[object]]::new();Invoke-FileScan -Targets @($root) -Exclude @() -FolderDepth 3 -LastAccessSetting $null -OnFile {} -OnFolder {$rows.Add($args[0])} -OnError {} -OnProgress {} | Out-Null
        ($rows | Where-Object Path -eq $root).TotalSizeBytes | Should -Be 9;($rows | Where-Object Path -eq $root).FileCount | Should -Be 3;[IO.Directory]::Delete($root,$true)
    }
    It 'resolves known and preserves unknown UserAssist GUID paths' {
        $known=Resolve-UserAssistPath '{6D809377-6AF0-444B-8957-A3773F02200E}\Tool\x.exe'
        $base=[Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles);if($base){$known | Should -Be ($base + '\Tool\x.exe')}else{$known | Should -Be '{6D809377-6AF0-444B-8957-A3773F02200E}\Tool\x.exe'}
        Resolve-UserAssistPath '{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}\x.exe' | Should -Be '{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}\x.exe'
    }
    It 'classifies recent, disabled tracking, invalid dates and evidence without time' {
        $app=[pscustomobject]@{Name='App';SystemComponent=0;InstallDate='bad';LastRunTime=[datetime]::Now;RunCount=1}
        (Get-AppClassification $app (Get-Date) 365 $true @()).ExcludedReason | Should -Match '使用中'
        $app.LastRunTime=$null;(Get-AppClassification $app (Get-Date) 365 $false @()).Status | Should -Be '判定不能'
        (Get-AppClassification $app (Get-Date) 365 $true @()).Status | Should -Be '判定不能'
        $app.RunCount=0;(Get-AppClassification $app (Get-Date) 365 $true @()).Status | Should -Be '判定不能'
    }
    It 'normalizes concrete paths and checks containment' { $base=Join-Path ([IO.Path]::GetTempPath()) 'folder';(ConvertTo-NormalizedPath (Join-Path $base 'x/../y')) | Should -Be (Join-Path $base 'y');(Test-PathWithin 'C:\a\b' 'C:\a') | Should -BeTrue }
}

Describe 'Entry point end to end' {
    It 'writes file, folder, error and summary outputs on Linux' {
        $repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
        $fixture=Join-Path $TestDrive 'e2e';$out=Join-Path $TestDrive 'out'
        [IO.Directory]::CreateDirectory($fixture)|Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture 'one.txt'),'1')
        [IO.Directory]::CreateDirectory((Join-Path $fixture 'nested'))|Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture 'nested/two.txt'),'22')
        $pwsh=(Get-Command pwsh -ErrorAction SilentlyContinue).Source;if(!$pwsh){$pwsh='~/opt/pwsh/pwsh'}
        $pwsh=[Environment]::ExpandEnvironmentVariables($pwsh)
        & $pwsh -NoLogo -NoProfile -File (Join-Path $repo 'src/Invoke-WinInspector.ps1') -Mode All -Target $fixture -OutputDir $out 2>&1 | Out-Null
        $run=Get-ChildItem $out -Directory | Select-Object -First 1
        foreach($prefix in 'files_','folders_','errors_','summary_'){Get-ChildItem $run -Filter "$prefix*" | Should -Not -BeNullOrEmpty}
        $fileCsv=Get-Content (Get-ChildItem $run -Filter 'files_*'|Select-Object -First 1).FullName
        ($fileCsv.Count-1) | Should -Be 2
    }
}
