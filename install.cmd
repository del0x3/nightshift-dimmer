@echo off
setlocal
cd /d "%~dp0"

echo [NightMode] Installing NightModeService...

:: Add to user registry autostart
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "NightModeService" /t REG_SZ /d "\"%~dp0NightModeService.exe\"" /f >nul 2>&1

:: Create Startup shortcut via PowerShell
powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([Environment]::GetFolderPath('Startup') + '\AutoNightMode.lnk'); $s.TargetPath = '%~dp0NightModeService.exe'; $s.WorkingDirectory = '%~dp0'; $s.Save()" >nul 2>&1

:: Remove Windows autostart delay for instantaneous startup
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" /v "StartupDelayInMSec" /t REG_DWORD /d 0 /f >nul 2>&1

echo [NightMode] Starting background service...
start "" "%~dp0NightModeService.exe"

echo [NightMode] Installation complete. Service is running in background.
echo.
pause
