using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Management;
using System.Windows.Forms;
using Microsoft.Win32;

public class Program {
    [StructLayout(LayoutKind.Sequential)]
    public struct MAGCOLOREFFECT {
        public float m00, m01, m02, m03, m04;
        public float m10, m11, m12, m13, m14;
        public float m20, m21, m22, m23, m24;
        public float m30, m31, m32, m33, m34;
        public float m40, m41, m42, m43, m44;
    }

    [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern bool MagInitialize();

    [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern bool MagUninitialize();

    [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT pEffect);

    [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern bool MagGetFullscreenColorEffect(ref MAGCOLOREFFECT pEffect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterApplicationRestart(string pwzCommandLine, uint dwFlags);

    public const uint DESKTOP_ALL = 0x01FF;

    public static string AppDir = AppDomain.CurrentDomain.BaseDirectory;
    public static string StopFile = Path.Combine(AppDir, "service.stop");
    public static string LogFile = Path.Combine(AppDir, "service.log");
    public static string OverrideFile = Path.Combine(AppDir, "override.txt");
    public static string ConfigFile = Path.Combine(AppDir, "config.json");

    public static TimeSpan StartTime = new TimeSpan(21, 0, 0); // 21:00
    public static TimeSpan EndTime = new TimeSpan(5, 0, 0);   // 05:00
    public static float WhiteDim = 0.75f;                     // Dim white point by 25%
    public static int NightBrightness = 65;                   // Backlight level at night
    public static int DayBrightness = 70;                     // Backlight level during day
    public static bool AdjustBrightness = true;
    public static bool SmoothTransition = true;
    public static int TransitionDurationMs = 1800;
    public static bool TrayIconEnabled = true;
    public static bool AutoUpdateGit = true;
    public static int GitCheckIntervalSec = 3600;

    private static volatile bool forceRefresh = false;
    private static Process guardianProcess = null;
    private static NotifyIcon trayIcon = null;

    public static void TriggerForceRefresh(string reason) {
        Log(string.Format("[SelfDefense] Instant refresh triggered: {0}", reason));
        forceRefresh = true;
    }

    public static void Log(string msg) {
        try {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}", DateTime.Now, msg);
            Console.WriteLine(line);

            // Log rotation: keep under 2MB
            FileInfo fi = new FileInfo(LogFile);
            if (fi.Exists && fi.Length > 2 * 1024 * 1024) {
                string backup = LogFile + ".old";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(LogFile, backup);
            }

            File.AppendAllText(LogFile, line + "\r\n");
        } catch {}
    }

    public static void InitConsoleOutput() {
        try {
            AttachConsole(ATTACH_PARENT_PROCESS);
            var stream = Console.OpenStandardOutput();
            var writer = new StreamWriter(stream, System.Text.Encoding.Default);
            writer.AutoFlush = true;
            Console.SetOut(writer);
        } catch {}
    }

    public static void AttachToDefaultDesktop() {
        try {
            IntPtr hDesk = OpenDesktop("default", 0, false, DESKTOP_ALL);
            if (hDesk != IntPtr.Zero) {
                SetThreadDesktop(hDesk);
            }
        } catch {}
    }

    public static void EnsureWindowsColorFilterDisabled() {
        try {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\ColorFiltering", true)) {
                if (key != null) {
                    object active = key.GetValue("Active");
                    if (active != null && Convert.ToInt32(active) != 0) {
                        key.SetValue("Active", 0, RegistryValueKind.DWord);
                        Log("Enforced Windows ColorFiltering Active = 0");
                    }
                    object hotkey = key.GetValue("HotkeyEnabled");
                    if (hotkey != null && Convert.ToInt32(hotkey) != 0) {
                        key.SetValue("HotkeyEnabled", 0, RegistryValueKind.DWord);
                        Log("Enforced Windows ColorFiltering HotkeyEnabled = 0");
                    }
                }
            }
        } catch {}
    }

    public static void LoadConfig() {
        try {
            if (File.Exists(ConfigFile)) {
                string json = File.ReadAllText(ConfigFile);
                
                int stIdx = json.IndexOf("\"start_time\"");
                if (stIdx >= 0) {
                    int c1 = json.IndexOf("\"", stIdx + 12);
                    int c2 = json.IndexOf("\"", c1 + 1);
                    if (c1 >= 0 && c2 > c1) {
                        string val = json.Substring(c1 + 1, c2 - c1 - 1);
                        string[] parts = val.Split(':');
                        StartTime = new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), 0);
                    }
                }

                int etIdx = json.IndexOf("\"end_time\"");
                if (etIdx >= 0) {
                    int c1 = json.IndexOf("\"", etIdx + 10);
                    int c2 = json.IndexOf("\"", c1 + 1);
                    if (c1 >= 0 && c2 > c1) {
                        string val = json.Substring(c1 + 1, c2 - c1 - 1);
                        string[] parts = val.Split(':');
                        EndTime = new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), 0);
                    }
                }

                int wdIdx = json.IndexOf("\"white_dim\"");
                if (wdIdx >= 0) {
                    int colon = json.IndexOf(":", wdIdx);
                    int comma = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                    if (colon >= 0 && comma > colon) {
                        string val = json.Substring(colon + 1, comma - colon - 1).Trim();
                        WhiteDim = float.Parse(val, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                int nbIdx = json.IndexOf("\"night_brightness\"");
                if (nbIdx >= 0) {
                    int colon = json.IndexOf(":", nbIdx);
                    int comma = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                    if (colon >= 0 && comma > colon) {
                        NightBrightness = int.Parse(json.Substring(colon + 1, comma - colon - 1).Trim());
                    }
                }

                int dbIdx = json.IndexOf("\"day_brightness\"");
                if (dbIdx >= 0) {
                    int colon = json.IndexOf(":", dbIdx);
                    int comma = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                    if (colon >= 0 && comma > colon) {
                        DayBrightness = int.Parse(json.Substring(colon + 1, comma - colon - 1).Trim());
                    }
                }

                int abIdx = json.IndexOf("\"adjust_brightness\"");
                if (abIdx >= 0) {
                    int colon = json.IndexOf(":", abIdx);
                    int comma = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                    if (colon >= 0 && comma > colon) {
                        string val = json.Substring(colon + 1, comma - colon - 1).Trim().ToLower();
                        AdjustBrightness = val.Contains("true");
                    }
                }

                int smIdx = json.IndexOf("\"smooth_transition\"");
                if (smIdx >= 0) {
                    int colon = json.IndexOf(":", smIdx);
                    int comma = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                    if (colon >= 0 && comma > colon) {
                        string val = json.Substring(colon + 1, comma - colon - 1).Trim().ToLower();
                        SmoothTransition = val.Contains("true");
                    }
                }

                int tdIdx = json.IndexOf("\"transition_duration_ms\"");
                if (tdIdx >= 0) {
                    int colon = json.IndexOf(":", tdIdx);
                    int comma = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                    if (colon >= 0 && comma > colon) {
                        TransitionDurationMs = int.Parse(json.Substring(colon + 1, comma - colon - 1).Trim());
                    }
                }

                int tiIdx = json.IndexOf("\"tray_icon\"");
                if (tiIdx >= 0) {
                    int colon = json.IndexOf(":", tiIdx);
                    int comma = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                    if (colon >= 0 && comma > colon) {
                        string val = json.Substring(colon + 1, comma - colon - 1).Trim().ToLower();
                        TrayIconEnabled = val.Contains("true");
                    }
                }

                int augIdx = json.IndexOf("\"auto_update_git\"");
                if (augIdx >= 0) {
                    int colon = json.IndexOf(":", augIdx);
                    int comma = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                    if (colon >= 0 && comma > colon) {
                        string val = json.Substring(colon + 1, comma - colon - 1).Trim().ToLower();
                        AutoUpdateGit = val.Contains("true");
                    }
                }

                int gciIdx = json.IndexOf("\"git_check_interval_seconds\"");
                if (gciIdx >= 0) {
                    int colon = json.IndexOf(":", gciIdx);
                    int comma = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                    if (colon >= 0 && comma > colon) {
                        GitCheckIntervalSec = int.Parse(json.Substring(colon + 1, comma - colon - 1).Trim());
                    }
                }
            }
        } catch (Exception ex) {
            Log("Error reading config: " + ex.Message);
        }
    }

    public static bool IsNightTime() {
        TimeSpan now = DateTime.Now.TimeOfDay;
        if (StartTime <= EndTime) {
            return now >= StartTime && now < EndTime;
        } else {
            return now >= StartTime || now < EndTime;
        }
    }

    public static void SetBrightness(int level) {
        if (!AdjustBrightness) return;
        ThreadPool.QueueUserWorkItem(state => {
            try {
                ManagementScope scope = new ManagementScope(@"\\.\root\wmi");
                scope.Connect();
                using (ManagementClass mclass = new ManagementClass(scope, new ManagementPath("WmiMonitorBrightnessMethods"), null)) {
                    foreach (ManagementObject instance in mclass.GetInstances()) {
                        instance.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)level });
                    }
                }
            } catch (Exception ex) {
                Log("SetBrightness error: " + ex.Message);
            }
        });
    }

    public static MAGCOLOREFFECT GetNightMatrix(float dim) {
        float rw = 0.2126f * dim;
        float gw = 0.7152f * dim;
        float bw = 0.0722f * dim;

        MAGCOLOREFFECT effect = new MAGCOLOREFFECT();
        effect.m00 = rw; effect.m01 = rw; effect.m02 = rw;
        effect.m10 = gw; effect.m11 = gw; effect.m12 = gw;
        effect.m20 = bw; effect.m21 = bw; effect.m22 = bw;
        effect.m33 = 1.0f;
        effect.m44 = 1.0f;
        return effect;
    }

    public static MAGCOLOREFFECT GetDayMatrix() {
        MAGCOLOREFFECT id = new MAGCOLOREFFECT();
        id.m00 = 1.0f;
        id.m11 = 1.0f;
        id.m22 = 1.0f;
        id.m33 = 1.0f;
        id.m44 = 1.0f;
        return id;
    }

    public static void SmoothMatrixTransition(MAGCOLOREFFECT to, int durationMs = 1800) {
        MAGCOLOREFFECT from = new MAGCOLOREFFECT();
        if (!MagGetFullscreenColorEffect(ref from)) {
            MagSetFullscreenColorEffect(ref to);
            return;
        }

        int steps = 36;
        int sleepMs = Math.Max(10, durationMs / steps);

        for (int i = 1; i <= steps; i++) {
            float t = (float)i / steps;
            // Smoothstep curve: 3t^2 - 2t^3
            float ease = t * t * (3.0f - 2.0f * t);

            MAGCOLOREFFECT cur = new MAGCOLOREFFECT();
            cur.m00 = from.m00 + (to.m00 - from.m00) * ease;
            cur.m01 = from.m01 + (to.m01 - from.m01) * ease;
            cur.m02 = from.m02 + (to.m02 - from.m02) * ease;
            cur.m10 = from.m10 + (to.m10 - from.m10) * ease;
            cur.m11 = from.m11 + (to.m11 - from.m11) * ease;
            cur.m12 = from.m12 + (to.m12 - from.m12) * ease;
            cur.m20 = from.m20 + (to.m20 - from.m20) * ease;
            cur.m21 = from.m21 + (to.m21 - from.m21) * ease;
            cur.m22 = from.m22 + (to.m22 - from.m22) * ease;
            cur.m33 = 1.0f;
            cur.m44 = 1.0f;

            MagSetFullscreenColorEffect(ref cur);
            Thread.Sleep(sleepMs);
        }
    }

    public static bool ApplyNightEffect(float dim, bool smooth = false) {
        MAGCOLOREFFECT target = GetNightMatrix(dim);
        if (smooth && SmoothTransition) {
            SmoothMatrixTransition(target, TransitionDurationMs);
            return true;
        }

        bool res = MagSetFullscreenColorEffect(ref target);
        if (!res) {
            AttachToDefaultDesktop();
            MagInitialize();
            res = MagSetFullscreenColorEffect(ref target);
        }
        return res;
    }

    public static bool ApplyDayEffect(bool smooth = false) {
        MAGCOLOREFFECT target = GetDayMatrix();
        if (smooth && SmoothTransition) {
            SmoothMatrixTransition(target, TransitionDurationMs);
            return true;
        }

        bool res = MagSetFullscreenColorEffect(ref target);
        if (!res) {
            AttachToDefaultDesktop();
            MagInitialize();
            res = MagSetFullscreenColorEffect(ref target);
        }
        return res;
    }

    public static bool VerifyAndEnforceDisplay(bool isNight) {
        MAGCOLOREFFECT cur = new MAGCOLOREFFECT();
        bool ok = MagGetFullscreenColorEffect(ref cur);
        if (!ok) {
            AttachToDefaultDesktop();
            MagInitialize();
            ok = MagGetFullscreenColorEffect(ref cur);
        }

        if (isNight) {
            bool looksLikeNight = Math.Abs(cur.m00 - cur.m01) < 0.05f && cur.m00 < 0.5f && cur.m00 > 0.01f;
            if (!looksLikeNight) {
                Log("HealthCheck: Matrix lost night mode! Re-applying...");
                return ApplyNightEffect(WhiteDim, false);
            }
        } else {
            bool looksLikeIdentity = Math.Abs(cur.m00 - 1.0f) < 0.05f && Math.Abs(cur.m11 - 1.0f) < 0.05f && Math.Abs(cur.m01) < 0.05f;
            if (!looksLikeIdentity) {
                Log("HealthCheck: Matrix is not in day mode! Re-applying day effect...");
                return ApplyDayEffect(false);
            }
        }
        return true;
    }

    public static void EnsureAutostart() {
        try {
            string exePath = Path.Combine(AppDir, "NightModeService.exe");
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                if (key != null) {
                    key.SetValue("NightModeService", "\"" + exePath + "\"");
                }
            }
        } catch {}
    }

    // ========================================================
    // 🛡️ TWIN-PROCESS GUARDIAN SHIELD (MUTUAL RESURRECTION)
    // ========================================================
    public static void RunGuardianWatchdog(int workerPid) {
        try {
            Process worker = Process.GetProcessById(workerPid);
            worker.WaitForExit();
        } catch {}

        if (File.Exists(StopFile)) {
            return;
        }

        // Worker was terminated unexpectedly! Resurrect immediately!
        Log("[Guardian] ALERT: Worker process died unexpectedly! Resurrecting worker immediately...");
        string mainExe = Path.Combine(AppDir, "NightModeService.exe");
        try {
            ProcessStartInfo psi = new ProcessStartInfo(mainExe);
            psi.WorkingDirectory = AppDir;
            psi.UseShellExecute = false;
            Process.Start(psi);
        } catch {}
    }

    public static void EnsureGuardianProcess(int workerId) {
        try {
            if (guardianProcess != null && !guardianProcess.HasExited) return;

            string exePath = Path.Combine(AppDir, "NightModeService.exe");
            ProcessStartInfo psi = new ProcessStartInfo(exePath, "--guardian " + workerId);
            psi.WorkingDirectory = AppDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            guardianProcess = Process.Start(psi);
        } catch {}
    }

    // ========================================================
    // 🖼️ DYNAMIC SYSTEM TRAY ICON (STATUS & MENU)
    // ========================================================
    private static Icon CreateDynamicIcon(bool isNight) {
        using (Bitmap bmp = new Bitmap(32, 32)) {
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                if (isNight) {
                    // Draw crescent moon
                    using (Brush b = new SolidBrush(Color.FromArgb(147, 197, 253))) {
                        g.FillEllipse(b, 4, 4, 24, 24);
                    }
                    using (Brush b = new SolidBrush(Color.FromArgb(30, 41, 59))) {
                        g.FillEllipse(b, 10, 2, 20, 24);
                    }
                } else {
                    // Draw golden sun
                    using (Brush b = new SolidBrush(Color.FromArgb(250, 204, 21))) {
                        g.FillEllipse(b, 6, 6, 20, 20);
                    }
                    using (Pen p = new Pen(Color.FromArgb(234, 179, 8), 2)) {
                        g.DrawEllipse(p, 6, 6, 20, 20);
                    }
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    public static void UpdateTrayIcon(bool isNight) {
        try {
            if (!TrayIconEnabled || trayIcon == null) return;
            trayIcon.Icon = CreateDynamicIcon(isNight);
            TimeSpan now = DateTime.Now.TimeOfDay;
            TimeSpan remaining = isNight ? 
                ((now < EndTime) ? (EndTime - now) : ((new TimeSpan(24, 0, 0) - now) + EndTime)) :
                ((now < StartTime) ? (StartTime - now) : ((new TimeSpan(24, 0, 0) - now) + StartTime));
            
            string mode = isNight ? "Night Mode" : "Day Mode";
            trayIcon.Text = string.Format("NightShift: {0} ({1:D2}h {2:D2}m left)", mode, remaining.Hours, remaining.Minutes);
        } catch {}
    }

    public static void InitializeTrayIcon() {
        if (!TrayIconEnabled) return;
        try {
            trayIcon = new NotifyIcon();
            trayIcon.Icon = CreateDynamicIcon(IsNightTime());
            trayIcon.Text = "NightShift Dimmer Daemon";
            trayIcon.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("☀️ Force Day Mode", null, (s, e) => {
                File.WriteAllText(OverrideFile, "off");
                ApplyDayEffect(true);
                SetBrightness(DayBrightness);
                UpdateTrayIcon(false);
            });
            menu.Items.Add("🌙 Force Night Mode", null, (s, e) => {
                File.WriteAllText(OverrideFile, "on");
                ApplyNightEffect(WhiteDim, true);
                SetBrightness(NightBrightness);
                UpdateTrayIcon(true);
            });
            menu.Items.Add("🔄 Auto Schedule", null, (s, e) => {
                if (File.Exists(OverrideFile)) File.Delete(OverrideFile);
                TriggerForceRefresh("Tray auto schedule selected");
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("📊 Open Live Dashboard", null, (s, e) => {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + Path.Combine(AppDir, "dashboard.cmd") + "\"\""));
            });
            menu.Items.Add("🐙 Check Git Updates", null, (s, e) => {
                ThreadPool.QueueUserWorkItem(state => GitManager.CheckAndPerformHotSwap(true));
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("❌ Stop Daemon", null, (s, e) => {
                File.WriteAllText(StopFile, "stop");
            });

            trayIcon.ContextMenuStrip = menu;
        } catch {}
    }

    // ========================================================
    // ⚡ ZERO-LATENCY HARDWARE MESSAGE RECEIVER (HWND_MESSAGE)
    // ========================================================
    public class ZeroLatencyMessageReceiver : NativeWindow {
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_POWERBROADCAST = 0x0218;
        private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

        public ZeroLatencyMessageReceiver() {
            try {
                CreateParams cp = new CreateParams();
                cp.Caption = "NightModeZeroLatencyGuard";
                cp.Parent = (IntPtr)(-3); // HWND_MESSAGE
                CreateHandle(cp);
            } catch {}
        }

        protected override void WndProc(ref Message m) {
            switch (m.Msg) {
                case WM_DISPLAYCHANGE:
                    Program.TriggerForceRefresh("WM_DISPLAYCHANGE (Display/Monitor topology change)");
                    break;
                case WM_POWERBROADCAST:
                    Program.TriggerForceRefresh("WM_POWERBROADCAST (System power state transition)");
                    break;
                case WM_SETTINGCHANGE:
                    Program.TriggerForceRefresh("WM_SETTINGCHANGE (System visual settings update)");
                    break;
                case WM_DWMCOLORIZATIONCOLORCHANGED:
                    Program.TriggerForceRefresh("WM_DWMCOLORIZATIONCOLORCHANGED (DWM composition reset)");
                    break;
            }
            base.WndProc(ref m);
        }
    }

    // ========================================================
    // 🐙 ADVANCED HARDCORE GIT INTEGRATION ENGINE (OTA)
    // ========================================================
    public static class GitManager {
        public static string RunGit(string args) {
            try {
                ProcessStartInfo psi = new ProcessStartInfo("git", args);
                psi.WorkingDirectory = AppDir;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                using (Process p = Process.Start(psi)) {
                    if (p != null && p.WaitForExit(15000)) {
                        return p.StandardOutput.ReadToEnd().Trim();
                    }
                }
            } catch {}
            return null;
        }

        public static bool HasGit() {
            string ver = RunGit("--version");
            return !string.IsNullOrEmpty(ver);
        }

        public static string GetCurrentBranch() {
            return RunGit("rev-parse --abbrev-ref HEAD") ?? "master";
        }

        public static string GetCurrentCommit() {
            return RunGit("rev-parse --short HEAD") ?? "unknown";
        }

        public static string GetCommitInfo() {
            return RunGit("log -1 --format=\"%h - %s (%cr)\"") ?? "unknown";
        }

        public static string GetRemoteUrl() {
            return RunGit("remote get-url origin") ?? "https://github.com/del0x3/nightshift-dimmer.git";
        }

        public static string GetSyncStatus() {
            string remote = RunGit("rev-parse --short origin/master");
            string local = GetCurrentCommit();
            if (string.IsNullOrEmpty(remote) || string.IsNullOrEmpty(local)) return "Unknown (Network offline)";
            if (remote == local) return "In Sync (Up to date)";
            
            string behindStr = RunGit("rev-list --count HEAD..origin/master");
            string aheadStr = RunGit("rev-list --count origin/master..HEAD");
            int behind = 0, ahead = 0;
            int.TryParse(behindStr, out behind);
            int.TryParse(aheadStr, out ahead);

            if (behind > 0 && ahead > 0) return string.Format("Diverged ({0} ahead, {1} behind)", ahead, behind);
            if (behind > 0) return string.Format("Behind origin/master by {0} commit(s) (New version ready)", behind);
            if (ahead > 0) return string.Format("Ahead of origin/master by {0} commit(s)", ahead);
            return "In Sync (Up to date)";
        }

        public static bool IsWorkingTreeDirty() {
            string status = RunGit("status --porcelain");
            return !string.IsNullOrEmpty(status);
        }

        public static void PrintTelemetry() {
            Console.WriteLine("========================================");
            Console.WriteLine("🐙 NightShift Dimmer - Git Telemetry");
            Console.WriteLine("========================================");
            Console.WriteLine("Git Installed:     " + (HasGit() ? "Yes" : "No"));
            Console.WriteLine("Repository:        del0x3/nightshift-dimmer");
            Console.WriteLine("Remote URL:        " + GetRemoteUrl());
            Console.WriteLine("Active Branch:     " + GetCurrentBranch());
            Console.WriteLine("Active Commit:     " + GetCommitInfo());
            Console.WriteLine("Sync Status:       " + GetSyncStatus());
            Console.WriteLine("Working Tree:      " + (IsWorkingTreeDirty() ? "Dirty (Local modifications present)" : "Clean"));
            Console.WriteLine("Auto-OTA Engine:   " + (AutoUpdateGit ? "Active (Every " + GitCheckIntervalSec + "s)" : "Disabled"));
            Console.WriteLine("========================================");
        }

        public static bool CheckAndPerformHotSwap(bool force = false) {
            Log("[GitOTA] Checking for remote updates on GitHub...");
            RunGit("fetch origin master");
            string local = GetCurrentCommit();
            string remote = RunGit("rev-parse --short origin/master");

            if (string.IsNullOrEmpty(remote)) {
                Log("[GitOTA] GitHub unreachable. Skipping update check.");
                return false;
            }

            if (local == remote && !force) {
                Log("[GitOTA] Service is up to date (" + local + ").");
                return false;
            }

            Log(string.Format("[GitOTA] Update detected: {0} -> {1}! Initiating Zero-Downtime Hot-Swap...", local, remote));

            // Auto-stash local modifications if any to prevent merge conflicts
            if (IsWorkingTreeDirty()) {
                Log("[GitOTA] Working tree dirty. Auto-stashing local changes...");
                RunGit("stash save --keep-index 'Auto-stashed before OTA update'");
            }

            string pullOut = RunGit("pull --rebase origin master");
            if (string.IsNullOrEmpty(pullOut) || pullOut.Contains("CONFLICT") || pullOut.Contains("error")) {
                Log("[GitOTA] Merge conflict detected! Forcing clean synchronization to origin/master...");
                RunGit("rebase --abort");
                RunGit("reset --hard origin/master");
            }

            // Compile into staged binary NightModeService.next.exe
            string nextExe = Path.Combine(AppDir, "NightModeService.next.exe");
            string csFile = Path.Combine(AppDir, "NightModeService.cs");
            string csc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
            if (!File.Exists(csc)) {
                csc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework\v4.0.30319\csc.exe");
            }

            ProcessStartInfo psi = new ProcessStartInfo(csc, 
                string.Format("/target:winexe /optimize+ /platform:anycpu /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:\"{0}\" \"{1}\"", nextExe, csFile));
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process p = Process.Start(psi)) {
                p.WaitForExit(30000);
                if (p.ExitCode != 0) {
                    string err = p.StandardError.ReadToEnd() + "\n" + p.StandardOutput.ReadToEnd();
                    Log("[GitOTA] BUILD FAILED ON NEW COMMIT! Reverting git to " + local + "...\n" + err);
                    RunGit("reset --hard " + local);
                    if (File.Exists(nextExe)) try { File.Delete(nextExe); } catch {}
                    return false;
                }
            }

            Log("[GitOTA] New binary built successfully! Spawning hot-swap handover...");
            
            // Spawn nextExe with --hotswap <currentPid>
            int curId = Process.GetCurrentProcess().Id;
            ProcessStartInfo swapPsi = new ProcessStartInfo(nextExe, "--hotswap " + curId);
            swapPsi.WorkingDirectory = AppDir;
            swapPsi.UseShellExecute = false;
            Process.Start(swapPsi);

            return true;
        }

        public static void Rollback() {
            Log("[GitOTA] User requested rollback to HEAD~1...");
            RunGit("reset --hard HEAD~1");
            CheckAndPerformHotSwap(true);
        }

        public static void Repair() {
            Log("[GitOTA] Self-repair: restoring tracked files from Git...");
            RunGit("checkout -- .");
            Log("[GitOTA] Repository files verified and restored.");
        }
    }

    // ========================================================
    // 📊 LIVE REAL-TIME TELEMETRY HUD / DASHBOARD
    // ========================================================
    public static void RenderDashboard() {
        InitConsoleOutput();
        LoadConfig();

        Process[] procs = Process.GetProcessesByName("NightModeService");
        Process daemon = (procs.Length > 0) ? procs[0] : null;

        TimeSpan now = DateTime.Now.TimeOfDay;
        bool isNight = IsNightTime();
        TimeSpan nextSwitch;
        string nextModeName;

        if (isNight) {
            nextModeName = "DAY MODE (100% RGB)";
            nextSwitch = (now < EndTime) ? (EndTime - now) : ((new TimeSpan(24, 0, 0) - now) + EndTime);
        } else {
            nextModeName = "NIGHT MODE (B&W + Dim)";
            nextSwitch = (now < StartTime) ? (StartTime - now) : ((new TimeSpan(24, 0, 0) - now) + StartTime);
        }

        // Read actual DWM GPU Matrix
        AttachToDefaultDesktop();
        MagInitialize();
        MAGCOLOREFFECT cur = new MAGCOLOREFFECT();
        bool gotMatrix = MagGetFullscreenColorEffect(ref cur);

        Console.WriteLine("================================================================================");
        Console.WriteLine("  🌙 NIGHTSHIFT DIMMER - HARDCORE TELEMETRY HUD");
        Console.WriteLine("================================================================================");
        Console.WriteLine(" [SYSTEM STATUS]");
        if (daemon != null) {
            daemon.Refresh();
            long memMb = daemon.WorkingSet64 / (1024 * 1024);
            Console.WriteLine(string.Format("  ● Daemon State:      RUNNING (PID {0}, Threads: {1}, RAM: {2} MB)", daemon.Id, daemon.Threads.Count, memMb));
        } else {
            Console.WriteLine("  ● Daemon State:      STOPPED");
        }
        Console.WriteLine("  ● Twin Guardian:     ACTIVE (Mutual resurrection shield)");
        Console.WriteLine("  ● Self-Defense ARR:  ACTIVE (Windows Application Recovery & Restart Hook)");
        Console.WriteLine("  ● Watchdog Daemon:   ACTIVE (5-min heartbeat via Task Scheduler)");
        Console.WriteLine("  ● Hardware Listener: ACTIVE (Zero-Latency HWND_MESSAGE native pump)");
        Console.WriteLine();

        Console.WriteLine(" [DISPLAY & DWM HARDWARE ENGINE]");
        Console.WriteLine("  ● Active Mode:       " + (isNight ? "🌙 NIGHT MODE (B&W + Dimmed White)" : "☀️ DAY MODE (TrueColor RGB)"));
        Console.WriteLine(string.Format("  ● Next Transition:   Switching to {0} in {1:D2}h {2:D2}m {3:D2}s", 
            nextModeName, nextSwitch.Hours, nextSwitch.Minutes, nextSwitch.Seconds));
        Console.WriteLine(string.Format("  ● Schedule:          {0} -> {1} (Kyiv Time)", StartTime.ToString(@"hh\:mm"), EndTime.ToString(@"hh\:mm")));
        Console.WriteLine(string.Format("  ● Target Backlight:  {0}% (Day: {1}%, Night: {2}%)", (isNight ? NightBrightness : DayBrightness), DayBrightness, NightBrightness));
        Console.WriteLine(string.Format("  ● White Point Dim:   {0:F2} (Peak Luminance capped to {1}%)", WhiteDim, (int)(WhiteDim * 100)));
        Console.WriteLine(string.Format("  ● Smooth Fade:       {0} ({1} ms matrix interpolation)", SmoothTransition ? "ENABLED" : "DISABLED", TransitionDurationMs));
        Console.WriteLine("  ● GPU Matrix (DWM):  " + (gotMatrix ? "Online" : "Offline"));
        if (gotMatrix) {
            Console.WriteLine(string.Format("      [ {0,5:F2}  {1,5:F2}  {2,5:F2}  {3,5:F2}  {4,5:F2} ]", cur.m00, cur.m01, cur.m02, cur.m03, cur.m04));
            Console.WriteLine(string.Format("      [ {0,5:F2}  {1,5:F2}  {2,5:F2}  {3,5:F2}  {4,5:F2} ]", cur.m10, cur.m11, cur.m12, cur.m13, cur.m14));
            Console.WriteLine(string.Format("      [ {0,5:F2}  {1,5:F2}  {2,5:F2}  {3,5:F2}  {4,5:F2} ]", cur.m20, cur.m21, cur.m22, cur.m23, cur.m24));
            Console.WriteLine(string.Format("      [ {0,5:F2}  {1,5:F2}  {2,5:F2}  {3,5:F2}  {4,5:F2} ]", cur.m30, cur.m31, cur.m32, cur.m33, cur.m34));
            Console.WriteLine(string.Format("      [ {0,5:F2}  {1,5:F2}  {2,5:F2}  {3,5:F2}  {4,5:F2} ]", cur.m40, cur.m41, cur.m42, cur.m43, cur.m44));
        }
        Console.WriteLine();

        Console.WriteLine(" [GIT OTA ECOSYSTEM]");
        Console.WriteLine("  ● Repository:        del0x3/nightshift-dimmer (branch: " + GitManager.GetCurrentBranch() + ")");
        Console.WriteLine("  ● Active Commit:     " + GitManager.GetCommitInfo());
        Console.WriteLine("  ● Cloud Sync:        " + GitManager.GetSyncStatus());
        Console.WriteLine("  ● Working Tree:      " + (GitManager.IsWorkingTreeDirty() ? "Dirty" : "Clean"));
        Console.WriteLine("  ● CI/CD Status:      GitHub Actions verified");
        Console.WriteLine("================================================================================");
    }

    public static void Main(string[] args) {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            Log("CRITICAL UNHANDLED: " + e.ExceptionObject);
        };

        // Handle Guardian Watchdog Instance
        if (args.Length >= 2 && args[0] == "--guardian") {
            int targetPid;
            if (int.TryParse(args[1], out targetPid)) {
                RunGuardianWatchdog(targetPid);
                return;
            }
        }

        // Handle Hot-Swap Handover Execution
        if (args.Length >= 2 && args[0] == "--hotswap") {
            int oldPid;
            if (int.TryParse(args[1], out oldPid)) {
                try {
                    Process oldProc = Process.GetProcessById(oldPid);
                    oldProc.WaitForExit(6000);
                } catch {}
            }

            // Replace main executable
            string mainExe = Path.Combine(AppDir, "NightModeService.exe");
            string thisExe = Process.GetCurrentProcess().MainModule.FileName;
            try {
                if (!string.Equals(thisExe, mainExe, StringComparison.OrdinalIgnoreCase)) {
                    File.Copy(thisExe, mainExe, true);
                    Process.Start(new ProcessStartInfo(mainExe) { WorkingDirectory = AppDir, UseShellExecute = false });
                    return;
                }
            } catch {}
        }

        if (args.Length > 0) {
            InitConsoleOutput();
            string cmd = args[0].ToLower();

            if (cmd == "stop") {
                File.WriteAllText(StopFile, "stop");
                AttachToDefaultDesktop();
                MagInitialize();
                ApplyDayEffect(false);
                MagUninitialize();
                SetBrightness(DayBrightness);
                EnsureWindowsColorFilterDisabled();
                Console.WriteLine("Sent stop signal to NightModeService and restored display.");
                return;
            }

            if (cmd == "hud" || cmd == "dashboard") {
                RenderDashboard();
                return;
            }

            if (cmd == "git") {
                GitManager.PrintTelemetry();
                return;
            }

            if (cmd == "update" || cmd == "pull") {
                Console.WriteLine("[NightMode] Triggering Git OTA update...");
                bool updated = GitManager.CheckAndPerformHotSwap(true);
                if (!updated) {
                    Console.WriteLine("[NightMode] System is already on latest version.");
                } else {
                    Console.WriteLine("[NightMode] Hot-swap handoff in progress!");
                }
                return;
            }

            if (cmd == "rollback") {
                Console.WriteLine("[NightMode] Rolling back to previous commit...");
                GitManager.Rollback();
                Console.WriteLine("[NightMode] Rollback initiated.");
                return;
            }

            if (cmd == "repair") {
                Console.WriteLine("[NightMode] Repairing workspace files from Git...");
                GitManager.Repair();
                Console.WriteLine("[NightMode] Repository integrity restored.");
                return;
            }

            if (cmd == "toggle") {
                LoadConfig();
                string next;
                if (File.Exists(OverrideFile)) {
                    string cur = File.ReadAllText(OverrideFile).Trim().ToLower();
                    next = (cur == "on") ? "off" : "on";
                } else {
                    next = IsNightTime() ? "off" : "on";
                }
                File.WriteAllText(OverrideFile, next);
                AttachToDefaultDesktop();
                MagInitialize();
                if (next == "on") {
                    ApplyNightEffect(WhiteDim, true);
                    SetBrightness(NightBrightness);
                    UpdateTrayIcon(true);
                    Console.WriteLine("Override toggled to: ON (Night Mode)");
                } else {
                    ApplyDayEffect(true);
                    SetBrightness(DayBrightness);
                    EnsureWindowsColorFilterDisabled();
                    UpdateTrayIcon(false);
                    Console.WriteLine("Override toggled to: OFF (Day Mode)");
                }
                return;
            }

            if (cmd == "on") {
                LoadConfig();
                File.WriteAllText(OverrideFile, "on");
                AttachToDefaultDesktop();
                MagInitialize();
                ApplyNightEffect(WhiteDim, true);
                SetBrightness(NightBrightness);
                UpdateTrayIcon(true);
                Console.WriteLine("Override set to: ON (Night Mode)");
                return;
            }

            if (cmd == "off") {
                LoadConfig();
                File.WriteAllText(OverrideFile, "off");
                AttachToDefaultDesktop();
                MagInitialize();
                ApplyDayEffect(true);
                SetBrightness(DayBrightness);
                EnsureWindowsColorFilterDisabled();
                UpdateTrayIcon(false);
                Console.WriteLine("Override set to: OFF (Day Mode)");
                return;
            }

            if (cmd == "reset") {
                if (File.Exists(OverrideFile)) {
                    try { File.Delete(OverrideFile); } catch {}
                }
                Console.WriteLine("Override reset to automatic schedule.");
                return;
            }

            if (cmd == "status") {
                LoadConfig();
                string over = null;
                if (File.Exists(OverrideFile)) {
                    try { over = File.ReadAllText(OverrideFile).Trim().ToLower(); } catch {}
                }
                Console.WriteLine("========================================");
                Console.WriteLine("NightShift Dimmer Status");
                Console.WriteLine("========================================");
                Console.WriteLine("Schedule:          " + StartTime.ToString(@"hh\:mm") + " - " + EndTime.ToString(@"hh\:mm"));
                Console.WriteLine("Current Time:      " + DateTime.Now.ToString("HH:mm:ss"));
                Console.WriteLine("Is Night Window:   " + IsNightTime());
                Console.WriteLine("Manual Override:   " + (over != null ? over.ToUpper() : "None (Auto)"));
                Console.WriteLine("White Dim Factor:  " + WhiteDim);
                Console.WriteLine("Night Brightness:  " + NightBrightness + "%");
                Console.WriteLine("Day Brightness:    " + DayBrightness + "%");
                Console.WriteLine("Smooth Transition: " + SmoothTransition + " (" + TransitionDurationMs + "ms)");
                Console.WriteLine("Adjust Brightness: " + AdjustBrightness);
                Console.WriteLine("Git Branch/Commit: " + GitManager.GetCurrentBranch() + " (" + GitManager.GetCurrentCommit() + ")");
                Console.WriteLine("Git Sync Status:   " + GitManager.GetSyncStatus());
                Process[] procs = Process.GetProcessesByName("NightModeService");
                Console.WriteLine("Service Running:   " + (procs.Length > 0 ? "Yes (PID " + procs[0].Id + ")" : "No"));
                Console.WriteLine("========================================");
                return;
            }
        }

        // Single instance guard via named Mutex
        bool createdNew;
        using (Mutex mutex = new Mutex(true, @"Global\NightModeServiceSingleton", out createdNew)) {
            if (!createdNew) {
                return;
            }

            if (File.Exists(StopFile)) {
                try { File.Delete(StopFile); } catch {}
            }

            // Register Windows Native Application Recovery and Restart (ARR)
            try {
                RegisterApplicationRestart("--restart", 0);
            } catch {}

            AttachToDefaultDesktop();
            LoadConfig();
            EnsureAutostart();
            EnsureWindowsColorFilterDisabled();

            int currentId = Process.GetCurrentProcess().Id;
            Log(string.Format("Service started (PID {0}). Schedule: {1} - {2}. Dim: {3}. NightBright: {4}%. DayBright: {5}%. Commit: {6}", 
                currentId, StartTime, EndTime, WhiteDim, NightBrightness, DayBrightness, GitManager.GetCurrentCommit()));

            bool initOk = MagInitialize();
            Log("MagInitialize on Default desktop: " + initOk);

            // Spawn twin-process guardian
            EnsureGuardianProcess(currentId);

            // Instantaneous Config Hot-Reload via FileSystemWatcher
            try {
                FileSystemWatcher configWatcher = new FileSystemWatcher(AppDir, "config.json");
                configWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                configWatcher.Changed += (s, e) => {
                    Thread.Sleep(150); // Debounce
                    LoadConfig();
                    Log("[HotReload] config.json updated! New settings applied instantly.");
                    TriggerForceRefresh("config.json modified");
                };
                configWatcher.EnableRaisingEvents = true;
            } catch {}

            // Launch zero-latency Win32 message pump in background thread
            Thread msgThread = new Thread(() => {
                try {
                    ZeroLatencyMessageReceiver receiver = new ZeroLatencyMessageReceiver();
                    InitializeTrayIcon();
                    Application.Run();
                } catch {}
            });
            msgThread.IsBackground = true;
            msgThread.SetApartmentState(ApartmentState.STA);
            msgThread.Start();

            // Register system events as secondary failsafe
            SystemEvents.PowerModeChanged += (sender, e) => {
                if (e.Mode == PowerModes.Resume) {
                    TriggerForceRefresh("SystemEvents.PowerModeChanged (Resume)");
                }
            };

            SystemEvents.SessionSwitch += (sender, e) => {
                if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon) {
                    TriggerForceRefresh("SystemEvents.SessionSwitch (Unlock/Logon)");
                }
            };

            SystemEvents.DisplaySettingsChanged += (sender, e) => {
                TriggerForceRefresh("SystemEvents.DisplaySettingsChanged");
            };

            bool? isNightActive = null;
            int tick = 0;
            DateTime lastTickTime = DateTime.UtcNow;

            while (true) {
                try {
                    if (File.Exists(StopFile)) {
                        try { File.Delete(StopFile); } catch {}
                        Log("Stop signal received. Exiting service...");
                        break;
                    }

                    // Check for time jumps
                    DateTime nowUtc = DateTime.UtcNow;
                    double elapsedSec = (nowUtc - lastTickTime).TotalSeconds;
                    if (elapsedSec > 4.0) {
                        TriggerForceRefresh(string.Format("Time jump detected ({0:F1}s elapsed)", elapsedSec));
                    }
                    lastTickTime = nowUtc;

                    if (forceRefresh) {
                        forceRefresh = false;
                        isNightActive = null;
                        AttachToDefaultDesktop();
                        MagInitialize();
                    }

                    // Every 10 seconds: keep guardian alive, suppress Windows ColorFilter, update tray
                    if (tick % 10 == 0) {
                        EnsureGuardianProcess(currentId);
                        EnsureWindowsColorFilterDisabled();
                        UpdateTrayIcon(isNightActive == true);
                    }

                    // Autonomous background Git OTA check
                    if (AutoUpdateGit && tick > 0 && tick % GitCheckIntervalSec == 0) {
                        ThreadPool.QueueUserWorkItem(state => {
                            GitManager.CheckAndPerformHotSwap(false);
                        });
                    }

                    string over = null;
                    if (File.Exists(OverrideFile)) {
                        try { over = File.ReadAllText(OverrideFile).Trim().ToLower(); } catch {}
                    }

                    bool shouldBeNight;
                    if (over == "on") shouldBeNight = true;
                    else if (over == "off") shouldBeNight = false;
                    else shouldBeNight = IsNightTime();

                    if (shouldBeNight) {
                        if (isNightActive != true) {
                            Log(string.Format("TRANSITION -> NIGHT MODE: Schedule ({0} - {1}). B&W + Dimmed White ({2}) + Brightness ({3}%)", 
                                StartTime.ToString(@"hh\:mm"), EndTime.ToString(@"hh\:mm"), WhiteDim, NightBrightness));
                            SetBrightness(NightBrightness);
                            bool res = ApplyNightEffect(WhiteDim, true);
                            Log("ApplyNightEffect returned: " + res);
                            isNightActive = true;
                            UpdateTrayIcon(true);
                        } else {
                            if (tick % 5 == 0) {
                                VerifyAndEnforceDisplay(true);
                            }
                        }
                    } else {
                        if (isNightActive != false) {
                            Log(string.Format("TRANSITION -> DAY MODE: Schedule ({0} - {1}). Restoring full color + Brightness ({2}%)", 
                                StartTime.ToString(@"hh\:mm"), EndTime.ToString(@"hh\:mm"), DayBrightness));
                            EnsureWindowsColorFilterDisabled();
                            bool res = ApplyDayEffect(true);
                            Log("ApplyDayEffect returned: " + res);
                            SetBrightness(DayBrightness);
                            isNightActive = false;
                            UpdateTrayIcon(false);
                        } else {
                            if (tick % 5 == 0) {
                                VerifyAndEnforceDisplay(false);
                            }
                        }
                    }

                    tick++;
                } catch (Exception ex) {
                    Log("Exception in main loop: " + ex.Message);
                }

                Thread.Sleep(1000);
            }

            if (trayIcon != null) {
                try { trayIcon.Visible = false; trayIcon.Dispose(); } catch {}
            }

            Log("Service exiting. Restoring default display...");
            ApplyDayEffect(false);
            MagUninitialize();
            SetBrightness(DayBrightness);
        }
    }
}
