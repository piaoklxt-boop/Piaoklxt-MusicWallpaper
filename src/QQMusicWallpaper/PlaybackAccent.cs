using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

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
        internal Func<Rectangle?> ReadLiveBounds;
    }
    private static volatile Target target;
    private static volatile int sampledColor;
    internal static Color LastSampledColor { get { int value = sampledColor; return value == 0 ? Color.Empty : Color.FromArgb(value); } }
    private const string ControlIdentity = "QQMusic.Lyrics.PlayPause.v3";
    private static readonly object colorGate = new object();
    private static string cachePath;

    // Control appearance outlives its current screen rectangle. Only a verified
    // new control sample may replace it; hidden/moved controls provide no sample.
    internal static void RememberColor(Color value)
    {
        if (value.IsEmpty) return;
        lock (colorGate)
        {
            if (sampledColor == value.ToArgb()) return;
            sampledColor = value.ToArgb();
            if (string.IsNullOrEmpty(cachePath)) return;
            try
            {
                AppearanceIni.WriteAtomic(cachePath, "Control=" + ControlIdentity + "\nColor=" +
                    (value.ToArgb() & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture) + "\n", Encoding.UTF8);
            }
            catch (IOException) { RuntimeLog.Write("Confirmed playback color could not be saved."); }
            catch (UnauthorizedAccessException) { RuntimeLog.Write("Confirmed playback color cache is not writable."); }
        }
    }

    private static int pendingColor, pendingCount;
    private static DateTime pendingSince, pendingLast;

    // A fade/hover transition is not a new control style. Corroborate a changed
    // color in three fresh frames over at least two seconds before replacing it.
    internal static bool TryConfirmColor(Color observed, DateTime now, out Color confirmed)
    {
        lock (colorGate)
        {
            confirmed = LastSampledColor;
            if (observed.IsEmpty) return false;
            if (confirmed.IsEmpty) { RememberColor(observed); confirmed = observed; return true; }
            if (ColorDistance(confirmed, observed) <= 9) { pendingColor = 0; pendingCount = 0; return true; }
            if (pendingColor == 0 || ColorDistance(Color.FromArgb(pendingColor), observed) > 9 ||
                now < pendingLast || now - pendingLast > TimeSpan.FromSeconds(3))
            {
                pendingColor = observed.ToArgb(); pendingCount = 1; pendingSince = pendingLast = now;
                return false;
            }
            pendingLast = now; pendingCount++;
            if (pendingCount < 3 || now - pendingSince < TimeSpan.FromSeconds(2)) return false;
            RememberColor(observed); confirmed = observed; pendingColor = 0; pendingCount = 0;
            return true;
        }
    }
    private static int ColorDistance(Color a, Color b)
    { return Math.Abs(a.R-b.R) + Math.Abs(a.G-b.G) + Math.Abs(a.B-b.B); }

    internal static void ConfigureCache(string path)
    {
        lock (colorGate)
        {
            cachePath = path; sampledColor = 0; target = null; pendingColor = 0; pendingCount = 0;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                bool identified = false; int rgb = -1;
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line == "Control=" + ControlIdentity) identified = true;
                    if (line.StartsWith("Color=") && line.Length == 12 &&
                        int.TryParse(line.Substring(6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsed)) rgb = parsed;
                }
                if (identified && rgb >= 0 && rgb <= 0xFFFFFF) sampledColor = Color.FromArgb(255, Color.FromArgb(rgb)).ToArgb();
            }
            catch (IOException) { RuntimeLog.Write("Confirmed playback color cache could not be read."); }
            catch (UnauthorizedAccessException) { RuntimeLog.Write("Confirmed playback color cache is not readable."); }
        }
    }
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
        if (!snapshot.TryGetLyricsPlaybackTarget(source, out node) ||
            !source.Contains(node.Bounds)) { target = null; return; }
        Native.GetWindowThreadProcessId(window, out uint pid);
        var button = node.Bounds;
        button.Offset(-source.Left, -source.Top);
        target = new Target { Window = window, Pid = pid, Source = source, Button = button, Captured = DateTime.UtcNow, ReadLiveBounds = node.ReadLiveBounds };
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
        if (value.ReadLiveBounds == null) return false;
        Rectangle? live;
        try { live = value.ReadLiveBounds(); } catch { return false; }
        if (!live.HasValue || !value.Source.Contains(live.Value)) return false;
        bounds = live.Value;
        bounds.Offset(-value.Source.Left, -value.Source.Top);
        return new Rectangle(Point.Empty, frame).Contains(bounds);
    }

    internal static bool TryColor(Bitmap frame, Rectangle bounds, out Color color)
    {
        color = Color.Empty;
        if (bounds.Width < 2 || bounds.Height < 2 || !new Rectangle(Point.Empty, frame.Size).Contains(bounds)) return false;
        Rectangle region = Rectangle.Intersect(new Rectangle(Point.Empty, frame.Size),
            Rectangle.Inflate(bounds, 1, 1));
        if (region == bounds) return false;
        int width = region.Width, height = region.Height, total = width * height;
        var pixels = new Color[total]; var background = new bool[total];
        var queue = new System.Collections.Generic.Queue<int>();
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            pixels[i] = frame.GetPixel(region.Left + x, region.Top + y);
            if (!bounds.Contains(region.Left + x, region.Top + y))
            { background[i] = true; queue.Enqueue(i); }
        }
        // Start outside the semantic control, so a filled button touching its
        // own bounds is retained. Remove connected artwork, including gradients.
        // The actual button body wins by area; a transparent icon keeps its glyph.
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % width, y = i / width;
            foreach (int next in new[] { x > 0 ? i - 1 : -1, x + 1 < width ? i + 1 : -1,
                y > 0 ? i - width : -1, y + 1 < height ? i + width : -1 })
            {
                if (next < 0 || background[next]) continue;
                Color a = pixels[i], b = pixels[next];
                if (Math.Abs(a.R-b.R) + Math.Abs(a.G-b.G) + Math.Abs(a.B-b.B) > 45) continue;
                background[next] = true; queue.Enqueue(next);
            }
        }
        var counts = new int[4096];
        var red = new long[4096]; var green = new long[4096]; var blue = new long[4096];
        int best = 0;
        for (int i = 0; i < total; i++)
        {
            if (background[i] || pixels[i].A < 240 ||
                !bounds.Contains(region.Left + i % width, region.Top + i / width)) continue;
            Color p = pixels[i]; int bin = Bin(p);
            counts[bin]++; red[bin] += p.R; green[bin] += p.G; blue[bin] += p.B;
            if (counts[bin] > counts[best]) best = bin;
        }
        if (counts[best] < Math.Max(4, bounds.Width * bounds.Height / 100)) return false;
        color = Color.FromArgb((int)(red[best] / counts[best]), (int)(green[best] / counts[best]), (int)(blue[best] / counts[best]));
        return true;
    }

    private static int Bin(Color color) { return ((color.R >> 4) << 8) | ((color.G >> 4) << 4) | (color.B >> 4); }
}
