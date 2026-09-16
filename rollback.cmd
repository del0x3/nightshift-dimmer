@echo off
setlocal
cd /d "%~dp0"

echo ===================================================
echo   ⏪ NightShift Dimmer - Rollback Utility
echo ===================================================
echo.

where git >nul 2>&1
if %errorlevel% neq 0 (
    echo [Error] Git is not installed or not found in PATH.
    pause
    exit /b 1
)

for /f %%i in ('git rev-parse HEAD') do set CURRENT_COMMIT=%%i
for /f %%i in ('git log -1 --format="%%s"') do set CURRENT_MSG=%%i

echo Current Active Version:
echo   Commit:  %CURRENT_COMMIT:~0,7%
echo   Message: %CURRENT_MSG%
echo.

set /p CONFIRM="Are you sure you want to rollback to the previous commit (HEAD~1)? (Y/N): "
if /i not "%CONFIRM%" == "Y" (
    echo Rollback cancelled.
    exit /b 0
)

echo.
echo [NightMode] Stopping service...
"%~dp0NightModeService.exe" stop >nul 2>&1
timeout /t 1 /nobreak >nul 2>&1

echo [NightMode] Rolling back Git repository to HEAD~1...
git reset --hard HEAD~1
if %errorlevel% neq 0 (
    echo [Error] git reset failed!
    pause
    exit /b 1
)

echo [NightMode] Recompiling previous version...
call "%~dp0build.cmd" /ci
if %errorlevel% neq 0 (
    echo [Error] Compilation of rolled back version failed!
    pause
    exit /b 1
)

echo [NightMode] Starting rolled back service...
start "" "%~dp0NightModeService.exe"

for /f %%i in ('git rev-parse HEAD') do set NEW_COMMIT=%%i
for /f %%i in ('git log -1 --format="%%s"') do set NEW_MSG=%%i

echo.
echo ===================================================
echo   [SUCCESS] Rollback complete!
echo   Active Commit:  %NEW_COMMIT:~0,7%
echo   Active Message: %NEW_MSG%
echo ===================================================
echo.
timeout /t 3
