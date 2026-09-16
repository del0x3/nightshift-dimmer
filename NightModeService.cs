using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Management;
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
    public static bool AutoUpdateGit = true;
    public static int GitCheckIntervalSec = 3600;             // 1 hour check

    private static volatile bool forceRefresh = false;

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

    public static bool ApplyNightEffect(float dim) {
        float rw = 0.2126f * dim;
        float gw = 0.7152f * dim;
        float bw = 0.0722f * dim;

        MAGCOLOREFFECT effect = new MAGCOLOREFFECT();
        effect.m00 = rw; effect.m01 = rw; effect.m02 = rw;
        effect.m10 = gw; effect.m11 = gw; effect.m12 = gw;
        effect.m20 = bw; effect.m21 = bw; effect.m22 = bw;
        effect.m33 = 1.0f;
        effect.m44 = 1.0f;

        bool res = MagSetFullscreenColorEffect(ref effect);
        if (!res) {
            AttachToDefaultDesktop();
            MagInitialize();
            res = MagSetFullscreenColorEffect(ref effect);
        }
        return res;
    }

    public static bool ApplyDayEffect() {
        MAGCOLOREFFECT id = new MAGCOLOREFFECT();
        id.m00 = 1.0f;
        id.m11 = 1.0f;
        id.m22 = 1.0f;
        id.m33 = 1.0f;
        id.m44 = 1.0f;

        bool res = MagSetFullscreenColorEffect(ref id);
        if (!res) {
            AttachToDefaultDesktop();
            MagInitialize();
            res = MagSetFullscreenColorEffect(ref id);
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
                return ApplyNightEffect(WhiteDim);
            }
        } else {
            bool looksLikeIdentity = Math.Abs(cur.m00 - 1.0f) < 0.05f && Math.Abs(cur.m11 - 1.0f) < 0.05f && Math.Abs(cur.m01) < 0.05f;
            if (!looksLikeIdentity) {
                Log("HealthCheck: Matrix is not in day mode! Re-applying day effect...");
                return ApplyDayEffect();
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

    // ==========================================
    // 🐙 ADVANCED GIT INTEGRATION ENGINE (OTA)
    // ==========================================
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
            Console.WriteLine("Working Tree:      " + (IsWorkingTreeDirty() ? "Dirty (Local modifications)" : "Clean"));
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
            string pullOut = RunGit("pull origin master");
            Log("[GitOTA] git pull: " + (pullOut ?? "done"));

            // Compile into staged binary NightModeService.next.exe
            string nextExe = Path.Combine(AppDir, "NightModeService.next.exe");
            string csFile = Path.Combine(AppDir, "NightModeService.cs");
            string csc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
            if (!File.Exists(csc)) {
                csc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework\v4.0.30319\csc.exe");
            }

            ProcessStartInfo psi = new ProcessStartInfo(csc, 
                string.Format("/target:winexe /optimize+ /platform:anycpu /r:System.Management.dll /out:\"{0}\" \"{1}\"", nextExe, csFile));
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

    public static void Main(string[] args) {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            Log("CRITICAL UNHANDLED: " + e.ExceptionObject);
        };

        // Handle Hot-Swap Handover Execution
        if (args.Length >= 2 && args[0] == "--hotswap") {
            int oldPid;
            if (int.TryParse(args[1], out oldPid)) {
                try {
                    Process oldProc = Process.GetProcessById(oldPid);
                    // Wait up to 6 seconds for old instance to gracefully exit
                    oldProc.WaitForExit(6000);
                } catch {}
            }

            // Replace main executable
            string mainExe = Path.Combine(AppDir, "NightModeService.exe");
            string thisExe = Process.GetCurrentProcess().MainModule.FileName;
            try {
                if (!string.Equals(thisExe, mainExe, StringComparison.OrdinalIgnoreCase)) {
                    File.Copy(thisExe, mainExe, true);
                    // Relaunch the production binary
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
                ApplyDayEffect();
                MagUninitialize();
                SetBrightness(DayBrightness);
                EnsureWindowsColorFilterDisabled();
                Console.WriteLine("Sent stop signal to NightModeService and restored display.");
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
                    ApplyNightEffect(WhiteDim);
                    SetBrightness(NightBrightness);
                    Console.WriteLine("Override toggled to: ON (Night Mode)");
                } else {
                    ApplyDayEffect();
                    SetBrightness(DayBrightness);
                    EnsureWindowsColorFilterDisabled();
                    Console.WriteLine("Override toggled to: OFF (Day Mode)");
                }
                return;
            }

            if (cmd == "on") {
                LoadConfig();
                File.WriteAllText(OverrideFile, "on");
                AttachToDefaultDesktop();
                MagInitialize();
                ApplyNightEffect(WhiteDim);
                SetBrightness(NightBrightness);
                Console.WriteLine("Override set to: ON (Night Mode)");
                return;
            }

            if (cmd == "off") {
                LoadConfig();
                File.WriteAllText(OverrideFile, "off");
                AttachToDefaultDesktop();
                MagInitialize();
                ApplyDayEffect();
                SetBrightness(DayBrightness);
                EnsureWindowsColorFilterDisabled();
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

            AttachToDefaultDesktop();
            LoadConfig();
            EnsureAutostart();
            EnsureWindowsColorFilterDisabled();

            int currentId = Process.GetCurrentProcess().Id;
            Log(string.Format("Service started (PID {0}). Schedule: {1} - {2}. Dim: {3}. NightBright: {4}%. DayBright: {5}%. Commit: {6}", 
                currentId, StartTime, EndTime, WhiteDim, NightBrightness, DayBrightness, GitManager.GetCurrentCommit()));

            bool initOk = MagInitialize();
            Log("MagInitialize on Default desktop: " + initOk);

            // Register system events
            SystemEvents.PowerModeChanged += (sender, e) => {
                if (e.Mode == PowerModes.Resume) {
                    Log("System resumed from sleep/hibernate. Forcing display state refresh.");
                    forceRefresh = true;
                }
            };

            SystemEvents.SessionSwitch += (sender, e) => {
                if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon) {
                    Log("User session unlocked/logon detected. Forcing display state refresh.");
                    forceRefresh = true;
                }
            };

            SystemEvents.DisplaySettingsChanged += (sender, e) => {
                Log("Display settings or monitors changed. Forcing display state refresh.");
                forceRefresh = true;
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
                    if (elapsedSec > 5.0) {
                        Log(string.Format("Time jump detected ({0:F1}s elapsed, possible sleep/wake). Refreshing display.", elapsedSec));
                        forceRefresh = true;
                    }
                    lastTickTime = nowUtc;

                    if (forceRefresh) {
                        forceRefresh = false;
                        isNightActive = null;
                        AttachToDefaultDesktop();
                        MagInitialize();
                    }

                    // Reload config and suppress Windows ColorFilter every 10 seconds
                    if (tick % 10 == 0) {
                        LoadConfig();
                        EnsureWindowsColorFilterDisabled();
                    }

                    // Autonomous background Git OTA check (default: every hour)
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
                            bool res = ApplyNightEffect(WhiteDim);
                            Log("ApplyNightEffect returned: " + res);
                            isNightActive = true;
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
                            bool res = ApplyDayEffect();
                            Log("ApplyDayEffect returned: " + res);
                            SetBrightness(DayBrightness);
                            isNightActive = false;
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

            Log("Service exiting. Restoring default display...");
            ApplyDayEffect();
            MagUninitialize();
            SetBrightness(DayBrightness);
        }
    }
}
