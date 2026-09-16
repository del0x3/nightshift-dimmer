@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo [NightMode] Building NightModeService.exe from source...

set CSC=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo [Error] csc.exe compiler not found in Windows directory.
    if /i "%~1" neq "/ci" if /i "%~1" neq "/nopause" pause
    exit /b 1
)

if /i "%~1" == "/test" (
    "%CSC%" /target:winexe /optimize+ /platform:anycpu /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%TEMP%\NightModeService.test.exe" "%~dp0NightModeService.cs" >nul 2>&1
    set ERR=!errorlevel!
    if exist "%TEMP%\NightModeService.test.exe" del "%TEMP%\NightModeService.test.exe" >nul 2>&1
    exit /b !ERR!
)

"%CSC%" /target:winexe /optimize+ /platform:anycpu /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0NightModeService.exe" "%~dp0NightModeService.cs"

if %errorlevel% equ 0 (
    echo [NightMode] Build succeeded: NightModeService.exe created.
) else (
    echo [NightMode] Build failed with error code %errorlevel%.
)

echo.
if /i "%~1" neq "/ci" if /i "%~1" neq "/nopause" pause
exit /b %errorlevel%
