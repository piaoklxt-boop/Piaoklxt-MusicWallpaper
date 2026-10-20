using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
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
        private bool shuttingDown;
        private bool enabled;
        private bool matchBrightness;
        private bool colorizeShell;
        private bool darkApps;
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
            ReadSettings();
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
            shellItem = menu.MenuItems.Add("任务栏与开始菜单跟随颜色（深色）", (s, e) => ToggleShell());
            appsItem = menu.MenuItems.Add("文件资源管理器等使用深色", (s, e) => ToggleApps());
            menu.MenuItems.Add("立即取色", (s, e) => UpdateColor(true));
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

        public void Shutdown()
        {
            timer.Dispose();
            lock (updateLock)
            {
                shuttingDown = true;
                wallpaperSessionActive = false;
                ExplorerTint.Reset();
                ThemeRegistry.Restore(backupPath);
            }
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
        }

        private void ReadSettings()
        {
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
            File.WriteAllText(settingsPath, "Enabled=" + (enabled ? "1" : "0") + Environment.NewLine +
                "MatchBrightness=" + (matchBrightness ? "1" : "0") + Environment.NewLine +
                "ColorizeShell=" + (colorizeShell ? "1" : "0") + Environment.NewLine +
                "DarkApps=" + (darkApps ? "1" : "0") + Environment.NewLine, Encoding.UTF8);
            UpdateMenu();
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
                lastColor = Color.Empty;
                lastLight = null;
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
                ExplorerTint.Reset();
                ThemeRegistry.Restore(backupPath);
                lastColor = Color.Empty;
                lastLight = null;
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

        private void UpdateColor(bool force)
        {
            lock (updateLock)
            {
                if (!enabled || !wallpaperSessionActive || shuttingDown) return;
                try
                {
                    Color color; double brightness;
                    if (!Sampler.TrySample(out color, out brightness)) return;
                    int delta = lastColor.IsEmpty ? 1000 : Math.Abs(color.R - lastColor.R) + Math.Abs(color.G - lastColor.G) + Math.Abs(color.B - lastColor.B);
                    bool light = brightness > 0.61;
                    if (force || delta > 35 || (matchBrightness && lastLight.HasValue && lastLight.Value != light))
                    {
                        ThemeRegistry.Apply(color, matchBrightness ? (bool?)light : null, colorizeShell, darkApps);
                        lastColor = color;
                        lastLight = light;
                    }
                }
                catch (Exception e) { File.AppendAllText(QQMusicWallpaper.AppPaths.ThemeLogPath, DateTime.Now + " " + e + Environment.NewLine); }
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
                try
                {
                    if (Process.GetProcessById((int)pid).ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                        action(hwnd);
                }
                catch (ArgumentException) { }
                return true;
            }, IntPtr.Zero);
        }
    }

    internal static class Sampler
    {
        private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int L, T, R, B; }

        private static IntPtr FindRenderer()
        {
            IntPtr found = IntPtr.Zero;
            EnumProc visit = (hwnd, data) =>
            {
                var name = new StringBuilder(128);
                GetClassName(hwnd, name, name.Capacity);
                if (name.ToString() != "Win32CaptureSample.SampleWindow") return true;
                uint pid; GetWindowThreadProcessId(hwnd, out pid);
                try { if (Process.GetProcessById((int)pid).ProcessName == "Piaoklxt-MusicWallpaperRenderer") found = hwnd; }
                catch (ArgumentException) { }
                return true;
            };
            EnumWindows((hwnd, data) => { visit(hwnd, data); EnumChildWindows(hwnd, visit, IntPtr.Zero); return true; }, IntPtr.Zero);
            return found;
        }

        public static bool TrySample(out Color result, out double meanLight)
        {
            result = Color.Empty; meanLight = 0;
            IntPtr hwnd = FindRenderer();
            Rect rect;
            if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out rect)) return false;
            int w = rect.R - rect.L, h = rect.B - rect.T;
            if (w < 300 || h < 200) return false;
            using (var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr dc = graphics.GetHdc();
                bool ok;
                try { ok = PrintWindow(hwnd, dc, 2); }
                finally { graphics.ReleaseHdc(dc); }
                if (!ok) return false;
                // QQ 播放页的唱片/封面在右侧；按高饱和像素的色相聚类，避开歌词和空白背景。
                int[] weights = new int[36], rr = new int[36], gg = new int[36], bb = new int[36];
                long lightSum = 0; int lightCount = 0;
                var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = h / 10; y < h * 8 / 10; y += 10)
                        for (int x = 0; x < w; x += 10)
                        {
                            int pixel = Marshal.ReadInt32(data.Scan0, y * data.Stride + x * 4);
                            int r = (pixel >> 16) & 255, g = (pixel >> 8) & 255, b = pixel & 255;
                            lightSum += (r * 2126 + g * 7152 + b * 722) / 10000; lightCount++;
                            if (x < w * 55 / 100 || x > w * 96 / 100) continue;
                            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                            if (max - min < 35 || max < 45 || max > 248) continue;
                            Color c = Color.FromArgb(r, g, b);
                            int bin = Math.Min(35, (int)(c.GetHue() / 10));
                            int weight = max - min;
                            weights[bin] += weight; rr[bin] += r * weight; gg[bin] += g * weight; bb[bin] += b * weight;
                        }
                }
                finally { bitmap.UnlockBits(data); }
                meanLight = lightCount == 0 ? 0.5 : (double)lightSum / lightCount / 255;
                int best = 0;
                for (int i = 1; i < 36; i++) if (weights[i] > weights[best]) best = i;
                if (weights[best] < 2000) return false;
                result = Color.FromArgb(rr[best] / weights[best], gg[best] / weights[best], bb[best] / weights[best]);
                return true;
            }
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
        {
            int rgb = (c.R << 16) | (c.G << 8) | c.B;
            int bgr = unchecked((int)0xFF000000) | (c.B << 16) | (c.G << 8) | c.R;
            using (var key = Registry.CurrentUser.CreateSubKey(Dwm))
            {
                key.SetValue("AccentColor", bgr, RegistryValueKind.DWord);
                key.SetValue("ColorizationColor", rgb, RegistryValueKind.DWord);
                key.SetValue("ColorizationAfterglow", rgb, RegistryValueKind.DWord);
                key.SetValue("ColorPrevalence", 1, RegistryValueKind.DWord);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(Explorer))
            {
                key.SetValue("AccentColorMenu", bgr, RegistryValueKind.DWord);
                key.SetValue("StartColorMenu", bgr, RegistryValueKind.DWord);
                key.SetValue("AccentPalette", MakePalette(c), RegistryValueKind.Binary);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(Desktop))
                key.SetValue("AutoColorization", 0, RegistryValueKind.DWord);
            if (light.HasValue)
                using (var key = Registry.CurrentUser.CreateSubKey(Personalize))
                    key.SetValue("AppsUseLightTheme", light.Value ? 1 : 0, RegistryValueKind.DWord);
            else if (darkApps)
                using (var key = Registry.CurrentUser.CreateSubKey(Personalize))
                    key.SetValue("AppsUseLightTheme", 0, RegistryValueKind.DWord);
            if (colorizeShell)
                using (var key = Registry.CurrentUser.CreateSubKey(Personalize))
                {
                    key.SetValue("SystemUsesLightTheme", 0, RegistryValueKind.DWord);
                    key.SetValue("ColorPrevalence", 1, RegistryValueKind.DWord);
                }
            Notify();
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
        {
            IntPtr result;
            SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 2, 1000, out result);
            SendMessageTimeout(new IntPtr(0xffff), 0x031A, IntPtr.Zero, null, 2, 1000, out result);
        }
    }
}
