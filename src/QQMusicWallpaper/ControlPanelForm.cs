using System;
using System.Drawing;
using System.Windows.Forms;

namespace QQMusicWallpaper;

internal sealed class ControlPanelForm : Form
{
    private readonly CheckBox follow = Check("FollowArtwork", "启用统一主题色");
    private readonly CheckBox shell = Check("ColorizeShell", "任务栏和开始菜单跟随主题色");
    private readonly CheckBox brightness = Check("MatchBrightness", "根据封面亮度自动切换应用明暗");
    private readonly CheckBox dark = Check("DarkApps", "应用默认使用深色模式");
    private readonly CheckBox word = Check("Word", "Word");
    private readonly CheckBox excel = Check("Excel", "Excel");
    private readonly CheckBox powerpoint = Check("PowerPoint", "PowerPoint");
    private readonly CheckBox officeInterface = Check("OfficeInterface", "渲染界面区域（标题栏、功能区及侧栏）");
    private readonly CheckBox officeContent = Check("OfficeContent", "渲染内容区域（正文纸张、单元格及幻灯片）");
    private readonly TrackBar officeTint = Slider("OfficeTint", 0, 45);
    private readonly CheckBox explorerEnabled = Check("ExplorerEnabled", "启用文件资源管理器外观");
    private readonly CheckBox sidebar = Check("SidebarAcrylic", "左侧导航栏使用亚克力");
    private readonly CheckBox files = Check("FileAreaAcrylic", "文件区域使用亚克力");
    private readonly TrackBar transparency = Slider("AcrylicTransparency", 0, 90);
    private readonly TrackBar gray = Slider("AcrylicGray", 0, 200);
    private readonly TrackBar headerTint = Slider("HeaderTint", 0, 100);
    private readonly ComboBox header = new ComboBox { Name = "HeaderStyle", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox palette = new ComboBox { Name = "ThemePalette", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly CheckBox taskbar = Check("TaskbarAcrylic", "任务栏使用亚克力");
    private readonly CheckBox extendBottom = Check("ExtendBottom", "向下延伸壁纸底边至任务栏");
    private readonly TrackBar taskbarTransparency = Slider("TaskbarTransparency", 0, 100);
    private readonly TrackBar edgeDepth = Slider("EdgeDepth", 1, 100);
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
        FormClosed += delegate { OperationLog.Record(OperationKind.ControlPanel, "closed"); };
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
        var themePage = Page("统一主题");
        var taskbarPage = Page("任务栏");
        var officePage = Page("Microsoft Office");
        tabs.TabPages.Add(explorerPage.Item1);
        tabs.TabPages.Add(taskbarPage.Item1);
        tabs.TabPages.Add(themePage.Item1);
        tabs.TabPages.Add(officePage.Item1);
        var o = officePage.Item2;
        Add(o, Hint("跟随统一主题；分别控制应用和渲染区域。"));
        Add(o, word); Add(o, excel); Add(o, powerpoint);
        Add(o, officeInterface); Add(o, officeContent);
        Add(o, SliderRow("颜色强度", officeTint, "%"));
        Add(o, Hint("只改变屏幕显示，不修改文件格式、打印或导出颜色。关闭统一主题或暂停壁纸后恢复原显示。区域无法识别时暂不渲染，避免影响已关闭的区域。"));
        var e = explorerPage.Item2;
        Add(e, explorerEnabled);
        Add(e, Hint("文件区与预览区共用亚克力；顶部样式作用于地址栏和命令栏。"));
        Add(e, SliderRow("亚克力透明度", transparency, "%"));
        Add(e, SliderRow("底色亮度", gray, ""));
        Add(e, files);
        Add(e, Hint("控制文件列表及其空白区域；关闭后使用不透明背景。"));
        Add(e, sidebar);
        Add(e, Hint("控制左侧“桌面、下载、文档”一列；关闭后与顶部同色且不透明。"));
        header.Items.AddRange(new object[] { "主题色（不透明）", "深灰底色（不透明）", "跟随文件区亚克力" });
        Add(e, new Label { Text = "顶部内容区渲染", AutoSize = true, Margin = new Padding(0, 12, 0, 4) });
        Add(e, header);
        Add(e, SliderRow("顶部主题色强度", headerTint, "%"));
        var t = themePage.Item2;
        Add(t, follow);
        palette.Items.AddRange(new object[] { "单色 · 跟随播放／暂停键", "亚克力 · 中性", "暖色炫彩", "冷色炫彩", "全色炫彩" });
        Add(t, palette);
        Add(t, Hint("炫彩在同一区域内让多种颜色持续游动、交融。资源管理器、任务栏和 Office 共用配色；系统强调色保持稳定。"));
        var b = taskbarPage.Item2;
        Add(b, taskbar);
        Add(b, SliderRow("亚克力透明度", taskbarTransparency, "%"));
        Add(b, extendBottom);
        Add(b, SliderRow("底边采样深度", edgeDepth, "%"));
        Add(b, Hint("仅延伸画面最下方的一条色带，不改变上方画面比例或点击位置。较小采样深度更接近底边纯色；开启任务栏亚克力可看到模糊后的延伸。"));
        Add(t, Hint("单色主题取当前播放／暂停键的颜色。暂停动态壁纸后恢复原系统主题色。"));
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
        foreach (var c in new[] { follow, shell, brightness, dark, explorerEnabled, sidebar, files, taskbar, extendBottom, word, excel, powerpoint, officeInterface, officeContent }) c.CheckedChanged += delegate { MarkDirty(); };
        foreach (var c in new[] { transparency, gray, headerTint, taskbarTransparency, edgeDepth, officeTint }) c.ValueChanged += delegate { MarkDirty(); };
        palette.SelectedIndexChanged += delegate { MarkDirty(); };
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
        files.Checked = explorer.FileAreaAcrylic;
        transparency.Value = explorer.Transparency; gray.Value = explorer.TintGray;
        header.SelectedIndex = (int)explorer.HeaderStyle; headerTint.Value = explorer.HeaderTint;
        palette.SelectedIndex = (int)theme.Palette;
        word.Checked = theme.Word; excel.Checked = theme.Excel; powerpoint.Checked = theme.PowerPoint;
        officeInterface.Checked = theme.OfficeInterface; officeContent.Checked = theme.OfficeContent;
        officeTint.Value = theme.OfficeTint;
        taskbar.Checked = explorer.TaskbarAcrylic; taskbarTransparency.Value = explorer.TaskbarTransparency;
        extendBottom.Checked = explorer.ExtendBottom; edgeDepth.Value = explorer.EdgeDepth;
        loading = false;
        UpdateAvailability();
    }
    private void UpdateAvailability()
    {
        shell.Enabled = brightness.Enabled = palette.Enabled = follow.Checked;
        word.Enabled = excel.Enabled = powerpoint.Enabled = officeInterface.Enabled = officeContent.Enabled = follow.Checked;
        officeTint.Enabled = follow.Checked && (officeInterface.Checked || officeContent.Checked);
        taskbarTransparency.Enabled = taskbar.Checked; edgeDepth.Enabled = extendBottom.Checked;
        dark.Enabled = follow.Checked && !brightness.Checked;
        transparency.Enabled = gray.Enabled = sidebar.Enabled = files.Enabled = header.Enabled = explorerEnabled.Checked;
        headerTint.Enabled = explorerEnabled.Checked && header.SelectedIndex == 0;
    }
    private void MarkDirty()
    {
        if (loading || applying) return;
        UpdateAvailability();
        apply.Enabled = true;
        status.Text = "有未应用的更改。";
        OperationLog.Record(OperationKind.SettingsChange, "pending", SettingsSummary());
    }
    private string SettingsSummary()
    {
        return "enabled=" + explorerEnabled.Checked + ";files=" + files.Checked + ";sidebar=" + sidebar.Checked +
            ";transparency=" + transparency.Value + ";gray=" + gray.Value + ";header=" + header.SelectedIndex +
            ";tint=" + headerTint.Value + ";follow=" + follow.Checked + ";shell=" + shell.Checked +
            ";brightness=" + brightness.Checked + ";dark=" + dark.Checked + ";palette=" + palette.SelectedIndex +
            ";taskbar=" + taskbar.Checked + ";taskbarTransparency=" + taskbarTransparency.Value + ";edge=" + extendBottom.Checked + ";depth=" + edgeDepth.Value;
    }
    internal ExplorerAppearanceSettings ReadExplorer() { return new ExplorerAppearanceSettings { Enabled = explorerEnabled.Checked, Transparency = transparency.Value, TintGray = gray.Value, SidebarAcrylic = sidebar.Checked, FileAreaAcrylic = files.Checked, HeaderStyle = (ExplorerHeaderStyle)header.SelectedIndex, HeaderTint = headerTint.Value, TaskbarAcrylic = taskbar.Checked, TaskbarTransparency = taskbarTransparency.Value, ExtendBottom = extendBottom.Checked, EdgeDepth = edgeDepth.Value }; }
    internal ThemePreferences ReadTheme() { return new ThemePreferences { Enabled = follow.Checked, ColorizeShell = shell.Checked, MatchBrightness = brightness.Checked, DarkApps = dark.Checked, Palette = (ThemePalette)palette.SelectedIndex, Word = word.Checked, Excel = excel.Checked, PowerPoint = powerpoint.Checked, OfficeInterface = officeInterface.Checked, OfficeContent = officeContent.Checked, OfficeTint = officeTint.Value }; }
    private void Apply()
    {
        if (applying || ApplyRequested == null) return;
        applying = true; tabs.Enabled = false; apply.Enabled = false; defaults.Enabled = false;
        status.Text = "正在应用外观设置…";
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        OperationLog.Record(OperationKind.SettingsApply, "requested", SettingsSummary());
        ApplyRequested(ReadTheme(), ReadExplorer(), error =>
        {
            OperationLog.Record(OperationKind.SettingsApply, error == null ? "completed" : "failed",
                "elapsedMs=" + elapsed.ElapsedMilliseconds);
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
