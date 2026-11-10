using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace QQMusicWallpaper;

internal sealed class SurfaceAppearanceState
{
    internal readonly ThemePreferences Theme;
    internal readonly ExplorerAppearanceSettings Explorer;
    internal readonly Color Accent;
    internal readonly bool Active;
    internal SurfaceAppearanceState(ThemePreferences theme, ExplorerAppearanceSettings explorer, Color accent, bool active)
    { Theme = theme.Copy(); Explorer = explorer.Copy(); Accent = accent; Active = active && theme.Enabled; }
}

// Runs on the theme worker's UI thread, never the desktop input hook thread.
// These are display-only, click-through surfaces. No formatting writes,
// input interception or application injection is involved.
internal sealed class AppearanceSurfaces : IDisposable
{
    private readonly System.Windows.Forms.Timer timer;
    private readonly Dictionary<IntPtr, TintSurface> surfaces = new Dictionary<IntPtr, TintSurface>();
    private readonly OfficeContentProbe probe = new OfficeContentProbe();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private volatile SurfaceAppearanceState state;
    private SurfaceAppearanceState previous;
    private double nextDiscovery;
    private DateTime nextErrorLog;
    private Bitmap field;
    private readonly Func<IntPtr, SurfaceAppearanceState, SurfaceKind> identify;

    internal AppearanceSurfaces() : this(SurfaceWindows.Identify) { }

    internal AppearanceSurfaces(Func<IntPtr, SurfaceAppearanceState, SurfaceKind> identify)
    {
        this.identify = identify;
        timer = new System.Windows.Forms.Timer { Interval = 66 };
        timer.Tick += delegate
        {
            try { Tick(); }
            catch (Exception error)
            {
                Clear();
                if (DateTime.UtcNow >= nextErrorLog)
                {
                    RuntimeLog.Write("Appearance surface refresh failed: " + error.GetType().Name);
                    nextErrorLog = DateTime.UtcNow.AddMinutes(1);
                }
            }
        };
        timer.Start();
    }

    internal void Configure(SurfaceAppearanceState value) { state = value; }

    internal void Tick()
    {
        // UIA and DWM return physical pixels. Scope DPI awareness to appearance
        // work so legacy QQ Music input/sampling coordinates are unaffected.
        using (new SurfaceDpiScope()) Refresh();
    }

