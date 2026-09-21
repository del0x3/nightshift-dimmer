@echo off
setlocal
cd /d "%~dp0"
"%~dp0NightModeService.exe" snooze %*
