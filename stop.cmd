@echo off
setlocal
cd /d "%~dp0"
"%~dp0NightModeService.exe" stop
echo.
pause
