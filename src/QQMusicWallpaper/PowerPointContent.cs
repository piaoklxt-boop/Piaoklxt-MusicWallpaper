using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace QQMusicWallpaper;

// Some Office builds expose only the outer MDI pane through UIA. Query the
// running application's read-only geometry API; never launch or activate it.
internal static class PowerPointContent
{
    internal static Rectangle[] Capture(IntPtr target, Rectangle bounds)
    {
        using (new SurfaceDpiScope()) return CapturePhysical(target, bounds);
    }

    private static Rectangle[] CapturePhysical(IntPtr target, Rectangle bounds)
    {
        var references = new List<object>();
        try
        {
            object app = Marshal.GetActiveObject("PowerPoint.Application"); references.Add(app);
            object window = Get(app, "ActiveWindow"); references.Add(window);
            string caption = Convert.ToString(Get(window, "Caption"));
            if (!MatchesFrame(target, caption)) return new Rectangle[0];
            int view = Convert.ToInt32(Get(window, "ViewType"));
            if (view != 9 && view != 1) return new Rectangle[0]; // Normal / slide view.
            object presentation = Get(window, "Presentation"); references.Add(presentation);
            object setup = Get(presentation, "PageSetup"); references.Add(setup);
            float width = Convert.ToSingle(Get(setup, "SlideWidth")), height = Convert.ToSingle(Get(setup, "SlideHeight"));
            int left = Pixel(window, "PointsToScreenPixelsX", 0), top = Pixel(window, "PointsToScreenPixelsY", 0);
            int right = Pixel(window, "PointsToScreenPixelsX", width), bottom = Pixel(window, "PointsToScreenPixelsY", height);
            Rectangle slide = Rectangle.FromLTRB(left, top, right, bottom);
            if (slide.Width <= 0 || slide.Height <= 0) return new Rectangle[0];
            // Reject a mismatched/transitioning frame instead of coloring a
            // different PowerPoint window or the ribbon during a zoom change.
            if (!bounds.Contains(slide)) return new Rectangle[0];
            Rectangle document = Rectangle.Empty;
            Native.EnumChildWindows(target, (child, unused) =>
            {
                Native.RECT rect;
                if (Native.IsWindowVisible(child) && SurfaceWindows.ClassName(child) == "MDIClient" && Native.GetWindowRect(child, out rect))
                    document = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                return true;
            }, IntPtr.Zero);
            if (!document.Contains(slide)) return new Rectangle[0];
            if (view == 9)
            {
                int outline = Convert.ToInt32(Get(window, "SplitHorizontal")), notes = Convert.ToInt32(Get(window, "SplitVertical"));
                if (outline < 0 || outline >= 100 || notes <= 0 || notes > 100 ||
                    slide.Left < document.Left + document.Width * outline / 100 ||
                    slide.Bottom > document.Top + document.Height * notes / 100) return new Rectangle[0];
            }
            if (Convert.ToString(Get(window, "Caption")) != caption || !MatchesFrame(target, caption) ||
                left != Pixel(window, "PointsToScreenPixelsX", 0) || top != Pixel(window, "PointsToScreenPixelsY", 0) ||
                right != Pixel(window, "PointsToScreenPixelsX", width) || bottom != Pixel(window, "PointsToScreenPixelsY", height)) return new Rectangle[0];
            return new[] { slide };
        }
        catch (Exception) { return new Rectangle[0]; }
        finally
        {
            for (int i = references.Count - 1; i >= 0; i--)
                if (references[i] != null && Marshal.IsComObject(references[i])) Marshal.ReleaseComObject(references[i]);
        }
    }

    private static object Get(object value, string property)
    { return value.GetType().InvokeMember(property, BindingFlags.GetProperty | BindingFlags.IgnoreCase, null, value, null); }
    private static int Pixel(object value, string method, float points)
    { return Convert.ToInt32(value.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, value, new object[] { points })); }

    private static bool MatchesFrame(IntPtr target, string caption)
    {
        // The ROT's legacy PowerPoint Application interface lacks HWND on some
        // builds. Match the exact active document caption and reject duplicates.
        // Caption strings are used only transiently and never logged or saved.
        if (string.IsNullOrEmpty(caption)) return false;
        IntPtr match = IntPtr.Zero; int count = 0;
        uint targetPid; Native.GetWindowThreadProcessId(target, out targetPid);
        Native.EnumWindows((hwnd, unused) =>
        {
            uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid != targetPid || SurfaceWindows.ClassName(hwnd) != "PPTFrameClass") return true;
            var title = new StringBuilder(1024); Native.GetWindowText(hwnd, title, title.Capacity);
            string value = title.ToString();
            if (value == caption || value == caption + " - PowerPoint") { count++; match = hwnd; }
            return true;
        }, IntPtr.Zero);
        return count == 1 && match == target;
    }
}
