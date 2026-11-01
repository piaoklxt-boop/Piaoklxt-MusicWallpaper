using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class Program
{
    private static int passed;
    private static int failed;

    [STAThread]
    private static int Main()
    {
        Run("comment invocation uses the active launcher from duplicate player bars",
            CommentInvocationUsesActiveLauncher);
        Run("missing comment controls do not dispatch a click", MissingCommentDoesNotClick);
        Run("unchanged source parking preserves the pending refresh deadline",
            RepeatedParkingPreservesRefreshDeadline);
        Run("source movement invalidates cached control mappings", SourceMovementInvalidatesCache);
        Run("source recovery invalidates cached control mappings", SourceRecoveryInvalidatesCache);
        Console.WriteLine("Controller regressions: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void CommentInvocationUsesActiveLauncher()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Rectangle bounds = source.Bounds;
            var oldTarget = new Rectangle(bounds.Left + bounds.Width / 4, bounds.Top + bounds.Height / 2, 50, 30);
            var activeTarget = new Rectangle(bounds.Left + bounds.Width / 8, bounds.Top + bounds.Height * 3 / 4, 50, 30);
            SetField(controller, "qqAccessibilitySnapshot", new AccessibleTreeSnapshot(new[]
            {
                new AccessibleNodeSnapshot("评论999+", "评论", "", 43, false, 6, oldTarget),
                new AccessibleNodeSnapshot("评论999+", "评论", "", 43, false, 5, activeTarget)
            }));
            bool sent = (bool)Invoke(controller, "TryInvokeQQControl", (object)new string[] { "评论", "Comments" });
            Application.DoEvents();
            Assert(sent, "The semantic comment action was not dispatched.");
            Assert(source.Clicks.Count == 1, "Expected one posted button-down message.");
            Point expected = new Point(activeTarget.Left - bounds.Left + activeTarget.Width / 2,
                activeTarget.Top - bounds.Top + activeTarget.Height / 2);
            Assert(source.Clicks[0] == expected,
                "The production invocation clicked the stale player bar instead of the active launcher.");
        }
    }

    private static void MissingCommentDoesNotClick()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            SetField(controller, "qqAccessibilitySnapshot",
                new AccessibleTreeSnapshot(new AccessibleNodeSnapshot[0]));
            bool sent = (bool)Invoke(controller, "TryInvokeQQControl", (object)new string[] { "评论", "Comments" });
            Application.DoEvents();
            Assert(!sent && source.Clicks.Count == 0, "A missing launcher must not use a fallback click.");
        }
    }

    private static void RepeatedParkingPreservesRefreshDeadline()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Initial parking failed.");
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            SetField(controller, "nextAccessibilityRefresh", deadline);
            for (int tick = 0; tick < 3; tick++)
                Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Repeated parking failed.");
            Assert((DateTime)GetField(controller, "nextAccessibilityRefresh") == deadline,
                "Routine parking cleared the pending five-second accessibility refresh deadline.");
        }
    }

    private static void SourceMovementInvalidatesCache()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Initial parking failed.");
            Rectangle bounds = source.Bounds;
            Native.SetWindowPos(source.Handle, IntPtr.Zero, bounds.Left - 10, bounds.Top,
                bounds.Width, bounds.Height, 0x14u);
            SetField(controller, "nextAccessibilityRefresh", DateTime.UtcNow.AddSeconds(5));
            Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Recovery parking failed.");
            Assert((DateTime)GetField(controller, "nextAccessibilityRefresh") == DateTime.MinValue,
                "Moving the source must invalidate old control mappings.");
        }
    }

    private static void SourceRecoveryInvalidatesCache()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Initial parking failed.");
            Native.ShowWindow(source.Handle, 0);
            SetField(controller, "nextAccessibilityRefresh", DateTime.UtcNow.AddSeconds(5));
            Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Hidden-source recovery failed.");
            Assert((DateTime)GetField(controller, "nextAccessibilityRefresh") == DateTime.MinValue,
                "Recovering a hidden source must invalidate cached controls.");
        }
    }

    private static WallpaperContext CreateController(TestSourceWindow source)
    {
        // Do not run the application constructor: it would install hooks, start QQ
        // Music, create a tray icon and change the theme. Only our own HWND is used.
        var controller = (WallpaperContext)FormatterServices.GetUninitializedObject(typeof(WallpaperContext));
        GC.SuppressFinalize(controller);
        SetField(controller, "qqWindow", source.Handle);
        SetField(controller, "originalStyle", Native.GetWindowLongPtr(source.Handle, -16).ToInt64() | 0x10000000L);
        SetField(controller, "originalExStyle", Native.GetWindowLongPtr(source.Handle, -20).ToInt64());
        return controller;
    }

    private static object Invoke(object target, string name, params object[] args)
    {
        return typeof(WallpaperContext).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, args);
    }

    private static void SetField(object target, string name, object value)
    {
        typeof(WallpaperContext).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    private static object GetField(object target, string name)
    {
        return typeof(WallpaperContext).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + (error.InnerException ?? error).Message);
        }
    }

    private sealed class TestSourceWindow : NativeWindow, IDisposable
    {
        internal readonly List<Point> Clicks = new List<Point>();

        internal TestSourceWindow()
        {
            Screen leftmost = Screen.AllScreens[0];
            foreach (Screen screen in Screen.AllScreens)
                if (screen.Bounds.Left < leftmost.Bounds.Left) leftmost = screen;
            Rectangle work = leftmost.WorkingArea;
            Rectangle parked = new Rectangle(work.Left - work.Width - 256, work.Top, work.Width, work.Height);
            foreach (Screen screen in Screen.AllScreens)
                Assert(!screen.Bounds.IntersectsWith(parked), "No safe offscreen parking area for this test.");
            CreateHandle(new CreateParams
            {
                Caption = "MusicWallpaper controller regression fixture",
                X = parked.Left,
                Y = parked.Top,
                Width = work.Width,
                Height = work.Height,
                Style = unchecked((int)0x80000000),
                ExStyle = 0x08000080
            });
            Native.GetWindowThreadProcessId(Handle, out uint processId);
            Assert(processId == (uint)Process.GetCurrentProcess().Id,
                "The fixture must only own windows created by the test process.");
        }

        internal Rectangle Bounds
        {
            get
            {
                Assert(Native.GetWindowRect(Handle, out var rect), "Cannot read the test window bounds.");
                return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x201)
            {
                int packed = message.LParam.ToInt32();
                Clicks.Add(new Point((short)(packed & 0xffff), (short)(packed >> 16)));
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }

        public void Dispose() { DestroyHandle(); }
    }
}
