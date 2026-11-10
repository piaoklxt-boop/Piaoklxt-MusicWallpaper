using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QQMusicWallpaper;

internal sealed class ThemePreview : Control
{
    private readonly Timer timer = new Timer { Interval = 50 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private Color paintedAccent;
    internal ThemePalette Palette;
    internal ThemeTone Tone;
    internal ThemePreview()
    {
        Name = "ThemePreview"; Height = 132; Dock = DockStyle.Top;
        Margin = new Padding(0, 10, 0, 8);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        timer.Tick += delegate
        {
            Color sampled = PlaybackAccent.LastSampledColor;
            if (Visible && (ThemePaletteColors.IsFlowing(Palette) || (!sampled.IsEmpty && sampled != paintedAccent))) Invalidate();
        };
        VisibleChanged += delegate { timer.Enabled = Visible; };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Color accent = PlaybackAccent.LastSampledColor;
        if (accent.IsEmpty) accent = Color.FromArgb(35, 180, 150);
        paintedAccent = accent;
        if (ThemePaletteColors.IsFlowing(Palette))
        {
            using (var bitmap = ThemePaletteColors.CreateField(Palette, clock.Elapsed.TotalSeconds, accent, Tone))
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                e.Graphics.DrawImage(bitmap, ClientRectangle, 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attributes);
            }
        }
        else e.Graphics.Clear(ThemePaletteColors.ApplyTone(Palette == ThemePalette.Artwork ? accent : ThemePaletteColors.Resolve(Palette, 0), Tone));
        using (var brush = new SolidBrush(Color.FromArgb(190, Color.White)))
            e.Graphics.FillRectangle(brush, 14, Height - 45, Math.Min(Width - 28, 252), 31);
        TextRenderer.DrawText(e.Graphics, "配色预览  ·  Aa  文本与色彩", Font,
            new Rectangle(24, Height - 43, Math.Max(1, Width - 48), 27), Color.FromArgb(30, 52, 57),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
