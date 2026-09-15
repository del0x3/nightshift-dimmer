@echo off
setlocal
cd /d "%~dp0"

echo [NightMode] Stopping service and restoring original colors...
"%~dp0NightModeService.exe" stop >nul 2>&1

echo [NightMode] Removing from autostart...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "NightModeService" /f >nul 2>&1
powershell -NoProfile -Command "Remove-Item ([Environment]::GetFolderPath('Startup') + '\AutoNightMode.lnk') -ErrorAction SilentlyContinue" >nul 2>&1

echo [NightMode] Uninstalled successfully.
echo.
pause
