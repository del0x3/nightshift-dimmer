@echo off
setlocal
cd /d "%~dp0"
start "" "%~dp0NightModeService.exe"
echo [NightMode] Service launched in background.
