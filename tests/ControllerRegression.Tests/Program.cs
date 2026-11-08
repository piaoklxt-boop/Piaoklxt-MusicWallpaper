using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class Program
{
    private static int passed;
    private static int failed;
    private static string filter;

    [STAThread]
    private static int Main(string[] args)
    {
        filter = args.Length > 0 ? args[0] : null;
        typeof(AppPaths).GetField("LogPath", BindingFlags.Static | BindingFlags.Public)
            .SetValue(null, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "controller-regression.log"));
        Run("comment invocation uses the active launcher from duplicate player bars",
            CommentInvocationUsesActiveLauncher);
        Run("missing comment controls do not dispatch a click", MissingCommentDoesNotClick);
        Run("unchanged source parking preserves the pending refresh deadline",
            RepeatedParkingPreservesRefreshDeadline);
        Run("maximized original style does not repeatedly invalidate parked controls",
            () => DynamicStyleDoesNotInvalidateCache(0x01000000L));
        Run("minimized original style does not repeatedly invalidate parked controls",
            () => DynamicStyleDoesNotInvalidateCache(0x20000000L));
        Run("source movement invalidates cached control mappings", SourceMovementInvalidatesCache);
        Run("source recovery invalidates cached control mappings", SourceRecoveryInvalidatesCache);
        Run("HQ keeps its quality identity after its display name changes to accompaniment", RenamedQualityLauncher);
        Run("the right accompaniment entry remains a separate launcher", RightAccompanimentLauncher);
        Run("visible accompaniment wins over the hidden translation at identical bounds", () => SharedTranslationSlot(false));
        Run("visible translation wins over the hidden accompaniment at identical bounds", () => SharedTranslationSlot(true));
        Run("a hidden translation replaced by accompaniment cannot dispatch", HiddenTranslationDoesNotDispatch);
        Run("a parked translation without a visible replacement remains usable", ParkedTranslationRemainsUsable);
        Run("an untracked offscreen accessory does not turn blank desktop into a control", UntrackedAccessoryIsNotMirrorInput);
        Run("quality duplicate mapping and invocation share the active node", () => DuplicatePopupLauncher(false));
        Run("effects duplicate mapping and invocation share the active node", () => DuplicatePopupLauncher(true));
        Run("popup launcher name is used only when its description is absent", LauncherNameFallback);
        Run("a same-surface menu item cannot replace the launcher", MenuDoesNotReplaceLauncher);
        Run("queued popup action retains its hit snapshot", () => QueuedLauncher(0));
        Run("a newer desktop click cancels a queued popup action", () => QueuedLauncher(1));
        Run("source geometry change cancels a queued popup action", () => QueuedLauncher(2));
        Run("dismissal of More cannot reuse an earlier quality launcher", () => DifferentTransientToggle("更多"));
        Run("dismissal of a playlist cannot reuse an earlier quality launcher", () => DifferentTransientToggle("播放队列"));
        Run("a newer click during native activation cannot publish the old popup", () => CancellationDuringNativeClick(true));
        Run("cancellation during the actual native click cannot publish a popup", () => CancellationDuringNativeClick(false));
        PopupLifecycleRegression.Register(Run);
        InteractionDiagnosticsRegression.Register(Run);
        Run("hook exceptions release input and unwind callback state", HookExceptionReleasesInput);
        Console.WriteLine("Controller regressions: " + passed + " passed, " + failed + " failed.");
        if (passed + failed == 0) return 2;
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

    private static void HookExceptionReleasesInput()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            // A missing payload deterministically fails inside the real event
            // processor; no global hook or real desktop event is installed.
            IntPtr result = (IntPtr)Invoke(controller, "MouseHookCallback", 0,
                new IntPtr(0x201), IntPtr.Zero);
            Assert(result == IntPtr.Zero, "A failed event must pass to the next hook.");
            Assert((int)GetField(controller, "activeMouseHookCallbacks") == 0,
                "Callback accounting leaked after failure.");
            Assert((int)GetField(controller, "mouseHookStage") == 0,
                "Callback stage was not reset.");
            Assert(GetField(controller, "pendingMouseHookException") is Exception,
                "The callback must retain the failure for deferred diagnostics.");
            Invoke(controller, "MouseHookCallback", -1, IntPtr.Zero, IntPtr.Zero);
            Assert((int)GetField(controller, "activeMouseHookCallbacks") == 0,
                "A callback after the failure did not complete.");
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

    private static void DynamicStyleDoesNotInvalidateCache(long originalState)
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            long originalStyle = (long)GetField(controller, "originalStyle") | originalState;
            SetField(controller, "originalStyle", originalStyle);
            Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Initial parking failed.");
            long firstStyle = Native.GetWindowLongPtr(source.Handle, -16).ToInt64();
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            SetField(controller, "nextAccessibilityRefresh", deadline);
            Assert((bool)Invoke(controller, "KeepSourceMaximizedBehindDesktop"), "Repeated parking failed.");
            long finalStyle = Native.GetWindowLongPtr(source.Handle, -16).ToInt64();
            Assert((DateTime)GetField(controller, "nextAccessibilityRefresh") == deadline,
                "A normalized window state cleared the deadline: original=0x" + originalStyle.ToString("X") +
                ", first=0x" + firstStyle.ToString("X") + ", final=0x" + finalStyle.ToString("X") + ".");
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
        SetField(controller, "enabled", true);
        SetField(controller, "embedded", true);
        SetField(controller, "activeTransientToggleDescriptions", new string[0]);
        SetField(controller, "originalStyle", Native.GetWindowLongPtr(source.Handle, -16).ToInt64() | 0x10000000L);
        SetField(controller, "originalExStyle", Native.GetWindowLongPtr(source.Handle, -20).ToInt64());
        return controller;
    }

    private static void RenamedQualityLauncher()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Rectangle quality = LauncherBounds(source, false);
            Rectangle right = new Rectangle(source.Bounds.Right - 48, source.Bounds.Bottom - 131, 24, 24);
            var snapshot = new AccessibleTreeSnapshot(new[]
            {
                Node("伴唱", "切换品质", 5, quality), Node("勃", "伴唱", 3, right)
            });
            PopupLauncherMap map = MapAt(controller, source, snapshot, quality);
            Assert(map != null && map.Action == DesktopSpecialAction.ClickQuality,
                "The HQ display name stole the stable quality description.");
            AssertPopupClick(controller, source, map, quality);
        }
    }

    private static void RightAccompanimentLauncher()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Rectangle right = new Rectangle(source.Bounds.Right - 48, source.Bounds.Bottom - 131, 24, 24);
            var snapshot = new AccessibleTreeSnapshot(new[]
            {
                Node("伴唱", "切换品质", 5, LauncherBounds(source, false)), Node("勃", "伴唱", 3, right)
            });
            PopupLauncherMap map = MapAt(controller, source, snapshot, right);
            Assert(map != null && map.Action == DesktopSpecialAction.ClickAccompaniment,
                "The right accompaniment entry lost its identity.");
            AssertPopupClick(controller, source, map, right);
        }
    }

    private static AccessibleTreeSnapshot SharedSlotSnapshot(Rectangle slot, bool translationVisible)
    {
        return new AccessibleTreeSnapshot(new[] {
            new AccessibleNodeSnapshot("", "翻译歌词", "", 43, !translationVisible, 3, slot),
            new AccessibleNodeSnapshot("", "伴唱", "", 43, translationVisible, 3, slot)
        });
    }

    private static void SharedTranslationSlot(bool translationVisible)
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Rectangle slot = new Rectangle(source.Bounds.Right - 48, source.Bounds.Bottom - 131, 24, 24);
            var snapshot = SharedSlotSnapshot(slot, translationVisible);
            PopupLauncherMap map = MapAt(controller, source, snapshot, slot);
            Native.GetWindowRect(source.Handle, out var rect);
            var mapped = (Rectangle[])typeof(WallpaperContext).GetMethod("GetMappedControlCandidates",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] {
                    snapshot, rect, new Rectangle(Point.Empty, source.Bounds.Size), true,
                    new[] { "翻译歌词", "Translation lyrics" } });
            SetField(controller, "lyricTranslationControlAreas", mapped);
            bool translation = (bool)Invoke(controller, "IsLyricTranslationPoint", LocalCenter(source, slot));
            var action = DesktopInputPolicy.ResolveSpecialAction(false, false, false, translation,
                map != null && map.Action == DesktopSpecialAction.ClickAccompaniment, false, false, false, false);
            Assert(action == (translationVisible ? DesktopSpecialAction.ClickLyricTranslation : DesktopSpecialAction.ClickAccompaniment),
                "The hidden occupant stole the shared translation/accompaniment slot: " + action);
            Assert(!translationVisible || map == null, "Hidden accompaniment remained an active launcher.");
            if (!translationVisible) AssertPopupClick(controller, source, map, slot);
            else
            {
                Assert((bool)Invoke(controller, "TryInvokeQQControl", (object)new[] { "翻译歌词" }),
                    "Visible translation could not dispatch.");
                Application.DoEvents();
                Assert(source.Clicks.Count == 1 && source.Clicks[0] == LocalCenter(source, slot),
                    "Translation invocation used a different control than hit testing.");
            }
        }
    }

    private static void HiddenTranslationDoesNotDispatch()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            SetField(controller, "qqAccessibilitySnapshot", SharedSlotSnapshot(LauncherBounds(source, false), false));
            bool invoked = (bool)Invoke(controller, "TryInvokeQQControl", (object)new[] { "翻译歌词", "Translation lyrics" });
            Application.DoEvents();
            Assert(!invoked && source.Clicks.Count == 0, "The hidden translation dispatched to accompaniment's shared location.");
        }
    }

    private static void ParkedTranslationRemainsUsable()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Rectangle slot = LauncherBounds(source, false);
            var snapshot = new AccessibleTreeSnapshot(new[] {
                new AccessibleNodeSnapshot("", "翻译歌词", "", 43, true, 3, slot) });
            SetField(controller, "qqAccessibilitySnapshot", snapshot);
            Assert(snapshot.GetControlBounds("翻译歌词", source.Bounds, true).Length == 1,
                "Parking alone removed translation's hit bounds.");
            Assert((bool)Invoke(controller, "TryInvokeQQControl", (object)new[] { "翻译歌词" }),
                "Parking alone disabled translation dispatch.");
            Application.DoEvents();
            Assert(source.Clicks.Count == 1, "The hidden fallback did not dispatch exactly once.");
        }
    }

    private static void UntrackedAccessoryIsNotMirrorInput()
    {
        using (var source = new TestSourceWindow())
        using (var accessory = new TestSourceWindow())
        {
            var controller = CreateController(source);
            foreach (var field in typeof(WallpaperContext).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                if (field.FieldType == typeof(Rectangle[])) field.SetValue(controller, new Rectangle[0]);
            Native.SetWindowPos(accessory.Handle, IntPtr.Zero, source.Bounds.Left + 400,
                source.Bounds.Top + 200, 414, 360, 0x54u);
            Native.GetWindowRect(source.Handle, out var rect);
            var snapshot = new AccessibleTreeSnapshot(new[] { Node("button", "button", 3,
                new Rectangle(source.Bounds.Left + 10, source.Bounds.Top + 10, 24, 24)) });
            var areas = (Rectangle[])Invoke(controller, "GetQQInteractiveAreas", snapshot, rect,
                new Rectangle(Point.Empty, source.Bounds.Size));
            SetField(controller, "qqInteractiveAreas", areas);
            Assert(!(bool)Invoke(controller, "IsQQInteractionPoint", new Point(600, 350)),
                "An offscreen top-level window projected a fake control onto blank desktop.");
            Assert((bool)Invoke(controller, "IsQQInteractionPoint", new Point(20, 20)),
                "A real source control lost its mirror input area.");
        }
    }

    private static void DuplicatePopupLauncher(bool effects)
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Rectangle active = LauncherBounds(source, effects);
            Rectangle old = active; old.Offset(-12, -12);
            string description = effects ? "音效" : "切换品质";
            var snapshot = new AccessibleTreeSnapshot(new[]
            {
                Node(effects ? "剰" : "HQ", description, 6, old),
                Node(effects ? "剰" : "HQ", description, 5, active)
            });
            PopupLauncherMap map = MapAt(controller, source, snapshot, active);
            Assert(map != null && map.Target.Bounds == active, "A deeper stale bar replaced the active launcher.");
            AssertPopupClick(controller, source, map, active);
            source.Clicks.Clear();
            Assert((bool)Invoke(controller, "TryInvokeQQControl", (object)new[] { description }),
                "The alias-based invocation failed.");
            Application.DoEvents();
            Assert(source.Clicks.Count == 1 && source.Clicks[0] == LocalCenter(source, active),
                "Alias-based invocation selected a different duplicate than the hit map.");
        }
    }

    private static void LauncherNameFallback()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Rectangle target = LauncherBounds(source, true);
            PopupLauncherMap map = MapAt(controller, source,
                new AccessibleTreeSnapshot(new[] { Node("音效", "", 5, target) }), target);
            Assert(map != null && map.Action == DesktopSpecialAction.ClickSoundEffects,
                "An absent description must allow the semantic name fallback.");
            AssertPopupClick(controller, source, map, target);
            map = MapAt(controller, source,
                new AccessibleTreeSnapshot(new[] { Node("音效", "其他功能", 5, target) }), target);
            Assert(map == null, "A display name overrode a different stable description.");
        }
    }

    private static void MenuDoesNotReplaceLauncher()
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Rectangle target = LauncherBounds(source, true);
            Rectangle menu = new Rectangle(target.Left - 100, target.Bottom + 1, 80, 20);
            var snapshot = new AccessibleTreeSnapshot(new[]
            {
                Node("剰", "音效", 5, target),
                new AccessibleNodeSnapshot("音效", "音效", "", 12, false, 10, menu)
            });
            PopupLauncherMap map = MapAt(controller, source, snapshot, target);
            AssertPopupClick(controller, source, map, target);
        }
    }

    private static void QueuedLauncher(int change)
    {
        using (var source = new TestSourceWindow())
        using (var dispatchWindow = (Form)Activator.CreateInstance(
            typeof(WallpaperContext).GetNestedType("HiddenWindow", BindingFlags.NonPublic),
            new object[] { (Action)(() => { }) }))
        {
            IntPtr dispatchHandle = dispatchWindow.Handle;
            var controller = CreateController(source);
            SetField(controller, "window", dispatchWindow);
            Rectangle active = LauncherBounds(source, false);
            var snapshot = new AccessibleTreeSnapshot(new[] { Node("伴唱", "切换品质", 5, active) });
            PopupLauncherMap map = MapAt(controller, source, snapshot, active);
            Invoke(controller, "QueueAccessibleControlAction", "Audio quality",
                new Rectangle(Point.Empty, source.Bounds.Size), new[] { "切换品质", "Change quality" }, false, map);
            Rectangle replacement = active; replacement.Offset(-100, -100);
            SetField(controller, "qqAccessibilitySnapshot", new AccessibleTreeSnapshot(new[]
                { Node("HQ", "切换品质", 7, replacement) }));
            if (change == 1) SetField(controller, "desktopClickSequence", 1L);
            if (change == 2)
                Native.SetWindowPos(source.Handle, IntPtr.Zero, source.Bounds.Left - 10,
                    source.Bounds.Top, source.Bounds.Width, source.Bounds.Height, 0x14u);
            Application.DoEvents();
            Assert(change == 0 ? source.Clicks.Count == 1 && source.Clicks[0] == LocalCenter(source, active)
                : source.Clicks.Count == 0, "A queued action used a substituted snapshot or outlived its click/geometry.");
        }
    }

    private static void DifferentTransientToggle(string description)
    {
        using (var source = new TestSourceWindow())
        {
            var controller = CreateController(source);
            Rectangle quality = LauncherBounds(source, false);
            PopupLauncherMap oldMap = MapAt(controller, source,
                new AccessibleTreeSnapshot(new[] { Node("HQ", "切换品质", 5, quality) }), quality);
            Rectangle target = quality; target.Offset(-100, -100);
            SetField(controller, "qqAccessibilitySnapshot", new AccessibleTreeSnapshot(new[]
                { Node(description, description, 5, target) }));
            Assert((bool)Invoke(controller, "TryInvokeTransientToggle", new[] { description }, oldMap),
                "The current transient entry could not be dismissed.");
            Application.DoEvents();
            Assert(source.Clicks.Count == 1 && source.Clicks[0] == LocalCenter(source, target),
                "The previous popup launcher was reused to dismiss a different control.");
        }
    }

    private static void CancellationDuringNativeClick(bool advanceClick)
    {
        TestSourceWindow source = null;
        Exception workerError = null;
        using (var ready = new ManualResetEvent(false))
        using (var clicked = new ManualResetEvent(false))
        using (var dispatchWindow = (Form)Activator.CreateInstance(
            typeof(WallpaperContext).GetNestedType("HiddenWindow", BindingFlags.NonPublic),
            new object[] { (Action)(() => { }) }))
        {
            var worker = new Thread(() =>
            {
                try
                {
                    using (var fixture = new TestSourceWindow())
                    {
                        source = fixture;
                        ready.Set();
                        Application.Run();
                    }
                }
                catch (Exception error) { workerError = error; ready.Set(); }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            Assert(ready.WaitOne(2000) && source != null && workerError == null,
                "The independent fixture message thread did not start.");
            try
            {
                IntPtr dispatchHandle = dispatchWindow.Handle;
                var controller = CreateController(source);
                SetField(controller, "window", dispatchWindow);
                Rectangle quality = LauncherBounds(source, false);
                PopupLauncherMap map = MapAt(controller, source,
                    new AccessibleTreeSnapshot(new[] { Node("HQ", "切换品质", 5, quality) }), quality);
                source.OnButtonDown = () =>
                {
                    if (advanceClick) SetField(controller, "desktopClickSequence", 1L);
                    Invoke(controller, "CancelPendingQQPopupPresentation");
                    clicked.Set();
                };
                Invoke(controller, "QueueAccessibleControlAction", "Audio quality",
                    new Rectangle(Point.Empty, source.Bounds.Size), new[] { "切换品质" }, true, map);
                Application.DoEvents();
                Assert(clicked.WaitOne(1000), "The real posted button-down was not received.");
                Assert(((string[])GetField(controller, "activeTransientToggleDescriptions")).Length == 0 &&
                    GetField(controller, "activeTransientLauncher") == null,
                    "An action cancelled during its native click published a new transient owner.");
                Invoke(controller, "CancelPendingQQPopupPresentation");
            }
            finally
            {
                Native.PostMessage(source.Handle, 0x8001, IntPtr.Zero, IntPtr.Zero);
                Assert(worker.Join(2000), "The owned fixture thread did not stop.");
            }
        }
    }

    private static AccessibleNodeSnapshot Node(string name, string description, int depth, Rectangle bounds) =>
        new AccessibleNodeSnapshot(name, description, "", 43, false, depth, bounds);

    private static Rectangle LauncherBounds(TestSourceWindow source, bool effects) => effects
        ? new Rectangle(source.Bounds.Right - 143, source.Bounds.Bottom - 56, 41, 35)
        : new Rectangle(source.Bounds.Right - 193, source.Bounds.Bottom - 51, 43, 24);

    private static PopupLauncherMap MapAt(WallpaperContext controller, TestSourceWindow source,
        AccessibleTreeSnapshot snapshot, Rectangle target)
    {
        SetField(controller, "qqAccessibilitySnapshot", snapshot);
        Invoke(controller, "RefreshPopupLauncherMaps", snapshot, source.Handle, source.Bounds,
            new Rectangle(Point.Empty, source.Bounds.Size), Point.Empty);
        return (PopupLauncherMap)Invoke(controller, "GetPopupLauncherAtPoint", LocalCenter(source, target));
    }

    private static Point LocalCenter(TestSourceWindow source, Rectangle target) =>
        new Point(target.Left - source.Bounds.Left + target.Width / 2,
            target.Top - source.Bounds.Top + target.Height / 2);

    private static void AssertPopupClick(WallpaperContext controller, TestSourceWindow source,
        PopupLauncherMap map, Rectangle target)
    {
        Assert(map != null && (bool)Invoke(controller, "TryInvokePopupLauncher", map), "The mapped launcher was not invoked.");
        Application.DoEvents();
        Assert(source.Clicks.Count == 1 && source.Clicks[0] == LocalCenter(source, target),
            "The actual posted click did not use the launcher that supplied the hit bounds.");
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
        if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return;
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
        internal Action OnButtonDown;

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
                OnButtonDown?.Invoke();
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
            if (message.Msg == 0x8001) { Application.ExitThread(); message.Result = IntPtr.Zero; return; }
            base.WndProc(ref message);
        }

        public void Dispose() { DestroyHandle(); }
    }
}
