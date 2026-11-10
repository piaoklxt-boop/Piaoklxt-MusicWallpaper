using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class VividThemeRegression
{
    internal static void Register(Action<string, Action> run)
    {
        foreach (var value in new[] { ThemePalette.Warm, ThemePalette.Cool, ThemePalette.Spectrum })
        {
            var palette = value;
            run("vivid theme remains visibly animated after Office compositing: " + palette, () => VisibleMotion(palette));
        }
        run("vivid panel separates theme, material and wallpaper settings", PanelGrouping);
        run("vivid single-hue gradient follows the sampled color and survives settings", Gradient);
        run("vivid panel keeps text and controls separate at small and enlarged layouts", PanelLayout);
    }

    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void VisibleMotion(ThemePalette palette)
    {
        using (var owner = new Form { Bounds = new Rectangle(-18000, -18000, 320, 200) })
        using (var surface = new TintSurface(owner.Handle, (uint)Process.GetCurrentProcess().Id, SurfaceKind.Word))
        using (var region = new Region(new Rectangle(0, 0, 320, 200)))
        using (var field0 = ThemePaletteColors.CreateField(palette, 0))
        using (var field2 = ThemePaletteColors.CreateField(palette, 2))
        using (var a = new Bitmap(320, 200))
        using (var b = new Bitmap(320, 200))
        {
            surface.Present(owner.Bounds, region, new ThemePreferences().OfficeTint, field0, Color.Teal);
            surface.DrawToBitmap(a, new Rectangle(Point.Empty, a.Size));
            surface.Present(owner.Bounds, region, new ThemePreferences().OfficeTint, field2, Color.Teal);
            surface.DrawToBitmap(b, new Rectangle(Point.Empty, b.Size));
            double change = 0; int samples = 0;
            for (int y = 10; y < 190; y += 10) for (int x = 10; x < 310; x += 10)
            {
                Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
                change += (Math.Abs(ca.R-cb.R)+Math.Abs(ca.G-cb.G)+Math.Abs(ca.B-cb.B)) * surface.Opacity;
                samples++;
            }
            double mean = change / samples;
            Console.WriteLine("INFO " + palette + " composited 2-second RGB movement=" + mean.ToString("F2"));
            string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appearance-visuals");
            Directory.CreateDirectory(output);
            a.Save(Path.Combine(output, "vivid-" + palette + "-0.png"));
            b.Save(Path.Combine(output, "vivid-" + palette + "-2.png"));
            Assert(mean >= 23, "Color movement becomes too faint after actual display opacity: " + mean.ToString("F2"));
        }
    }

    private static TabPage Page(Control control)
    { while (control != null && !(control is TabPage)) control = control.Parent; return control as TabPage; }
    private static void PanelGrouping()
    {
        using (var form = new ControlPanelForm(new ThemePreferences(), new ExplorerAppearanceSettings()))
        {
            var tabs = (TabControl)form.Controls.Find("SettingsTabs", true)[0];
            Assert(Page(form.Controls.Find("ThemePalette", true)[0]) == tabs.TabPages[0], "Theme selection is not the entry page.");
            Assert(Page(form.Controls.Find("ExtendBottom", true)[0]) == Page(form.Controls.Find("TaskbarAcrylic", true)[0]), "Taskbar wallpaper extension is on a separate page.");
            Assert(Page(form.Controls.Find("ColorizeShell", true)[0]) == Page(form.Controls.Find("TaskbarAcrylic", true)[0]), "Taskbar accent settings are spread across pages.");
        }
    }

    private static void Gradient()
    {
        var palette = ThemePalette.MonochromeGradient;
        Assert((int)palette == 5 && ThemePaletteColors.UsesPlaybackColor(palette), "New mode lost playback color or old palette compatibility.");
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, new ThemePreferences { Palette = palette }.Serialize());
            Assert(ThemePreferences.Load(path).Palette == palette, "Gradient did not survive restart.");
            using (var panel = new ControlPanelForm(ThemePreferences.Load(path), new ExplorerAppearanceSettings()))
                Assert(panel.ReadTheme().Palette == palette, "Panel changed the selected gradient.");
        }
        finally { File.Delete(path); }
        Color accent = Color.FromArgb(40, 180, 120);
        using (var first = ThemePaletteColors.CreateField(palette, 0, accent))
        using (var later = ThemePaletteColors.CreateField(palette, 3, accent))
        using (var neutral = ThemePaletteColors.CreateField(palette, 0, Color.White))
        {
            int minimum = 255, maximum = 0, change = 0;
            for (int y = 0; y < first.Height; y++) for (int x = 0; x < first.Width; x++)
            {
                var a = first.GetPixel(x, y); var b = later.GetPixel(x, y); var gray = neutral.GetPixel(x, y);
                Assert(Math.Abs(a.GetHue() - accent.GetHue()) < 3, "Single-hue gradient changed hue family.");
                Assert(gray.R == gray.G && gray.G == gray.B, "Neutral button acquired a colored gradient.");
                int v = Math.Max(a.R, Math.Max(a.G, a.B)); minimum = Math.Min(minimum,v); maximum = Math.Max(maximum,v);
                change += Math.Abs(a.G-b.G);
            }
            Assert(maximum-minimum > 150 && change / (first.Width*first.Height) > 25, "Gradient lacks depth or movement.");
            first.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appearance-visuals", "single-gradient.png"));
        }
        using (var owner = new Form { Bounds = new Rectangle(-18000, -18000, 320, 200) })
        {
            Native.ShowWindow(owner.Handle, 4);
            using (var manager = new AppearanceSurfaces((hwnd,state) => hwnd == owner.Handle ? SurfaceKind.Word : SurfaceKind.None))
            {
                var theme = new ThemePreferences { Palette = palette };
                manager.Configure(new SurfaceAppearanceState(theme, new ExplorerAppearanceSettings(), Color.Empty, true)); manager.Tick();
                var layers = (Dictionary<IntPtr,TintSurface>)typeof(AppearanceSurfaces).GetField("surfaces",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(manager);
                Assert(layers.Count == 0, "Gradient invented a color before sampling.");
                manager.Configure(new SurfaceAppearanceState(theme, new ExplorerAppearanceSettings(), accent, true)); manager.Tick();
                Assert(layers.Count == 1 && layers[owner.Handle].Field != null, "Gradient did not reach production rendering.");
                Assert(Math.Abs(layers[owner.Handle].Field.GetPixel(20,20).GetHue()-accent.GetHue()) < 3, "Renderer discarded sampled hue.");
            }
        }
    }

    private static void PanelLayout()
    {
        foreach (float size in new[] { 9f, 12f, 13.5f })
        using (var form = new ControlPanelForm(new ThemePreferences { Palette = ThemePalette.MonochromeGradient }, new ExplorerAppearanceSettings()))
        {
            form.Font = new Font("Microsoft YaHei UI", size);
            form.Size = size == 9 ? form.MinimumSize : new Size(900, 850);
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-18000, -18000); form.ShowInTaskbar = false;
            Native.SetWindowLongPtr(form.Handle, -20, new IntPtr(Native.GetWindowLongPtr(form.Handle,-20).ToInt64() | 0x08000000L));
            form.Show(); Application.DoEvents();
            var tabs = (TabControl)form.Controls.Find("SettingsTabs", true)[0];
            for (int i = 0; i < tabs.TabCount; i++)
            {
                tabs.SelectedIndex = i; Application.DoEvents(); form.PerformLayout();
                using (var bitmap = new Bitmap(form.Width,form.Height))
                {
                    form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"appearance-visuals","panel-"+size.ToString(System.Globalization.CultureInfo.InvariantCulture)+"-"+i+".png"));
                }
                CheckLayout(form);
            }
        }
    }
    private static void CheckLayout(Control root)
    {
        if (root is TableLayoutPanel || root is FlowLayoutPanel)
        {
            foreach (Control a in root.Controls) if(a.Visible)
            {
                Assert(a.Right <= root.ClientSize.Width + 2, "Control exceeds panel width: " + a.Name + " / " + a.Text);
                foreach (Control b in root.Controls) if (b != a && b.Visible)
                    Assert(!a.Bounds.IntersectsWith(b.Bounds), "Controls overlap: " + a.Name + " " + a.Bounds + " / " + b.Name + " " + b.Bounds);
            }
        }
        foreach (Control child in root.Controls) if(child.Visible) CheckLayout(child);
    }
}
