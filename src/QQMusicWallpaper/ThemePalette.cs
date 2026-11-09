using System;
using System.Drawing;

namespace QQMusicWallpaper;

internal enum ThemePalette { Artwork, Acrylic, Warm, Cool, Spectrum }

internal static class ThemePaletteColors
{
    internal static Color Resolve(ThemePalette palette, double seconds)
    {
        if (palette == ThemePalette.Acrylic) return Color.FromArgb(125, 139, 153);
        double phase = (seconds % 120 + 120) % 120 / 120;
        double hue = palette == ThemePalette.Warm ? 325 + 90 * (0.5 - 0.5 * Math.Cos(phase * Math.PI * 2)) :
            palette == ThemePalette.Cool ? 165 + 100 * (0.5 - 0.5 * Math.Cos(phase * Math.PI * 2)) : phase * 360;
        hue %= 360;
        const double saturation = 0.48, value = 0.78;
        double sector = hue / 60;
        int i = (int)sector;
        double f = sector - i, p = value * (1 - saturation), q = value * (1 - saturation * f), t = value * (1 - saturation * (1 - f));
        double r, g, b;
        switch (i)
        {
            case 0: r = value; g = t; b = p; break;
            case 1: r = q; g = value; b = p; break;
            case 2: r = p; g = value; b = t; break;
            case 3: r = p; g = q; b = value; break;
            case 4: r = t; g = p; b = value; break;
            default: r = value; g = p; b = q; break;
        }
        return Color.FromArgb((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }
}