    private void Refresh()
    {
        var current = state;
        if (current == null || !current.Active || (current.Accent.IsEmpty &&
            (ThemePaletteColors.UsesPlaybackColor(current.Theme.Palette) || !ThemePaletteColors.IsFlowing(current.Theme.Palette))))
        { Clear(); return; }
        if (current != previous) { nextDiscovery = 0; previous = current; }
        double seconds = clock.Elapsed.TotalSeconds;
        if (seconds >= nextDiscovery)
        {
            nextDiscovery = seconds + 1;
            Discover(current);
        }
        var remove = new List<IntPtr>();
        bool flowing = ThemePaletteColors.IsFlowing(current.Theme.Palette);
        if (flowing && surfaces.Count > 0)
        {
            Bitmap old = field;
            field = ThemePaletteColors.CreateField(current.Theme.Palette, seconds, current.Accent);
            foreach (var surface in surfaces.Values) surface.Field = field;
            if (old != null) old.Dispose();
        }
        foreach (var pair in surfaces)
        {
            IntPtr hwnd = pair.Key;
            TintSurface surface = pair.Value;
            uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
            if (!Native.IsWindow(hwnd) || pid != surface.ProcessId) { remove.Add(hwnd); continue; }
            Rectangle bounds;
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || SurfaceWindows.IsCloaked(hwnd) ||
                !SurfaceWindows.TryBounds(hwnd, out bounds) || bounds.Width < 2 || bounds.Height < 2 ||
                (long)bounds.Width * bounds.Height > 16000000)
            { surface.Hide(); continue; }
            Rectangle[] content = null;
            var kind = surface.Kind;
            int strength = current.Theme.OfficeTint;
            if (kind == SurfaceKind.Word || kind == SurfaceKind.Excel || kind == SurfaceKind.PowerPoint)
            {
                if (current.Theme.OfficeInterface != current.Theme.OfficeContent)
                {
                    // Refresh native screen geometry every frame. Asynchronous
                    // masks expire quickly and never survive a move or resize.
                    content = SurfaceWindows.NativeOfficeContent(hwnd, bounds, kind);
                    if (content.Length == 0) content = probe.Get(hwnd, bounds, pid);
                    if (content == null || content.Length == 0) { surface.Hide(); continue; }
                }
                using (Region region = OfficeRegion(bounds, content, current.Theme.OfficeInterface, current.Theme.OfficeContent))
                    surface.Present(bounds, region, strength, flowing ? field : null, current.Accent);
            }
            else
            {
                using (Region region = SurfaceWindows.ShellRegion(hwnd, bounds, kind, current.Explorer))
                {
                    strength = kind == SurfaceKind.Taskbar ? (100 - current.Explorer.TaskbarTransparency) : 42;
                    surface.Present(bounds, region, Math.Min(45, strength), field, current.Accent);
                }
            }
        }
        foreach (IntPtr hwnd in remove) { surfaces[hwnd].Dispose(); surfaces.Remove(hwnd); }
    }

    internal static Region OfficeRegion(Rectangle bounds, Rectangle[] content, bool ui, bool document)
    {
        var region = new Region(new Rectangle(Point.Empty, bounds.Size));
        if (!ui && !document) { region.MakeEmpty(); return region; }
        if (ui && document) return region;
        if (content == null || content.Length == 0) { region.MakeEmpty(); return region; }
        if (!ui) region.MakeEmpty();
        foreach (Rectangle value in content)
        {
            Rectangle local = Rectangle.Intersect(bounds, value); local.Offset(-bounds.X, -bounds.Y);
            if (ui) region.Exclude(local); else region.Union(local);
        }
        return region;
    }

    private void Discover(SurfaceAppearanceState current)
    {
        var wanted = new HashSet<IntPtr>();
        Native.EnumWindows((hwnd, unused) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
            SurfaceKind kind = identify(hwnd, current);
            if (kind == SurfaceKind.None) return true;
            wanted.Add(hwnd);
            if (!surfaces.ContainsKey(hwnd))
            {
                uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
                surfaces.Add(hwnd, new TintSurface(hwnd, pid, kind));
            }
            return true;
        }, IntPtr.Zero);
        var removed = new List<IntPtr>();
        foreach (var pair in surfaces) if (!wanted.Contains(pair.Key)) removed.Add(pair.Key);
        foreach (IntPtr hwnd in removed) { surfaces[hwnd].Dispose(); surfaces.Remove(hwnd); }
    }

    private void Clear()
    {
        foreach (var value in surfaces.Values) value.Dispose();
        surfaces.Clear();
        if (field != null) { field.Dispose(); field = null; }
        nextDiscovery = 0;
    }

    public void Dispose() { timer.Dispose(); Clear(); }
}

internal enum SurfaceKind { None, Word, Excel, PowerPoint, Explorer, Taskbar }

