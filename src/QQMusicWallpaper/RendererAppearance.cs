using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace QQMusicWallpaper;

internal static class RendererAppearance
{
    internal static IntPtr FindSurface(uint pid, IntPtr desktop)
    {
        if (desktop == IntPtr.Zero || !Native.IsWindow(desktop)) return IntPtr.Zero;
        IntPtr surface = IntPtr.Zero;
        Native.EnumChildWindows(desktop, (candidate, data) =>
        {
            Native.GetWindowThreadProcessId(candidate, out var owner);
            if (owner != pid) return true;
            var name = new StringBuilder(128);
            Native.GetClassName(candidate, name, name.Capacity);
            if (name.ToString() != "Win32CaptureSample.SampleWindow") return true;
            surface = candidate;
            return false;
        }, IntPtr.Zero);
        return surface;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    internal static void Apply(Process renderer, ExplorerAppearanceSettings appearance)
    {
        if (renderer == null) return;
        try
        {
            if (renderer.HasExited) return;
            uint pid = (uint)renderer.Id;
            uint message = RegisterWindowMessage("PiaoklxtMusicWallpaper.EdgeSettings.v1");
            if (message == 0) return;
            // The renderer is a child of Progman, so top-level enumeration alone
            // cannot find it. Identity is checked before sending the message.
            IntPtr surface = FindSurface(pid, Native.FindWindow("Progman", null));
            if (surface != IntPtr.Zero)
                Native.PostMessage(surface, message, appearance.ExtendBottom ? new IntPtr(1) : IntPtr.Zero,
                    new IntPtr(appearance.EdgeDepth));
        }
        catch (InvalidOperationException) { }
    }
}
