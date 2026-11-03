using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class PopupLifecycleRegression
{
    private static readonly List<IntPtr> FixtureWindows = new List<IntPtr>();
    private static readonly HashSet<string> RegisteredClasses = new HashSet<string>();
    private static readonly WindowProc FixtureWindowProc = DefWindowProc;
    private static readonly List<WallpaperContext> Controllers = new List<WallpaperContext>();

    internal static void Register(Action<string, Action> run)
    {
        // The production discovery path logs successful presentation. Keep test
        // output beside the executable rather than writing the user's app log.
        typeof(AppPaths).GetField("LogPath", BindingFlags.Static | BindingFlags.Public)
            .SetValue(null, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "popup-regression.log"));
        RunCase(run, "source maintenance preserves an owned popup above unrelated windows",
            () => PopupSurvivesParking(true));
        RunCase(run, "source maintenance preserves an unowned popup",
            () => PopupSurvivesParking(false));
        RunCase(run, "new native popup is discovered without activation", NewlyVisiblePopupIsDiscovered);
        RunCase(run, "a matching visible popup can be restored", ExistingVisiblePopupCanBePresented);
        RunCase(run, "cold native popup remains discoverable after one second", DelayedPopupIsDiscovered);
        RunCase(run, "a shadow cannot consume native popup discovery", ShadowDoesNotConsumeDiscovery);
        RunCase(run, "cancelled popup discovery does not present a late window", CancelledRequestDoesNotPresent);
        RunCase(run, "a closing tracked popup is not resurrected", TrackedClosingPopupIsNotResurrected);
        RunCase(run, "an unrelated visible popup is not restored", UnrelatedVisiblePopupIsNotRestored);
        RunCase(run, "a newer desktop click cancels popup discovery", NewClickCancelsDiscovery);
        RunCase(run, "source movement cancels popup discovery", SourceMovementCancelsDiscovery);
        RunCase(run, "a newer popup request supersedes the earlier request", NewPopupRequestSupersedesEarlierRequest);
    }

    private static void RunCase(Action<string, Action> run, string name, Action test)
    {
        run(name, () => {
            try { test(); }
            finally {
                foreach (WallpaperContext controller in Controllers)
                    Invoke(controller, "CancelPendingQQPopupPresentation");
                Controllers.Clear();
                // Requests rejected by a test need not wait for the full deadline.
                Pump(60);
            }
        });
    }

    private static void PopupSurvivesParking(bool owned)
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(owned ? source.Handle : IntPtr.Zero, true))
        using (var otherWindow = new FixtureWindow(IntPtr.Zero, true, "TXGuiFoundation", "Unrelated fixture"))
        {
            WallpaperContext controller = CreateController(source);
            Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Initial source parking failed.");
            Rectangle work = source.Bounds;
            Assert(GetWindow(source.Handle, 4) == IntPtr.Zero, "The source fixture must itself be unowned.");
            Rectangle otherBounds = otherWindow.Bounds;
            Assert(Native.SetWindowPos(otherWindow.Handle, IntPtr.Zero, otherBounds.Left, otherBounds.Top,
                otherBounds.Width, otherBounds.Height, 0x50u), "Cannot show the unrelated fixture.");
            Assert((bool)Invoke(controller, "TryPositionQQPopup", popup.Handle, work), "Initial popup presentation failed.");
            Invoke(controller, "PublishQQPopupSurface", popup.Handle, work);
            Assert(IsTopmost(popup.Handle) && IsAbove(popup.Handle, otherWindow.Handle),
                "The popup must start above the unrelated non-topmost fixture.");
            Assert(GetWindow(popup.Handle, 4) == (owned ? source.Handle : IntPtr.Zero), "Unexpected popup owner.");
            for (int tick = 0; tick < 3; tick++)
                Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Repeated source parking failed.");
            Invoke(controller, "PruneQQPopupSurfaces");
            Assert(IsTopmost(popup.Handle) && IsAbove(popup.Handle, otherWindow.Handle),
                "Routine source parking put the still-visible popup behind another window.");
            Assert(Tracks(controller, popup.Handle), "A visible popup lost its input surface.");
            AssertFixturesNeverActivated();
        }
    }

    private static void NewlyVisiblePopupIsDiscovered()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true))
        {
            WallpaperContext controller = CreateParkedController(source);
            QueueDiscovery(controller, source.Bounds);
            Native.ShowWindow(popup.Handle, 4);
            Pump(1300);
            Assert(Tracks(controller, popup.Handle) && IsTopmost(popup.Handle),
                "The newly visible popup was not presented and published.");
        }
    }

    private static void ExistingVisiblePopupCanBePresented()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true))
        {
            WallpaperContext controller = CreateParkedController(source);
            Native.ShowWindow(popup.Handle, 4);
            QueueDiscovery(controller, source.Bounds);
            Pump(1300);
            Assert(Tracks(controller, popup.Handle) && IsTopmost(popup.Handle),
                "The matching visible HWND was excluded from popup recovery.");
        }
    }

    private static void DelayedPopupIsDiscovered()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true))
        {
            WallpaperContext controller = CreateParkedController(source);
            QueueDiscovery(controller, source.Bounds);
            Pump(1250);
            Native.ShowWindow(popup.Handle, 4);
            Pump(350);
            Assert(Tracks(controller, popup.Handle),
                "The cold popup appearing after one second remained unpublished.");
        }
    }

    private static void ShadowDoesNotConsumeDiscovery()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var shadow = new FixtureWindow(source.Handle, true, "TXGFLayerMask", ""))
        using (var popup = new FixtureWindow(source.Handle, true))
        {
            WallpaperContext controller = CreateParkedController(source);
            var before = (HashSet<IntPtr>)Invoke(controller, "GetVisibleQQTopLevelWindows");
            Native.ShowWindow(shadow.Handle, 4);
            Present(controller, before, source.Bounds, "Sound effects");
            Pump(150);
            Native.ShowWindow(popup.Handle, 4);
            Pump(1150);
            Assert(Tracks(controller, popup.Handle) && !Tracks(controller, shadow.Handle),
                "The shadow ended discovery before the actual dialog appeared.");
        }
    }

    private static void CancelledRequestDoesNotPresent()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true))
        {
            WallpaperContext controller = CreateParkedController(source);
            QueueDiscovery(controller, source.Bounds);
            Invoke(controller, "CancelPendingQQPopupPresentation");
            Native.ShowWindow(popup.Handle, 4);
            Pump(250);
            Assert(!Tracks(controller, popup.Handle) && !IsTopmost(popup.Handle),
                "A cancelled request presented the late native popup.");
        }
    }

    private static void TrackedClosingPopupIsNotResurrected()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true))
        {
            WallpaperContext controller = CreateParkedController(source);
            Assert((bool)Invoke(controller, "TryPositionQQPopup", popup.Handle, source.Bounds), "Initial presentation failed.");
            Invoke(controller, "PublishQQPopupSurface", popup.Handle, source.Bounds);
            QueueDiscovery(controller, source.Bounds);
            Native.ShowWindow(popup.Handle, 0);
            Pump(250);
            Assert(!Native.IsWindowVisible(popup.Handle), "Discovery resurrected a tracked popup after its toggle closed it.");
        }
    }

    private static void UnrelatedVisiblePopupIsNotRestored()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true, "TXGuiFoundation", "切换品质"))
        {
            WallpaperContext controller = CreateParkedController(source);
            Native.ShowWindow(popup.Handle, 4);
            QueueDiscovery(controller, source.Bounds);
            Pump(250);
            Assert(!Tracks(controller, popup.Handle) && !IsTopmost(popup.Handle),
                "A sound-effects request restored the unrelated quality popup.");
        }
    }

    private static void NewClickCancelsDiscovery()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true))
        {
            WallpaperContext controller = CreateParkedController(source);
            QueueDiscovery(controller, source.Bounds);
            SetField(controller, "desktopClickSequence", 1L);
            Native.ShowWindow(popup.Handle, 4);
            Pump(250);
            Assert(!Tracks(controller, popup.Handle) && !IsTopmost(popup.Handle),
                "An older click's request presented a native popup after a new click.");
        }
    }

    private static void SourceMovementCancelsDiscovery()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true))
        {
            WallpaperContext controller = CreateParkedController(source);
            Rectangle old = source.Bounds;
            QueueDiscovery(controller, old);
            Assert(Native.SetWindowPos(source.Handle, IntPtr.Zero, old.Left - 10, old.Top,
                old.Width, old.Height, 0x14u), "Cannot move the source fixture.");
            Native.ShowWindow(popup.Handle, 4);
            Pump(250);
            Assert(!Tracks(controller, popup.Handle) && !IsTopmost(popup.Handle),
                "A request using stale source bounds presented a native popup.");
        }
    }

    private static void NewPopupRequestSupersedesEarlierRequest()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var effects = new FixtureWindow(source.Handle, true))
        using (var quality = new FixtureWindow(source.Handle, true, "TXGuiFoundation", "切换品质"))
        {
            WallpaperContext controller = CreateParkedController(source);
            QueueDiscovery(controller, source.Bounds);
            var before = (HashSet<IntPtr>)Invoke(controller, "GetVisibleQQTopLevelWindows");
            Present(controller, before, source.Bounds, "Audio quality");
            Native.ShowWindow(effects.Handle, 4);
            Pump(250);
            Assert(!Tracks(controller, effects.Handle) && !IsTopmost(effects.Handle),
                "The superseded sound-effects request presented its late window.");
            Native.ShowWindow(quality.Handle, 4);
            Pump(250);
            Assert(Tracks(controller, quality.Handle), "The current quality request was cancelled along with the old request.");
        }
    }

    private static WallpaperContext CreateParkedController(FixtureWindow source)
    {
        WallpaperContext controller = CreateController(source);
        Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Initial source parking failed.");
        return controller;
    }

    private static WallpaperContext CreateController(FixtureWindow source)
    {
        // Never run the app constructor: no hooks, source startup, renderer,
        // tray icon, theme changes, or interaction with existing applications.
        var controller = (WallpaperContext)FormatterServices.GetUninitializedObject(typeof(WallpaperContext));
        GC.SuppressFinalize(controller);
        SetField(controller, "qqWindow", source.Handle);
        SetField(controller, "originalStyle", Native.GetWindowLongPtr(source.Handle, -16).ToInt64() | 0x10000000L);
        SetField(controller, "originalExStyle", Native.GetWindowLongPtr(source.Handle, -20).ToInt64());
        SetField(controller, "enabled", true);
        SetField(controller, "embedded", true);
        Controllers.Add(controller);
        return controller;
    }

    private static void QueueDiscovery(WallpaperContext controller, Rectangle work)
    {
        var before = (HashSet<IntPtr>)Invoke(controller, "GetVisibleQQTopLevelWindows");
        Present(controller, before, work, "Sound effects");
    }

    private static void Present(WallpaperContext controller, HashSet<IntPtr> before,
        Rectangle work, string controlName)
    {
        object request = Invoke(controller, "BeginQQPopupPresentation",
            (long)GetField(controller, "desktopClickSequence"));
        Assert(request != null, "Cannot begin a current popup presentation request.");
        Invoke(controller, "QueuePresentQQPopup", request, before, work, controlName);
    }

    private static bool Tracks(WallpaperContext controller, IntPtr window)
    {
        foreach (AccessibleControlSurface surface in ((AccessoryWindowCoordinator)typeof(WallpaperContext).GetProperty("Accessories", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller, null)).Surfaces)
            if (surface.Window == window) return true;
        return false;
    }

    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            AssertFixturesNeverActivated();
            Thread.Sleep(5);
        }
    }

    private static void AssertFixturesNeverActivated()
    {
        Assert(!FixtureWindows.Contains(Native.GetForegroundWindow()),
            "A popup test fixture stole foreground activation.");
    }

    private static bool IsTopmost(IntPtr window) => (Native.GetWindowLongPtr(window, -20).ToInt64() & 8) != 0;

    private static bool IsAbove(IntPtr first, IntPtr second)
    {
        bool sawFirst = false;
        bool result = false;
        Native.EnumWindows((candidate, data) => {
            if (candidate == first) sawFirst = true;
            if (candidate != second) return true;
            result = sawFirst;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    private static object Invoke(object target, string name, params object[] args) => typeof(WallpaperContext).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void SetField(object target, string name, object value) => typeof(WallpaperContext).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object GetField(object target, string name) => typeof(WallpaperContext).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint style;
        public IntPtr wndProc;
        public int classExtra;
        public int windowExtra;
        public IntPtr instance;
        public IntPtr icon;
        public IntPtr cursor;
        public IntPtr brush;
        public string menuName;
        public string className;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WindowClass value);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string title,
        int style, int left, int top, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr parameter);

    private sealed class FixtureWindow : NativeWindow, IDisposable
    {
        internal FixtureWindow(IntPtr owner, bool popup, string className = "TXGuiFoundation", string title = null)
        {
            if (RegisteredClasses.Add(className))
            {
                var value = new WindowClass { wndProc = Marshal.GetFunctionPointerForDelegate(FixtureWindowProc),
                    instance = GetModuleHandle(null), className = className };
                Assert(RegisterClass(ref value) != 0, "Cannot register the local fixture window class.");
            }
            Screen leftmost = Screen.AllScreens[0];
            foreach (Screen screen in Screen.AllScreens)
                if (screen.Bounds.Left < leftmost.Bounds.Left) leftmost = screen;
            Rectangle work = leftmost.WorkingArea;
            Rectangle parked = new Rectangle(work.Left - work.Width - 256, work.Top, work.Width, work.Height);
            if (popup) parked = new Rectangle(parked.Left + 30, parked.Top + 40, 360, 240);
            AssertOffscreen(parked);
            IntPtr handle = CreateWindowEx(0x08000080, className,
                title ?? (popup ? "音效盒子" : "MusicWallpaper isolated source fixture"),
                unchecked((int)0x80000000), parked.Left, parked.Top, parked.Width, parked.Height,
                owner, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            Assert(handle != IntPtr.Zero, "CreateWindowEx failed: " + Marshal.GetLastWin32Error());
            AssignHandle(handle);
            Native.GetWindowThreadProcessId(handle, out uint processId);
            Assert(processId == (uint)Process.GetCurrentProcess().Id, "Fixture HWND must belong to this test process.");
            FixtureWindows.Add(handle);
            AssertFixturesNeverActivated();
        }

        internal Rectangle Bounds
        {
            get
            {
                Assert(Native.GetWindowRect(Handle, out var rect), "Cannot read fixture bounds.");
                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                AssertOffscreen(bounds);
                return bounds;
            }
        }

        private static void AssertOffscreen(Rectangle bounds)
        {
            foreach (Screen screen in Screen.AllScreens)
                Assert(!screen.Bounds.IntersectsWith(bounds), "Fixture bounds must remain outside physical screens.");
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            FixtureWindows.Remove(Handle);
            DestroyHandle();
        }
    }
}
