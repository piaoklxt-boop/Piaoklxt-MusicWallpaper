using System;
using System.Runtime.InteropServices;

namespace QQMusicWallpaper;

// Out-of-context events are delivered on the registering appearance UI thread.
// Keep the delegate rooted and release both hooks before disposing its surfaces.
internal sealed class SurfaceMotionEvents : IDisposable
{
    private delegate void WinEvent(IntPtr hook, uint type, IntPtr window, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    private readonly WinEvent callback;
    private IntPtr moveHook, locationHook;
    private bool disposed;
    internal SurfaceMotionEvents(Action<IntPtr, uint> changed)
    {
        callback = (hook, type, window, obj, child, thread, time) =>
        {
            if (disposed || window == IntPtr.Zero || obj != 0 || child != 0) return;
            try { changed(window, type); }
            catch (Exception error) { RuntimeLog.Write("Appearance motion event failed: " + error.GetType().Name); }
        };
        moveHook = SetWinEventHook(0xA, 0xB, IntPtr.Zero, callback, 0, 0, 0);
        locationHook = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, callback, 0, 0, 0);
        if (moveHook == IntPtr.Zero || locationHook == IntPtr.Zero)
        { Dispose(); throw new InvalidOperationException("Appearance motion tracking unavailable."); }
    }
    public void Dispose()
    {
        disposed = true;
        if (moveHook != IntPtr.Zero) { UnhookWinEvent(moveHook); moveHook = IntPtr.Zero; }
        if (locationHook != IntPtr.Zero) { UnhookWinEvent(locationHook); locationHook = IntPtr.Zero; }
    }
}
