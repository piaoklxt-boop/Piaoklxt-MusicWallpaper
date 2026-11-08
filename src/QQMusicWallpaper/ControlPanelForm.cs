using System;
using System.Drawing;
using System.Windows.Forms;

namespace QQMusicWallpaper;

internal sealed class ControlPanelForm : Form
{
    private readonly CheckBox follow = Check("FollowArtwork", "随 QQ 音乐封面同步主题色");
    private readonly CheckBox shell = Check("ColorizeShell", "任务栏和开始菜单跟随主题色");
    private readonly CheckBox brightness = Check("MatchBrightness", "根据封面亮度自动切换应用明暗");
    private readonly CheckBox dark = Check("DarkApps", "应用默认使用深色模式");
    private readonly CheckBox explorerEnabled = Check("ExplorerEnabled", "启用文件资源管理器外观");
    private readonly CheckBox sidebar = Check("SidebarAcrylic", "侧边栏使用亚克力");
    private readonly TrackBar transparency = Slider("AcrylicTransparency", 0, 90);
    private readonly TrackBar gray = Slider("AcrylicGray", 0, 200);
    private readonly TrackBar headerTint = Slider("HeaderTint", 0, 100);
    private readonly ComboBox header = new ComboBox { Name = "HeaderStyle", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly Button apply = new Button { Name = "Apply", Text = "应用", AutoSize = true, MinimumSize = new Size(88, 32) };
    private readonly Button defaults = new Button { Name = "Defaults", Text = "恢复默认值", AutoSize = true, MinimumSize = new Size(104, 32) };
    private readonly Label status = new Label { Name = "Status", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8) };
    private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill, Name = "SettingsTabs" };
    private bool loading;
    private bool applying;

    public event Action<ThemePreferences, ExplorerAppearanceSettings, Action<string>> ApplyRequested;

    public ControlPanelForm(ThemePreferences theme, ExplorerAppearanceSettings explorer)
    {
        Text = "MusicWallpaper · 控制面板";
        Name = "MusicWallpaperControlPanel";
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(640, 570);
        MinimumSize = new Size(570, 550);
        StartPosition = FormStartPosition.CenterScreen;
        ShowIcon = false;
        BackColor = SystemColors.Window;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 16, 22, 16) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = "桌面外观", AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), ForeColor = Color.FromArgb(0, 105, 100), Margin = new Padding(0, 0, 0, 16) };
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        root.Controls.Add(status, 0, 2);

        var explorerPage = Page("文件资源管理器");
        var themePage = Page("主题同步");
        tabs.TabPages.Add(explorerPage.Item1);
        tabs.TabPages.Add(themePage.Item1);
        var e = explorerPage.Item2;
        Add(e, explorerEnabled);
        Add(e, Hint("文件区与预览区共用亚克力。关闭总开关可恢复系统原样。"));
        Add(e, SliderRow("亚克力透明度", transparency, "%"));
        Add(e, SliderRow("底色亮度", gray, ""));
        Add(e, sidebar);
        Add(e, Hint("关闭侧边栏亚克力后，使用不透明的系统明暗底色。"));
        header.Items.AddRange(new object[] { "主题色（不透明）", "深灰底色（不透明）", "跟随文件区亚克力" });
        Add(e, new Label { Text = "顶部整体渲染", AutoSize = true, Margin = new Padding(0, 12, 0, 4) });
        Add(e, header);
        Add(e, SliderRow("顶部主题色强度", headerTint, "%"));
        var t = themePage.Item2;
        Add(t, follow);
        Add(t, Hint("播放封面变化时自动取色。暂停动态壁纸后恢复原系统主题色。"));
        Add(t, shell);
        Add(t, brightness);
        Add(t, dark);
        Add(t, Hint("自动明暗优先于默认深色。这里的明暗设置也会影响其他支持系统主题的应用。"));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = new Button { Text = "关闭", AutoSize = true, MinimumSize = new Size(88, 32) };
        close.Click += delegate { Close(); };
        defaults.Click += delegate { LoadValues(new ThemePreferences(), new ExplorerAppearanceSettings()); MarkDirty(); };
        buttons.Controls.Add(close);
        buttons.Controls.Add(apply);
        buttons.Controls.Add(defaults);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        AcceptButton = apply;
        CancelButton = close;
        foreach (var c in new[] { follow, shell, brightness, dark, explorerEnabled, sidebar }) c.CheckedChanged += delegate { MarkDirty(); };
        foreach (var c in new[] { transparency, gray, headerTint }) c.ValueChanged += delegate { MarkDirty(); };
        header.SelectedIndexChanged += delegate { MarkDirty(); };
        apply.Click += delegate { Apply(); };
        LoadValues(theme, explorer);
        status.Text = "修改后点击“应用”，设置会保留到下次启动。";
        apply.Enabled = false;
    }

    private static CheckBox Check(string name, string text) { return new CheckBox { Name = name, Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 4) }; }
    private static TrackBar Slider(string name, int minimum, int maximum) { return new TrackBar { Name = name, Minimum = minimum, Maximum = maximum, TickStyle = TickStyle.None, Dock = DockStyle.Fill, AutoSize = false, Height = 30, SmallChange = 1, LargeChange = 5 }; }
    private static Label Hint(string text) { return new Label { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(520, 0), Margin = new Padding(0, 0, 0, 8) }; }
    private static Tuple<TabPage, TableLayoutPanel> Page(string title)
    {
        var page = new TabPage(title) { UseVisualStyleBackColor = true, AutoScroll = true, Padding = new Padding(16, 8, 16, 8) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(table);
        return Tuple.Create(page, table);
    }
    private static void Add(TableLayoutPanel panel, Control control)
    {
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        int row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(control, 0, row);
    }
    private static Control SliderRow(string text, TrackBar slider, string suffix)
    {
        var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 2) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        row.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        row.Controls.Add(slider, 1, 0);
        var value = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Text = slider.Value + suffix };
        slider.ValueChanged += delegate { value.Text = slider.Value + suffix; };
        row.Controls.Add(value, 2, 0);
        return row;
    }

    private void LoadValues(ThemePreferences theme, ExplorerAppearanceSettings explorer)
    {
        loading = true;
        follow.Checked = theme.Enabled; shell.Checked = theme.ColorizeShell;
        brightness.Checked = theme.MatchBrightness; dark.Checked = theme.DarkApps;
        explorerEnabled.Checked = explorer.Enabled; sidebar.Checked = explorer.SidebarAcrylic;
        transparency.Value = explorer.Transparency; gray.Value = explorer.TintGray;
        header.SelectedIndex = (int)explorer.HeaderStyle; headerTint.Value = explorer.HeaderTint;
        loading = false;
        UpdateAvailability();
    }
    private void UpdateAvailability()
    {
        shell.Enabled = brightness.Enabled = follow.Checked;
        dark.Enabled = follow.Checked && !brightness.Checked;
        transparency.Enabled = gray.Enabled = sidebar.Enabled = header.Enabled = explorerEnabled.Checked;
        headerTint.Enabled = explorerEnabled.Checked && header.SelectedIndex == 0;
    }
    private void MarkDirty()
    {
        if (loading || applying) return;
        UpdateAvailability();
        apply.Enabled = true;
        status.Text = "有未应用的更改。";
    }
    internal ExplorerAppearanceSettings ReadExplorer() { return new ExplorerAppearanceSettings { Enabled = explorerEnabled.Checked, Transparency = transparency.Value, TintGray = gray.Value, SidebarAcrylic = sidebar.Checked, HeaderStyle = (ExplorerHeaderStyle)header.SelectedIndex, HeaderTint = headerTint.Value }; }
    internal ThemePreferences ReadTheme() { return new ThemePreferences { Enabled = follow.Checked, ColorizeShell = shell.Checked, MatchBrightness = brightness.Checked, DarkApps = dark.Checked }; }
    private void Apply()
    {
        if (applying || ApplyRequested == null) return;
        applying = true; tabs.Enabled = false; apply.Enabled = false; defaults.Enabled = false;
        status.Text = "正在应用外观设置…";
        ApplyRequested(ReadTheme(), ReadExplorer(), error =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    applying = false; tabs.Enabled = true; defaults.Enabled = true;
                    apply.Enabled = error != null;
                    status.Text = error ?? "已应用并保存。";
                });
            }
            catch (InvalidOperationException) { /* The panel closed while applying. */ }
        });
    }
}
