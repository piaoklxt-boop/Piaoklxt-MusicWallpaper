using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace QQMusicWallpaper;

internal sealed class ThemePreferences
{
    public bool Enabled = true;
    public bool MatchBrightness;
    public bool ColorizeShell = true;
    public bool DarkApps = true;

    public static ThemePreferences Load(string path)
    {
        var value = new ThemePreferences();
        if (!File.Exists(path)) return value;
        string text = File.ReadAllText(path);
        value.Enabled = AppearanceIni.ReadInt(text, "Enabled", 1, 0, 1) == 1;
        value.MatchBrightness = AppearanceIni.ReadInt(text, "MatchBrightness", 0, 0, 1) == 1;
        value.ColorizeShell = AppearanceIni.ReadInt(text, "ColorizeShell", 1, 0, 1) == 1;
        value.DarkApps = AppearanceIni.ReadInt(text, "DarkApps", 1, 0, 1) == 1;
        return value;
    }
}

internal enum ExplorerHeaderStyle { ThemeColor, Neutral, Acrylic }

internal sealed class ExplorerAppearanceSettings
{
    public bool Enabled = true;
    public int Transparency = 30;
    public int TintGray = 61;
    public bool SidebarAcrylic = true;
    public ExplorerHeaderStyle HeaderStyle;
    public int HeaderTint = 38;

    public static string SettingsPath { get { return Path.Combine(AppPaths.DataDirectory, "explorer-appearance.ini"); } }

    public static ExplorerAppearanceSettings Load(string path)
    {
        var value = new ExplorerAppearanceSettings();
        if (!File.Exists(path)) return value;
        string text = File.ReadAllText(path);
        value.Enabled = AppearanceIni.ReadInt(text, "Enabled", 1, 0, 1) == 1;
        value.Transparency = AppearanceIni.ReadInt(text, "Transparency", 30, 0, 90);
        value.TintGray = AppearanceIni.ReadInt(text, "TintGray", 61, 0, 200);
        value.SidebarAcrylic = AppearanceIni.ReadInt(text, "SidebarAcrylic", 1, 0, 1) == 1;
        value.HeaderStyle = (ExplorerHeaderStyle)AppearanceIni.ReadInt(text, "HeaderStyle", 0, 0, 2);
        value.HeaderTint = AppearanceIni.ReadInt(text, "HeaderTint", 38, 0, 100);
        return value;
    }

    public void Save(string path)
    {
        string text = "Enabled=" + (Enabled ? 1 : 0) + "\nTransparency=" + Transparency +
            "\nTintGray=" + TintGray + "\nSidebarAcrylic=" + (SidebarAcrylic ? 1 : 0) +
            "\nHeaderStyle=" + (int)HeaderStyle + "\nHeaderTint=" + HeaderTint + "\n";
        AppearanceIni.WriteAtomic(path, text, Encoding.UTF8);
    }

    internal bool SameValues(ExplorerAppearanceSettings other)
    {
        return other != null && Enabled == other.Enabled && Transparency == other.Transparency &&
            TintGray == other.TintGray && SidebarAcrylic == other.SidebarAcrylic &&
            HeaderStyle == other.HeaderStyle && HeaderTint == other.HeaderTint;
    }

    internal string CreateRuntimeConfiguration(string seed)
    {
        int transparency = Math.Max(0, Math.Min(90, Transparency));
        int opacity = (int)Math.Round(255 * (100 - transparency) / 100.0, MidpointRounding.AwayFromZero);
        string text = AppearanceIni.Set(seed, "Disabled", Enabled ? "0" : "1");
        text = AppearanceIni.Set(text, "backgroundTranslucentEffect", "acrylicblur");
        text = AppearanceIni.Set(text, "backgroundTranslucentEffectRegion", "");
        text = AppearanceIni.Set(text, "SettingsChangeTime", ((DateTime.UtcNow.Ticks - new DateTime(1970, 1, 1).Ticks) / TimeSpan.TicksPerSecond).ToString(CultureInfo.InvariantCulture));
        text = Regex.Replace(text, @"(?m)^controlStyles\[\d+\]\.[^\r\n]*(?:\r?\n|$)", "");
        string brush;
        if (HeaderStyle == ExplorerHeaderStyle.Acrylic)
            brush = "<SolidColorBrush Color=\"Transparent\"/>";
        else
        {
            double tint = HeaderStyle == ExplorerHeaderStyle.Neutral ? 0 : Math.Max(0, Math.Min(100, HeaderTint)) / 100.0;
            brush = "<WindhawkBlur TintColor=\"{ThemeResource SystemAccentColor}\" TintOpacity=\"" +
                tint.ToString("0.##", CultureInfo.InvariantCulture) + "\" OpaqueBase=\"True\"/>";
        }
        string[] targets = {
            "Grid#NavigationBarControlGrid",
            "FileExplorerExtensions.CommandBarControl_Wave1 > Grid, Grid#CommandBarControlRootGrid",
            "CommandBar#FileExplorerCommandBar", "CommandBar#FileExplorerSecondaryCommandBar",
            "Grid#TabContainerGrid", "Grid#ContentRoot",
            "FileExplorerExtensions.NavigationBarControl, FileExplorerExtensions.CommandBarControl_Wave1, FileExplorerExtensions.CommandBarControl, FileExplorerExtensions.FileExplorerTabControl"
        };
        var styles = new StringBuilder();
        for (int i = 0; i < targets.Length; i++)
            styles.Append("controlStyles[").Append(i).Append("].target=").Append(targets[i]).Append('\n')
                .Append("controlStyles[").Append(i).Append("].styles[0]=Background:=").Append(brush).Append('\n');
        text = text.TrimEnd() + "\n" + styles;
        text = AppearanceIni.Set(text, "piaoklxtAcrylicOpacity", opacity.ToString(CultureInfo.InvariantCulture));
        text = AppearanceIni.Set(text, "piaoklxtAcrylicGray", Math.Max(0, Math.Min(200, TintGray)).ToString(CultureInfo.InvariantCulture));
        return AppearanceIni.Set(text, "piaoklxtSidebarAcrylic", SidebarAcrylic ? "1" : "0");
    }
}

internal static class AppearanceIni
{
    internal static int ReadInt(string text, string key, int fallback, int min, int max)
    {
        Match match = Regex.Match(text, @"(?m)^" + Regex.Escape(key) + @"\s*=\s*([^\r\n]*)");
        int value;
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            ? Math.Max(min, Math.Min(max, value)) : fallback;
    }

    internal static string Set(string text, string key, string value)
    {
        string pattern = @"(?m)^" + Regex.Escape(key) + @"=[^\r\n]*";
        return Regex.IsMatch(text, pattern) ? Regex.Replace(text, pattern, _ => key + "=" + value)
            : text.TrimEnd() + "\n" + key + "=" + value + "\n";
    }

    internal static void WriteAtomic(string path, string text, Encoding encoding)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + ".pending";
        File.WriteAllText(temporary, text, encoding);
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
}
