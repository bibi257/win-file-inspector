BeforeAll {
    $root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $allow=Import-PowerShellDataFile (Join-Path $root 'tools/allowlist.psd1')
    $scripts=@(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter '*.ps1')
}
Describe 'Static safety checks' {
    It 'parses every source script and rejects dangerous commands and dynamic execution' {
        foreach($file in $scripts){
            $tokens=$null;$parseErrors=$null
            $ast=[System.Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors)
            $parseErrors | Should -BeNullOrEmpty -Because $file.Name
            $commands=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst]},$true) | ForEach-Object {$_.GetCommandName()} | Where-Object {$_})
            foreach($command in $commands){$command | Should -Not -BeIn $allow.ForbiddenCommands -Because "$($file.Name) invokes $command";$command | Should -BeIn $allow.Commands -Because "$($file.Name) uses unreviewed command $command"}
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Expression.Extent.Text -match 'scriptblock' -and $n.Member.Extent.Text -eq 'Create'},$true) | Should -BeNullOrEmpty
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Add-Type'},$true) | Should -BeNullOrEmpty
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'New-Object' -and ($n.CommandElements.Extent.Text -join ' ') -match 'ComObject' -and ($n.CommandElements.Extent.Text -join ' ') -notmatch 'WScript\.Shell'},$true) | Should -BeNullOrEmpty
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Extent.Text -eq 'Save'},$true) | Should -BeNullOrEmpty
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Start-Process'},$true) | Should -BeNullOrEmpty
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-Expression'},$true) | Should -BeNullOrEmpty
        }
    }
    It 'stores source scripts and launchers with UTF-8 BOM and CRLF' {
        $files=@($scripts)+@(Get-ChildItem $root -Filter '*.cmd')
        foreach($file in $files){$bytes=[IO.File]::ReadAllBytes($file.FullName);$text=[Text.Encoding]::UTF8.GetString($bytes);if($file.Extension -eq '.ps1'){$bytes[0..2] | Should -Be @(0xEF,0xBB,0xBF)};$text | Should -Match "`r`n";$text.Replace("`r`n",'') | Should -Not -Match "`n"}
    }
}
