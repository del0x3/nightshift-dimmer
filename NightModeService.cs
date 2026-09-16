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

    private static volatile bool forceRefresh = false;

    public static void Log(string msg) {
        try {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}", DateTime.Now, msg);
            File.AppendAllText(LogFile, line + "\r\n");
            Console.WriteLine(line);
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
            // Check if current matrix represents night effect (m00 approx equals m01 and < 0.5)
            bool looksLikeNight = Math.Abs(cur.m00 - cur.m01) < 0.05f && cur.m00 < 0.5f && cur.m00 > 0.01f;
            if (!looksLikeNight) {
                Log("HealthCheck: Matrix lost night mode! Re-applying...");
                return ApplyNightEffect(WhiteDim);
            }
        } else {
            // Check if current matrix is identity (m00 == 1, m11 == 1, m22 == 1, m01 == 0)
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

    public static void Main(string[] args) {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            Log("CRITICAL UNHANDLED: " + e.ExceptionObject);
        };

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
                // Another instance is already running
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
            Log(string.Format("Service started (PID {0}). Schedule: {1} - {2}. WhiteDim: {3}. NightBrightness: {4}%. DayBrightness: {5}%", 
                currentId, StartTime, EndTime, WhiteDim, NightBrightness, DayBrightness));

            bool initOk = MagInitialize();
            Log("MagInitialize on Default desktop: " + initOk);

            // Register system events for sleep/wake, session lock/unlock, and monitor connect/disconnect
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

                    // Check for time jumps (e.g. sleep/hibernate wake-up without event)
                    DateTime nowUtc = DateTime.UtcNow;
                    double elapsedSec = (nowUtc - lastTickTime).TotalSeconds;
                    if (elapsedSec > 5.0) {
                        Log(string.Format("Time jump detected ({0:F1}s elapsed, possible sleep/wake). Refreshing display.", elapsedSec));
                        forceRefresh = true;
                    }
                    lastTickTime = nowUtc;

                    if (forceRefresh) {
                        forceRefresh = false;
                        isNightActive = null; // Forces immediate re-application of target state
                        AttachToDefaultDesktop();
                        MagInitialize();
                    }

                    // Reload config every 10 seconds
                    if (tick % 10 == 0) {
                        LoadConfig();
                        EnsureWindowsColorFilterDisabled();
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
                            // Periodic health check every 5 seconds: ensure night matrix is still active
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
                            // Periodic health check every 5 seconds: ensure screen is truly in day mode
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
