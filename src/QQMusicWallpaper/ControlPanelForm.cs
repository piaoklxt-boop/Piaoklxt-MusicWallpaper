using System;
using System.Drawing;
using System.Windows.Forms;

namespace QQMusicWallpaper;

internal sealed class ControlPanelForm : Form
{
    private readonly CheckBox follow = Check("FollowArtwork", "启用主题配色与流动效果");
    private readonly CheckBox shell = Check("ColorizeShell", "任务栏和开始菜单使用系统强调色");
    private readonly CheckBox brightness = Check("MatchBrightness", "跟随歌词页原生配色自动切换应用明暗");
    private readonly CheckBox dark = Check("DarkApps", "应用默认使用深色模式");
    private readonly CheckBox word = Check("Word", "Word");
    private readonly CheckBox excel = Check("Excel", "Excel");
    private readonly CheckBox powerpoint = Check("PowerPoint", "PowerPoint");
    private readonly CheckBox officeInterface = Check("OfficeInterface", "渲染界面区域（标题栏、功能区及侧栏）");
    private readonly CheckBox officeContent = Check("OfficeContent", "渲染内容区域（正文纸张、单元格及幻灯片）");
    private readonly TrackBar officeTint = Slider("OfficeTint", 0, 45);
    private readonly CheckBox explorerEnabled = Check("ExplorerEnabled", "启用文件资源管理器外观");
    private readonly CheckBox folderSizes = Check("FolderSizes", "在“大小”列显示文件夹大小");
    private readonly CheckBox sidebar = Check("SidebarAcrylic", "左侧导航栏使用亚克力");
    private readonly CheckBox files = Check("FileAreaAcrylic", "文件区域使用亚克力");
    private readonly TrackBar transparency = Slider("AcrylicTransparency", 0, 90);
    private readonly TrackBar gray = Slider("AcrylicGray", 0, 200);
    private readonly TrackBar headerTint = Slider("HeaderTint", 0, 100);
    private readonly ComboBox header = new ComboBox { Name = "HeaderStyle", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox palette = new ComboBox { Name = "ThemePalette", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox tone = new ComboBox { Name = "ThemeTone", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly CheckBox general = Check("GeneralApps", "其他普通应用窗口使用主题效果");
    private readonly TrackBar generalTint = Slider("GeneralTint", 0, 45);
    private readonly CheckBox taskbarColors = Check("TaskbarColors", "任务栏叠加主题配色（含炫彩与渐变）");
    private readonly CheckBox taskbar = Check("TaskbarAcrylic", "任务栏使用亚克力");
    private readonly CheckBox extendBottom = Check("ExtendBottom", "向下延伸壁纸底边至任务栏");
    private readonly TrackBar taskbarTransparency = Slider("TaskbarTransparency", 0, 100);
    private readonly TrackBar edgeDepth = Slider("EdgeDepth", 1, 100);
    private readonly Button apply = new Button { Name = "Apply", Text = "应用", AutoSize = true, MinimumSize = new Size(88, 32) };
    private readonly Button defaults = new Button { Name = "Defaults", Text = "恢复默认值", AutoSize = true, MinimumSize = new Size(104, 32) };
    private readonly Label status = new Label { Name = "Status", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8) };
    private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill, Name = "SettingsTabs", Multiline = true };
    private readonly ThemePreview preview = new ThemePreview();
    private readonly Label paletteHint = Hint("");
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
        ClientSize = new Size(760, 660);
        MinimumSize = new Size(660, 570);
        StartPosition = FormStartPosition.CenterScreen;
        ShowIcon = false;
        FormClosed += delegate { OperationLog.Record(OperationKind.ControlPanel, "closed"); };
        BackColor = Color.FromArgb(244, 247, 248);
        foreach (var combo in new[] { header, palette, tone })
        {
            combo.Margin = new Padding(0, 4, 0, 12);
            combo.MinimumSize = new Size(0, combo.PreferredHeight);
            combo.FontChanged += delegate { combo.MinimumSize = new Size(0, combo.PreferredHeight); };
        }

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 16, 22, 16) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = "外观与配色", AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), ForeColor = Color.FromArgb(0, 105, 100), Margin = new Padding(0, 0, 0, 16) };
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        root.Controls.Add(status, 0, 2);

        var explorerPage = Page("文件资源管理器");
        var themePage = Page("主题配色");
        var taskbarPage = Page("任务栏");
        var officePage = Page("Microsoft Office");
        var generalPage = Page("其他应用");
        tabs.TabPages.Add(themePage.Item1);
        tabs.TabPages.Add(explorerPage.Item1);
        tabs.TabPages.Add(taskbarPage.Item1);
        tabs.TabPages.Add(officePage.Item1);
        tabs.TabPages.Add(generalPage.Item1);
        var g = generalPage.Item2;
        Add(g, general);
        Add(g, Hint("默认覆盖浏览器、编辑器等普通应用主窗口，使用相同的单色、渐变或炫彩及浅色／深色效果。"));
        Add(g, SliderRow("颜色强度", generalTint, "%"));
        Add(g, Hint("只改变屏幕显示，不改应用内容。桌面、菜单、工具提示及 QQ 音乐源窗口不叠加；Office、资源管理器和任务栏由各自页面控制。"));
        var o = officePage.Item2;
        Add(o, Hint("使用“主题配色”页选择的颜色；在这里决定应用与区域。"));
        Add(o, Section("启用应用"));
        var officeApps = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
        foreach (var app in new[] { word, excel, powerpoint }) { app.Margin = new Padding(0, 5, 28, 5); officeApps.Controls.Add(app); }
        Add(o, officeApps);
        Add(o, Section("渲染范围"));
        Add(o, officeInterface); Add(o, officeContent);
        Add(o, Section("显示强度"));
        Add(o, SliderRow("颜色强度", officeTint, "%"));
        Add(o, Hint("只改变显示，不改变文件、打印或导出颜色。区域无法识别时暂不渲染。"));
        var e = explorerPage.Item2;
        Add(e, explorerEnabled);
        Add(e, Section("亚克力区域"));
        Add(e, files);
        Add(e, sidebar);
        Add(e, Hint("分别控制文件列表与左侧导航；关闭的区域使用不透明底色。"));
        Add(e, SliderRow("亚克力透明度", transparency, "%"));
        Add(e, SliderRow("亚克力底色亮度", gray, ""));
        Add(e, Section("地址栏与命令栏"));
        header.Items.AddRange(new object[] { "主题色（不透明）", "深灰底色（不透明）", "跟随文件区亚克力" });
        Add(e, header);
        Add(e, SliderRow("顶部主题色强度", headerTint, "%"));
        Add(e, Section("文件夹大小"));
        Add(e, folderSizes);
        Add(e, Hint("后台计算本地磁盘文件夹，完成后自动填入大小列；结果缓存两分钟。大目录需等待；不跟随目录链接，无法完整读取时保持空白。"));
        var t = themePage.Item2;
        Add(t, follow);
        Add(t, Hint("控制动态配色。各应用的渲染范围与亚克力材质在对应页面设置。"));
        palette.Items.AddRange(new object[] { "单色 · 播放／暂停键", "中性灰蓝", "暖色炫彩", "冷色炫彩", "全色炫彩", "渐变单色 · 播放／暂停键" });
        Add(t, palette);
        Add(t, Section("配色明暗"));
        tone.Items.AddRange(new object[] { "原色 · 保持配色亮度", "浅色 · 明亮柔和", "深色 · 深沉低亮" });
        Add(t, tone);
        Add(t, preview);
        Add(t, paletteHint);
        Add(t, Section("系统明暗"));
        Add(t, brightness);
        Add(t, dark);
        Add(t, Hint("配色明暗只改变色层亮度，不切换系统黑白主题。应用明暗由上面的设置独立控制，暂停后恢复原系统主题。"));
        var b = taskbarPage.Item2;
        Add(b, taskbarColors);
        Add(b, Hint("配色色层与亚克力分别控制，共用下方透明度。关闭色层后，亚克力仍可显示 Windows 原有颜色。"));
        Add(b, Section("亚克力材质"));
        Add(b, taskbar);
        Add(b, SliderRow("任务栏透明度", taskbarTransparency, "%"));
        Add(b, Hint("流动配色跟随主题选择；透明度越高，颜色越淡。"));
        Add(b, Section("Windows 系统强调色"));
        Add(b, shell);
        Add(b, Hint("决定系统是否使用主题代表色，与上方亚克力材质开关独立。"));
        var w = b;
        Add(w, Section("任务栏下方的壁纸"));
        Add(w, extendBottom);
        Add(w, SliderRow("底边采样深度", edgeDepth, "%"));
        Add(w, Hint("将最下方色带延伸到任务栏下方，保留上方画面的比例与点击位置。较小的采样深度更接近底边纯色；透过任务栏亚克力可以看到延伸画面。"));

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var close = new Button { Text = "关闭", AutoSize = true, MinimumSize = new Size(88, 32) };
        close.Click += delegate { Close(); };
        defaults.Click += delegate { LoadValues(new ThemePreferences(), new ExplorerAppearanceSettings()); MarkDirty(); };
        apply.BackColor = Color.FromArgb(0, 105, 100); apply.ForeColor = Color.White; apply.FlatStyle = FlatStyle.Flat;
        apply.FlatAppearance.BorderSize = 0;
        buttons.Controls.Add(defaults, 0, 0);
        buttons.Controls.Add(apply, 2, 0);
        buttons.Controls.Add(close, 3, 0);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        AcceptButton = apply;
        CancelButton = close;
        foreach (var c in new[] { follow, shell, brightness, dark, explorerEnabled, sidebar, files, taskbar, extendBottom, word, excel, powerpoint, officeInterface, officeContent, general, taskbarColors, folderSizes }) c.CheckedChanged += delegate { MarkDirty(); };
        foreach (var c in new[] { transparency, gray, headerTint, taskbarTransparency, edgeDepth, officeTint, generalTint }) c.ValueChanged += delegate { MarkDirty(); };
        palette.SelectedIndexChanged += delegate { UpdatePreview(); MarkDirty(); };
        tone.SelectedIndexChanged += delegate { UpdatePreview(); MarkDirty(); };
        header.SelectedIndexChanged += delegate { MarkDirty(); };
        apply.Click += delegate { Apply(); };
        LoadValues(theme, explorer);
        status.Text = "修改后点击“应用”，设置会保留到下次启动。";
        apply.Enabled = false;
    }

    private static CheckBox Check(string name, string text) { return new CheckBox { Name = name, Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 4) }; }
    private static TrackBar Slider(string name, int minimum, int maximum) { return new TrackBar { Name = name, Minimum = minimum, Maximum = maximum, TickStyle = TickStyle.None, Dock = DockStyle.Fill, AutoSize = false, Height = 30, SmallChange = 1, LargeChange = 5 }; }
    private static Label Hint(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(90, 109, 114), MaximumSize = new Size(600, 0), Margin = new Padding(0, 4, 0, 10) }; }
    private static Label Section(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(0, 105, 100), Margin = new Padding(0, 16, 0, 5) }; }
    private static Tuple<TabPage, TableLayoutPanel> Page(string title)
    {
        var page = new TabPage(title) { BackColor = Color.White, AutoScroll = true, Padding = new Padding(20, 12, 20, 12) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, GrowStyle = TableLayoutPanelGrowStyle.AddRows, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(table);
        table.SizeChanged += delegate
        {
            foreach (Control child in table.Controls)
                if (child is Label) child.MaximumSize = new Size(Math.Max(180, table.ClientSize.Width - child.Margin.Horizontal), 0);
        };
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
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        row.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(160, 0), Anchor = AnchorStyles.Left }, 0, 0);
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
        tone.SelectedIndex = (int)theme.Tone;
        general.Checked = theme.GeneralApps; generalTint.Value = theme.GeneralTint; taskbarColors.Checked = theme.TaskbarColors;
        UpdatePreview();
        word.Checked = theme.Word; excel.Checked = theme.Excel; powerpoint.Checked = theme.PowerPoint;
        officeInterface.Checked = theme.OfficeInterface; officeContent.Checked = theme.OfficeContent;
        officeTint.Value = theme.OfficeTint;
        taskbar.Checked = explorer.TaskbarAcrylic; taskbarTransparency.Value = explorer.TaskbarTransparency;
        extendBottom.Checked = explorer.ExtendBottom; edgeDepth.Value = explorer.EdgeDepth;
        folderSizes.Checked = explorer.FolderSizes;
        loading = false;
        UpdateAvailability();
    }
    private void UpdateAvailability()
    {
        shell.Enabled = palette.Enabled = tone.Enabled = general.Enabled = taskbarColors.Enabled = follow.Checked;
        brightness.Enabled = follow.Checked;
        generalTint.Enabled = follow.Checked && general.Checked;
        word.Enabled = excel.Enabled = powerpoint.Enabled = officeInterface.Enabled = officeContent.Enabled = follow.Checked;
        officeTint.Enabled = follow.Checked && (officeInterface.Checked || officeContent.Checked);
        taskbarTransparency.Enabled = taskbar.Checked || (follow.Checked && taskbarColors.Checked); edgeDepth.Enabled = extendBottom.Checked;
        dark.Enabled = follow.Checked && !brightness.Checked;
        transparency.Enabled = gray.Enabled = sidebar.Enabled = files.Enabled = header.Enabled = explorerEnabled.Checked;
        headerTint.Enabled = explorerEnabled.Checked && header.SelectedIndex == 0;
    }
    private void UpdatePreview()
    {
        preview.Palette = (ThemePalette)Math.Max(0, palette.SelectedIndex);
        preview.Tone = (ThemeTone)Math.Max(0, tone.SelectedIndex);
        preview.Invalidate();
        paletteHint.Text = palette.SelectedIndex == 5 ? "同一基色从深色到浅色流动，亮度范围 30%–100%；基色取播放／暂停键。" :
            palette.SelectedIndex >= 2 ? "多种高饱和颜色同时游动、交融，伴随明显的深浅变化。" :
            palette.SelectedIndex == 0 ? "读取歌词页共享高亮色属性；播放键隐藏时仍可更新。" : "固定的灰蓝配色；亚克力透明度在各应用页设置。";
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
            ";tone=" + tone.SelectedIndex + ";general=" + general.Checked + ";generalTint=" + generalTint.Value + ";taskbarColors=" + taskbarColors.Checked +
            ";taskbar=" + taskbar.Checked + ";taskbarTransparency=" + taskbarTransparency.Value + ";edge=" + extendBottom.Checked + ";depth=" + edgeDepth.Value;
    }
    internal ExplorerAppearanceSettings ReadExplorer() { return new ExplorerAppearanceSettings { FolderSizes = folderSizes.Checked, Enabled = explorerEnabled.Checked, Transparency = transparency.Value, TintGray = gray.Value, SidebarAcrylic = sidebar.Checked, FileAreaAcrylic = files.Checked, HeaderStyle = (ExplorerHeaderStyle)header.SelectedIndex, HeaderTint = headerTint.Value, TaskbarAcrylic = taskbar.Checked, TaskbarTransparency = taskbarTransparency.Value, ExtendBottom = extendBottom.Checked, EdgeDepth = edgeDepth.Value }; }
    internal ThemePreferences ReadTheme() { return new ThemePreferences { Enabled = follow.Checked, ColorizeShell = shell.Checked, MatchBrightness = brightness.Checked, DarkApps = dark.Checked, Palette = (ThemePalette)palette.SelectedIndex, Tone = (ThemeTone)tone.SelectedIndex, GeneralApps = general.Checked, GeneralTint = generalTint.Value, TaskbarColors = taskbarColors.Checked, Word = word.Checked, Excel = excel.Checked, PowerPoint = powerpoint.Checked, OfficeInterface = officeInterface.Checked, OfficeContent = officeContent.Checked, OfficeTint = officeTint.Value }; }
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
