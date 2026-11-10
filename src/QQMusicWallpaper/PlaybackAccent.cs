using System;
using System.Drawing;

namespace QQMusicWallpaper;

// Published by the existing semantic scan; sampling never starts another MSAA traversal.
internal static class PlaybackAccent
{
    private sealed class Target
    {
        internal IntPtr Window;
        internal uint Pid;
        internal Rectangle Source, Button;
        internal DateTime Captured;
    }
    private static volatile Target target;
    private static volatile int sampledColor;
    internal static Color LastSampledColor { get { int value = sampledColor; return value == 0 ? Color.Empty : Color.FromArgb(value); } }
    internal static void RememberColor(Color value) { sampledColor = value.ToArgb(); }
    private static DateTime nextStandaloneScan;

    internal static void RefreshStandalone()
    {
        if (DateTime.UtcNow < nextStandaloneScan) return;
        nextStandaloneScan = DateTime.UtcNow.AddSeconds(5);
        IntPtr source = IntPtr.Zero; Rectangle sourceBounds = Rectangle.Empty;
        Native.EnumWindows((hwnd, unused) =>
        {
            uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
            Native.RECT rect;
            if (!Native.IsWindowVisible(hwnd) || SurfaceWindows.ClassName(hwnd) != "TXGuiFoundation" ||
                !ProcessIdentity.Matches(pid, "QQMusic") || !Native.GetWindowRect(hwnd, out rect)) return true;
            Rectangle bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if ((long)bounds.Width * bounds.Height > (long)sourceBounds.Width * sourceBounds.Height)
            { source = hwnd; sourceBounds = bounds; }
            return true;
        }, IntPtr.Zero);
        if (source != IntPtr.Zero) Publish(source, sourceBounds, QQMusicAccessibility.CaptureSnapshot(source));
    }

    internal static void Publish(IntPtr window, Rectangle source, AccessibleTreeSnapshot snapshot)
    {
        AccessibleNodeSnapshot node;
        if (!snapshot.TryGetBottommostControlTarget(source, out node, "播放", "暂停", "Play", "Pause") ||
            !source.Contains(node.Bounds)) { target = null; return; }
        Native.GetWindowThreadProcessId(window, out uint pid);
        var button = node.Bounds;
        button.Offset(-source.Left, -source.Top);
        target = new Target { Window = window, Pid = pid, Source = source, Button = button, Captured = DateTime.UtcNow };
    }

    internal static bool TryGetBounds(Size frame, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        var value = target;
        if (value == null || DateTime.UtcNow - value.Captured > TimeSpan.FromSeconds(12) ||
            Native.IsIconic(value.Window) || !Native.GetWindowRect(value.Window, out var rect)) return false;
        Native.GetWindowThreadProcessId(value.Window, out uint pid);
        if (pid != value.Pid || Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) != value.Source ||
            frame.Width != value.Source.Width || frame.Height < value.Source.Height) return false;
        bounds = value.Button;
        return new Rectangle(Point.Empty, frame).Contains(bounds);
    }

    internal static bool TryColor(Bitmap frame, Rectangle bounds, out Color color)
    {
        color = Color.Empty;
        if (bounds.Width < 2 || bounds.Height < 2 || !new Rectangle(Point.Empty, frame.Size).Contains(bounds)) return false;
        var counts = new int[4096];
        var red = new long[4096]; var green = new long[4096]; var blue = new long[4096];
        // Estimate the surrounding skin outside the semantic button, so a black
        // icon on a white skin is as valid as a bright icon on a dark skin.
        var border = new int[4096]; int background = -1, borderBest = 0;
        for (int i = 1; i <= 3; i++)
        {
            int x = bounds.Left + bounds.Width * i / 4, y = bounds.Top + bounds.Height * i / 4;
            foreach (Point point in new[] { new Point(bounds.Left - 1, y), new Point(bounds.Right, y),
                new Point(x, bounds.Top - 1), new Point(x, bounds.Bottom) })
            {
                if (point.X < 0 || point.Y < 0 || point.X >= frame.Width || point.Y >= frame.Height) continue;
                int bin = Bin(frame.GetPixel(point.X, point.Y));
                if (++border[bin] > borderBest) { borderBest = border[bin]; background = bin; }
            }
        }
        if (borderBest < 6) background = -1;
        int best = 0;
        // Quantized foreground clusters reject antialiased edge noise. Saturated
        // button pixels outrank neutral surroundings; neutral icons remain valid.
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                Color p = frame.GetPixel(x, y);
                int max = Math.Max(p.R, Math.Max(p.G, p.B)), min = Math.Min(p.R, Math.Min(p.G, p.B));
                int bin = Bin(p);
                if (bin == background) continue;
                int weight = max - min > 35 ? 12 : 2;
                counts[bin] += weight; red[bin] += p.R * weight; green[bin] += p.G * weight; blue[bin] += p.B * weight;
                if (counts[bin] > counts[best]) best = bin;
            }
        if (counts[best] < 4) return false;
        color = Color.FromArgb((int)(red[best] / counts[best]), (int)(green[best] / counts[best]), (int)(blue[best] / counts[best]));
        return true;
    }

    private static int Bin(Color color) { return ((color.R >> 4) << 8) | ((color.G >> 4) << 4) | (color.B >> 4); }
}
