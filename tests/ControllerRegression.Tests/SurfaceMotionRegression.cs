using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using QQMusicWallpaper;

namespace ControllerRegression.Tests;

internal static class SurfaceMotionRegression
{
    [DllImport("user32.dll")] private static extern void NotifyWinEvent(uint type, IntPtr hwnd, int obj, int child);
    internal static void Register(Action<string, Action> run)
    { run("motion color layer never remains at the old position during a native drag", Drag); }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pump(int milliseconds)
    { var watch = Stopwatch.StartNew(); do { Application.DoEvents(); Thread.Sleep(1); } while (watch.ElapsedMilliseconds < milliseconds); }
    private static void Drag()
    {
        using (var owner = new Form { Bounds = new Rectangle(-18000, -18000, 340, 220), ShowInTaskbar = false })
        using (var other = new Form { Bounds = new Rectangle(-19000, -19000, 340, 220), ShowInTaskbar = false })
        {
            Native.ShowWindow(owner.Handle, 4); Native.ShowWindow(other.Handle, 4);
            using (var manager = new AppearanceSurfaces((hwnd,state) => hwnd == owner.Handle ? SurfaceKind.Word : SurfaceKind.None))
            {
                manager.Configure(new SurfaceAppearanceState(new ThemePreferences { Palette = ThemePalette.Spectrum }, new ExplorerAppearanceSettings(), Color.Teal, true));
                manager.Tick(); Pump(80);
                var layers = (Dictionary<IntPtr,TintSurface>)typeof(AppearanceSurfaces).GetField("surfaces", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
                var surface = layers[owner.Handle];
                Assert(surface.Visible, "Missing initial color layer");
                NotifyWinEvent(0xA, other.Handle, 0, 0); Pump(20);
                Assert(surface.Visible, "Unrelated window drag hid this window");
                NotifyWinEvent(0xA, owner.Handle, 0, 0); Pump(25);
                Assert(!surface.Visible, "Old-position color frame remains visible after drag starts");
                for (int i = 0; i < 8; i++)
                {
                    owner.Left += 30; owner.Width += 4; manager.Tick(); Pump(8);
                    Assert(!surface.Visible, "Animation repainted a detached frame during drag");
                }
                NotifyWinEvent(0xB, owner.Handle, 0, 0); Pump(160);
                Rectangle bounds; SurfaceWindows.TryBounds(owner.Handle, out bounds);
                Assert(surface.Visible && surface.Bounds == bounds, "Color failed to resume at final owner geometry");
                manager.Configure(new SurfaceAppearanceState(new ThemePreferences(), new ExplorerAppearanceSettings(), Color.Teal, false)); manager.Tick();
                NotifyWinEvent(0xA, owner.Handle, 0, 0); NotifyWinEvent(0xB, owner.Handle, 0, 0); Pump(80);
                Assert(layers.Count == 0, "Late native event resurrected paused color layers");
            }
        }
    }
}
