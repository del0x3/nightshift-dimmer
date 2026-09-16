@echo off
setlocal
cd /d "%~dp0"

echo [NightMode] Installing NightModeService...

:: Add to user registry autostart
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "NightModeService" /t REG_SZ /d "\"%~dp0NightModeService.exe\"" /f >nul 2>&1

:: Clean up legacy Startup shortcut if present (Run registry key is used for autostart)
powershell -NoProfile -Command "Remove-Item ([Environment]::GetFolderPath('Startup') + '\AutoNightMode.lnk') -ErrorAction SilentlyContinue" >nul 2>&1

:: Remove Windows autostart delay for instantaneous startup
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" /v "StartupDelayInMSec" /t REG_DWORD /d 0 /f >nul 2>&1

echo [NightMode] Starting background service...
start "" "%~dp0NightModeService.exe"

echo [NightMode] Installation complete. Service is running in background.
echo.
pause
