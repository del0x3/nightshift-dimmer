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

:: Register Task Scheduler watchdog with battery resilience (5-minute heartbeat)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$a = New-ScheduledTaskAction -Execute '%~dp0NightModeService.exe'; $t = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5); $s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable; Register-ScheduledTask -TaskName 'NightModeWatchdog' -Action $a -Trigger $t -Settings $s -Force" >nul 2>&1

echo [NightMode] Starting background service...
start "" "%~dp0NightModeService.exe"

echo [NightMode] Installation complete. Service is running in background.
echo.
pause
