using System;
using System.Drawing;

namespace QQMusicWallpaper;

internal enum ThemePalette { Artwork, Acrylic, Warm, Cool, Spectrum }

internal static class ThemePaletteColors
{
    internal static Color Resolve(ThemePalette palette, double seconds)
    {
        if (palette == ThemePalette.Acrylic) return Color.FromArgb(125, 139, 153);
        // Windows exposes one accent color. Keep it stable; spatial animation
        // belongs to the display surfaces, not repeated registry broadcasts.
        return Hsv(palette == ThemePalette.Warm ? 15 : palette == ThemePalette.Cool ? 215 : 285);
    }

    internal static bool IsFlowing(ThemePalette palette)
    { return palette == ThemePalette.Warm || palette == ThemePalette.Cool || palette == ThemePalette.Spectrum; }

    internal static Bitmap CreateField(ThemePalette palette, double seconds)
    {
        const int width = 64, height = 40, count = 7;
        var colors = new Color[count]; var xs = new double[count]; var ys = new double[count];
        for (int i = 0; i < count; i++)
        {
            double hue = palette == ThemePalette.Warm ? 325 + 90.0 * i / (count - 1) :
                palette == ThemePalette.Cool ? 165 + 100.0 * i / (count - 1) : 360.0 * i / count;
            colors[i] = Hsv(hue % 360);
            // Different periods keep colored areas wandering and mixing without
            // rotating the hue of the entire surface in lockstep.
            xs[i] = .5 + .48 * Math.Sin(seconds * (.17 + i * .019) + i * 2.4);
            ys[i] = .5 + .48 * Math.Cos(seconds * (.13 + i * .023) + i * 1.7);
        }
        var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            double sum = 0, r = 0, g = 0, b = 0;
            for (int i = 0; i < count; i++)
            {
                double dx = x / (double)(width - 1) - xs[i], dy = y / (double)(height - 1) - ys[i];
                double weight = Math.Exp(-18 * (dx * dx + dy * dy)) + .004;
                sum += weight; r += colors[i].R * weight; g += colors[i].G * weight; b += colors[i].B * weight;
            }
            bitmap.SetPixel(x, y, Color.FromArgb((int)(r / sum), (int)(g / sum), (int)(b / sum)));
        }
        return bitmap;
    }

    private static Color Hsv(double hue)
    {
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
