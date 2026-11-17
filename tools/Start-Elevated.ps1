[CmdletBinding()]
param([string]$ArgumentsFile,[Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
$env:POWERSHELL_TELEMETRY_OPTOUT='1';$env:POWERSHELL_UPDATECHECK='Off'
$Arguments=@($Arguments)
$scriptPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'src/Invoke-WinInspector.ps1'
if($ArgumentsFile){$tempRoot=if($env:TEMP){[IO.Path]::GetFullPath($env:TEMP)}else{[IO.Path]::GetFullPath([IO.Path]::GetTempPath())};$argumentPath=[IO.Path]::GetFullPath($ArgumentsFile);$prefix=[IO.Path]::GetFullPath((Join-Path $tempRoot 'wininspector-args-'));if(!$argumentPath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($argumentPath) -notmatch '^wininspector-args-'){[Console]::Error.WriteLine('ArgumentsFile は TEMP 配下の wininspector-args- ファイルに限ります');exit 2};try{$argsToRun=@([IO.File]::ReadAllLines($argumentPath));& $scriptPath @argsToRun}finally{Remove-Item -LiteralPath $argumentPath -Force -ErrorAction SilentlyContinue};exit $LASTEXITCODE}
foreach($arg in $Arguments){if($arg.Contains("`r") -or $arg.Contains("`n")){[Console]::Error.WriteLine('改行を含む引数は指定できません');exit 2}}
$tempRoot=if($env:TEMP){$env:TEMP}else{[IO.Path]::GetTempPath()};$tempPath=Join-Path $tempRoot ('wininspector-args-'+[guid]::NewGuid().ToString('N')+'.txt')
try{[IO.File]::WriteAllLines($tempPath,$Arguments,[Text.UTF8Encoding]::new($false));$items=@('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-ArgumentsFile',('"'+$tempPath+'"'));$process=Start-Process -FilePath 'pwsh.exe' -Verb RunAs -Wait -PassThru -ArgumentList ($items -join ' ');exit $process.ExitCode}finally{Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue}
