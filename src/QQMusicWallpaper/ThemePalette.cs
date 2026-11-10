using System;
using System.Drawing;

namespace QQMusicWallpaper;

internal enum ThemePalette { Artwork, Acrylic, Warm, Cool, Spectrum, MonochromeGradient }
internal enum ThemeTone { Original, Light, Dark }

internal static class ThemePaletteColors
{
    internal static Color ApplyTone(Color color, ThemeTone tone)
    {
        if (color.IsEmpty || tone == ThemeTone.Original) return color;
        double keep = tone == ThemeTone.Light ? .48 : .42;
        double background = tone == ThemeTone.Light ? 255 * (1 - keep) : 0;
        return Color.FromArgb((int)(color.R * keep + background), (int)(color.G * keep + background), (int)(color.B * keep + background));
    }

    internal static Bitmap CreateField(ThemePalette palette, double seconds, Color accent, ThemeTone tone)
    {
        Bitmap field = CreateField(palette, seconds, accent);
        if (tone != ThemeTone.Original)
            for (int y = 0; y < field.Height; y++) for (int x = 0; x < field.Width; x++)
                field.SetPixel(x, y, ApplyTone(field.GetPixel(x, y), tone));
        return field;
    }
    internal static Color Resolve(ThemePalette palette, double seconds)
    {
        if (palette == ThemePalette.Acrylic) return Color.FromArgb(125, 139, 153);
        // Windows exposes one accent color. Keep it stable; spatial animation
        // belongs to the display surfaces, not repeated registry broadcasts.
        return Hsv(palette == ThemePalette.Warm ? 15 : palette == ThemePalette.Cool ? 215 : 285);
    }

    internal static bool IsFlowing(ThemePalette palette)
    { return palette == ThemePalette.Warm || palette == ThemePalette.Cool || palette == ThemePalette.Spectrum || palette == ThemePalette.MonochromeGradient; }

    internal static bool UsesPlaybackColor(ThemePalette palette)
    { return palette == ThemePalette.Artwork || palette == ThemePalette.MonochromeGradient; }

    internal static Bitmap CreateField(ThemePalette palette, double seconds)
    { return CreateField(palette, seconds, Color.FromArgb(35, 180, 150)); }

    internal static Bitmap CreateField(ThemePalette palette, double seconds, Color accent)
    {
        if (palette == ThemePalette.MonochromeGradient) return CreateMonochrome(seconds, accent);
        const int width = 64, height = 40, count = 7;
        var colors = new Color[count]; var xs = new double[count]; var ys = new double[count];
        for (int i = 0; i < count; i++)
        {
            double hue = palette == ThemePalette.Warm ? 325 + 90.0 * i / (count - 1) :
                palette == ThemePalette.Cool ? 165 + 100.0 * i / (count - 1) : 360.0 * i / count;
            colors[i] = Hsv(hue % 360, .96, .65 + .35 * Math.Sin(seconds * .85 + i * 2.1));
            // Different periods keep colored areas wandering and mixing without
            // rotating the hue of the entire surface in lockstep.
            xs[i] = .5 + .48 * Math.Sin(seconds * (.8 + i * .071) + i * 2.4);
            ys[i] = .5 + .48 * Math.Cos(seconds * (.61 + i * .083) + i * 1.7);
        }
        var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            double sum = 0, r = 0, g = 0, b = 0;
            for (int i = 0; i < count; i++)
            {
                double dx = x / (double)(width - 1) - xs[i], dy = y / (double)(height - 1) - ys[i];
                double weight = Math.Exp(-42 * (dx * dx + dy * dy)) + .00005;
                sum += weight; r += colors[i].R * weight; g += colors[i].G * weight; b += colors[i].B * weight;
            }
            bitmap.SetPixel(x, y, Color.FromArgb((int)(r / sum), (int)(g / sum), (int)(b / sum)));
        }
        return bitmap;
    }

    private static Bitmap CreateMonochrome(double seconds, Color accent)
    {
        var bitmap = new Bitmap(64, 40);
        double hue = accent.GetHue(), saturation = accent.GetSaturation();
        // A single hue, with a moving light-to-deep range. Achromatic playback
        // buttons remain gray instead of acquiring an arbitrary red hue.
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
        {
            double wave = .5 + .5 * Math.Sin(x / 63.0 * 4.2 + y / 39.0 * 2.3 + seconds * .55 +
                .65 * Math.Sin(y / 39.0 * 3.1 - seconds * .32));
            double value = .30 + .70 * wave;
            double sat = saturation < .08 ? 0 : Math.Max(.35, Math.Min(.95, saturation)) * (.60 + .40 * (1 - wave));
            bitmap.SetPixel(x, y, Hsv(hue, sat, value));
        }
        return bitmap;
    }

    private static Color Hsv(double hue, double saturation = .48, double value = .78)
    {
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
