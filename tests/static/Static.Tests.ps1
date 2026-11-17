BeforeAll {
    $root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $allow=Import-PowerShellDataFile (Join-Path $root 'tools/allowlist.psd1')
    $scripts=@(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter '*.ps1')+@(Get-ChildItem (Join-Path $root 'tools') -Filter '*.ps1')
    function Get-StaticViolations { param($Ast)
        $violations=[Collections.Generic.List[string]]::new()
        foreach($n in $Ast.FindAll({param($x)$x -is [System.Management.Automation.Language.InvokeMemberExpressionAst]},$true)){
            $member=$n.Member.Extent.Text
            if($member -in @('InvokeScript','NewScriptBlock')){$violations.Add("dynamic:$member")}
            if($n.Expression.Extent.Text -match '(?i)Marshal' -and $member -ne 'ReleaseComObject'){$violations.Add("marshal:$member")}
            if($member -in @('WriteAllText','AppendAllText','WriteAllBytes','WriteAllLines','OpenWrite')){$violations.Add("write:$member")}
        }
        foreach($n in $Ast.FindAll({param($x)$x -is [System.Management.Automation.Language.CommandAst] -and $x.GetCommandName() -eq 'New-Object'},$true)){
            $parts=@($n.CommandElements | ForEach-Object {$_.Extent.Text});$type=$null
            for($i=1;$i -lt $parts.Count;$i++){if($parts[$i] -match '^-TypeName$' -and $i+1 -lt $parts.Count){$type=$parts[$i+1];break}}
            if(!$type){$type=$parts | Where-Object {$_ -notmatch '^-' -and $_ -ne 'New-Object'} | Select-Object -First 1}
            if($type -and $type -notmatch '^(?i:Com\w*)$' -and -not ($parts | Where-Object {$_ -match '^-Com\w*$'})){$violations.Add("newobject:$type")}
        }
        return @($violations)
    }
}
Describe 'Static safety checks' {
    It 'rejects intentional dynamic-call and write-method violation samples' {
        $sample='& $command; [IO.File]::Delete(''sample'')'
        $tokens=$null;$parseErrors=$null;$ast=[System.Management.Automation.Language.Parser]::ParseInput($sample,[ref]$tokens,[ref]$parseErrors)
        @($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and -not $n.GetCommandName()},$true)).Count | Should -Be 1
        @($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Extent.Text -eq 'Delete'},$true)).Count | Should -Be 1
    }
    It 'detects reviewed dangerous AST shapes in negative samples' {
        $samples=@('$ExecutionContext.InvokeCommand.InvokeScript($code)','$x.NewScriptBlock($text)','New-Object Net.WebClient','New-Object -TypeName System.Diagnostics.Process','[IO.File]::WriteAllText("x","y")','[IO.File]::AppendAllText("x","y")','[IO.File]::WriteAllBytes("x",$b)','[IO.File]::WriteAllLines("x",$a)','[IO.File]::OpenWrite("x")','[Runtime.InteropServices.Marshal]::GetTypeFromCLSID($id)')
        foreach($sample in $samples){$t=$null;$e=$null;$ast=[System.Management.Automation.Language.Parser]::ParseInput($sample,[ref]$t,[ref]$e);(Get-StaticViolations $ast).Count | Should -BeGreaterThan 0 -Because $sample}
    }
    It 'parses every source script and rejects dangerous commands and dynamic execution' {
        foreach($file in $scripts){
            $tokens=$null;$parseErrors=$null
            $ast=[System.Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors)
            $parseErrors | Should -BeNullOrEmpty -Because $file.Name
            $violations=@(Get-StaticViolations $ast)
            foreach($v in $violations){if($file.Name -eq 'LogWriter.ps1' -and $v -eq 'write:WriteAllText'){$ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Extent.Text -eq 'WriteAllText' -and $n.Arguments[0].Extent.Text -eq '$path'},$true).Count | Should -Be 1;continue};if($file.Name -eq 'Start-Elevated.ps1' -and $v -eq 'write:WriteAllLines'){$ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Extent.Text -eq 'WriteAllLines' -and $n.Arguments[0].Extent.Text -eq '$tempPath'},$true).Count | Should -Be 1;continue};if($v -match '^newobject:'){$type=$v.Substring(10);if($type -match '^System\.'){$type=$type.Substring(7)};$type | Should -BeIn $allow.Types -Because "$($file.Name) uses unreviewed New-Object type $type";continue};$v | Should -BeNullOrEmpty -Because "$($file.Name) has violation $v"}
            $commands=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst]},$true) | ForEach-Object {$_.GetCommandName()} | Where-Object {$_})
            $dynamic=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and -not $n.GetCommandName()},$true) | Where-Object { -not ($_.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Dot -and $_.CommandElements[0].Extent.Text -match '^\$f\.FullName$') -and -not ($file.Name -eq 'Start-Elevated.ps1' -and $_.Extent.Text -eq '& $scriptPath @argsToRun') -and -not ($file.Name -eq 'Invoke-WinInspector.ps1' -and $_.Extent.Text -match '^& \$csvRowWriter\b') })
            $dynamic | Should -BeNullOrEmpty -Because "$($file.Name): calls must have a statically known command; dot-sourcing is allowed only for the Get-ChildItem loop's `$f.FullName path"
            foreach($command in $commands){if($file.Name -eq "Start-Elevated.ps1" -and $command -eq "Start-Process"){$ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq "Start-Process" -and $n.Extent.Text -match "-Verb RunAs"},$true).Count | Should -Be 1;continue};$command | Should -Not -BeIn $allow.ForbiddenCommands -Because "$($file.Name) invokes $command";$command | Should -BeIn $allow.Commands -Because "$($file.Name) uses unreviewed command $command"}
            $typeNames=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.TypeExpressionAst]},$true)|ForEach-Object {$_.TypeName.FullName})
            foreach($typeName in $typeNames){if($typeName -match "(?i)^(?:System\.)?Runtime\.InteropServices\.Marshal$"){$ast.FindAll({param($n)$n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Expression.Extent.Text -match "Marshal" -and $n.Member.Extent.Text -eq "ReleaseComObject"},$true).Count | Should -BeGreaterThan 0 -Because "Marshal is permitted only for ReleaseComObject";continue};$typeName | Should -Not -BeIn $allow.ForbiddenTypes -Because "$($file.Name) uses forbidden type $typeName";$typeName | Should -BeIn $allow.Types -Because "$($file.Name) uses unreviewed type $typeName"}
            foreach($command in @($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'New-Object'},$true))){if($command.Extent.Text -match '(?i)(?:^|\s)(Net\.WebClient|System\.Diagnostics\.Process)(?:\s|$)'){if($matches[1] -eq 'System.Diagnostics.Process'){'Diagnostics.Process' | Should -BeIn $allow.Types}else{$matches[1] | Should -BeIn $allow.Types}}}
            $writeCalls=@($ast.FindAll({param($n) if($n -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst]){return $false};$m=$n.Member.Extent.Text;if($m -match '^(Delete|Set\w*)$' -or $m -in @('CreateSubKey','CreateDirectory','Create','Move','Copy','Save','DeleteValue','DeleteSubKey','DeleteSubKeyTree','WriteAllText','AppendAllText','WriteAllBytes','WriteAllLines','OpenWrite')){return $true};if($m -in @('Replace','Start') -and $n.Expression.Extent.Text -match '(?i)\[(?:System\.)?(?:IO\.File|Diagnostics\.Process)\]'){return $true};$false},$true))
            $newWriteCalls=@($writeCalls|Where-Object {$_.Member.Extent.Text -in @('WriteAllText','AppendAllText','WriteAllBytes','WriteAllLines','OpenWrite')});$allowedWrites=@();if($file.Name -eq 'LogWriter.ps1'){$allowedWrites=@($newWriteCalls|Where-Object {$_.Member.Extent.Text -eq 'WriteAllText' -and $_.Arguments[0].Extent.Text -eq '$path'})};if($file.Name -eq 'Start-Elevated.ps1'){$allowedWrites=@($newWriteCalls|Where-Object {$_.Member.Extent.Text -eq 'WriteAllLines' -and $_.Arguments[0].Extent.Text -eq '$tempPath'})};$newWriteCalls|Where-Object {$allowedWrites -notcontains $_} | Should -BeNullOrEmpty -Because "$($file.Name) invokes an unapproved write-capable .NET method";if($file.Name -ne 'LogWriter.ps1'){$writeCalls|Where-Object {$_.Member.Extent.Text -notin @('WriteAllText','AppendAllText','WriteAllBytes','WriteAllLines','OpenWrite')}|Should -BeNullOrEmpty -Because "$($file.Name) invokes a write-capable .NET method"};if($allowedWrites.Count){$allowedWrites.Count | Should -Be 1}
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
