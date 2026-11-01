using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ProgressPreview.Tests;

internal static class Program
{
    private static int passed;

    [STAThread]
    private static int Main(string[] args)
    {
        bool originalCrossThreadChecks = Control.CheckForIllegalCrossThreadCalls;
        Control.CheckForIllegalCrossThreadCalls = true;
        try
        {
            string outputDirectory = args.Length == 0 ? null : Path.GetFullPath(args[0]);
            if (outputDirectory != null) Directory.CreateDirectory(outputDirectory);
            Rectangle timeline = GetOffscreenTimeline();
            bool outsideDisplays = true;
            foreach (Screen screen in Screen.AllScreens)
                outsideDisplays &= !timeline.IntersectsWith(screen.Bounds);
            Check(outsideDisplays, "fixture remains outside every display");
            IntPtr foreground = Native.GetForegroundWindow();
            using (var preview = new ProgressPreviewForm(bounds => null))
            {
                preview.ShowProgress(timeline, timeline.Left + 80);
                Application.DoEvents();
                Check(preview.Bounds == timeline, "preview is bounded to the supplied live timeline");
                Check(Native.GetForegroundWindow() == foreground, "show preserves the foreground window");
                long styles = Native.GetWindowLongPtr(preview.Handle, -20).ToInt64();
                Check((styles & 0x080800A0L) == 0x080800A0L,
                    "window is layered, transparent, non-activating and a tool window");
                Check(Native.SendMessage(preview.Handle, 0x84, IntPtr.Zero, IntPtr.Zero).ToInt64() == -1,
                    "hit testing passes through the preview");
                Check(Native.SendMessage(preview.Handle, 0x21, IntPtr.Zero, IntPtr.Zero).ToInt64() == 3,
                    "mouse activation is refused");
                Check(HasThumbAt(preview, 80), "thumb is painted at the initial pointer position");

                Exception workerError = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        for (int index = 0; index < 2000; index++)
                            preview.UpdatePointer(timeline.Left + index % timeline.Width);
                        preview.UpdatePointer(timeline.Left + 240);
                    }
                    catch (Exception error) { workerError = error; }
                });
                worker.Start();
                worker.Join();
                Check(workerError == null, "hook-thread pointer updates never access UI controls");
                Check(PumpUntil(() => HasThumbAt(preview, 240) && !HasThumbAt(preview, 80)),
                    "UI timer coalesces updates into the latest single thumb position");

