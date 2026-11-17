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
    It 'normalizes Start_TrackProgs as unsigned DWORD' { (Get-AppTrackingSetting -Value -2147483648).Enabled | Should -BeTrue }
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
        $countOnly=[pscustomobject]@{Path='/opt/app/tool.exe';Source='UserAssist';LastRunTime=$null;RunCount=8};$r=Resolve-AppEvidence $app @($countOnly,[pscustomobject]@{Path='/opt/app/tool.exe';Source='UserAssist';LastRunTime=$now;RunCount=2});$r.Evidence.CountOnly | Should -BeTrue;$r.Evidence.RunCount | Should -Be 8;$r.Evidence.LastRunTime | Should -Be $now
        (Get-AppClassification ([pscustomobject]@{Name='App';CountOnly=$r.Evidence.CountOnly;RunCount=$r.Evidence.RunCount;LastRunTime=$r.Evidence.LastRunTime}) $now 365 $true @()).ExcludedReason | Should -Match '実行回数のみの記録あり'
        $msi=[pscustomobject]@{InstallLocation='';DisplayIcon='';UninstallString='MsiExec.exe /X{ABC}';PackageFamilyName=''};$r=Resolve-AppEvidence $msi @([pscustomobject]@{Path='MSIEXEC.EXE-12345678.pf';ExeName='msiexec.exe';Source='Prefetch';LastRunTime=$now});$r | Should -BeNullOrEmpty
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
        $guidCases=@(@('{F7F1ED05-9F6D-47A2-AAAE-29D317C6F066}\x.exe',[Environment+SpecialFolder]::CommonProgramFiles),@('{DE974D24-D9C6-4D3E-BF91-F4455120B917}\x.exe',[Environment+SpecialFolder]::CommonProgramFilesX86),@('{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}\x.exe',[Environment+SpecialFolder]::SystemX86))
        foreach($case in $guidCases){$base=[Environment]::GetFolderPath($case[1]);$resolved=Resolve-UserAssistPath $case[0];if($base){$resolved | Should -Be ($base+'\x.exe')}else{$resolved | Should -Be $case[0]}}
    }
    It 'classifies recent, disabled tracking, invalid dates and evidence without time' {
        $app=[pscustomobject]@{Name='App';SystemComponent=0;InstallDate='bad';LastRunTime=[datetime]::Now;RunCount=1}
        (Get-AppClassification $app (Get-Date) 365 $true @()).ExcludedReason | Should -Match '使用中'
        $app.LastRunTime=$null;(Get-AppClassification $app (Get-Date) 365 $false @()).Status | Should -Be '判定不能'
        (Get-AppClassification $app (Get-Date) 365 $true @()).Status | Should -Be '判定不能'
        $app.RunCount=0;(Get-AppClassification $app (Get-Date) 365 $true @()).Status | Should -Be '判定不能'
    }
    It 'normalizes concrete paths and checks containment' { $base=Join-Path ([IO.Path]::GetTempPath()) 'folder';(ConvertTo-NormalizedPath (Join-Path $base 'x/../y')) | Should -Be (Join-Path $base 'y');(Test-PathWithin 'C:\a\b' 'C:\a') | Should -BeTrue }
    It 'keeps the platform root separator and applies drive-root exclusions' {
        $entry=Get-Content (Join-Path $root 'src/Invoke-WinInspector.ps1') -Raw
        $entry | Should -Match "\^\[A-Za-z\]:\\\\\?\$"
        $entry | Should -Match '\$Recycle\.Bin'
        $entry | Should -Match 'System Volume Information'
        $entry | Should -Match 'Windows\\WinSxS'
        (ConvertTo-InspectorPath ([IO.Path]::GetPathRoot([IO.Path]::GetFullPath('/')))) | Should -Be ([IO.Path]::GetPathRoot([IO.Path]::GetFullPath('/')))
    }
    It 'rejects a symbolic link used as a scan target root' {
        $base=Join-Path $TestDrive 'root-link';$target=Join-Path $base 'target';$link=Join-Path $base 'link';[IO.Directory]::CreateDirectory($target)|Out-Null
        try { New-Item -ItemType SymbolicLink -Path $link -Target $target -ErrorAction Stop | Out-Null;$errors=[Collections.Generic.List[object]]::new();$result=Invoke-FileScan -Targets @($link) -Exclude @() -FolderDepth 2 -LastAccessSetting $null -OnFile {} -OnFolder {} -OnError {param($p,$o,$t,$m)$errors.Add([pscustomobject]@{Type=$t})} -OnProgress {}; $result.FileCount | Should -Be 0;$result.FolderCount | Should -Be 0;$errors[0].Type | Should -Be 'ReparsePointRejected' } finally { Remove-Item $base -Recurse -Force -ErrorAction SilentlyContinue }
    }
    It 'treats a negative uninstall EstimatedSize as unset' {
        . (Join-Path $root 'src/lib/AppInventory.ps1')
        $fake=[pscustomobject]@{EstimatedSize=-12};$fake|Add-Member ScriptMethod GetValue {param($name,$default) if($name -eq 'EstimatedSize'){$this.EstimatedSize}else{$default}}
        (Convert-UninstallKey $fake 'HKCU').EstimatedSize | Should -BeNullOrEmpty
    }
    It 'treats a nonnumeric uninstall EstimatedSize as unset and continues' {
        . (Join-Path $root 'src/lib/AppInventory.ps1')
        $fake=[pscustomobject]@{EstimatedSize='not-a-number'};$fake|Add-Member ScriptMethod GetValue {param($name,$default) if($name -eq 'EstimatedSize'){$this.EstimatedSize}else{$default}}
        (Convert-UninstallKey $fake 'HKCU').EstimatedSize | Should -BeNullOrEmpty
    }
    It 'returns unknown folder size and records an error when the install root cannot be enumerated' {
        . (Join-Path $root 'src/lib/AppInventory.ps1')
        $errors=[Collections.Generic.List[object]]::new()
        $size=Measure-InstallFolder (Join-Path $TestDrive 'missing-install-root') {param($p,$o,$t,$m)$errors.Add([pscustomobject]@{Operation=$o})}
        $size | Should -BeNullOrEmpty
        $errors.Count | Should -Be 1
    }
    It 'finds a symlink among the path ancestors' {
        . (Join-Path $root 'src/lib/AppInventory.ps1')
        $base=Join-Path $TestDrive 'reparse';$target=Join-Path $base 'target';$link=Join-Path $base 'link';[IO.Directory]::CreateDirectory($target)|Out-Null
        try{New-Item -ItemType SymbolicLink -Path $link -Target $target -ErrorAction Stop|Out-Null;(Get-ReparsePointAncestor (Join-Path $link 'child')) | Should -Be $link
            $errs=[Collections.Generic.List[object]]::new();$size=Measure-InstallFolder $link {param($p,$o,$t,$m)$errs.Add($o)};$size | Should -BeNullOrEmpty;$errs.Count | Should -Be 1
            {Assert-NoReparseOutputPath (Join-Path $link 'child')} | Should -Throw
        }finally{Remove-Item $base -Recurse -Force -ErrorAction SilentlyContinue}
    }
}

