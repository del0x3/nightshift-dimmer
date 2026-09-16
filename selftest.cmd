@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& '%~dp0NightModeService.exe' selftest | Out-Host"
echo.
if /i "%~1" neq "/nopause" pause
