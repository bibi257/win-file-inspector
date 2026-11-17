[CmdletBinding()]
param([string]$ArgumentsFile,[Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
$env:POWERSHELL_TELEMETRY_OPTOUT='1';$env:POWERSHELL_UPDATECHECK='Off'
$scriptPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'src/Invoke-WinInspector.ps1'
if($ArgumentsFile){try{$argsToRun=@([IO.File]::ReadAllLines($ArgumentsFile));& $scriptPath @argsToRun}finally{Remove-Item -LiteralPath $ArgumentsFile -Force -ErrorAction SilentlyContinue};exit $LASTEXITCODE}
foreach($arg in $Arguments){if($arg.Contains("`r") -or $arg.Contains("`n")){[Console]::Error.WriteLine('改行を含む引数は指定できません');exit 2}}
$tempPath=Join-Path $env:TEMP ('wininspector-args-'+[guid]::NewGuid().ToString('N')+'.txt');[IO.File]::WriteAllLines($tempPath,$Arguments,[Text.UTF8Encoding]::new($false))
$items=@('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-ArgumentsFile',('"'+$tempPath+'"'));Start-Process -FilePath 'pwsh.exe' -Verb RunAs -Wait -ArgumentList ($items -join ' ')
