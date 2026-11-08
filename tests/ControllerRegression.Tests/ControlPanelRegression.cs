using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class ControlPanelRegression
{
    internal static void Register(Action<string, Action> run)
    {
        run("control panel settings survive a newer runtime seed", SettingsSurviveSeedUpgrade);
        run("control panel clamps malformed settings and emits invariant XAML", ConfigurationBoundaries);
        run("control panel applies explicit values once and permits retry on failure", ApplyLifecycle);
        run("control panel native layout renders without external app automation", RenderPanel);
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static string TemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "MusicWallpaper-panel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static void SettingsSurviveSeedUpgrade()
    {
        string root = TemporaryDirectory();
        try
        {
            var appearance = new ExplorerAppearanceSettings { Transparency = 65, TintGray = 90, SidebarAcrylic = false, HeaderStyle = ExplorerHeaderStyle.Neutral, HeaderTint = 22 };
            string saved = Path.Combine(root, "preferences.ini");
            appearance.Save(saved);
            string runtime = Path.Combine(root, "runtime");
            string relative = "Engine/Mods/windows-11-file-explorer-styler.ini";
            string seed = Path.Combine(runtime, "Seed/" + relative);
            Directory.CreateDirectory(Path.GetDirectoryName(seed));
            File.WriteAllText(seed, "[Mod]\nLibraryFileName=old.dll\nDisabled=0\n[Settings]\ncontrolStyles[8].target=obsolete\n", Encoding.Unicode);
            string destination = Path.Combine(root, "active");
            ExplorerThemeRuntime.InstallSeedFiles(runtime, destination, ExplorerAppearanceSettings.Load(saved));
            File.WriteAllText(seed, "[Mod]\nLibraryFileName=new.dll\nDisabled=0\n[Settings]\n", Encoding.Unicode);
            ExplorerThemeRuntime.InstallSeedFiles(runtime, destination, ExplorerAppearanceSettings.Load(saved));
            string result = File.ReadAllText(Path.Combine(destination, relative));
            Assert(result.Contains("LibraryFileName=new.dll") && result.Contains("piaoklxtAcrylicOpacity=89"), "Upgrade lost the selected transparency or new binary identity.");
            Assert(result.Contains("piaoklxtSidebarAcrylic=0") && result.Contains("TintOpacity=\"0\""), "Upgrade reset navigation/header preferences.");
            Assert(!result.Contains("obsolete") && !result.Contains("controlStyles[8]"), "A stale style survived generation.");
            byte[] bytes = File.ReadAllBytes(Path.Combine(destination, relative));
            Assert(bytes[0] == 255 && bytes[1] == 254, "Windhawk config lost UTF-16LE encoding.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void ConfigurationBoundaries()
    {
        string root = TemporaryDirectory();
        CultureInfo previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            string path = Path.Combine(root, "settings.ini");
            File.WriteAllText(path, "Transparency=500\nTintGray=-20\nHeaderStyle=invalid\nHeaderTint=38\nEnabled=0\n");
            var settings = ExplorerAppearanceSettings.Load(path);
            Assert(settings.Transparency == 90 && settings.TintGray == 0 && settings.HeaderStyle == ExplorerHeaderStyle.ThemeColor, "Invalid settings escaped validation.");
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string generated = settings.CreateRuntimeConfiguration("[Mod]\nDisabled=0\n[Settings]\n");
            Assert(generated.Contains("TintOpacity=\"0.38\"") && generated.Contains("Disabled=1"), "Locale or master switch changed generated behavior.");
            settings.HeaderStyle = ExplorerHeaderStyle.Acrylic;
            generated = settings.CreateRuntimeConfiguration("[Mod]\n[Settings]\n");
            Assert(generated.Contains("Color=\"Transparent\"") && !generated.Contains("OpaqueBase="), "Acrylic header still had an opaque override.");
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; Directory.Delete(root, true); }
    }
    private static T Find<T>(Control root, string name) where T : Control { return (T)root.Controls.Find(name, true)[0]; }
    private static void Click(Button button) { typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty }); }
    private static void ApplyLifecycle()
    {
        using (var form = new ControlPanelForm(new ThemePreferences(), new ExplorerAppearanceSettings()))
        {
            IntPtr handle = form.Handle;
            Find<TrackBar>(form, "AcrylicTransparency").Value = 67;
            Find<CheckBox>(form, "SidebarAcrylic").Checked = false;
            Find<ComboBox>(form, "HeaderStyle").SelectedIndex = 2;
            Find<CheckBox>(form, "ColorizeShell").Checked = false;
            Action<string> finish = null;
            int requests = 0;
            form.ApplyRequested += (theme, explorer, completion) =>
            {
                requests++;
                Assert(explorer.Transparency == 67 && !explorer.SidebarAcrylic && explorer.HeaderStyle == ExplorerHeaderStyle.Acrylic && !theme.ColorizeShell, "UI submitted stale or toggled values.");
                finish = completion;
            };
            var apply = Find<Button>(form, "Apply");
            Click(apply); Click(apply);
            Assert(requests == 1 && !Find<Button>(form, "Defaults").Enabled, "Pending apply was submitted twice or could be reset.");
            finish("test failure"); Application.DoEvents();
            Assert(apply.Enabled && Find<Label>(form, "Status").Text == "test failure", "Failed apply could not be retried.");
            Click(apply); finish(null); Application.DoEvents();
            Assert(requests == 2 && !apply.Enabled, "Success was not acknowledged.");
            Find<TrackBar>(form, "AcrylicGray").Value = 62;
            Click(apply);
            form.Dispose();
            finish(null); // Completion after closing a panel must not escape onto the worker thread.
        }
    }
    private static void RenderPanel()
    {
        Application.EnableVisualStyles();
        using (var form = new ControlPanelForm(new ThemePreferences(), new ExplorerAppearanceSettings()))
        {
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-18000, -18000);
            IntPtr handle = form.Handle;
            Native.SetWindowLongPtr(handle, -20, new IntPtr(Native.GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000L));
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "control-panel-preview.png");
            using (var bitmap = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(output); }
            Assert(Find<ComboBox>(form, "HeaderStyle").Items.Count == 3, "Header modes missing.");
        }
    }
}
