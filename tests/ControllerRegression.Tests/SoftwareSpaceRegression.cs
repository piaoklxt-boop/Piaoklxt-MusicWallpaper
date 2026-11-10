using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class SoftwareSpaceRegression
{
    internal static void Register(Action<string, Action> run)
    {
        run("software space captures deep navigation and excludes inactive descendants", CaptureTree);
        run("software space text writes verify the live semantic identity", TextValue);
        run("software space taskbar preferences survive save and clamp configuration", TaskbarSettings);
        run("software space palettes remain within the selected hue family", Palettes);
        run("software space controls submit taskbar and palette choices", PanelChoices);
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void CaptureTree()
    {
        using (var form = new TreeFixture())
        {
            var snapshot = QQMusicAccessibility.CaptureSnapshot(form.Root);
            Assert(snapshot.TryGetControlTargetAtPoint(new Point(35, 35), out var nav) && nav.Name == "Navigation" && nav.Depth > 16,
                "Deep named-pane navigation was not captured.");
            Assert(!snapshot.TryGetControlTargetAtPoint(new Point(235, 35), out var stale), "An invisible page leaked its visible-looking child.");
            Assert(!snapshot.TryGetControlTargetAtPoint(new Point(435, 35), out var disabled), "A disabled control owns input.");
            Assert(snapshot.GetInteractiveBounds(new Rectangle(0, 0, 1000, 600), false).Length == 2, "Unexpected active hit areas.");
        }
    }
    private static void TextValue()
    {
        using (var form = new TreeFixture())
        {
            var snapshot = QQMusicAccessibility.CaptureSnapshot(form.Root);
            Assert(snapshot.TryGetControlTargetAtPoint(new Point(35, 135), out var edit), "Editable control missing.");
            string value = "";
            Assert(QQMusicAccessibility.AccessTextValue(form.Root, edit, false, ref value) && value == "initial", "Native value could not be read.");
            value = "中文 test";
            Assert(QQMusicAccessibility.AccessTextValue(form.Root, edit, true, ref value) && form.Editor.Value == value, "Native value write failed.");
            form.Editor.Caption = "Replacement";
            Assert(!QQMusicAccessibility.AccessTextValue(form.Root, edit, true, ref value), "A substituted editor accepted stale input.");
            form.Editor.Caption = "Search"; form.Editor.Flags = AccessibleStates.ReadOnly;
            Assert(!QQMusicAccessibility.AccessTextValue(form.Root, edit, true, ref value), "A read-only editor accepted input.");
        }
    }
    private static void TaskbarSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), "MusicWallpaper-appearance-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            var settings = new ExplorerAppearanceSettings { Enabled = false, TaskbarAcrylic = true, TaskbarTransparency = 87, ExtendBottom = true, EdgeDepth = 7 };
            settings.Save(path);
            Assert(ExplorerAppearanceSettings.Load(path).SameValues(settings), "Taskbar values were lost on save.");
            string config = settings.CreateTaskbarConfiguration("[Mod]\nDisabled=1\n[Settings]\ncontrolStyles[9].target=old\n");
            Assert(config.Contains("Disabled=0") && config.Contains("TintOpacity=\"0.13\"") && !config.Contains("old"), "Taskbar styling depends on Explorer's switch or retained old styles.");
            settings.TaskbarAcrylic = false;
            Assert(settings.CreateTaskbarConfiguration(config).Contains("Disabled=1"), "Taskbar acrylic cannot be disabled.");
            File.WriteAllText(path, "EdgeDepth=0\nTaskbarTransparency=500\nPalette=99\n");
            Assert(ExplorerAppearanceSettings.Load(path).EdgeDepth == 1 && ExplorerAppearanceSettings.Load(path).TaskbarTransparency == 100,
                "Invalid extension settings escaped clamping.");
            Assert(ThemePreferences.Load(path).Palette == ThemePalette.MonochromeGradient, "Invalid palette escaped clamping.");
        }
        finally { File.Delete(path); }
    }
    private static void Palettes()
    {
        for (int second = 0; second < 240; second++)
        {
            float warm = ThemePaletteColors.Resolve(ThemePalette.Warm, second).GetHue();
            float cool = ThemePaletteColors.Resolve(ThemePalette.Cool, second).GetHue();
            Assert(warm >= 323 || warm <= 57, "Warm palette left its hue family.");
            Assert(cool >= 163 && cool <= 267, "Cool palette left its hue family.");
        }
        Assert(ThemePaletteColors.Resolve(ThemePalette.Spectrum, 0) == ThemePaletteColors.Resolve(ThemePalette.Spectrum, 120), "Palette cycle is discontinuous.");
    }
    private static void PanelChoices()
    {
        using (var form = new ControlPanelForm(new ThemePreferences { Palette = ThemePalette.Cool },
            new ExplorerAppearanceSettings { TaskbarAcrylic = true, ExtendBottom = true, EdgeDepth = 9 }))
        {
            Assert(form.ReadTheme().Palette == ThemePalette.Cool && form.ReadExplorer().TaskbarAcrylic && form.ReadExplorer().ExtendBottom && form.ReadExplorer().EdgeDepth == 9,
                "Control panel changed the loaded theme/taskbar preferences.");
        }
    }
    private sealed class TreeFixture : IDisposable
    {
        internal readonly Node Editor = new Node("Search", AccessibleRole.Text, new Rectangle(20, 120, 150, 30)) { Value = "initial" };
        internal readonly Node Root;
        internal TreeFixture()
        {
            Node deep = new Node("Navigation", AccessibleRole.Pane, new Rectangle(20, 20, 150, 40));
            for (int i = 0; i < 21; i++) deep = new Node("", AccessibleRole.Client, Rectangle.Empty, deep);
            var inactive = new Node("", AccessibleRole.Client, Rectangle.Empty,
                new Node("Stale", AccessibleRole.PushButton, new Rectangle(220, 20, 150, 40))) { Flags = AccessibleStates.Invisible };
            var disabled = new Node("Disabled", AccessibleRole.PushButton, new Rectangle(420, 20, 150, 40)) { Flags = AccessibleStates.Unavailable };
            Root = new Node("", AccessibleRole.Client, Rectangle.Empty, deep, inactive, disabled, Editor);
        }
        public void Dispose() { }
    }
    private sealed class Node : Accessibility.IAccessible
    {
        internal string Caption;
        internal string Value;
        internal AccessibleStates Flags;
        private readonly AccessibleRole role;
        private readonly Rectangle bounds;
        private readonly Node[] children;
        internal Node(string caption, AccessibleRole role, Rectangle bounds, params Node[] children)
        { Caption = caption; this.role = role; this.bounds = bounds; this.children = children; }
        public int accChildCount { get { return children.Length; } }
        public object accParent { get { return null; } }
        public object accFocus { get { return null; } }
        public object accSelection { get { return null; } }
        public object get_accChild(object child) { return children[Convert.ToInt32(child) - 1]; }
        public string get_accName(object child) { return Caption; }
        public void set_accName(object child, string value) { Caption = value; }
        public string get_accValue(object child) { return Value; }
        public void set_accValue(object child, string value) { Value = value; }
        public object get_accRole(object child) { return (int)role; }
        public object get_accState(object child) { return (int)Flags; }
        public string get_accDescription(object child) { return ""; }
        public string get_accDefaultAction(object child) { return ""; }
        public string get_accHelp(object child) { return ""; }
        public int get_accHelpTopic(out string file, object child) { file = ""; return 0; }
        public string get_accKeyboardShortcut(object child) { return ""; }
        public void accLocation(out int x, out int y, out int w, out int h, object child) { x = bounds.X; y = bounds.Y; w = bounds.Width; h = bounds.Height; }
        public void accSelect(int flags, object child) { }
        public void accDoDefaultAction(object child) { }
        public object accHitTest(int x, int y) { return null; }
        public object accNavigate(int direction, object child) { return null; }
    }
}
