using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using QQMusicWallpaper;
using QQMusicWallpaperThemeSync;

namespace ControllerRegression.Tests;

internal static class AppearanceSurfacesRegression
{
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);

    // Explicit fixture window supplied by the local test runner, never selected
    // by its document title. No input is sent and no existing document is edited.
    internal static int VerifyOfficeFixture(IntPtr target)
    {
        try
        {
            var theme = new ThemePreferences { Palette = ThemePalette.Spectrum, OfficeTint = 35 };
            var explorer = new ExplorerAppearanceSettings { Enabled = false };
            var initial = new SurfaceAppearanceState(theme, explorer, Color.Teal, true);
            var kind = SurfaceWindows.Identify(target, initial);
            Assert(kind == SurfaceKind.Word || kind == SurfaceKind.Excel || kind == SurfaceKind.PowerPoint, "Fixture is not an Office editor.");
            Rectangle bounds;
            Assert(SurfaceWindows.TryBounds(target, out bounds), "Fixture bounds unavailable.");
            var content = SurfaceWindows.NativeOfficeContent(target, bounds, kind);
            if (content.Length == 0) content = OfficeContentProbe.Capture(target, bounds);
            Console.WriteLine(kind + " contentRegions=" + content.Length);
            Assert(content.Length > 0, "Actual content geometry was not found.");
            var point = new Point(content[0].Left + content[0].Width / 2, content[0].Top + content[0].Height / 2);
            IntPtr before = Native.GetAncestor(WindowFromPoint(point), 2);
            using (var manager = new AppearanceSurfaces((hwnd, state) => hwnd == target ? SurfaceWindows.Identify(hwnd, state) : SurfaceKind.None))
            {
                manager.Configure(initial);
                Pump(manager, 250);
                Assert(Layers(manager).ContainsKey(target), "Fixture did not receive a surface.");
                var surface = Layers(manager)[target];
                IntPtr after = Native.GetAncestor(WindowFromPoint(point), 2);
                Assert(after == target || (before != target && before == after), "Display layer changed native hit testing: before=" + SurfaceWindows.ClassName(before) + " after=" + SurfaceWindows.ClassName(after));
                if (after != target) Console.WriteLine("Native Office hit-through check skipped: fixture occluded");
                SaveFixtureImage(target, bounds, kind + "-flow-0");
                Pump(manager, 2000);
                SaveFixtureImage(target, bounds, kind + "-flow-2");
                theme.OfficeContent = false;
                manager.Configure(new SurfaceAppearanceState(theme, explorer, Color.Teal, true));
                Pump(manager, 1000);
                Assert(surface.Visible && !surface.Region.IsVisible(point.X - bounds.X, point.Y - bounds.Y), "Content switch failed on real Office controls.");
                theme.OfficeContent = true; theme.OfficeInterface = false;
                manager.Configure(new SurfaceAppearanceState(theme, explorer, Color.Teal, true));
                Pump(manager, 1000);
                Assert(surface.Visible && surface.Region.IsVisible(point.X - bounds.X, point.Y - bounds.Y) &&
                    !surface.Region.IsVisible(30, 10), "Interface switch failed on real Office controls.");
                SaveFixtureImage(target, bounds, kind + "-content-only");
                manager.Configure(new SurfaceAppearanceState(theme, explorer, Color.Teal, false));
                manager.Tick();
                Assert(Layers(manager).Count == 0, "Fixture retained a layer after pause.");
            }
            Console.WriteLine(kind + " real-window region switches and cleanup passed; capture/hit-test skips listed above");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error.Message); return 1; }
    }

    private static void Pump(AppearanceSurfaces manager, int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds) { manager.Tick(); Application.DoEvents(); Thread.Sleep(35); }
    }

    private static void SaveFixtureImage(IntPtr target, Rectangle bounds, string name)
    {
        foreach (double x in new[] { .1, .5, .9 }) foreach (double y in new[] { .1, .5, .9 })
            if (Native.GetAncestor(WindowFromPoint(new Point(bounds.Left + (int)(bounds.Width * x), bounds.Top + (int)(bounds.Height * y))), 2) != target)
            { SaveNativeImage(target, bounds, name + "-native-only"); Console.WriteLine("Screen visual skipped: fixture occluded"); return; }
        string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appearance-visuals");
        Directory.CreateDirectory(output);
        using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            bitmap.Save(Path.Combine(output, name + ".png"));
        }
    }

    private static void SaveNativeImage(IntPtr target, Rectangle bounds, string name)
    {
        string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appearance-visuals");
        Directory.CreateDirectory(output);
        using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr dc = graphics.GetHdc();
            try { PrintWindow(target, dc, 2); } finally { graphics.ReleaseHdc(dc); }
            bitmap.Save(Path.Combine(output, name + ".png"));
        }
    }

    // Opt-in, read-only probe. It emits geometry/counts, never document titles,
    // document text, account details or full-screen captures.
    internal static int ProbeLive()
    {
        var state = new SurfaceAppearanceState(new ThemePreferences { Palette = ThemePalette.Spectrum },
            new ExplorerAppearanceSettings { TaskbarAcrylic = true }, Color.Teal, true);
        IntPtr source = IntPtr.Zero; long area = 0;
        Native.EnumWindows((hwnd, unused) =>
        {
            var kind = SurfaceWindows.Identify(hwnd, state);
            Rectangle bounds;
            if (kind != SurfaceKind.None && Native.IsWindowVisible(hwnd) && SurfaceWindows.TryBounds(hwnd, out bounds))
            {
                var rectangles = SurfaceWindows.NativeOfficeContent(hwnd, bounds, kind);
                Console.WriteLine(kind + " size=" + bounds.Size + " minimized=" + Native.IsIconic(hwnd) + " nativeContent=" + rectangles.Length);
                if (kind == SurfaceKind.PowerPoint && !Native.IsIconic(hwnd))
                {
                    var watch = Stopwatch.StartNew();
                    try { Console.WriteLine("PowerPoint semanticContent=" + OfficeContentProbe.Capture(hwnd, bounds).Length + " ms=" + watch.ElapsedMilliseconds); }
                    catch (Exception error) { Console.WriteLine("PowerPoint probe=" + error.GetType().Name); }
                }
            }
            uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
            Native.RECT rect;
            if (SurfaceWindows.ClassName(hwnd) == "TXGuiFoundation" && ProcessIdentity.Matches(pid, "QQMusic") &&
                Native.IsWindowVisible(hwnd) && Native.GetWindowRect(hwnd, out rect))
            {
                long current = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (current > area) { source = hwnd; area = current; }
            }
            return true;
        }, IntPtr.Zero);
        if (source == IntPtr.Zero) { Console.WriteLine("Playback source unavailable"); return 2; }
        Native.RECT sourceRect; Native.GetWindowRect(source, out sourceRect);
        Rectangle sourceBounds = Rectangle.FromLTRB(sourceRect.Left, sourceRect.Top, sourceRect.Right, sourceRect.Bottom);
        PlaybackAccent.Publish(source, sourceBounds, QQMusicAccessibility.CaptureSnapshot(source));
        Rectangle button;
        Console.WriteLine("Playback sourceSize=" + sourceBounds.Size + " semanticButton=" + PlaybackAccent.TryGetBounds(sourceBounds.Size, out button) + " localButton=" + button);
        Color accent; double light;
        bool sampled = Sampler.TrySample(out accent, out light);
        Console.WriteLine("Playback rendererSample=" + sampled + " accent=" + (sampled ? ColorTranslator.ToHtml(accent) : "unavailable"));
        IntPtr renderer = (IntPtr)typeof(Sampler).GetMethod("FindRenderer", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Native.RECT client;
        Console.WriteLine("Renderer found=" + (renderer != IntPtr.Zero) + " clientAvailable=" + Native.GetClientRect(renderer, out client) +
            " size=" + (client.Right - client.Left) + "x" + (client.Bottom - client.Top));
        return sampled ? 0 : 2;
    }

    internal static void Register(Action<string, Action> run)
    {
        run("appearance Office switches submit and persist independently", OfficeSettings);
        run("appearance content masks exclude disabled regions and unknown geometry", Masks);
        run("appearance display surface follows owner without activating or receiving input", WindowLifecycle);
        run("appearance manager removes surfaces on pause and app disable", ManagerLifecycle);
        run("appearance flowing palettes contain moving spatial colors", Flow);
        run("appearance sampler follows the active playback button and rejects moved sources", ButtonSample);
    }

    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Find<T>(Control root, string name) where T : Control { return (T)root.Controls.Find(name, true)[0]; }

    private static void OfficeSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            using (var panel = new ControlPanelForm(new ThemePreferences(), new ExplorerAppearanceSettings()))
            {
                Find<CheckBox>(panel, "Word").Checked = false;
                Find<CheckBox>(panel, "OfficeContent").Checked = false;
                Find<TrackBar>(panel, "OfficeTint").Value = 31;
                ThemePreferences submitted = null;
                panel.ApplyRequested += (theme, explorer, done) => { submitted = theme; done(null); };
                typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(Find<Button>(panel, "Apply"), new object[] { EventArgs.Empty });
                Assert(submitted != null && !submitted.Word && submitted.Excel && submitted.PowerPoint &&
                    !submitted.OfficeContent && submitted.OfficeInterface && submitted.OfficeTint == 31, "Panel submitted stale Office switches.");
                AppearanceIni.WriteAtomic(path, submitted.Serialize(), System.Text.Encoding.UTF8);
                var loaded = ThemePreferences.Load(path);
                Assert(loaded.Serialize() == submitted.Serialize(), "Restart lost Office preferences.");
                Find<CheckBox>(panel, "FollowArtwork").Checked = false;
                Assert(!Find<CheckBox>(panel, "Word").Enabled && !Find<CheckBox>(panel, "OfficeContent").Enabled,
                    "Office controls do not reflect the theme master switch.");
            }
        }
        finally { File.Delete(path); }
    }

    private static void Masks()
    {
        var window = new Rectangle(-100, 40, 800, 600);
        var content = new[] { new Rectangle(-80, 240, 360, 360), new Rectangle(300, 240, 360, 360) };
        using (var ui = AppearanceSurfaces.OfficeRegion(window, content, true, false))
        using (var body = AppearanceSurfaces.OfficeRegion(window, content, false, true))
        using (var unknown = AppearanceSurfaces.OfficeRegion(window, null, true, false))
        using (var image = new Bitmap(800, 600))
        using (var graphics = Graphics.FromImage(image))
        {
            Assert(ui.IsVisible(20, 30) && !ui.IsVisible(100, 250) && !ui.IsVisible(500, 250), "UI tint covers a document pane.");
            Assert(!body.IsVisible(20, 30) && body.IsVisible(100, 250) && body.IsVisible(500, 250), "Content-only tint leaks into UI.");
            Assert(unknown.IsEmpty(graphics), "Unknown content boundaries must not tint the whole window.");
        }
    }

    private static Form Owner()
    {
        var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(-18000, -18000, 800, 600), BackColor = Color.White };
        var handle = form.Handle;
        Native.SetWindowLongPtr(handle, -20, new IntPtr(Native.GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000L));
        Native.ShowWindow(handle, 4);
        return form;
    }

    private static void WindowLifecycle()
    {
        using (var owner = Owner())
        using (var surface = new TintSurface(owner.Handle, (uint)Process.GetCurrentProcess().Id, SurfaceKind.Word))
        using (var region = new Region(new Rectangle(0, 0, 800, 600)))
        {
            IntPtr foreground = Native.GetForegroundWindow();
            surface.Present(owner.Bounds, region, 22, null, Color.Teal);
            Application.DoEvents();
            Assert(Native.GetForegroundWindow() == foreground, "Tint activated or focused a window.");
            long style = Native.GetWindowLongPtr(surface.Handle, -20).ToInt64();
            Assert((style & 0x080800A0L) == 0x080800A0L && (style & 8) == 0, "Tint is interactive or globally topmost.");
            Assert(Native.GetWindowLongPtr(surface.Handle, -8) == owner.Handle, "Tint has the wrong native owner.");
            Rectangle moved = new Rectangle(-17000, -17500, 900, 650);
            owner.Bounds = moved;
            surface.Present(moved, region, 22, null, Color.Teal);
            Assert(surface.Bounds == moved, "Tint failed to follow physical window bounds.");
            surface.Present(moved, region, 0, null, Color.Teal);
            Assert(!Native.IsWindowVisible(surface.Handle), "Zero intensity left a visible layer.");
        }
    }

    private static Dictionary<IntPtr, TintSurface> Layers(AppearanceSurfaces manager)
    { return (Dictionary<IntPtr, TintSurface>)typeof(AppearanceSurfaces).GetField("surfaces", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager); }

    private static void ManagerLifecycle()
    {
        using (var owner = Owner())
        using (var manager = new AppearanceSurfaces((hwnd, state) => hwnd == owner.Handle && state.Theme.Word ? SurfaceKind.Word : SurfaceKind.None))
        {
            var settings = new ThemePreferences { Palette = ThemePalette.Spectrum };
            manager.Configure(new SurfaceAppearanceState(settings, new ExplorerAppearanceSettings(), Color.Blue, true));
            manager.Tick();
            Assert(Layers(manager).Count == 1, "Enabled Office app did not acquire a display layer.");
            var layer = Layers(manager)[owner.Handle];
            Assert(layer.Visible && layer.Field != null, "Flow did not reach the actual display surface.");
            owner.Bounds = new Rectangle(-17500, -18000, 700, 500);
            manager.Tick();
            Rectangle actual; SurfaceWindows.TryBounds(owner.Handle, out actual);
            Assert(layer.Bounds == actual, "Manager retained stale geometry after movement.");
            Native.ShowWindow(owner.Handle, 6);
            manager.Tick();
            Assert(!Native.IsWindowVisible(layer.Handle), "Minimized Office retained a visible layer.");
            Native.ShowWindow(owner.Handle, 4);
            settings.Word = false;
            manager.Configure(new SurfaceAppearanceState(settings, new ExplorerAppearanceSettings(), Color.Blue, true));
            manager.Tick();
            Assert(Layers(manager).Count == 0 && layer.IsDisposed, "App disable leaked a layer.");
            settings.Word = true;
            manager.Configure(new SurfaceAppearanceState(settings, new ExplorerAppearanceSettings(), Color.Blue, true));
            manager.Tick();
            manager.Configure(new SurfaceAppearanceState(settings, new ExplorerAppearanceSettings(), Color.Blue, false));
            manager.Tick();
            Assert(Layers(manager).Count == 0, "Wallpaper pause retained themed windows.");
        }
    }

    private static void Flow()
    {
        string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appearance-visuals");
        Directory.CreateDirectory(output);
        foreach (var palette in new[] { ThemePalette.Warm, ThemePalette.Cool, ThemePalette.Spectrum })
        using (var first = ThemePaletteColors.CreateField(palette, 0))
        using (var later = ThemePaletteColors.CreateField(palette, 4))
        using (var montage = new Bitmap(640, 200))
        using (var graphics = Graphics.FromImage(montage))
        {
            int spatial = 0, temporal = 0;
            Color center = first.GetPixel(32, 20);
            for (int y = 0; y < first.Height; y += 4) for (int x = 0; x < first.Width; x += 4)
            {
                Color a = first.GetPixel(x, y), b = later.GetPixel(x, y);
                if (Distance(center, a) > 35) spatial++;
                if (Distance(a, b) > 25) temporal++;
                float hue = a.GetHue();
                Assert(palette != ThemePalette.Warm || hue >= 323 || hue <= 57, "Warm flow leaves its family.");
                Assert(palette != ThemePalette.Cool || (hue >= 163 && hue <= 267), "Cool flow leaves its family.");
            }
            Assert(spatial > 30 && temporal > 20, "Flow is a uniform wash or does not move.");
            graphics.DrawImage(first, new Rectangle(0, 0, 320, 200));
            graphics.DrawImage(later, new Rectangle(320, 0, 320, 200));
            montage.Save(Path.Combine(output, palette + ".png"));
        }
    }
    private static int Distance(Color a, Color b) { return Math.Abs(a.R-b.R) + Math.Abs(a.G-b.G) + Math.Abs(a.B-b.B); }

    private static void ButtonSample()
    {
        using (var source = Owner())
        using (var frame = new Bitmap(source.Width, source.Height))
        using (var graphics = Graphics.FromImage(frame))
        {
            PlaybackAccent.ConfigureCache(null);
            var oldButton = new Rectangle(source.Left + 100, source.Top + 100, 40, 40);
            var activeButton = new Rectangle(source.Left + 200, source.Top + 500, 40, 40);
            var snapshot = new AccessibleTreeSnapshot(new[] {
                new AccessibleNodeSnapshot("收起歌曲详情页", "收起歌曲详情页", "", 43, false, 2, oldButton),
                new AccessibleNodeSnapshot("播放", "播放", "", 43, true, 2, oldButton),
                new AccessibleNodeSnapshot("暂停", "暂停", "", 43, false, 2, activeButton) });
            PlaybackAccent.Publish(source.Handle, source.Bounds, snapshot);
            graphics.Clear(Color.OrangeRed);
            graphics.FillRectangle(Brushes.LimeGreen, new Rectangle(200, 500, 40, 40));
            graphics.FillRectangle(Brushes.White, new Rectangle(210, 510, 5, 20));
            Rectangle button;
            Assert(PlaybackAccent.TryGetBounds(frame.Size, out button) && button == new Rectangle(200, 500, 40, 40), "Sampling selected a stale player bar.");
            Color color; double light;
            Assert(Sampler.TrySampleFrame(frame, button, out color, out light) && Distance(color, Color.LimeGreen) < 10,
                "Artwork or the white glyph replaced the actual button body color.");
            graphics.Clear(Color.White);
            graphics.FillRectangle(Brushes.Black, new Rectangle(212, 510, 8, 20));
            Color pending;
            PlaybackAccent.TryConfirmColor(Color.Black, DateTime.UtcNow.AddSeconds(-3), out pending);
            PlaybackAccent.TryConfirmColor(Color.Black, DateTime.UtcNow.AddSeconds(-1.5), out pending);
            Assert(Sampler.TrySampleFrame(frame, button, out color, out light) && color.ToArgb() == Color.Black.ToArgb(),
                "A dark playback icon was replaced by its light background.");
            graphics.Clear(Color.White);
            Assert(!Sampler.TrySampleFrame(frame, button, out color, out light), "An empty control region sampled the surrounding skin.");
            source.Left += 10;
            Assert(!Sampler.TrySampleFrame(frame, button, out color, out light), "Moved source accepted stale button coordinates.");
        }
    }
}
