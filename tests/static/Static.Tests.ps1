BeforeAll {
    $root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $allow=Import-PowerShellDataFile (Join-Path $root 'tools/allowlist.psd1')
    $scripts=@(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter '*.ps1')+@(Get-ChildItem (Join-Path $root 'tools') -Filter '*.ps1')
}
Describe 'Static safety checks' {
    It 'rejects intentional dynamic-call and write-method violation samples' {
        $sample='& $command; [IO.File]::Delete(''sample'')'
        $tokens=$null;$parseErrors=$null;$ast=[System.Management.Automation.Language.Parser]::ParseInput($sample,[ref]$tokens,[ref]$parseErrors)
        @($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and -not $n.GetCommandName()},$true)).Count | Should -Be 1
        @($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Extent.Text -eq 'Delete'},$true)).Count | Should -Be 1
    }
    It 'detects reviewed dangerous AST shapes in negative samples' {
        $samples=@('$ExecutionContext.InvokeCommand.InvokeScript($code)','$x.NewScriptBlock($text)','New-Object Net.WebClient','New-Object -TypeName System.Diagnostics.Process','$k.DeleteSubKey("x");$k.DeleteSubKeyTree("x");[IO.File]::Copy("a","b");[IO.File]::Move("a","b");[IO.File]::Replace("a","b","c");[IO.File]::Create("x");[Diagnostics.Process]::Start("x")','$k.OpenSubKey("x",$flag);$k.OpenSubKey("x",[bool]$true)','New-Object -ComObj WScript.Shell')
        foreach($sample in $samples){$t=$null;$e=$null;$ast=[System.Management.Automation.Language.Parser]::ParseInput($sample,[ref]$t,[ref]$e);$danger=@($ast.FindAll({param($n) if($n -is [System.Management.Automation.Language.InvokeMemberExpressionAst]){if($n.Member.Extent.Text -in @('InvokeScript','NewScriptBlock','DeleteSubKey','DeleteSubKeyTree','Copy','Move','Replace','Create','Start')){return $true};if($n.Member.Extent.Text -eq 'OpenSubKey' -and $n.Arguments.Count -gt 1 -and $n.Arguments[1].Extent.Text -ne '$false'){return $true}};if($n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'New-Object' -and (($n.Extent.Text -match '(?i)(?:Net\.WebClient|System\.Diagnostics\.Process)') -or ($n.Extent.Text -match '(?i)-Com\w*\s'))){return $true};$false},$true));$danger.Count | Should -BeGreaterThan 0 -Because $sample}
    }
    It 'parses every source script and rejects dangerous commands and dynamic execution' {
        foreach($file in $scripts){
            $tokens=$null;$parseErrors=$null
            $ast=[System.Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors)
            $parseErrors | Should -BeNullOrEmpty -Because $file.Name
            $commands=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst]},$true) | ForEach-Object {$_.GetCommandName()} | Where-Object {$_})
            $dynamic=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and -not $n.GetCommandName()},$true) | Where-Object { -not ($_.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Dot -and $_.CommandElements[0].Extent.Text -match '^\$f\.FullName$') -and -not ($file.Name -eq 'Start-Elevated.ps1' -and $_.Extent.Text -eq '& $scriptPath @argsToRun') -and -not ($file.Name -eq 'Invoke-WinInspector.ps1' -and $_.Extent.Text -match '^& \$csvRowWriter\b') })
            $dynamic | Should -BeNullOrEmpty -Because "$($file.Name): calls must have a statically known command; dot-sourcing is allowed only for the Get-ChildItem loop's `$f.FullName path"
            foreach($command in $commands){if($file.Name -eq "Start-Elevated.ps1" -and $command -eq "Start-Process"){$ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq "Start-Process" -and $n.Extent.Text -match "-Verb RunAs"},$true).Count | Should -Be 1;continue};$command | Should -Not -BeIn $allow.ForbiddenCommands -Because "$($file.Name) invokes $command";$command | Should -BeIn $allow.Commands -Because "$($file.Name) uses unreviewed command $command"}
            $typeNames=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.TypeExpressionAst]},$true)|ForEach-Object {$_.TypeName.FullName})
            foreach($typeName in $typeNames){if($typeName -match "(?i)^(?:System\.)?Runtime\.InteropServices\.Marshal$"){$ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Expression.Extent.Text -match "Marshal" -and $n.Member.Extent.Text -eq "ReleaseComObject"},$true).Count | Should -BeGreaterThan 0 -Because "Marshal is permitted only for ReleaseComObject";continue};$typeName | Should -Not -BeIn $allow.ForbiddenTypes -Because "$($file.Name) uses forbidden type $typeName";$typeName | Should -BeIn $allow.Types -Because "$($file.Name) uses unreviewed type $typeName"}
            foreach($command in @($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'New-Object'},$true))){if($command.Extent.Text -match '(?i)(?:^|\s)(Net\.WebClient|System\.Diagnostics\.Process)(?:\s|$)'){if($matches[1] -eq 'System.Diagnostics.Process'){'Diagnostics.Process' | Should -BeIn $allow.Types}else{$matches[1] | Should -BeIn $allow.Types}}}
            $writeCalls=@($ast.FindAll({param($n) if($n -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst]){return $false};$m=$n.Member.Extent.Text;if($m -match '^(Delete|Set\w*)$' -or $m -in @('CreateSubKey','CreateDirectory','Create','Move','Copy','Save','DeleteValue','DeleteSubKey','DeleteSubKeyTree')){return $true};if($m -in @('Replace','Start') -and $n.Expression.Extent.Text -match '(?i)\[(?:System\.)?(?:IO\.File|Diagnostics\.Process)\]'){return $true};$false},$true))
            if($file.Name -ne 'LogWriter.ps1'){$writeCalls | Should -BeNullOrEmpty -Because "$($file.Name) invokes a write-capable .NET method"}
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Extent.Text -eq 'OpenSubKey' -and $n.Arguments.Count -gt 1 -and $n.Arguments[1].Extent.Text -ne '$false'},$true) | Should -BeNullOrEmpty
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Extent.Text -eq 'Save'},$true) | Should -BeNullOrEmpty
            foreach($com in @($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'New-Object'},$true))){$text=$com.Extent.Text;if($text -match '-Com\w*\s+([^\s]+)'){$matches[1] | Should -BeIn $allow.ComProgIds}}
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Expression.Extent.Text -match 'scriptblock' -and $n.Member.Extent.Text -eq 'Create'},$true) | Should -BeNullOrEmpty
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Add-Type'},$true) | Should -BeNullOrEmpty
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'New-Object' -and ($n.CommandElements.Extent.Text -join ' ') -match 'ComObject' -and ($n.CommandElements.Extent.Text -join ' ') -notmatch 'WScript\.Shell'},$true) | Should -BeNullOrEmpty
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Extent.Text -eq 'Save'},$true) | Should -BeNullOrEmpty
            $startCalls=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Start-Process'},$true));if($file.Name -eq 'Start-Elevated.ps1'){$startCalls.Count | Should -Be 1;$startCalls[0].Extent.Text | Should -Match '-Verb RunAs'}else{$startCalls | Should -BeNullOrEmpty}
            $ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-Expression'},$true) | Should -BeNullOrEmpty
        }
    }
    It 'stores source scripts and launchers with UTF-8 BOM and CRLF' {
        $files=@($scripts)+@(Get-ChildItem $root -Filter '*.cmd')
        foreach($file in $files){$bytes=[IO.File]::ReadAllBytes($file.FullName);$text=[Text.Encoding]::UTF8.GetString($bytes);if($file.Extension -eq '.ps1'){$bytes[0..2] | Should -Be @(0xEF,0xBB,0xBF)};$text | Should -Match "`r`n";$text.Replace("`r`n",'') | Should -Not -Match "`n"}
    }
}
