using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QQMusicWallpaperThemeSync
{
    internal static class Program
    {
        private static Mutex instance;
        internal static void RunWorker(string root)
        {
            bool created;
            instance = new Mutex(true, @"Local\PiaoklxtMusicWallpaper.ThemeSync", out created);
            if (!created) return;
            try
            {
                Application.EnableVisualStyles();
                Application.Run(new ThemeContext(root));
            }
            finally { instance.ReleaseMutex(); instance.Dispose(); }
        }
        [STAThread]
        private static void Main(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            if (args.Length > 0 && args[0] == "--sample")
            {
                Color c; double light;
                if (!Sampler.TrySample(out c, out light)) Environment.Exit(2);
                Console.WriteLine("#{0:X2}{1:X2}{2:X2} brightness={3:F2}", c.R, c.G, c.B, light);
                return;
            }
            if (args.Length > 0 && args[0] == "--apply-once")
            {
                Color c; double light;
                if (!Sampler.TrySample(out c, out light)) Environment.Exit(2);
                ThemeRegistry.Backup(Path.Combine(root, "theme-original.reg.txt"));
                ThemeRegistry.Apply(c, null, false, false);
                Console.WriteLine("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
                return;
            }
            if (args.Length > 0 && args[0] == "--restore")
            {
                ExplorerTint.Reset();
                ThemeRegistry.Restore(Path.Combine(root, "theme-original.reg.txt"));
                return;
            }
            bool created;
            instance = new Mutex(true, @"Local\PiaoklxtMusicWallpaper.ThemeSync", out created);
            if (!created) return;
            try
            {
                Application.EnableVisualStyles();
                Application.Run(new ThemeContext(root));
            }
            finally { instance.ReleaseMutex(); instance.Dispose(); }
        }
    }

    internal sealed class ThemeContext : ApplicationContext
    {
        private readonly string settingsPath;
        private readonly string backupPath;
        private readonly NotifyIcon tray;
        private readonly Control dispatcher;
        private readonly MenuItem accentItem;
        private readonly MenuItem brightnessItem;
        private readonly MenuItem shellItem;
        private readonly MenuItem appsItem;
        private readonly System.Threading.Timer timer;
        private readonly object updateLock = new object();
        private readonly Action<Color, bool?, bool, bool> applyRegistry = ThemeRegistry.Apply;
        private readonly QQMusicWallpaper.ThemeSamples samples = new QQMusicWallpaper.ThemeSamples(() =>
        {
            Color color; double brightness;
            return Sampler.TrySample(out color, out brightness) ? new QQMusicWallpaper.ThemeSample(color, brightness) : null;
        });
        private bool shuttingDown;
        private bool enabled;
        private bool matchBrightness;
        private bool colorizeShell;
        private bool darkApps;
        private QQMusicWallpaper.ThemePalette palette;
        private QQMusicWallpaper.ThemePreferences officePreferences;
        private QQMusicWallpaper.ExplorerAppearanceSettings explorerPreferences;
        private readonly QQMusicWallpaper.AppearanceSurfaces surfaces;
        private bool wallpaperSessionActive;
        private Color lastColor = Color.Empty;
        private bool? lastLight;

        public ThemeContext(string root) : this(root, null, true)
        {
        }

        public ThemeContext(string root, bool showTray) : this(root, null, showTray)
        {
        }

        public ThemeContext(string root, ContextMenu hostMenu) : this(root, hostMenu, hostMenu == null)
        {
        }

        private ThemeContext(string root, ContextMenu hostMenu, bool showTray)
        {
            dispatcher = new Control();
            var dispatcherHandle = dispatcher.Handle;
            settingsPath = Path.Combine(root, "theme-sync.ini");
            backupPath = Path.Combine(root, "theme-original.reg.txt");
            QQMusicWallpaper.PlaybackAccent.ConfigureCache(Path.Combine(root, "lyrics-playback-color.ini"));
            ReadSettings();
            explorerPreferences = QQMusicWallpaper.ExplorerAppearanceSettings.Load(QQMusicWallpaper.ExplorerAppearanceSettings.SettingsPath);
            surfaces = new QQMusicWallpaper.AppearanceSurfaces();
            // A backup can survive an abnormal termination. Restore it before a
            // new wallpaper session snapshots the user's current color choices.
            if (File.Exists(backupPath)) ThemeRegistry.Restore(backupPath);
            wallpaperSessionActive = showTray || hostMenu != null;
            // Clear caption overrides left by older versions. Explorer's top row
            // now uses its native appearance; no per-window DWM tint is applied.
            ExplorerTint.Reset();
            var menu = hostMenu ?? new ContextMenu();
            accentItem = menu.MenuItems.Add("随 QQ 音乐桌面同步系统主题色", (s, e) => ToggleAccent());
            brightnessItem = menu.MenuItems.Add("同时切换系统明暗", (s, e) => ToggleBrightness());
            shellItem = menu.MenuItems.Add("任务栏与开始菜单跟随系统强调色", (s, e) => ToggleShell());
            appsItem = menu.MenuItems.Add("文件资源管理器等使用深色", (s, e) => ToggleApps());
            menu.MenuItems.Add("立即读取歌词配色", (s, e) => UpdateColor(true));
            if (showTray && hostMenu == null)
            {
                menu.MenuItems.Add("退出主题同步", (s, e) =>
                {
                    Shutdown();
                    ExitThread();
                });
                string iconPath = Path.Combine(root, "QQMusicThemeSync.ico");
                tray = new NotifyIcon { Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
                    Text = "QQ 音乐 · 系统主题同步", ContextMenu = menu, Visible = true };
                tray.DoubleClick += (s, e) => ToggleAccent();
            }
            // A 10-second poll made song changes visibly lag behind the wallpaper.
            // One sample takes ~0.1 s on this PC; only meaningful color changes
            // write registry values, so polling more often doesn't spam updates.
            // Sampling and system theme broadcasts must not block the wallpaper
            // controller's UI thread: its low-level mouse hook runs there.
            timer = new System.Threading.Timer(s => UpdateColor(false), null, 1500, 1500);
            UpdateMenu();
            if (enabled && wallpaperSessionActive)
            {
                ThemeRegistry.Backup(backupPath);
                UpdateColor(true);
            }
        }

        private void Post(Action action)
        {
            if (shuttingDown || dispatcher.IsDisposed || !dispatcher.IsHandleCreated) return;
            dispatcher.BeginInvoke(action);
        }

        public void RequestToggleAccent() { Post(ToggleAccent); }
        public void RequestToggleBrightness() { Post(ToggleBrightness); }
        public void RequestToggleShell() { Post(ToggleShell); }
        public void RequestToggleApps() { Post(ToggleApps); }
        public void RequestUpdateColor() { Post(() => UpdateColor(true)); }
        public void RequestBeginWallpaperSession() { Post(BeginWallpaperSession); }
        public void RequestEndWallpaperSession() { Post(EndWallpaperSession); }

        public void RequestApply(QQMusicWallpaper.ThemePreferences preferences, QQMusicWallpaper.ExplorerAppearanceSettings explorer, Action<string> completed)
        {
            QQMusicWallpaper.OperationLog.Record(QQMusicWallpaper.OperationKind.SettingsApply, "theme-queued");
            Post(() =>
            {
                QQMusicWallpaper.OperationLog.Record(QQMusicWallpaper.OperationKind.SettingsApply, "theme-started");
                string error = null;
                try
                {
                    lock (updateLock)
                    {
                        bool wasEnabled = enabled;
                        if (wasEnabled && !preferences.Enabled) ThemeRegistry.Restore(backupPath);
                        if (wasEnabled && colorizeShell && !preferences.ColorizeShell) ThemeRegistry.RestoreShell(backupPath);
                        if (wasEnabled && (darkApps || matchBrightness) && !preferences.DarkApps && !preferences.MatchBrightness)
                            ThemeRegistry.RestoreApplicationBrightness(backupPath);
                        enabled = preferences.Enabled;
                        matchBrightness = preferences.MatchBrightness;
                        colorizeShell = preferences.ColorizeShell;
                        darkApps = preferences.DarkApps;
                        palette = preferences.Palette;
                        officePreferences = preferences.Copy();
                        explorerPreferences = explorer.Copy();
                        if (!wasEnabled && enabled && wallpaperSessionActive) ThemeRegistry.Backup(backupPath);
                        SaveSettings();
                        lastColor = Color.Empty;
                        lastLight = null;
                        if (!UpdateColor(true)) throw new InvalidOperationException("Theme colors could not be applied.");
                        RefreshSurfaces();
                        QQMusicWallpaper.OperationLog.Record(QQMusicWallpaper.OperationKind.SettingsApply, "theme-saved");
                    }
                }
                catch (Exception ex)
                {
                    QQMusicWallpaper.RuntimeLog.Write("Theme preferences apply failed: " + ex.Message);
                    error = "主题同步设置未能完整应用，请重试。";
                }
                if (completed != null) completed(error);
            });
        }

        public void Shutdown()
        {
            if (dispatcher.InvokeRequired) { dispatcher.Invoke((Action)Shutdown); return; }
            timer.Dispose();
            samples.Dispose();
            lock (updateLock)
            {
                shuttingDown = true;
                wallpaperSessionActive = false;
                ExplorerTint.Reset();
                ThemeRegistry.Restore(backupPath);
                surfaces.Dispose();
            }
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
        }

        private void ReadSettings()
        {
            officePreferences = QQMusicWallpaper.ThemePreferences.Load(settingsPath);
            palette = officePreferences.Palette;
            if (!File.Exists(settingsPath)) return;
            foreach (string line in File.ReadAllLines(settingsPath))
            {
                if (line == "Enabled=1") enabled = true;
                if (line == "MatchBrightness=1") matchBrightness = true;
                if (line == "ColorizeShell=1") colorizeShell = true;
                if (line == "DarkApps=1") darkApps = true;
            }
        }

        private void SaveSettings()
        {
            QQMusicWallpaper.AppearanceIni.WriteAtomic(settingsPath, CurrentPreferences().Serialize(), Encoding.UTF8);
            RefreshSurfaces();
            UpdateMenu();
        }

        private QQMusicWallpaper.ThemePreferences CurrentPreferences()
        {
            var value = officePreferences.Copy();
            value.Enabled = enabled; value.MatchBrightness = matchBrightness;
            value.ColorizeShell = colorizeShell; value.DarkApps = darkApps; value.Palette = palette;
            return value;
        }

        private void RefreshSurfaces()
        {
            surfaces.Configure(new QQMusicWallpaper.SurfaceAppearanceState(CurrentPreferences(), explorerPreferences,
                lastColor, wallpaperSessionActive && !shuttingDown));
        }

        private void UpdateMenu()
        {
            accentItem.Checked = enabled;
            brightnessItem.Checked = matchBrightness;
            brightnessItem.Enabled = enabled;
            shellItem.Checked = colorizeShell;
            shellItem.Enabled = enabled;
            appsItem.Checked = darkApps;
            appsItem.Enabled = enabled;
        }

        private void ToggleAccent()
        {
            lock (updateLock)
            {
                if (!enabled)
                {
                    enabled = true;
                    SaveSettings();
                    if (wallpaperSessionActive)
                    {
                        ThemeRegistry.Backup(backupPath);
                        UpdateColor(true);
                    }
                }
                else
                {
                    enabled = false;
                    SaveSettings();
                    ExplorerTint.Reset();
                    ThemeRegistry.Restore(backupPath);
                    lastColor = Color.Empty;
                    lastLight = null;
                }
            }
        }

        private void BeginWallpaperSession()
        {
            lock (updateLock)
            {
                if (wallpaperSessionActive || shuttingDown) return;
                wallpaperSessionActive = true;
                samples.Clear();
                lastColor = Color.Empty;
                lastLight = null;
                RefreshSurfaces();
                if (!enabled) return;
                ThemeRegistry.Backup(backupPath);
                UpdateColor(true);
            }
        }

        private void EndWallpaperSession()
        {
            lock (updateLock)
            {
                if (!wallpaperSessionActive) return;
                wallpaperSessionActive = false;
                samples.Clear();
                ExplorerTint.Reset();
                ThemeRegistry.Restore(backupPath);
                lastColor = Color.Empty;
                lastLight = null;
                RefreshSurfaces();
            }
        }

        private void ToggleBrightness()
        {
            matchBrightness = !matchBrightness;
            SaveSettings();
            if (!matchBrightness) ThemeRegistry.RestoreApplicationBrightness(backupPath);
            if (enabled) UpdateColor(true);
        }

        private void ToggleShell()
        {
            colorizeShell = !colorizeShell;
            SaveSettings();
            if (!colorizeShell) ThemeRegistry.RestoreShell(backupPath);
            UpdateColor(true);
        }

        private void ToggleApps()
        {
            darkApps = !darkApps;
            SaveSettings();
            if (!darkApps && !matchBrightness) ThemeRegistry.RestoreApplicationBrightness(backupPath);
            UpdateColor(true);
        }

        private bool UpdateColor(bool force)
        {
            lock (updateLock)
            {
                if (!enabled || !wallpaperSessionActive || shuttingDown) return true;
                try
                {
                    bool playback = QQMusicWallpaper.ThemePaletteColors.UsesPlaybackColor(palette);
                    var sample = playback || matchBrightness ? samples.Read() : null;
                    Color color = playback ? (sample == null ? QQMusicWallpaper.PlaybackAccent.LastSampledColor : sample.Color) : QQMusicWallpaper.ThemePaletteColors.Resolve(palette, 0);
                    // Follow native lyric foreground contrast independently of accent color.
                    bool? light = matchBrightness ? (sample == null ? lastLight : (bool?)(sample.Brightness > .61)) : null;
                    if (color.IsEmpty)
                    {
                        if (force) ThemeRegistry.ApplyBrightness(light, darkApps);
                        return true;
                    }
                    int delta = lastColor.IsEmpty ? 1000 : Math.Abs(color.R - lastColor.R) + Math.Abs(color.G - lastColor.G) + Math.Abs(color.B - lastColor.B);
                    if (force || (playback ? delta > 0 : delta > 35) || light != lastLight)
                    {
                        applyRegistry(color, light, colorizeShell, darkApps);
                        lastColor = color;
                        lastLight = light;
                        RefreshSurfaces();
                    }
                }
                catch (Exception e)
                {
                    QQMusicWallpaper.RuntimeLog.Write("Theme color update failed: " + e.GetType().Name);
                    return false;
                }
                return true;
            }
        }
    }

    internal static class ExplorerTint
    {
        private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Reset()
        {
            int defaultColor = unchecked((int)0xFFFFFFFF);
            Visit(hwnd =>
            {
                DwmSetWindowAttribute(hwnd, 35, ref defaultColor, 4);
                DwmSetWindowAttribute(hwnd, 34, ref defaultColor, 4);
                DwmSetWindowAttribute(hwnd, 36, ref defaultColor, 4);
            });
        }

        private static void Visit(Action<IntPtr> action)
        {
            EnumWindows((hwnd, data) =>
            {
                var name = new StringBuilder(64);
                GetClassName(hwnd, name, name.Capacity);
                if (name.ToString() != "CabinetWClass" && name.ToString() != "ExploreWClass") return true;
                uint pid; GetWindowThreadProcessId(hwnd, out pid);
                if (QQMusicWallpaper.ProcessIdentity.Matches(pid, "explorer")) action(hwnd);
                return true;
            }, IntPtr.Zero);
        }
    }

    internal static class Sampler
    {
        internal static Func<QQMusicWallpaper.ThemeSample> ReadProperties = QQMusicWallpaper.QQMusicThemeProperties.ReadCurrent;
        public static bool TrySample(out Color result, out double meanLight)
        {
            result = Color.Empty; meanLight = 0;
            var sample = ReadProperties();
            if (sample == null || sample.Color.IsEmpty) return false;
            result = sample.Color; meanLight = sample.Brightness;
            QQMusicWallpaper.PlaybackAccent.RememberColor(result);
            return true;
        }
    }

    internal static class ThemeRegistry
    {
        private const string Dwm = @"Software\Microsoft\Windows\DWM";
        private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string Desktop = @"Control Panel\Desktop";
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wp, string lp, uint flags, uint timeout, out IntPtr result);

        public static void Backup(string path)
        {
            if (File.Exists(path)) return;
            var lines = new System.Collections.Generic.List<string>();
            Save(lines, Dwm, "AccentColor"); Save(lines, Dwm, "ColorizationColor"); Save(lines, Dwm, "ColorizationAfterglow");
            Save(lines, Dwm, "ColorPrevalence");
            Save(lines, Explorer, "AccentColorMenu"); Save(lines, Explorer, "StartColorMenu");
            SaveBinary(lines, Explorer, "AccentPalette");
            Save(lines, Personalize, "AppsUseLightTheme"); Save(lines, Personalize, "SystemUsesLightTheme"); Save(lines, Personalize, "ColorPrevalence");
            Save(lines, Desktop, "AutoColorization");
            File.WriteAllLines(path, lines.ToArray());
        }

        private static void Save(System.Collections.Generic.List<string> lines, string path, string name)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                object value = key == null ? null : key.GetValue(name, null);
                lines.Add(path + "|" + name + "|" + (value == null ? "missing" : unchecked((uint)Convert.ToInt32(value)).ToString()));
            }
        }

        private static void SaveBinary(System.Collections.Generic.List<string> lines, string path, string name)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                byte[] value = key == null ? null : key.GetValue(name, null) as byte[];
                lines.Add(path + "|" + name + "|" + (value == null ? "missing" : "base64:" + Convert.ToBase64String(value)));
            }
        }

        public static void Restore(string path)
        {
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] fields = line.Split('|');
                if (fields.Length != 3) continue;
                using (var key = Registry.CurrentUser.CreateSubKey(fields[0]))
                {
                    if (fields[2] == "missing") key.DeleteValue(fields[1], false);
                    else if (fields[2].StartsWith("base64:")) key.SetValue(fields[1], Convert.FromBase64String(fields[2].Substring(7)), RegistryValueKind.Binary);
                    else key.SetValue(fields[1], unchecked((int)UInt32.Parse(fields[2])), RegistryValueKind.DWord);
                }
            }
            Notify();
            File.Delete(path);
        }

        public static void RestoreApplicationBrightness(string path)
        {
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] fields = line.Split('|');
                if (fields.Length != 3 || fields[0] != Personalize || fields[1] != "AppsUseLightTheme") continue;
                using (var key = Registry.CurrentUser.CreateSubKey(Personalize))
                {
                    if (fields[2] == "missing") key.DeleteValue(fields[1], false);
                    else key.SetValue(fields[1], unchecked((int)UInt32.Parse(fields[2])), RegistryValueKind.DWord);
                }
            }
            Notify();
        }

        public static void RestoreShell(string path)
        {
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] fields = line.Split('|');
                if (fields.Length != 3 || fields[0] != Personalize ||
                    (fields[1] != "SystemUsesLightTheme" && fields[1] != "ColorPrevalence")) continue;
                using (var key = Registry.CurrentUser.CreateSubKey(Personalize))
                {
                    if (fields[2] == "missing") key.DeleteValue(fields[1], false);
                    else key.SetValue(fields[1], unchecked((int)UInt32.Parse(fields[2])), RegistryValueKind.DWord);
                }
            }
            Notify();
        }

        public static void Apply(Color c, bool? light, bool colorizeShell, bool darkApps)
        { Apply(c, light, colorizeShell, darkApps, path => Registry.CurrentUser.CreateSubKey(path), Notify); }

        internal static void Apply(Color c, bool? light, bool colorizeShell, bool darkApps, Func<string, RegistryKey> open, Action notify)
        {
            bool changed = false;
            int rgb = (c.R << 16) | (c.G << 8) | c.B;
            int bgr = unchecked((int)0xFF000000) | (c.B << 16) | (c.G << 8) | c.R;
            using (var key = open(Dwm))
            {
                changed |= Write(key, "AccentColor", bgr, RegistryValueKind.DWord);
                changed |= Write(key, "ColorizationColor", rgb, RegistryValueKind.DWord);
                changed |= Write(key, "ColorizationAfterglow", rgb, RegistryValueKind.DWord);
                changed |= Write(key, "ColorPrevalence", 1, RegistryValueKind.DWord);
            }
            using (var key = open(Explorer))
            {
                changed |= Write(key, "AccentColorMenu", bgr, RegistryValueKind.DWord);
                changed |= Write(key, "StartColorMenu", bgr, RegistryValueKind.DWord);
                changed |= Write(key, "AccentPalette", MakePalette(c), RegistryValueKind.Binary);
            }
            using (var key = open(Desktop))
                changed |= Write(key, "AutoColorization", 0, RegistryValueKind.DWord);
            if (light.HasValue)
                using (var key = open(Personalize))
                    changed |= Write(key, "AppsUseLightTheme", light.Value ? 1 : 0, RegistryValueKind.DWord);
            else if (darkApps)
                using (var key = open(Personalize))
                    changed |= Write(key, "AppsUseLightTheme", 0, RegistryValueKind.DWord);
            if (colorizeShell)
                using (var key = open(Personalize))
                {
                    changed |= Write(key, "ColorPrevalence", 1, RegistryValueKind.DWord);
                }
            if (changed) notify();
        }

        private static bool Write(RegistryKey key, string name, object value, RegistryValueKind kind)
        {
            object before = key.GetValue(name);
            if (before is byte[] && value is byte[])
            {
                var a = (byte[])before; var b = (byte[])value;
                bool same = a.Length == b.Length;
                for (int i = 0; same && i < a.Length; i++) same = a[i] == b[i];
                if (same) return false;
            }
            else if (Equals(before, value)) return false;
            key.SetValue(name, value, kind);
            return true;
        }

        internal static void ApplyBrightness(bool? light, bool darkApps)
        {
            if (!light.HasValue && !darkApps) return;
            using (var key = Registry.CurrentUser.CreateSubKey(Personalize))
                if (Write(key, "AppsUseLightTheme", light == true ? 1 : 0, RegistryValueKind.DWord)) Notify();
        }

        private static byte[] MakePalette(Color c)
        {
            double[] factors = { 0.72, 0.52, 0.30, 0.12, 0.0, -0.15, -0.32, -0.48 };
            var bytes = new byte[32];
            for (int i = 0; i < 8; i++)
            {
                double f = factors[i];
                bytes[i * 4] = Shade(c.R, f);
                bytes[i * 4 + 1] = Shade(c.G, f);
                bytes[i * 4 + 2] = Shade(c.B, f);
                bytes[i * 4 + 3] = 0;
            }
            return bytes;
        }

        private static byte Shade(byte channel, double factor)
        {
            return (byte)Math.Max(0, Math.Min(255, Math.Round(factor >= 0 ? channel + (255 - channel) * factor : channel * (1 + factor))));
        }

        private static void Notify()
        { notifications.Request(); }

        private static readonly QQMusicWallpaper.ThemeNotifications notifications = new QQMusicWallpaper.ThemeNotifications(DeliverNotifications);

        private static void DeliverNotifications()
        {
            var elapsed = Stopwatch.StartNew(); int delayed = 0, visited = 0;
            QQMusicWallpaper.Native.EnumWindows((window, unused) =>
            {
                uint pid; QQMusicWallpaper.Native.GetWindowThreadProcessId(window, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id) return true;
                visited++;
                if (!NotifyWindow(window)) delayed++;
                return true;
            }, IntPtr.Zero);
            if (delayed > 0 || elapsed.ElapsedMilliseconds > 1500)
                QQMusicWallpaper.RuntimeLog.Write("Theme notification batch: windows=" + visited + "; delayed=" + delayed + "; elapsedMs=" + elapsed.ElapsedMilliseconds);
        }

        internal static bool NotifyWindow(IntPtr window)
        {
            IntPtr result;
            // Explicit recipients avoid HWND_BROADCAST's timeout multiplication.
            // These calls run only on the coalescing notification worker.
            bool settings = SendMessageTimeout(window, 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x23, 100, out result) != IntPtr.Zero;
            bool theme = SendMessageTimeout(window, 0x031A, IntPtr.Zero, null, 0x23, 100, out result) != IntPtr.Zero;
            return settings && theme;
        }
    }
}
