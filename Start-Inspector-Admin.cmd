@echo off
setlocal
set "POWERSHELL_TELEMETRY_OPTOUT=1"
set "POWERSHELL_UPDATECHECK=Off"
pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$a = @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File','%~dp0src\Invoke-WinInspector.ps1'); $a += $args; Start-Process -FilePath 'pwsh.exe' -Verb RunAs -Wait -ArgumentList $a" %*
echo.
pause
