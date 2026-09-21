@echo off
setlocal
cd /d "%~dp0"

echo ===================================================
echo   🌙 NightShift Dimmer - Git Updater
echo ===================================================
echo.

where git >nul 2>&1
if %errorlevel% neq 0 (
    echo [Error] Git is not installed or not found in PATH.
    echo Please install Git from https://git-scm.com/
    echo.
    pause
    exit /b 1
)

echo [NightMode] Fetching latest updates from GitHub...
git fetch origin master >nul 2>&1
if %errorlevel% neq 0 (
    echo [Warning] Could not reach GitHub. Check your internet connection.
    echo Continuing without update...
    echo.
    pause
    exit /b 1
)

for /f %%i in ('git rev-parse HEAD') do set LOCAL_HASH=%%i
for /f %%i in ('git rev-parse origin/master') do set REMOTE_HASH=%%i

if "%LOCAL_HASH%" == "%REMOTE_HASH%" (
    echo [NightMode] System is already up to date! (Commit: %LOCAL_HASH:~0,7%)
    echo.
    set /p FORCE="Do you want to recompile and restart the service anyway? (Y/N): "
    if /i not "%FORCE%" == "Y" (
        exit /b 0
    )
) else (
    echo [NightMode] New version detected!
    echo   Current: %LOCAL_HASH:~0,7%
    echo   Latest:  %REMOTE_HASH:~0,7%
    echo.
    echo [NightMode] Pulling latest changes...
    git pull origin master
    if %errorlevel% neq 0 (
        echo [Error] git pull failed.
        pause
        exit /b 1
    )
)

echo.
echo [NightMode] Stopping running service for safe upgrade...
"%~dp0NightModeService.exe" stop >nul 2>&1
timeout /t 1 /nobreak >nul 2>&1

echo [NightMode] Recompiling NightModeService.exe from source...
set CSC=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo [Error] csc.exe compiler not found in Windows directory.
    pause
    exit /b 1
)

"%CSC%" /target:winexe /optimize+ /platform:anycpu /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0NightModeService.exe" "%~dp0NightModeService.cs"

if %errorlevel% neq 0 (
    echo [Error] Compilation failed!
    pause
    exit /b 1
)

echo [NightMode] Compilation successful.
echo [NightMode] Relaunching service in background...
start "" "%~dp0NightModeService.exe"

echo [NightMode] Refreshing watchdog scheduled task (5-minute interval)...
schtasks /create /tn "NightModeWatchdog" /tr "\"%~dp0NightModeService.exe\"" /sc minute /mo 5 /f >nul 2>&1

echo.
echo ===================================================
echo   [SUCCESS] NightShift Dimmer updated and running!
echo ===================================================
echo.
timeout /t 3
