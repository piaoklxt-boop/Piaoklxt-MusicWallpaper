using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace QQMusicWallpaper;

internal enum DesktopPinResult { Pinned, AlreadyPinned, Stale, Unavailable }

// A single worker checks only the selected source window. No desktop switching,
// duplicated capture sessions, application-wide pin, or synchronous UI-thread COM.
internal sealed class SourceDesktopPinning : IDisposable
{
    private readonly object gate = new object();
    private readonly Func<IntPtr, uint, bool> owns;
    private readonly Func<IntPtr, Func<bool>, DesktopPinResult> pin;
    private readonly Action<Action> schedule;
    private IntPtr window;
    private uint process;
    private long generation;
    private bool busy, disposed;
    private DateTime nextCheck;
    private DesktopPinResult? lastResult;

    internal SourceDesktopPinning() : this(IsQQSource, VirtualDesktopPinApi.Pin,
        action => ThreadPool.QueueUserWorkItem(_ => action())) { }

    internal SourceDesktopPinning(Func<IntPtr, uint, bool> owns,
        Func<IntPtr, Func<bool>, DesktopPinResult> pin, Action<Action> schedule)
    { this.owns = owns; this.pin = pin; this.schedule = schedule; }

    internal void EnsureAsync(IntPtr source, uint pid)
    {
        long request;
        lock (gate)
        {
            if (disposed) return;
            if (window != source || process != pid)
            { window = source; process = pid; generation++; nextCheck = DateTime.MinValue; lastResult = null; }
            if (source == IntPtr.Zero || pid == 0 || busy || DateTime.UtcNow < nextCheck) return;
            busy = true; request = generation;
        }
        schedule(() =>
        {
            Func<bool> current = () =>
            {
                lock (gate) { if (disposed || generation != request) return false; }
                return owns(source, pid);
            };
            DesktopPinResult result = DesktopPinResult.Unavailable;
            try { result = current() ? pin(source, current) : DesktopPinResult.Stale; }
            catch (Exception) { result = DesktopPinResult.Unavailable; }
            finally
            {
                lock (gate)
                {
                    busy = false;
                    if (!disposed && generation == request)
                    {
                        nextCheck = DateTime.UtcNow.AddSeconds(result == DesktopPinResult.Unavailable ? 5 : 30);
                        if (lastResult != result) RuntimeLog.Write("Source desktop pin: " + result);
                        lastResult = result;
                    }
                }
            }
        });
    }

    private static bool IsQQSource(IntPtr source, uint expected)
    {
        if (source == IntPtr.Zero || !Native.IsWindow(source)) return false;
        uint actual; Native.GetWindowThreadProcessId(source, out actual);
        if (actual != expected) return false;
        try { using (var p = Process.GetProcessById((int)actual))
            return !p.HasExited && string.Equals(p.ProcessName, "QQMusic", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    // Preserve a user's existing pin, including while wallpaper is paused.
    // New QQ Music windows are pinned again after process/window recovery.
    public void Dispose() { lock (gate) { disposed = true; generation++; } }
}

internal static class VirtualDesktopPinApi
{
    // Shell pinning is an undocumented, capability-probed interface. Only the
    // established prefix of each vtable is used; no version-dependent desktop
    // manager slots or Explorer injection are needed here.
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    }
    [ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationViewCollection
    {
        [PreserveSig] int GetViews(out IntPtr views);
        [PreserveSig] int GetViewsByZOrder(out IntPtr views);
        [PreserveSig] int GetViewsByAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr views);
        [PreserveSig] int GetViewForHwnd(IntPtr hwnd, [MarshalAs(UnmanagedType.IUnknown)] out object view);
    }
    [ComImport, Guid("4CE81583-1E4C-4632-A621-07A53543148F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPinnedApps
    {
        [PreserveSig] int IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] out bool pinned);
        [PreserveSig] int PinAppID([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int UnpinAppID([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int IsViewPinned([MarshalAs(UnmanagedType.IUnknown)] object view, [MarshalAs(UnmanagedType.Bool)] out bool pinned);
        [PreserveSig] int PinView([MarshalAs(UnmanagedType.IUnknown)] object view);
        [PreserveSig] int UnpinView([MarshalAs(UnmanagedType.IUnknown)] object view);
    }

    internal static DesktopPinResult Pin(IntPtr hwnd, Func<bool> current)
    { return SetPinned(hwnd, true, current); }

    internal static DesktopPinResult SetPinned(IntPtr hwnd, bool pin, Func<bool> current)
    {
        object shell = null, views = null, pinned = null, view = null;
        try
        {
            if (!current() || hwnd == IntPtr.Zero) return DesktopPinResult.Stale;
            shell = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239")));
            var provider = (IServiceProvider)shell;
            Guid viewId = typeof(IApplicationViewCollection).GUID;
            Marshal.ThrowExceptionForHR(provider.QueryService(ref viewId, ref viewId, out views));
            Guid serviceId = new Guid("B5A399E7-1C87-46B8-88E9-FC5747B171BD"), pinId = typeof(IPinnedApps).GUID;
            Marshal.ThrowExceptionForHR(provider.QueryService(ref serviceId, ref pinId, out pinned));
            Marshal.ThrowExceptionForHR(((IApplicationViewCollection)views).GetViewForHwnd(hwnd, out view));
            if (view == null || !current()) return DesktopPinResult.Stale;
            var api = (IPinnedApps)pinned;
            bool state; Marshal.ThrowExceptionForHR(api.IsViewPinned(view, out state));
            if (state == pin) return DesktopPinResult.AlreadyPinned;
            if (!current()) return DesktopPinResult.Stale;
            Marshal.ThrowExceptionForHR(pin ? api.PinView(view) : api.UnpinView(view));
            Marshal.ThrowExceptionForHR(api.IsViewPinned(view, out state));
            return state == pin ? DesktopPinResult.Pinned : DesktopPinResult.Unavailable;
        }
        catch (COMException) { return DesktopPinResult.Unavailable; }
        catch (InvalidCastException) { return DesktopPinResult.Unavailable; }
        finally
        {
            foreach (object value in new[] { view, pinned, views, shell })
                if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }
}
