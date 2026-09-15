@echo off
setlocal
cd /d "%~dp0"

echo [NightMode] Building NightModeService.exe from source...

set CSC=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo [Error] csc.exe compiler not found in Windows directory.
    pause
    exit /b 1
)

"%CSC%" /target:winexe /optimize+ /platform:anycpu /r:System.Management.dll /out:"%~dp0NightModeService.exe" "%~dp0NightModeService.cs"

if %errorlevel% equ 0 (
    echo [NightMode] Build succeeded: NightModeService.exe created.
) else (
    echo [NightMode] Build failed with error code %errorlevel%.
)
echo.
pause
