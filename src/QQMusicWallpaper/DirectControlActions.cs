using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

using Accessibility;

namespace QQMusicWallpaper;

internal static class DirectControlActions
{
    private sealed class Target { internal IAccessible Node; internal object Id; }
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref Guid iid,
        [In, Out, MarshalAs(UnmanagedType.Interface)] ref object value);

    internal static DirectActionResult Invoke(IntPtr window, AccessibleNodeSnapshot target, bool doubleClick)
    { return Invoke(window, target, doubleClick, () => true); }
    internal static DirectActionResult Invoke(IntPtr window, AccessibleNodeSnapshot target, bool doubleClick, Func<bool> current)
    {
        if (!current()) return DirectActionResult.Rejected;
        if (target.Unavailable) return DirectActionResult.Rejected;
        if (target.Hidden || target.Bounds.IsEmpty) return DirectActionResult.Unsupported;
        // A list's default action can play/open the item. Preserve single-click
        // selection until its separate selection interface is verified.
        if (!doubleClick && (target.Role == 16 || target.Role == 29 || target.Role == 34 || target.Role == 36)) return DirectActionResult.Unsupported;
        return Msaa(window, target, current);
    }
    private static bool Unsupported(Exception error)
    {
        int code = Marshal.GetHRForException(error);
        return error is NotSupportedException || code == unchecked((int)0x80004001) || code == unchecked((int)0x80020003);
    }
    private static DirectActionResult Msaa(IntPtr window, AccessibleNodeSnapshot expected, Func<bool> current)
    {
        bool attempted = false;
        try
        {
            object raw = null; Guid iid = typeof(IAccessible).GUID;
            if (AccessibleObjectFromWindow(window, 0xfffffffc, ref iid, ref raw) != 0 || !(raw is IAccessible root))
                return DirectActionResult.Unsupported;
            var matches = new List<Target>(); int visited = 0;
            var watch = Stopwatch.StartNew();
            Find(root, expected, 0, ref visited, watch, matches);
            if (watch.ElapsedMilliseconds > 300 || visited >= 8192) return DirectActionResult.Unsupported;
            if (matches.Count > 1) return DirectActionResult.Rejected;
            if (matches.Count == 0) return DirectActionResult.Unsupported;
            matches[0].Node.accLocation(out int x, out int y, out int w, out int h, matches[0].Id);
            if (expected.Bounds != new Rectangle(x, y, w, h) ||
                (Convert.ToInt64(matches[0].Node.get_accState(matches[0].Id)) & (32768 | 1)) != 0 ||
                watch.ElapsedMilliseconds > 300 || !current()) return DirectActionResult.Rejected;
            attempted = true;
            matches[0].Node.accDoDefaultAction(matches[0].Id);
            return DirectActionResult.Delivered;
        }
        catch (Exception error) { return Unsupported(error) || !attempted ? DirectActionResult.Unsupported : DirectActionResult.Uncertain; }
    }
    private static void Find(IAccessible parent, AccessibleNodeSnapshot expected, int depth,
        ref int visited, Stopwatch watch, List<Target> matches)
    {
        if (depth > 32 || visited++ >= 8192 || matches.Count > 1 || watch.ElapsedMilliseconds > 300) return;
        if (!Visit(parent, 0, expected, matches)) return;
        int count; try { count = parent.accChildCount; } catch { return; }
        for (int i = 1; i <= count && visited < 8192 && matches.Count < 2 && watch.ElapsedMilliseconds <= 300; i++)
        {
            IAccessible child = null; try { child = parent.get_accChild(i) as IAccessible; } catch { }
            if (child != null) Find(child, expected, depth + 1, ref visited, watch, matches);
            else { visited++; Visit(parent, i, expected, matches); }
        }
    }
    private static bool Visit(IAccessible node, object id, AccessibleNodeSnapshot expected, List<Target> matches)
    {
        try
        {
            long state = Convert.ToInt64(node.get_accState(id));
            if ((state & (32768 | 1)) != 0) return false;
            if (Convert.ToInt32(node.get_accRole(id)) == expected.Role)
            {
                node.accLocation(out int x, out int y, out int w, out int h, id);
                if (expected.Bounds == new Rectangle(x, y, w, h))
                {
                    string name = "", description = "";
                    try { name = node.get_accName(id) ?? ""; } catch { }
                    try { description = node.get_accDescription(id) ?? ""; } catch { }
                    if (name == expected.Name && description == expected.Description)
                        matches.Add(new Target { Node = node, Id = id });
                }
            }
            return true;
        }
        catch { return false; }
    }
}
