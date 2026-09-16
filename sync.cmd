@echo off
setlocal
cd /d "%~dp0"

echo [NightMode] Synchronizing with GitHub and checking for updates...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& '%~dp0NightModeService.exe' sync | Out-Host"
echo.
if /i "%~1" neq "/nopause" pause
