# 🌙 NightShift Dimmer (Grayscale & White-Point Dimmer for Windows)

> **Autonomous, eye-friendly grayscale display daemon for Windows 11 / 10 with calibrated white-point luminance dimming.**

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue.svg)](https://microsoft.com)
[![Runtime](https://img.shields.io/badge/.NET-Framework%204.0%2B-green.svg)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-MIT-purple.svg)](LICENSE)
[![CPU Usage](https://img.shields.io/badge/CPU-0.0%25-brightgreen.svg)]()

---

## 🧐 The Problem

1. **Standard Windows Night Light / f.lux:** Only shifts color temperature to amber/orange. In a dark room, bright white backgrounds on documents, browsers, and text editors still emit harsh light that strains the eyes.
2. **Standard Windows Grayscale (`Win + Ctrl + C`):** Turns the screen black-and-white, but pure white pixels (`#FFFFFF`) remain at **100% luminance**, causing significant glare. Furthermore, shortcut simulation frequently conflicts with Windows shortcuts (e.g., triggering Microsoft Copilot or Narrator).
3. **Screen Brightness controls alone:** Dropping monitor brightness too low ruins contrast, crushes dark text, and induces PWM flicker on OLED/LCD panels.

## 💡 The Solution

**NightShift Dimmer** operates directly at the Windows Desktop Window Manager (DWM) level via the native **Windows Magnification API (`Magnification.dll`)**:

- **True Luminance Grayscale:** Converts colors using standard Rec. 709 luminance weights:
  $$\text{Luminance} = 0.2126 \cdot R + 0.7152 \cdot G + 0.0722 \cdot B$$
- **Hardware-Level White-Point Dimming:** Multiplies the color matrix by a dimming coefficient (default: `0.75`). Pure white (`#FFFFFF`) is softly clamped to a smooth, matte paper-like gray (`#BFBFBF`), completely eliminating glare while keeping typography razor-sharp.
- **Zero Keystrokes / Silent Daemon:** Uses Win32 API calls directly. Never simulates keystrokes (`keybd_event` / `SendInput`), never steals window focus, and never interrupts games or typing.
- **100% Autonomous:** Runs in the background, survives reboots, sleep/wake cycles, monitor disconnects, and lock screens.

---

## 🚀 Quick Start

1. Download or clone this repository.
2. Double-click **`install.cmd`**:
   - Registers the service into Windows Autostart (`HKCU\...\Run` & Startup folder).
   - Starts the daemon immediately in the background.

Done! The daemon will now automatically manage your display according to your schedule.

---

## 🛠️ Management Scripts

| Script | Action |
|---|---|
| [`install.cmd`](install.cmd) | Installs autostart registry keys and launches the background service. |
| [`uninstall.cmd`](uninstall.cmd) | Stops service, restores original display colors, and removes autostart. |
| [`start.cmd`](start.cmd) | Starts the background daemon manually. |
| [`stop.cmd`](stop.cmd) | Stops the daemon and instantly restores normal full-color display. |
| [`toggle.cmd`](toggle.cmd) | Instantly toggles between Day (color) and Night (grayscale + dimming) mode. |
| [`status.cmd`](status.cmd) | Displays active configuration, current local time, and operational status. |
| [`build.cmd`](build.cmd) | Recompiles `NightModeService.exe` from source using Windows' built-in C# compiler. |
| [`update.cmd`](update.cmd) | Pulls latest changes from GitHub, rebuilds, and restarts the service. |
| [`rollback.cmd`](rollback.cmd) | Reverts repository to previous commit, recompiles, and hot-swaps service. |
| [`dashboard.cmd`](dashboard.cmd) | Opens live real-time ASCII telemetry dashboard in terminal. |
| [`healthcheck.cmd`](healthcheck.cmd) | Runs deep 6-point diagnostics of daemon, DWM, registry, and Git. |

---

## ⚙️ Configuration (`config.json`)

Settings are stored in [`config.json`](config.json) and are hot-reloaded automatically by the daemon without requiring a restart:

```json
{
  "start_time": "21:00",
  "end_time": "05:00",
  "white_dim": 0.75,
  "night_brightness": 65,
  "day_brightness": 70,
  "adjust_brightness": true
}
```

### Parameter Details

| Parameter | Type | Default | Description |
|---|---|---|---|
| `start_time` | string (`HH:mm`) | `"21:00"` | Time in 24-hour format when night mode activates. |
| `end_time` | string (`HH:mm`) | `"05:00"` | Time in 24-hour format when night mode deactivates. |
| `white_dim` | float (`0.0` - `1.0`) | `0.75` | White-point scale factor. `0.75` caps pure white to 75% peak luminance. |
| `night_brightness` | integer (`0` - `100`) | `65` | Target monitor backlight level during nighttime. |
| `day_brightness` | integer (`0` - `100`) | `70` | Restored backlight level during daytime. |
| `adjust_brightness`| boolean | `true` | Whether to adjust backlight via WMI (`WmiSetBrightness`). |

---

## 💻 CLI Commands

You can interact with the service directly from Command Prompt or PowerShell:

```cmd
# Show current status and active schedule
NightModeService.exe status

# Toggle between day and night mode immediately
NightModeService.exe toggle

# Force night mode ON (manual override)
NightModeService.exe on

# Force night mode OFF (manual override)
NightModeService.exe off

# Stop service and reset display to factory defaults
NightModeService.exe stop

# Display rich Git telemetry (branch, commit, sync status, working tree)
NightModeService.exe git

# Check for updates on GitHub, pull, verify build, and hot-swap
NightModeService.exe update

# Rollback to previous commit and hot-swap
NightModeService.exe rollback

# Self-repair any missing or corrupted workspace files from Git
NightModeService.exe repair

# Clear any manual override and return to automatic schedule
NightModeService.exe reset
```

---

## 🏗️ Architecture & How It Works

```
                     +---------------------------------------+
                     |         NightModeService.exe          |
                     |  (Background WinExe, ~25MB RAM, 0% CPU) |
                     +---------------------------------------+
                                        |
       +--------------------------------+--------------------------------+
       |                                                                 |
       v                                                                 v
+-------------------------------+                     +-----------------------------------+
|     Attach to User Desktop    |                     |       Config Hot-Reload Loop      |
|  SetThreadDesktop("default")  |                     | Checks config.json every 10s      |
+-------------------------------+                     +-----------------------------------+
       |                                                                 |
       v                                                                 v
+-------------------------------+                     +-----------------------------------+
|      Magnification API        |                     |      WMI Backlight Control        |
|  MagSetFullscreenColorEffect  |                     |   WmiMonitorBrightnessMethods     |
|  (5x5 100-byte Color Matrix)  |                     |  (Non-blocking ThreadPool async)  |
+-------------------------------+                     +-----------------------------------+
```

1. **Desktop Isolation Handling:** Windows services and background shells often run in isolated desktops or sessions. The service explicitly binds its execution thread to the interactive user session via `OpenDesktop("default")` and `SetThreadDesktop()`.
2. **Direct DWM Matrix Transformation:** By injecting a $5 \times 5$ `MAGCOLOREFFECT` structure into `Magnification.dll`, color translation is performed in GPU hardware without any screen latency or frame drops.
3. **Non-blocking WMI Operations:** Monitor backlight adjustments execute asynchronously in a `ThreadPool` work item, preventing system pauses.

---

## 🔨 Building From Source

No Visual Studio, SDK, or package managers required. Every modern Windows installation includes the C# compiler:

```cmd
build.cmd
```

Or manually:
```cmd
%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:anycpu /r:System.Management.dll /out:NightModeService.exe NightModeService.cs
```

---

## 📄 License

This project is open-source and licensed under the [MIT License](LICENSE).
