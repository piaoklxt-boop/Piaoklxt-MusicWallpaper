using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using Accessibility;

namespace QQMusicWallpaper;

// Explorer owns unknown/stale regions. Only a complete, current snapshot may
// establish that a point is clear. No COM, process-memory or message calls run
// on the hook thread; one background scan can be outstanding at a time.
internal sealed class DesktopIconCache : IDisposable
{
	private sealed class Snapshot
	{
		internal IntPtr Window;
		internal uint ProcessId;
		internal Rectangle WindowBounds;
		internal Rectangle[] Items;
		internal int Generation;
		internal long Expires;
	}

	private readonly Func<IntPtr, Rectangle[]> capture;
	private readonly WinEventProc eventProc;
	private IntPtr eventHook;
	private IntPtr target;
	private volatile Snapshot snapshot;
	private int generation;
	private int scanning;
	private int disposed;
	private long nextRefresh;

	internal DesktopIconCache() : this(Capture, true) { }

	internal DesktopIconCache(Func<IntPtr, Rectangle[]> capture, bool observeChanges)
	{
		this.capture = capture;
		if (observeChanges)
		{
			eventProc = OnWindowEvent;
			eventHook = SetWinEventHook(0x8000, 0x800B, IntPtr.Zero, eventProc, 0, 0, 0);
		}
	}

	internal void RequestRefresh(IntPtr window)
	{
		if (Interlocked.CompareExchange(ref disposed, 0, 0) != 0) return;
		if (Interlocked.Exchange(ref target, window) != window) Invalidate();
		long now = DateTime.UtcNow.Ticks;
		if (window == IntPtr.Zero || now < Interlocked.Read(ref nextRefresh) ||
			Interlocked.CompareExchange(ref scanning, 1, 0) != 0) return;
		Interlocked.Exchange(ref nextRefresh, now + TimeSpan.FromMilliseconds(750).Ticks);
		int requestedGeneration = Interlocked.CompareExchange(ref generation, 0, 0);
		try
		{
			ThreadPool.QueueUserWorkItem(_ =>
			{
				try
				{
					uint pid;
					Rectangle bounds;
					if (!TryIdentify(window, out pid, out bounds)) return;
					Rectangle[] items = capture(window);
					if (items == null) { snapshot = null; return; }
					uint finalPid;
					Rectangle finalBounds;
					if (!TryIdentify(window, out finalPid, out finalBounds) ||
						pid != finalPid || bounds != finalBounds ||
						requestedGeneration != Interlocked.CompareExchange(ref generation, 0, 0) ||
						window != Interlocked.CompareExchange(ref target, IntPtr.Zero, IntPtr.Zero) ||
						Interlocked.CompareExchange(ref disposed, 0, 0) != 0) return;
					snapshot = new Snapshot { Window = window, ProcessId = pid,
						WindowBounds = bounds, Items = items, Generation = requestedGeneration,
						Expires = now + TimeSpan.FromSeconds(2).Ticks };
				}
				catch (Exception) { snapshot = null; }
				finally { Interlocked.Exchange(ref scanning, 0); }
			});
		}
		catch
		{
			Interlocked.Exchange(ref scanning, 0);
			throw;
		}
	}

	internal void RequestRefresh()
	{
		RequestRefresh(Interlocked.CompareExchange(ref target, IntPtr.Zero, IntPtr.Zero));
	}

	internal bool ProtectsPoint(IntPtr window, Point point)
	{
		Snapshot current = snapshot;
		uint pid;
		Rectangle bounds;
		if (Interlocked.CompareExchange(ref disposed, 0, 0) != 0 || current == null ||
			current.Window != window || current.Expires <= DateTime.UtcNow.Ticks ||
			current.Generation != Interlocked.CompareExchange(ref generation, 0, 0) ||
			!TryIdentify(window, out pid, out bounds) || pid != current.ProcessId ||
			bounds != current.WindowBounds) return true;
		foreach (Rectangle item in current.Items)
			if (item.Contains(point)) return true;
		return false;
	}

