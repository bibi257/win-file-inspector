@echo off
setlocal
set "POWERSHELL_TELEMETRY_OPTOUT=1"
set "POWERSHELL_UPDATECHECK=Off"
pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$env:POWERSHELL_TELEMETRY_OPTOUT='1'; $env:POWERSHELL_UPDATECHECK='Off'; $scriptPath='%~dp0src\Invoke-WinInspector.ps1'; $items=@('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$scriptPath+'"')); $items += @($args | ForEach-Object {'"'+($_ -replace '"','\"')+'"'}); Start-Process -FilePath 'pwsh.exe' -Verb RunAs -Wait -ArgumentList ($items -join ' ')" %*
echo.
pause