                preview.HidePreview();
                var timer = (System.Windows.Forms.Timer)typeof(ProgressPreviewForm)
                    .GetField("paintTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
                Check(!preview.Visible && !timer.Enabled, "hide removes the preview and stops its timer");
                Check(Native.GetForegroundWindow() == foreground, "hide preserves the foreground window");
            }
            CheckVisualAppearance(timeline, outputDirectory);
            RecordRestorationTimings();
            Console.WriteLine("Progress preview regressions: " + passed + " passed, 0 failed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL " + error.Message);
            Console.WriteLine("Progress preview regressions: " + passed + " passed, 1 failed.");
            return 1;
        }
        finally { Control.CheckForIllegalCrossThreadCalls = originalCrossThreadChecks; }
    }

    private static Rectangle GetOffscreenTimeline()
    {
        Rectangle extent = Screen.AllScreens[0].Bounds;
        foreach (Screen screen in Screen.AllScreens)
            extent = Rectangle.Union(extent, screen.Bounds);
        // The fixed size is a test fixture, not a presumed screen resolution.
        // Placement is relative to the complete virtual desktop, including
        // negative-origin monitors. No QQ Music or Explorer window is touched.
        return new Rectangle(extent.Left - 500, extent.Top, 320, 18);
    }

    private static bool HasThumbAt(ProgressPreviewForm preview, int x)
    {
        using (var image = new Bitmap(preview.ClientSize.Width, preview.ClientSize.Height))
        {
            preview.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            Color color = image.GetPixel(x, image.Height / 2);
            return color.R > 220 && color.G > 220 && color.B > 220;
        }
    }

    private static void CheckVisualAppearance(Rectangle timeline, string outputDirectory)
    {
        using (Bitmap clean = MakeGradient(timeline.Size, false))
        using (Bitmap captured = (Bitmap)clean.Clone())
        {
            PaintOriginalTrack(captured);
            Check(IsThumb(captured.GetPixel(80, 9)), "synthetic captured skin contains an old native thumb");
            SaveOptional(captured, outputDirectory, "preview-before.png");
            ProgressPreviewForm.RestoreTrackBackground(captured);
            Check(ImagesMatch(clean, captured, 1), "background restoration preserves the colored two-axis gradient");
            Check(!IsThumb(captured.GetPixel(80, 9)), "background restoration removes the old native thumb");

            int captures = 0;
            using (var preview = new ProgressPreviewForm(bounds =>
            {
                captures++;
                return (Bitmap)captured.Clone();
            }))
            {
                preview.ShowProgress(timeline, timeline.Left + 240);
                using (Bitmap rendered = Render(preview))
                {
                    Check(BackgroundMatchesOutsideTrack(clean, rendered),
                        "preview keeps the skin around the thin rail instead of painting a rectangle");
                    Check(!IsThumb(rendered.GetPixel(80, 6)) && IsThumb(rendered.GetPixel(240, 9)),
                        "only the new thumb remains after the native thumb is removed");
                    Check(CountChangedRows(clean, rendered, 40) <= 3,
                        "two-pixel rail has at most one antialias fringe row");
                    Check(ThumbBounds(rendered).Height <= 6 && ThumbBounds(rendered).Width <= 6,
                        "thumb remains at most six pixels wide and high");
                    SaveOptional(rendered, outputDirectory, "preview-dark.png");
                }
                preview.ShowProgress(timeline, timeline.Left - 100);
                using (Bitmap start = Render(preview))
                {
                    Rectangle thumb = ThumbBounds(start);
                    Check(!thumb.IsEmpty && thumb.Left <= 1 && thumb.Right <= 6,
                        "pointer before the track clamps the whole thumb safely to the start");
                }
                preview.ShowProgress(timeline, timeline.Right + 100);
                using (Bitmap end = Render(preview))
                {
                    Rectangle thumb = ThumbBounds(end);
                    Check(!thumb.IsEmpty && thumb.Right >= timeline.Width - 1 && thumb.Left >= timeline.Width - 6,
                        "pointer after the track clamps the whole thumb safely to the end");
                }
                Check(captures == 1, "pointer movement reuses one captured backdrop for the gesture");
                preview.HidePreview();
            }
        }
        using (Bitmap lightSkin = MakeGradient(timeline.Size, true))
        using (var preview = new ProgressPreviewForm(bounds => (Bitmap)lightSkin.Clone()))
        {
            preview.ShowProgress(timeline, timeline.Left + 240);
            using (Bitmap rendered = Render(preview))
            {
                Check(BackgroundMatchesOutsideTrack(lightSkin, rendered),
                    "light skin remains light without a dark background block");
                Check(rendered.GetPixel(280, 9).GetBrightness() < lightSkin.GetPixel(280, 9).GetBrightness(),
                    "inactive rail remains visible against a light skin");
                SaveOptional(rendered, outputDirectory, "preview-light.png");
            }
            preview.HidePreview();
        }
        using (var preview = new ProgressPreviewForm(bounds => null))
        {
            preview.ShowProgress(timeline, timeline.Left + 240);
            using (Bitmap rendered = Render(preview))
            {
                bool clearOutsideRail = true;
                for (int x = 0; x < rendered.Width; x++)
                    for (int y = 0; y < rendered.Height; y++)
                        if (Math.Abs(y - rendered.Height / 2) > 3)
                            clearOutsideRail &= rendered.GetPixel(x, y).ToArgb() == preview.TransparencyKey.ToArgb();
                Check(clearOutsideRail, "capture failure leaves transparent pixels instead of a black rectangle");
                int paintedRailRows = 0;
                for (int y = 0; y < rendered.Height; y++)
                    if (rendered.GetPixel(40, y).ToArgb() != preview.TransparencyKey.ToArgb()) paintedRailRows++;
                Check(paintedRailRows == 2, "transparent fallback uses an exact two-pixel rail without colored fringes");
                SaveOptional(rendered, outputDirectory, "preview-fallback.png");
            }
            preview.HidePreview();
        }
    }

    private static Bitmap MakeGradient(Size size, bool light)
    {
        var image = new Bitmap(size.Width, size.Height);
        for (int x = 0; x < size.Width; x++)
        {
            int horizontal = x * 25 / Math.Max(1, size.Width - 1);
            for (int y = 0; y < size.Height; y++)
            {
                int vertical = y * 17 / Math.Max(1, size.Height - 1);
                image.SetPixel(x, y, light
                    ? Color.FromArgb(190 + horizontal / 3 + vertical, 205 + horizontal / 4 + vertical,
                        213 + horizontal / 6 + vertical)
                    : Color.FromArgb(30 + horizontal, 55 + vertical, 80 + horizontal + vertical));
            }
        }
        return image;
    }

    private static void PaintOriginalTrack(Bitmap image)
    {
        using (Graphics graphics = Graphics.FromImage(image))
        using (var track = new Pen(Color.FromArgb(100, 110, 120), 2))
        using (var thumb = new SolidBrush(Color.White))
        {
            graphics.DrawLine(track, 0, image.Height / 2, image.Width - 1, image.Height / 2);
            graphics.FillEllipse(thumb, 75, image.Height / 2 - 5, 10, 10);
        }
    }

    private static Bitmap Render(ProgressPreviewForm preview)
    {
        var image = new Bitmap(preview.ClientSize.Width, preview.ClientSize.Height);
        preview.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        return image;
    }

    private static bool IsThumb(Color color) { return color.R > 235 && color.G > 235 && color.B > 235; }

    private static Rectangle ThumbBounds(Bitmap image)
    {
        Rectangle bounds = Rectangle.Empty;
        for (int x = 0; x < image.Width; x++)
            for (int y = 0; y < image.Height; y++)
                if (IsThumb(image.GetPixel(x, y)))
                    bounds = bounds.IsEmpty ? new Rectangle(x, y, 1, 1)
                        : Rectangle.Union(bounds, new Rectangle(x, y, 1, 1));
        return bounds;
    }

    private static bool CloseColor(Color expected, Color actual, int tolerance)
    {
        return Math.Abs(expected.R - actual.R) <= tolerance &&
            Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance;
    }

    private static bool ImagesMatch(Bitmap expected, Bitmap actual, int tolerance)
    {
        for (int x = 0; x < expected.Width; x++)
            for (int y = 0; y < expected.Height; y++)
                if (!CloseColor(expected.GetPixel(x, y), actual.GetPixel(x, y), tolerance)) return false;
        return true;
    }

    private static bool BackgroundMatchesOutsideTrack(Bitmap expected, Bitmap actual)
    {
        for (int x = 0; x < expected.Width; x++)
            for (int y = 0; y < expected.Height; y++)
                if (Math.Abs(y - expected.Height / 2) > 4 &&
                    !CloseColor(expected.GetPixel(x, y), actual.GetPixel(x, y), 1)) return false;
        return true;
    }

    private static int CountChangedRows(Bitmap expected, Bitmap actual, int x)
    {
        int changed = 0;
        for (int y = 0; y < expected.Height; y++)
            if (!CloseColor(expected.GetPixel(x, y), actual.GetPixel(x, y), 1)) changed++;
        return changed;
    }

    private static void SaveOptional(Bitmap image, string directory, string filename)
    {
        if (directory != null) image.Save(Path.Combine(directory, filename), ImageFormat.Png);
    }

    private static void RecordRestorationTimings()
    {
        using (Bitmap warmup = MakeGradient(new Size(64, 18), false))
            ProgressPreviewForm.RestoreTrackBackground(warmup);
        foreach (int width in new[] { 1920, 3840 })
        {
            using (Bitmap image = MakeGradient(new Size(width, 18), false))
            {
                var elapsed = Stopwatch.StartNew();
                ProgressPreviewForm.RestoreTrackBackground(image);
                elapsed.Stop();
                Console.WriteLine("INFO RestoreTrackBackground " + width + "x18: " +
                    elapsed.Elapsed.TotalMilliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " ms");
            }
        }
    }

    private static bool PumpUntil(Func<bool> predicate)
    {
        var deadline = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            if (predicate()) return true;
            Thread.Sleep(5);
        } while (deadline.ElapsedMilliseconds < 1000);
        return false;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        passed++;
        Console.WriteLine("PASS " + name);
    }
}
