@echo off
setlocal
set "POWERSHELL_TELEMETRY_OPTOUT=1"
set "POWERSHELL_UPDATECHECK=Off"
pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0src\Invoke-WinInspector.ps1" %*
echo.
pause
