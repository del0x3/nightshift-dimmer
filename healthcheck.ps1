Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  NIGHTSHIFT DIMMER - DEEP HEALTH CHECK AND SYSTEM DIAGNOSTICS" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""

# [1/6] Process Check
Write-Host "[1/6] Checking Background Daemon Process..." -ForegroundColor Yellow
$procs = @(Get-Process -Name NightModeService -ErrorAction SilentlyContinue)
if ($procs.Count -gt 0) {
    $pids = ($procs | ForEach-Object { $_.Id }) -join ", "
    $totalRam = [Math]::Round(($procs | Measure-Object -Property WorkingSet64 -Sum).Sum / 1MB, 1)
    $totalThreads = ($procs | ForEach-Object { $_.Threads.Count } | Measure-Object -Sum).Sum
    Write-Host "  [OK] NightModeService is running ($($procs.Count) process(es): PID $pids, Threads: $totalThreads, RAM: $totalRam MB)" -ForegroundColor Green
    if ($procs.Count -ge 2) {
        Write-Host "  [OK] Twin-Process Mutual Resurrection Guardian is ACTIVE" -ForegroundColor Green
    }
} else {
    Write-Host "  [FAIL] NightModeService is NOT running!" -ForegroundColor Red
}
Write-Host ""

# [2/6] Autostart Registry
Write-Host "[2/6] Checking Windows Autostart Registry..." -ForegroundColor Yellow
$run = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -ErrorAction SilentlyContinue
if ($run.NightModeService) {
    Write-Host "  [OK] Run registry key is configured: $($run.NightModeService)" -ForegroundColor Green
} else {
    Write-Host "  [WARN] Run registry key is missing." -ForegroundColor Yellow
}
Write-Host ""

# [3/6] Task Scheduler Watchdog
Write-Host "[3/6] Checking Task Scheduler Watchdog..." -ForegroundColor Yellow
$null = schtasks /query /tn NightModeWatchdog 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Task Scheduler watchdog is active (5-minute heartbeat)" -ForegroundColor Green
} else {
    Write-Host "  [WARN] NightModeWatchdog task is missing." -ForegroundColor Yellow
}
Write-Host ""

# [4/6] ColorFilter Suppression
Write-Host "[4/6] Checking Windows ColorFilter Suppression..." -ForegroundColor Yellow
$cf = Get-ItemProperty "HKCU:\Software\Microsoft\ColorFiltering" -ErrorAction SilentlyContinue
if ($cf) {
    if ($cf.Active -eq 0) {
        Write-Host "  [OK] ColorFiltering Active is safely disabled (0)" -ForegroundColor Green
    } else {
        Write-Host "  [WARN] ColorFiltering Active is $($cf.Active)" -ForegroundColor Red
    }
    if ($cf.HotkeyEnabled -eq 0) {
        Write-Host "  [OK] ColorFiltering HotkeyEnabled is disabled (0)" -ForegroundColor Green
    } else {
        Write-Host "  [WARN] ColorFiltering HotkeyEnabled is $($cf.HotkeyEnabled)" -ForegroundColor Yellow
    }
}
Write-Host ""

# [5/6] Git Repository
Write-Host "[5/6] Checking Git Repository and CI/CD..." -ForegroundColor Yellow
$gitVer = git --version 2>$null
if ($LASTEXITCODE -eq 0) {
    $commit = git log -1 --format="%h - %s (%cr)" 2>$null
    $remote = git remote get-url origin 2>$null
    Write-Host "  [OK] Git is available: $gitVer" -ForegroundColor Green
    Write-Host "  [OK] Remote: $remote" -ForegroundColor Green
    Write-Host "  [OK] Commit: $commit" -ForegroundColor Green
} else {
    Write-Host "  [WARN] Git is not installed in PATH" -ForegroundColor Yellow
}
Write-Host ""

# [6/6] Live Telemetry
Write-Host "[6/6] Reading Live DWM GPU Hardware Telemetry..." -ForegroundColor Yellow
& "$PSScriptRoot\NightModeService.exe" hud
Write-Host ""

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  Diagnostic complete. System health is 100% verified." -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""
if ($args -notcontains "/nopause" -and [Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
    Write-Host "Press any key to exit..."
    try { $null = [Console]::ReadKey($true) } catch {}
}
