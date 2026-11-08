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
        RunCase(run, "untitled accompaniment is identified from its owned panel controls", () => UntitledAccompaniment(false, "Accompaniment"));
        RunCase(run, "quality restores the same reused untitled accompaniment panel", () => UntitledAccompaniment(true, "Audio quality"));
        RunCase(run, "untitled semantic proof rejects incomplete unrelated and shadow windows", RejectUnprovenUntitledPanels);
        RunCase(run, "a click cancelled during semantic capture never presents its panel", CancelDuringPanelCapture);
        RunCase(run, "redraw invalidates a logically visible offscreen layered panel", LayeredPanelIsRedrawn);
        RunCase(run, "a late native layout cannot move a new panel away from its launcher", () => PanelPlacementLifecycle(0));
        RunCase(run, "a second launcher reanchors an already tracked shared panel", () => PanelPlacementLifecycle(1));
        RunCase(run, "a new click ends pending placement correction", () => PanelPlacementLifecycle(2));
        RunCase(run, "a panel hidden during placement is never shown again", () => PanelPlacementLifecycle(3));
        RunCase(run, "placement correction expires and later movement remains free", () => PanelPlacementLifecycle(4));
        RunCase(run, "a matching visible popup can be restored", ExistingVisiblePopupCanBePresented);
        RunCase(run, "cold native popup remains discoverable after one second", DelayedPopupIsDiscovered);
        RunCase(run, "a shadow cannot consume native popup discovery", ShadowDoesNotConsumeDiscovery);
        RunCase(run, "cancelled popup discovery does not present a late window", CancelledRequestDoesNotPresent);
        RunCase(run, "a closing tracked popup is not resurrected", TrackedClosingPopupIsNotResurrected);
        RunCase(run, "an unrelated visible popup is not restored", UnrelatedVisiblePopupIsNotRestored);
        RunCase(run, "a newer desktop click cancels popup discovery", NewClickCancelsDiscovery);
        RunCase(run, "source movement cancels popup discovery", SourceMovementCancelsDiscovery);
        RunCase(run, "a newer popup request supersedes the earlier request", NewPopupRequestSupersedesEarlierRequest);
        RunCase(run, "moving a published popup refreshes its input bounds", MovedPopupRefreshesBounds);
        RunCase(run, "equally plausible popups are rejected with a diagnostic reason", () => UncertainPopupIsRejected(false));
        RunCase(run, "an unknown native popup is never guessed from its appearance", () => UncertainPopupIsRejected(true));
        RunCase(run, "delayed comments use one click and the shared presentation lifecycle", CommentsUseOneInvocation);
        RunCase(run, "comment discovery obeys the same explicit cancellation as other popups", CommentsRespectCancellation);
        RunCase(run, "recovery clears tracked input without dismissing unrelated native windows", RecoveryIsScoped);
    }

    private static AccessoryWindowCoordinator Coordinator(WallpaperContext controller)
    {
        return (AccessoryWindowCoordinator)typeof(WallpaperContext).GetProperty("Accessories", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller, null);
    }

    private static void RecoveryIsScoped()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true))
        using (var unrelated = new FixtureWindow(source.Handle, true, "TXGuiFoundation", "Unrelated settings"))
        {
            var controller = CreateParkedController(source);
            var coordinator = Coordinator(controller);
            Native.ShowWindow(popup.Handle, 4);
            Native.ShowWindow(unrelated.Handle, 4);
            coordinator.PublishQQPopupSurface(popup.Handle, source.Bounds);
            Rectangle before = source.Bounds;
            coordinator.Recover();
            Pump(100);
            Assert(coordinator.Surfaces.Length == 0 && source.Bounds == before, "Recovery did not clear ownership or moved the source.");
            Assert(popup.CancelMessages > 0 && unrelated.CancelMessages == 0, "Recovery dismissed a window it did not own.");
            AssertFixturesNeverActivated();
        }
    }

    private static void MovedPopupRefreshesBounds()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true))
        {
            var controller = CreateParkedController(source);
            var coordinator = Coordinator(controller);
            Native.ShowWindow(popup.Handle, 4);
            coordinator.PublishQQPopupSurface(popup.Handle, source.Bounds);
            Rectangle old = coordinator.Surfaces[0].Bounds;
            Native.SetWindowPos(popup.Handle, IntPtr.Zero, old.Left + 80, old.Top + 30, old.Width, old.Height, 0x14u);
            coordinator.PruneQQPopupSurfaces();
            Assert(coordinator.Surfaces[0].Bounds == Rectangle.Intersect(source.Bounds, popup.Bounds), "Published input bounds did not follow the window.");
            Assert(!coordinator.Surfaces[0].Bounds.Contains(old.Left + 1, old.Top + 1), "Old popup pixels still own input.");
        }
    }

    private static void UncertainPopupIsRejected(bool unknown)
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var first = new FixtureWindow(source.Handle, true, "TXGuiFoundation", unknown ? "private fixture title" : "音效盒子"))
        using (var second = new FixtureWindow(source.Handle, true))
        {
            var controller = CreateParkedController(source);
            var diagnostics = (InteractionDiagnostics)typeof(WallpaperContext).GetProperty("Diagnostics", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller, null);
            diagnostics.Begin(0, DesktopClickRoute.SpecialControl, DesktopSpecialAction.ClickSoundEffects, 0);
            QueueDiscovery(controller, source.Bounds);
            Native.ShowWindow(first.Handle, 4);
            if (!unknown) Native.ShowWindow(second.Handle, 4);
            Pump(5250);
            Assert(!Tracks(controller, first.Handle) && !Tracks(controller, second.Handle), "Ambiguous window became an input owner.");
            string report = diagnostics.Export();
            Assert(report.Contains("AmbiguousTarget") && !report.Contains("private fixture title"), "Missing safe diagnostic reason.");
        }
    }

    private static void CommentsUseOneInvocation()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        {
            var controller = CreateParkedController(source);
            var coordinator = Coordinator(controller);
            int calls = 0;
            coordinator.OpenNativeComments(source.Bounds, () => { calls++; return true; });
            Pump(1250);
            using (var comments = new FixtureWindow(source.Handle, true, "TXGuiFoundation", "评论"))
            {
                Native.ShowWindow(comments.Handle, 4);
                Pump(300);
                Assert(calls == 1 && Tracks(controller, comments.Handle), "Delayed comments were missed or clicked twice.");
            }
        }
    }

    private static void CommentsRespectCancellation()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        {
            var controller = CreateParkedController(source);
            var coordinator = Coordinator(controller);
            coordinator.OpenNativeComments(source.Bounds, () => true);
            coordinator.CancelPendingQQPopupPresentation();
            using (var comments = new FixtureWindow(source.Handle, true, "TXGuiFoundation", "评论"))
            {
                Native.ShowWindow(comments.Handle, 4);
                Pump(300);
                Assert(!Tracks(controller, comments.Handle) && !IsTopmost(comments.Handle), "A cancelled comment request was presented.");
            }
        }
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

    private static AccessibleTreeSnapshot AccompanimentControls(Rectangle bounds, bool complete = true)
    {
        var labels = new[] { "伴唱", "声伴比例", "变速：0.9", complete ? "变调：0" : "other" };
        var nodes = new List<AccessibleNodeSnapshot>();
        for (int i = 0; i < labels.Length; i++)
            nodes.Add(new AccessibleNodeSnapshot(labels[i], "", "", 41, false, 2,
                new Rectangle(bounds.Left + 10, bounds.Top + 10 + i * 25, 80, 20)));
        return new AccessibleTreeSnapshot(nodes.ToArray());
    }

    private static void UntitledAccompaniment(bool reused, string action)
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true, "TXGuiFoundation", ""))
        {
            int captures = 0;
            var diagnostics = new InteractionDiagnostics();
            var coordinator = new AccessoryWindowCoordinator(() => source.Handle, () => 1,
                _ => { }, diagnostics, w => { captures++; return AccompanimentControls(popup.Bounds); });
            if (reused) Native.ShowWindow(popup.Handle, 4);
            var before = coordinator.GetVisibleQQTopLevelWindows();
            Rectangle anchor = new Rectangle(source.Bounds.Right - 50, source.Bounds.Bottom - 50, 24, 24);
            var request = coordinator.BeginQQPopupPresentation(1, anchorBounds: anchor);
            Native.ShowWindow(popup.Handle, 4);
            coordinator.QueuePresentQQPopup(request, before, source.Bounds, action);
            Pump(700);
            coordinator.CancelPendingQQPopupPresentation();
            Pump(100);
            Assert(coordinator.Surfaces.Length == 1 && coordinator.Surfaces[0].Window == popup.Handle,
                "The owned untitled accompaniment panel was left offscreen/unpublished.");
            Assert(IsTopmost(popup.Handle) && captures == 1, "Panel proof was not bounded to one successful capture.");
            Assert(popup.Bounds.Right == anchor.Right && popup.Bounds.Bottom == anchor.Top,
                "The popup did not appear above its actual launcher.");
            coordinator.PruneQQPopupSurfaces();
            Assert(coordinator.Surfaces[0].AccompanimentIdentity, "Maintenance lost the panel identity.");
            // The launcher's toggle has already hidden the window by the time discovery starts.
            diagnostics.Begin(1, DesktopClickRoute.SpecialControl, DesktopSpecialAction.ClickAccompaniment, 0);
            before = coordinator.GetVisibleQQTopLevelWindows();
            request = coordinator.BeginQQPopupPresentation(1);
            Native.ShowWindow(popup.Handle, 0);
            coordinator.QueuePresentQQPopup(request, before, source.Bounds, action);
            Pump(5250);
            Assert(!Native.IsWindowVisible(popup.Handle) && diagnostics.Export().Contains("Closed"),
                "A closing untitled panel was resurrected or reported as a timeout.");
            AssertFixturesNeverActivated();
        }
    }

    private static void RejectUnprovenUntitledPanels()
    {
        for (int variant = 0; variant < 4; variant++)
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(variant == 1 ? IntPtr.Zero : source.Handle, true,
            variant == 2 ? "TXGFLayerMask" : "TXGuiFoundation", variant == 3 ? "Unrelated panel" : ""))
        {
            int captures = 0;
            var coordinator = new AccessoryWindowCoordinator(() => source.Handle, () => 1,
                _ => { }, new InteractionDiagnostics(), w => { captures++; return AccompanimentControls(popup.Bounds, false); });
            var before = coordinator.GetVisibleQQTopLevelWindows();
            Native.ShowWindow(popup.Handle, 4);
            coordinator.QueuePresentQQPopup(coordinator.BeginQQPopupPresentation(1), before, source.Bounds, "Accompaniment");
            Pump(1800);
            coordinator.CancelPendingQQPopupPresentation();
            Pump(100);
            Assert(coordinator.Surfaces.Length == 0, "An unproven window was presented.");
            Assert(captures == (variant == 0 ? 3 : 0), "Semantic scans were unbounded or reached an unrelated window.");
        }
    }

    private static void CancelDuringPanelCapture()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true, "TXGuiFoundation", ""))
        {
            long click = 1;
            var coordinator = new AccessoryWindowCoordinator(() => source.Handle, () => click,
                _ => { }, new InteractionDiagnostics(), w => { Interlocked.Increment(ref click); return AccompanimentControls(popup.Bounds); });
            var before = coordinator.GetVisibleQQTopLevelWindows();
            Native.ShowWindow(popup.Handle, 4);
            coordinator.QueuePresentQQPopup(coordinator.BeginQQPopupPresentation(1), before, source.Bounds, "Accompaniment");
            Pump(500);
            Assert(coordinator.Surfaces.Length == 0 && !IsTopmost(popup.Handle), "Cancelled semantic evidence presented a stale panel.");
        }
    }

    private static void LayeredPanelIsRedrawn()
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true))
        {
            Native.SetWindowLongPtr(popup.Handle, -20,
                new IntPtr(Native.GetWindowLongPtr(popup.Handle, -20).ToInt64() | 0x80000));
            Assert(SetLayeredWindowAttributes(popup.Handle, 0, 255, 2), "Layered fixture initialization failed.");
            Native.ShowWindow(popup.Handle, 4);
            Pump(100);
            ValidateRect(popup.Handle, IntPtr.Zero);
            Assert(!GetUpdateRect(popup.Handle, IntPtr.Zero, false), "Fixture already needs painting.");
            int before = popup.PaintMessages;
            var coordinator = new AccessoryWindowCoordinator(() => source.Handle, () => 1,
                _ => { }, new InteractionDiagnostics());
            Assert(coordinator.TryPositionQQPopup(popup.Handle, source.Bounds, null, false), "Panel positioning failed.");
            Assert(popup.PaintMessages > before || GetUpdateRect(popup.Handle, IntPtr.Zero, false),
                "Positioning did not invalidate the offscreen layered panel for repaint.");
            AssertFixturesNeverActivated();
        }
    }

    private static void PanelPlacementLifecycle(int variant)
    {
        using (var source = new FixtureWindow(IntPtr.Zero, false))
        using (var popup = new FixtureWindow(source.Handle, true, "TXGuiFoundation", ""))
        {
            long click = 1;
            int captures = 0;
            var coordinator = new AccessoryWindowCoordinator(() => source.Handle, () => click,
                _ => { }, new InteractionDiagnostics(), w => { captures++; return AccompanimentControls(popup.Bounds); });
            try
            {
                Rectangle anchor = new Rectangle(source.Bounds.Right - 50, source.Bounds.Bottom - 50, 24, 24);
                var before = coordinator.GetVisibleQQTopLevelWindows();
                Native.ShowWindow(popup.Handle, 4);
                coordinator.QueuePresentQQPopup(coordinator.BeginQQPopupPresentation(click, anchorBounds: anchor),
                    before, source.Bounds, "Accompaniment");
                Pump(350);
                Assert(coordinator.Surfaces.Length == 1, "Initial presentation failed.");
                if (variant == 4) Pump(1800);
                if (variant == 2) Interlocked.Increment(ref click);
                if (variant == 3)
                {
                    Native.ShowWindow(popup.Handle, 0);
                    Pump(350);
                    Assert(!Native.IsWindowVisible(popup.Handle), "Placement correction resurrected a closed panel.");
                    return;
                }
                Rectangle displaced = new Rectangle(source.Bounds.Left + 10, source.Bounds.Bottom - popup.Bounds.Height,
                    popup.Bounds.Width, popup.Bounds.Height);
                before = coordinator.GetVisibleQQTopLevelWindows();
                Native.SetWindowPos(popup.Handle, IntPtr.Zero, displaced.Left, displaced.Top,
                    displaced.Width, displaced.Height, 0x14u);
                if (variant == 1)
                {
                    Interlocked.Increment(ref click);
                    anchor.Offset(-180, -40);
                    coordinator.QueuePresentQQPopup(coordinator.BeginQQPopupPresentation(click, anchorBounds: anchor),
                        before, source.Bounds, "Audio quality");
                }
                Pump(350);
                if (variant <= 1)
                {
                    Assert(popup.Bounds.Right == anchor.Right && popup.Bounds.Bottom == anchor.Top,
                        "The shared panel remained at the left edge after native layout/reuse.");
                    Assert(captures == 1, "Repositioning unnecessarily rescanned the known panel.");
                    Assert(coordinator.Surfaces[0].Bounds == popup.Bounds, "Input bounds did not follow the corrected position.");
                }
                else Assert(popup.Bounds == displaced, "Expired/cancelled placement fought a later movement.");
                AssertFixturesNeverActivated();
            }
            finally { coordinator.CancelPendingQQPopupPresentation(); Pump(100); }
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
            var diagnostics = (InteractionDiagnostics)typeof(WallpaperContext).GetProperty("Diagnostics", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller, null);
            diagnostics.Begin(0, DesktopClickRoute.SpecialControl, DesktopSpecialAction.ClickSoundEffects, 0);
            QueueDiscovery(controller, source.Bounds);
            Native.ShowWindow(popup.Handle, 0);
            Pump(5250);
            Assert(!Native.IsWindowVisible(popup.Handle), "Discovery resurrected a tracked popup after its toggle closed it.");
            Assert(diagnostics.Export().Contains("Closed"), "Successful toggle-close must not count as a popup timeout.");
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
        Native.GetWindowThreadProcessId(source.Handle, out uint sourcePid);
        SetField(controller, "qqWindowProcessIdSource", source.Handle);
        SetField(controller, "qqWindowProcessId", sourcePid);
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

    [DllImport("user32.dll")]
    private static extern bool GetUpdateRect(IntPtr window, IntPtr rect, bool erase);
    [DllImport("user32.dll")]
    private static extern bool ValidateRect(IntPtr window, IntPtr rect);
    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);

    private sealed class FixtureWindow : NativeWindow, IDisposable
    {
        internal int CancelMessages;
        internal int PaintMessages;
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
            if (message.Msg == 31) CancelMessages++;
            if (message.Msg == 15) PaintMessages++;
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