Describe 'Entry point end to end' {
    It 'handles zero launcher arguments and removes its temporary argument file' {
        $repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path;$launcher=Join-Path $repo 'tools/Start-Elevated.ps1';$harness=Join-Path $TestDrive 'launcher-harness.ps1'
        $marker=Join-Path $TestDrive 'temp-was-created';$tempRoot=[IO.Path]::GetTempPath();$content="`$env:TEMP='$tempRoot';function Start-Process { param(`$FilePath,`$Verb,[switch]`$Wait,[switch]`$PassThru,`$ArgumentList) `$p=(`$ArgumentList -split '-ArgumentsFile ')[1].Trim().Trim([char]34);if(Test-Path -LiteralPath `$p){[IO.File]::WriteAllText('$marker','created')};[pscustomobject]@{ExitCode=17} }`n& '$launcher';`$code=`$LASTEXITCODE;[IO.File]::WriteAllText('$marker.exit',[string]`$code);exit `$code"
        [IO.File]::WriteAllText($harness,$content,[Text.UTF8Encoding]::new($true));$before=@(Get-ChildItem $tempRoot -Filter 'wininspector-args-*.txt' -ErrorAction SilentlyContinue | ForEach-Object FullName)
        $pwsh=(Get-Command pwsh).Source;& $pwsh -NoLogo -NoProfile -File $harness
        $harnessExitCode=$LASTEXITCODE;Test-Path $marker | Should -BeTrue;$harnessExitCode | Should -Be 17;$after=@(Get-ChildItem $tempRoot -Filter 'wininspector-args-*.txt' -ErrorAction SilentlyContinue | ForEach-Object FullName);Compare-Object $before $after | Should -BeNullOrEmpty
    }
    It 'writes file, folder, error and summary outputs on Linux' {
        $repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
        $fixture=Join-Path $TestDrive 'e2e';$out=Join-Path $TestDrive 'out'
        [IO.Directory]::CreateDirectory($fixture)|Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture 'one.txt'),'1')
        [IO.Directory]::CreateDirectory((Join-Path $fixture 'nested'))|Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture 'nested/two.txt'),'22')
        [IO.File]::WriteAllText((Join-Path $fixture 'three.txt'),'333')
        $pwsh=(Get-Command pwsh -ErrorAction SilentlyContinue).Source;if(!$pwsh){$pwsh='~/opt/pwsh/pwsh'}
        $pwsh=[Environment]::ExpandEnvironmentVariables($pwsh)
        & $pwsh -NoLogo -NoProfile -File (Join-Path $repo 'src/Invoke-WinInspector.ps1') -Mode All -Target $fixture -OutputDir $out 2>&1 | Out-Null
        $run=Get-ChildItem $out -Directory | Select-Object -First 1
        foreach($prefix in 'files_','folders_','errors_','summary_'){Get-ChildItem $run -Filter "$prefix*" | Should -Not -BeNullOrEmpty}
        $fileCsv=Get-Content (Get-ChildItem $run -Filter 'files_*'|Select-Object -First 1).FullName
        $fileCount=$fileCsv.Count-1
        $fileCount | Should -Be 3
        $folderCsv=Get-Content (Get-ChildItem $run -Filter 'folders_*'|Select-Object -First 1).FullName
        $folderCount=$folderCsv.Count-1
        $folderCount | Should -Be 2
        $summary=Get-Content (Get-ChildItem $run -Filter 'summary_*'|Select-Object -First 1).FullName -Raw
        $summary | Should -Match "ファイル件数: $fileCount"
        $summary | Should -Match "フォルダ件数: $folderCount"
    }
    It 'excludes the entire output directory from a scan' {
        $repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path;$target=Join-Path $TestDrive 'output-exclusion';$out=Join-Path $target 'out'
        [IO.Directory]::CreateDirectory($out)|Out-Null;[IO.File]::WriteAllText((Join-Path $target 'included.txt'),'ok');[IO.File]::WriteAllText((Join-Path $out 'old-log.csv'),'private')
        $pwsh=(Get-Command pwsh).Source;& $pwsh -NoLogo -NoProfile -File (Join-Path $repo 'src/Invoke-WinInspector.ps1') -Mode Files -Target $target -OutputDir $out 2>&1|Out-Null
        $run=Get-ChildItem $out -Directory|Select-Object -First 1;$rows=Import-Csv (Get-ChildItem $run -Filter 'files_*.csv').FullName
        @($rows).Count | Should -Be 1;$rows[0].Path | Should -Be (Join-Path $target 'included.txt')
    }
    It 'reports candidate summaries as not run in Files mode' {
        $repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path;$target=Join-Path $TestDrive 'empty';$out=Join-Path $TestDrive 'filesout';[IO.Directory]::CreateDirectory($target)|Out-Null
        $pwsh=(Get-Command pwsh).Source;& $pwsh -NoLogo -NoProfile -File (Join-Path $repo 'src/Invoke-WinInspector.ps1') -Mode Files -Target $target -OutputDir $out 2>&1|Out-Null
        $summary=Get-Content (Get-ChildItem (Get-ChildItem $out -Directory|Select-Object -First 1) -Filter 'summary_*').FullName -Raw;$summary | Should -Match '候補A 上位: 未実行';$summary | Should -Match '候補B 上位: 未実行'
    }
}