	internal void Invalidate()
	{
		Interlocked.Increment(ref generation);
		snapshot = null;
		Interlocked.Exchange(ref nextRefresh, 0);
	}

	private void OnWindowEvent(IntPtr hook, uint kind, IntPtr window,
		int objectId, int childId, uint threadId, uint time)
	{
		// Creation, destruction, visibility, reorder and location alter icon
		// ownership. Selection/focus/name events do not invalidate geometry.
		if (window == Interlocked.CompareExchange(ref target, IntPtr.Zero, IntPtr.Zero) &&
			(kind <= 0x8004 || kind == 0x800B))
		{
			Invalidate();
			// The event callback must also contain scheduling failures.
			try { RequestRefresh(); } catch (Exception) { }
		}
	}

	private static bool TryIdentify(IntPtr window, out uint pid, out Rectangle bounds)
	{
		Native.GetWindowThreadProcessId(window, out pid);
		Native.RECT rect;
		bounds = Rectangle.Empty;
		if (pid == 0 || !Native.GetWindowRect(window, out rect)) return false;
		bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
		return true;
	}

	internal static Rectangle[] Capture(IntPtr window)
	{
		object root = null;
		object[] children = null;
		try
		{
			Guid iid = typeof(IAccessible).GUID;
			if (AccessibleObjectFromWindow(window, unchecked((uint)-4), ref iid, ref root) != 0 ||
				!(root is IAccessible accessible)) return null;
			int count = accessible.accChildCount;
			if (count < 0 || count > 4096) return null;
			children = new object[count];
			int obtained;
			if (count > 0 && (AccessibleChildren(accessible, 0, count, children, out obtained) != 0 ||
				obtained != count)) return null;
			var items = new List<Rectangle>(count);
			foreach (object child in children)
			{
				IAccessible node = child as IAccessible ?? accessible;
				object childId = child is int ? child : (object)0;
				if (child == null) return null;
				if (Convert.ToInt32(node.get_accRole(childId)) != 34)
				{
					// Explorer also exposes its hidden label-edit window as a child.
					// An unexpected visible child remains unknown; rename ownership is
					// checked separately by the controller before routing a click.
					if ((Convert.ToInt32(node.get_accState(childId)) & 0x8000) != 0) continue;
					return null;
				}
				int x, y, width, height;
				node.accLocation(out x, out y, out width, out height, childId);
				if (width <= 0 || height <= 0) return null;
				items.Add(new Rectangle(x, y, width, height));
			}
			if (accessible.accChildCount != count) return null;
			return items.ToArray();
		}
		catch (Exception) { return null; }
		finally
		{
			if (children != null)
				foreach (object child in children)
					if (child != null && Marshal.IsComObject(child)) Marshal.ReleaseComObject(child);
			if (root != null && Marshal.IsComObject(root)) Marshal.ReleaseComObject(root);
		}
	}

	public void Dispose()
	{
		Interlocked.Exchange(ref disposed, 1);
		Invalidate();
		if (eventHook != IntPtr.Zero) { UnhookWinEvent(eventHook); eventHook = IntPtr.Zero; }
	}

	private delegate void WinEventProc(IntPtr hook, uint kind, IntPtr window,
		int objectId, int childId, uint threadId, uint time);
	[DllImport("user32.dll")]
	private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module,
		WinEventProc callback, uint process, uint thread, uint flags);
	[DllImport("user32.dll")]
	private static extern bool UnhookWinEvent(IntPtr hook);
	[DllImport("oleacc.dll")]
	private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId,
		ref Guid iid, [In, Out, MarshalAs(UnmanagedType.Interface)] ref object result);
	[DllImport("oleacc.dll")]
	private static extern int AccessibleChildren(IAccessible container, int start, int count,
		[Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] children, out int obtained);
}
