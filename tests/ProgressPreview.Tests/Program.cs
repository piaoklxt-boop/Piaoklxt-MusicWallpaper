using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ProgressPreview.Tests;

internal static class Program
{
    private static int passed;

    [STAThread]
    private static int Main()
    {
        bool originalCrossThreadChecks = Control.CheckForIllegalCrossThreadCalls;
        Control.CheckForIllegalCrossThreadCalls = true;
        try
        {
            Rectangle timeline = GetOffscreenTimeline();
            bool outsideDisplays = true;
            foreach (Screen screen in Screen.AllScreens)
                outsideDisplays &= !timeline.IntersectsWith(screen.Bounds);
            Check(outsideDisplays, "fixture remains outside every display");
            IntPtr foreground = Native.GetForegroundWindow();
            using (var preview = new ProgressPreviewForm())
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
