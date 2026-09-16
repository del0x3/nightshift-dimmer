@echo off
setlocal
cd /d "%~dp0"
if "%~1"=="" (
    "%~dp0NightModeService.exe" hud --live
) else (
    "%~dp0NightModeService.exe" hud %*
)