internal sealed class SurfaceDpiScope : IDisposable
{
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    private readonly IntPtr previous;
    internal SurfaceDpiScope() { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
    public void Dispose() { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
}

internal static class SurfaceWindows
{
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Native.RECT value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetInt(IntPtr window, int attribute, out int value, int size);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);

    internal static string ClassName(IntPtr hwnd)
    { var name = new StringBuilder(128); Native.GetClassName(hwnd, name, name.Capacity); return name.ToString(); }

    internal static bool TryBounds(IntPtr hwnd, out Rectangle bounds)
    {
        Native.RECT rect;
        if (DwmGetWindowAttribute(hwnd, 9, out rect, 16) != 0 && !Native.GetWindowRect(hwnd, out rect))
        { bounds = Rectangle.Empty; return false; }
        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    internal static bool IsCloaked(IntPtr hwnd)
    { int value; return DwmGetInt(hwnd, 14, out value, 4) == 0 && value != 0; }

    internal static SurfaceKind Identify(IntPtr hwnd, SurfaceAppearanceState state)
    {
        string name = ClassName(hwnd), process;
        SurfaceKind kind;
        bool content = state.Theme.OfficeTint > 0 && (state.Theme.OfficeInterface || state.Theme.OfficeContent);
        if (name == "OpusApp" && content && state.Theme.Word) { kind = SurfaceKind.Word; process = "WINWORD"; }
        else if (name == "XLMAIN" && content && state.Theme.Excel) { kind = SurfaceKind.Excel; process = "EXCEL"; }
        else if (name == "PPTFrameClass" && content && state.Theme.PowerPoint) { kind = SurfaceKind.PowerPoint; process = "POWERPNT"; }
        else if (ThemePaletteColors.IsFlowing(state.Theme.Palette) && state.Explorer.Enabled && name == "CabinetWClass")
        { kind = SurfaceKind.Explorer; process = "explorer"; }
        else if (ThemePaletteColors.IsFlowing(state.Theme.Palette) && state.Explorer.TaskbarAcrylic &&
            (name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd"))
        { kind = SurfaceKind.Taskbar; process = "explorer"; }
        else return SurfaceKind.None;
        uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
        return ProcessIdentity.Matches(pid, process) ? kind : SurfaceKind.None;
    }

    internal static Rectangle[] NativeOfficeContent(IntPtr window, Rectangle bounds, SurfaceKind kind)
    {
        using (new SurfaceDpiScope()) return ReadOfficeContent(window, bounds, kind);
    }

    private static Rectangle[] ReadOfficeContent(IntPtr window, Rectangle bounds, SurfaceKind kind)
    {
        var result = new List<Rectangle>();
        string target = kind == SurfaceKind.Word ? "_WwG" : kind == SurfaceKind.Excel ? "EXCEL7" : "";
        if (target.Length == 0) return result.ToArray();
        Native.EnumChildWindows(window, (hwnd, unused) =>
        {
            Native.RECT rect;
            if (Native.IsWindowVisible(hwnd) && ClassName(hwnd) == target && Native.GetWindowRect(hwnd, out rect))
            {
                Rectangle value = Rectangle.Intersect(bounds, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                if (value.Width > 0 && value.Height > 0) result.Add(value);
            }
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }

    internal static Region ShellRegion(IntPtr window, Rectangle bounds, SurfaceKind kind, ExplorerAppearanceSettings settings)
    {
        var region = new Region(new Rectangle(Point.Empty, bounds.Size));
        if (kind == SurfaceKind.Taskbar) return region;
        region.MakeEmpty();
        int contentTop = bounds.Bottom;
        Native.EnumChildWindows(window, (hwnd, unused) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            string name = ClassName(hwnd);
            bool file = name == "SHELLDLL_DefView", tree = name == "SysTreeView32";
            if (!file && !tree) return true;
            Native.RECT rect;
            if (!Native.GetWindowRect(hwnd, out rect)) return true;
            Rectangle value = Rectangle.Intersect(bounds, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
            if (value.Width <= 0 || value.Height <= 0) return true;
            contentTop = Math.Min(contentTop, value.Top);
            if ((file && settings.FileAreaAcrylic) || (tree && settings.SidebarAcrylic))
            { value.Offset(-bounds.X, -bounds.Y); region.Union(value); }
            return true;
        }, IntPtr.Zero);
        if (contentTop < bounds.Bottom && settings.HeaderStyle != ExplorerHeaderStyle.Neutral &&
            (settings.HeaderStyle == ExplorerHeaderStyle.Acrylic || settings.HeaderTint > 0))
            region.Union(new Rectangle(0, 0, bounds.Width, contentTop - bounds.Top));
        return region;
    }
}

// UIA calls may stall in an external provider. At most one outstanding scan is
// allowed; expired/moved results are discarded without waiting on a UI thread.
internal sealed class OfficeContentProbe
{
    private sealed class Result
    {
        internal IntPtr Window; internal Rectangle Bounds; internal uint Pid;
        internal Rectangle[] Content; internal DateTime Captured;
    }
    private readonly Dictionary<IntPtr, Result> cache = new Dictionary<IntPtr, Result>();
    private readonly object gate = new object();
    private int busy;

    internal Rectangle[] Get(IntPtr hwnd, Rectangle bounds, uint pid)
    {
        Result result;
        lock (gate) cache.TryGetValue(hwnd, out result);
        bool valid = result != null && result.Pid == pid && result.Bounds == bounds &&
            DateTime.UtcNow - result.Captured < TimeSpan.FromMilliseconds(750);
        bool refresh = !valid || DateTime.UtcNow - result.Captured > TimeSpan.FromMilliseconds(300);
        if (refresh && Interlocked.CompareExchange(ref busy, 1, 0) == 0)
        {
            ThreadPool.QueueUserWorkItem(unused =>
            {
                try
                {
                    Rectangle[] content = Capture(hwnd, bounds);
                    Rectangle after; uint afterPid; Native.GetWindowThreadProcessId(hwnd, out afterPid);
                    if (!SurfaceWindows.TryBounds(hwnd, out after) || after != bounds || afterPid != pid) return;
                    lock (gate)
                    {
                        if (cache.Count > 32) cache.Clear();
                        cache[hwnd] = new Result { Window = hwnd, Bounds = bounds, Pid = pid, Content = content, Captured = DateTime.UtcNow };
                    }
                }
                catch (Exception) { /* A closed, busy or inaccessible Office window remains untinted. */ }
                finally { Interlocked.Exchange(ref busy, 0); }
            });
        }
        return valid ? result.Content : null;
    }

    internal static Rectangle[] Capture(IntPtr hwnd, Rectangle bounds)
    {
        if (SurfaceWindows.ClassName(hwnd) == "PPTFrameClass")
        {
            var slide = PowerPointContent.Capture(hwnd, bounds);
            if (slide.Length > 0) return slide;
        }
        var result = new List<Rectangle>();
        var queue = new Queue<AutomationElement>();
        var watch = Stopwatch.StartNew();
        queue.Enqueue(AutomationElement.FromHandle(hwnd));
        int visited = 0;
        while (queue.Count > 0 && visited++ < 350 && watch.ElapsedMilliseconds < 250)
        {
            var element = queue.Dequeue();
            var info = element.Current;
            if (info.IsOffscreen) continue;
            if (info.ControlType == ControlType.ToolBar || info.ControlType == ControlType.MenuBar ||
                info.ControlType == ControlType.TitleBar || info.ControlType == ControlType.StatusBar ||
                info.ControlType == ControlType.Button || info.ControlType == ControlType.MenuItem) continue;
            if (info.ControlType == ControlType.Document || info.ControlType == ControlType.DataGrid)
            {
                var r = info.BoundingRectangle;
                if (!r.IsEmpty)
                {
                    var value = Rectangle.Intersect(bounds, Rectangle.FromLTRB((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom));
                    if (value.Width > 0 && value.Height > 0) result.Add(value);
                }
                continue;
            }
            var child = TreeWalker.ControlViewWalker.GetFirstChild(element);
            while (child != null && queue.Count + visited < 350 && watch.ElapsedMilliseconds < 250)
            { queue.Enqueue(child); child = TreeWalker.ControlViewWalker.GetNextSibling(child); }
        }
        // A partial scan cannot safely exclude every document from UI-only tint.
        return queue.Count == 0 ? result.ToArray() : new Rectangle[0];
    }
}

internal sealed class TintSurface : Form
{
    internal readonly IntPtr Target;
    internal readonly uint ProcessId;
    internal readonly SurfaceKind Kind;
    internal Bitmap Field;
    private Color accent;
    private Rectangle lastBounds;
    private Region lastRegion;

    internal TintSurface(IntPtr target, uint pid, SurfaceKind kind)
    {
        Target = target; ProcessId = pid; Kind = kind;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true; TabStop = false;
        SetStyle(ControlStyles.Selectable, false);
        // Native ownership tracks minimize/restore and keeps the surface above
        // only its target. Never promote Office to the global topmost band.
        using (new SurfaceDpiScope()) Native.SetWindowLongPtr(Handle, -8, target);
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x080800A0; return value; }
    }

    internal void Present(Rectangle bounds, Region region, int strength, Bitmap bitmap, Color color)
    {
        using (new SurfaceDpiScope()) PresentPhysical(bounds, region, strength, bitmap, color);
    }

    private void PresentPhysical(Rectangle bounds, Region region, int strength, Bitmap bitmap, Color color)
    {
        using (var graphics = CreateGraphics())
        {
            if (strength <= 0 || region.IsEmpty(graphics)) { Hide(); return; }
            if (lastRegion == null || lastBounds != bounds || !region.Equals(lastRegion, graphics))
            {
                Region old = Region;
                Region = region.Clone();
                if (old != null) old.Dispose();
                if (lastRegion != null) lastRegion.Dispose();
                lastRegion = region.Clone();
                lastBounds = bounds;
            }
        }
        bool repaint = bitmap != null || Field != bitmap || accent != color || Bounds != bounds;
        Field = bitmap; accent = color; BackColor = color.IsEmpty ? Color.Gray : color;
        Opacity = Math.Max(1, Math.Min(45, strength)) / 100.0;
        Bounds = bounds;
        if (!Visible) Show();
        // Place immediately above the owner, underneath any menus, dialogs or
        // unrelated applications which are already above that owner.
        IntPtr previous = SurfaceWindows.GetWindow(Target, 3); // GW_HWNDPREV
        if (previous != Handle)
            Native.SetWindowPos(Handle, previous, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0200);
        if (repaint) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Field == null) { e.Graphics.Clear(accent.IsEmpty ? Color.Gray : accent); return; }
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        using (var attributes = new System.Drawing.Imaging.ImageAttributes())
        {
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            e.Graphics.DrawImage(Field, ClientRectangle, 0, 0, Field.Width, Field.Height, GraphicsUnit.Pixel, attributes);
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; }
        if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && lastRegion != null) { lastRegion.Dispose(); lastRegion = null; }
        base.Dispose(disposing);
    }
}
