BeforeAll {
    $root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    foreach($name in 'EnvironmentProbe','FileScanner','LogWriter'){. (Join-Path $root "src/lib/$name.ps1")}
    $fixture=Join-Path $TestDrive 'WinInspectorFixture';[IO.Directory]::CreateDirectory($fixture)|Out-Null
    # Fixtures are intentionally generated only by tests.
    [IO.File]::WriteAllText((Join-Path $fixture 'sample_日本語,=".txt'),'fixture')
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'nested'))|Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'nested/child.txt'),'fixture')
}
Describe 'Windows runner smoke foundation' {
    It 'runs file enumeration against a small test fixture' -Skip:(!$IsWindows) {
        $seen=[Collections.Generic.List[string]]::new()
        $before=@(Get-ChildItem $fixture -File -Recurse | ForEach-Object {[pscustomobject]@{Path=$_.FullName;Creation=$_.CreationTimeUtc;Write=$_.LastWriteTimeUtc;Access=$_.LastAccessTimeUtc}})
        $null=Invoke-FileScan -Targets @($fixture) -Exclude @() -OutputDir (Join-Path $TestDrive 'logs') -FolderDepth 3 -LastAccessSetting (Get-LastAccessSetting $null) -OnFile {param($r)$seen.Add($r.Path)} -OnFolder {param($r)} -OnError {param($p,$o,$t,$m)} -OnProgress {param($n,$p,$e)}
        $seen.Count | Should -BeGreaterThan 0
        foreach($entry in $before){$after=Get-Item -LiteralPath $entry.Path;$after.CreationTimeUtc | Should -Be $entry.Creation;$after.LastWriteTimeUtc | Should -Be $entry.Write;$after.LastAccessTimeUtc | Should -Be $entry.Access}
    }
}
