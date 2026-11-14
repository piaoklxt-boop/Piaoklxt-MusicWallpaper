using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Accessibility;

namespace QQMusicWallpaper;

internal static class QQMusicAccessibility
{
	private sealed class ActionCandidate
	{
		public int Score;

		public int Depth;

		public long Area;

		public Rectangle Bounds;
	}

	private const uint ObjIdClient = 4294967292u;

	private const int MaxDepth = 32;

	private const int MaxNodes = 8192;

	private const long StateInvisible = 32768L;

	private const long StateOffscreen = 65536L;

	private static readonly HashSet<int> InteractiveRoles = new HashSet<int>
	{
		12, // menu item
		30, // link
		43, // push button
		44, // check box
		45, // radio button
		46, // combo box
		47, // drop list
		51  // slider
	};

	[DllImport("oleacc.dll")]
	private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid interfaceId, [In][Out][MarshalAs(UnmanagedType.Interface)] ref object accessibleObject);

	public static AccessibleTreeSnapshot CaptureSnapshot(IntPtr window)
	{
		List<AccessibleNodeSnapshot> nodes = new List<AccessibleNodeSnapshot>();
		object accessibleObject = null;
		Guid interfaceId = typeof(IAccessible).GUID;
		try
		{
			if (AccessibleObjectFromWindow(window, ObjIdClient, ref interfaceId,
				ref accessibleObject) != 0 ||
				!(accessibleObject is IAccessible accessible))
			{
				return new AccessibleTreeSnapshot(nodes.ToArray());
			}
            return CaptureSnapshot(accessible);
		}
		catch { }
		return new AccessibleTreeSnapshot(nodes.ToArray());
	}

    internal static AccessibleTreeSnapshot CaptureSnapshot(IAccessible accessible)
    {
        var nodes = new List<AccessibleNodeSnapshot>();
        int visited = 0;
        CaptureNodes(accessible, 0, ref visited, nodes, accessible);
        return new AccessibleTreeSnapshot(nodes.ToArray());
    }

    // Retain the actual accessible control, not only a historical rectangle.
    // Queried only by the bounded background appearance capture worker.
    private static Func<Rectangle?> PlaybackBoundsReader(IAccessible node, object id, int role, string name, IAccessible root)
    {
        if ((role != 43 && role != 44) || !IsPlaybackName(name)) return null;
        return () =>
        {
            try
            {
                int liveRole = Convert.ToInt32(node.get_accRole(id));
                if ((liveRole != 43 && liveRole != 44) || !IsPlaybackName(node.get_accName(id))) return null;
                if ((Convert.ToInt64(node.get_accState(id)) & (StateInvisible | 1)) != 0) return null;
                IAccessible parent = node;
                for (int depth = 0; parent != null && !ReferenceEquals(parent, root) && depth < MaxDepth; depth++)
                {
                    // QQ Music exposes a different COM wrapper for its parent window.
                    // The nearest native Window is the source boundary; ancestors
                    // above it belong to the desktop embedding host.
                    if (Convert.ToInt32(parent.get_accRole(0)) == 9) break;
                    if ((Convert.ToInt64(parent.get_accState(0)) & StateInvisible) != 0) return null;
                    parent = parent.accParent as IAccessible;
                }
                node.accLocation(out int x, out int y, out int w, out int h, id);
                return w > 0 && h > 0 ? (Rectangle?)new Rectangle(x, y, w, h) : null;
            }
            catch { return null; }
        };
    }
    private static bool IsPlaybackName(string name)
    { return name == "播放" || name == "暂停" || name == "Play" || name == "Pause"; }

    internal static bool AccessTextValue(IAccessible accessible, AccessibleNodeSnapshot target, bool write, ref string value)
    {
        if (target.Role != 42 || target.ReadOnly || target.Hidden || target.Unavailable) return false;
        int visited = 0;
        return VisitText(accessible, 0, ref visited, target, write, ref value);
    }

    internal static bool AccessTextValue(IntPtr window, AccessibleNodeSnapshot target,
        bool write, ref string value)
    {
        if (target.Role != 42 || target.ReadOnly || target.Hidden || target.Unavailable) return false;
        object root = null;
        Guid iid = typeof(IAccessible).GUID;
        try
        {
            if (AccessibleObjectFromWindow(window, ObjIdClient, ref iid, ref root) != 0 || !(root is IAccessible accessible)) return false;
            return AccessTextValue(accessible, target, write, ref value);
        }
        catch { return false; }
    }

    private static bool VisitText(IAccessible root, int depth, ref int visited,
        AccessibleNodeSnapshot target, bool write, ref string value)
    {
        if (depth > MaxDepth || visited >= MaxNodes) return false;
        int count;
        try { count = root.accChildCount; } catch { return false; }
        for (int i = 1; i <= count && visited++ < MaxNodes; i++)
        {
            IAccessible child = null;
            try { child = root.get_accChild(i) as IAccessible; } catch { }
            IAccessible node = child ?? root;
            object id = child == null ? (object)i : 0;
            try
            {
                long state = Convert.ToInt64(node.get_accState(id));
                if ((state & (StateInvisible | 1)) != 0) continue;
                if (Convert.ToInt32(node.get_accRole(id)) == 42 && (state & (64 | 0x20000000)) == 0)
                {
                    node.accLocation(out int x, out int y, out int w, out int h, id);
                    if (target.Bounds == new Rectangle(x, y, w, h) &&
                        (node.get_accName(id) ?? "") == target.Name)
                    {
                        if (write)
                        {
                            node.set_accValue(id, value);
                            return string.Equals(node.get_accValue(id) ?? "", value, StringComparison.Ordinal);
                        }
                        value = node.get_accValue(id) ?? "";
                        return true;
                    }
                }
                if (child != null && VisitText(child, depth + 1, ref visited, target, write, ref value)) return true;
            }
            catch { }
        }
        return false;
    }

	private static void CaptureNodes(IAccessible accessible, int depth,
		ref int visited, List<AccessibleNodeSnapshot> nodes, IAccessible root)
	{
		if (depth > MaxDepth || visited >= MaxNodes) return;
		int childCount;
		try { childCount = accessible.accChildCount; }
		catch { return; }
		for (int index = 1; index <= childCount && visited < MaxNodes; index++)
		{
			visited++;
			IAccessible child = null;
			try { child = accessible.get_accChild(index) as IAccessible; }
			catch { }
			IAccessible node = child ?? accessible;
			object childId = child == null ? (object)index : 0;
			int role = 0;
			try { role = Convert.ToInt32(node.get_accRole(childId)); } catch { }
			bool interactive = AccessibleTreeSnapshot.IsInteractiveRole(role);
			bool timeText = role == 41 || role == 42;
			long state = 0;
			try { state = Convert.ToInt64(node.get_accState(childId)); } catch { }
			string action = "";
			try { action = node.get_accDefaultAction(childId) ?? ""; } catch { }
			if (interactive || timeText || role == 16 || !string.IsNullOrWhiteSpace(action))
			{
				string name = "";
				string description = "";
				Rectangle bounds = Rectangle.Empty;
				try { name = node.get_accName(childId) ?? ""; } catch { }
				if (interactive || role == 16)
				{
					try { description = node.get_accDescription(childId) ?? ""; }
					catch { }
				}
				try
				{
					node.accLocation(out int left, out int top, out int width,
						out int height, childId);
					bounds = new Rectangle(left, top, width, height);
				}
				catch { }
				if (bounds.Width > 0 && bounds.Height > 0)
				{
					nodes.Add(new AccessibleNodeSnapshot(name, description, action, role,
						(state & StateInvisible) != 0, depth, bounds, (state & 1) != 0, (state & 64) != 0,
                        PlaybackBoundsReader(node, childId, role, name, root)));
				}
			}
			// Inactive QQ pages keep visible-looking descendants. Do not publish
			// them through an invisible ancestor. Offscreen alone is expected while
			// the source is parked; the owning surface bounds perform clipping.
			if (child != null && (state & StateInvisible) == 0)
				CaptureNodes(child, depth + 1, ref visited, nodes, root);
		}
	}

	public static bool TryFindVisibleControl(IntPtr window, string description, Rectangle visibleArea, out Rectangle bounds)
	{
		bounds = Rectangle.Empty;
		object accessibleObject = null;
		Guid interfaceId = typeof(IAccessible).GUID;
		try
		{
			if (AccessibleObjectFromWindow(window, 4294967292u, ref interfaceId, ref accessibleObject) != 0)
			{
				return false;
			}
			if (!(accessibleObject is IAccessible accessible))
			{
				return false;
			}
			int visited = 0;
			return TryFindVisibleControl(accessible, description, visibleArea, 0, ref visited, out bounds);
		}
		catch
		{
			return false;
		}
	}

	public static Rectangle[] GetVisibleControlBounds(IntPtr window,
		string description, Rectangle visibleArea)
	{
		return GetControlBounds(window, description, visibleArea, includeHidden: false);
	}

	public static Rectangle[] GetControlBoundsIncludingHidden(IntPtr window,
		string description, Rectangle searchArea)
	{
		return GetControlBounds(window, description, searchArea, includeHidden: true);
	}

	public static bool TryGetControlActionPoint(IntPtr window, Rectangle preferredArea,
		out Point actionPoint, params string[] descriptions)
	{
		actionPoint = Point.Empty;
		object accessibleObject = null;
		Guid interfaceId = typeof(IAccessible).GUID;
		try
		{
			if (AccessibleObjectFromWindow(window, ObjIdClient, ref interfaceId,
				ref accessibleObject) != 0 || !(accessibleObject is IAccessible accessible))
			{
				return false;
			}
			ActionCandidate best = null;
			int visited = 0;
			FindActionCandidate(accessible, preferredArea, descriptions, 0,
				ref visited, ref best);
			if (best == null || best.Bounds.Width <= 0 || best.Bounds.Height <= 0)
				return false;
			actionPoint = new Point(best.Bounds.Left + best.Bounds.Width / 2,
				best.Bounds.Top + best.Bounds.Height / 2);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool TryGetControlActionPointAtPoint(IntPtr window, Point screenPoint,
		out Point actionPoint)
	{
		actionPoint = Point.Empty;
		object accessibleObject = null;
		Guid interfaceId = typeof(IAccessible).GUID;
		try
		{
			if (AccessibleObjectFromWindow(window, ObjIdClient, ref interfaceId,
				ref accessibleObject) != 0 || !(accessibleObject is IAccessible accessible))
			{
				return false;
			}
			ActionCandidate best = null;
			int visited = 0;
			FindPointActionCandidate(accessible, screenPoint, 0, ref visited, ref best);
			if (best == null || best.Bounds.Width <= 0 || best.Bounds.Height <= 0)
				return false;
			actionPoint = new Point(best.Bounds.Left + best.Bounds.Width / 2,
				best.Bounds.Top + best.Bounds.Height / 2);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void FindPointActionCandidate(IAccessible accessible,
		Point screenPoint, int depth, ref int visited, ref ActionCandidate best)
	{
		if (depth > MaxDepth || visited++ > MaxNodes) return;
		int childCount;
		try { childCount = accessible.accChildCount; }
		catch { return; }
		for (int index = 1; index <= childCount; index++)
		{
			IAccessible child = null;
			try { child = accessible.get_accChild(index) as IAccessible; }
			catch { }
			IAccessible node = child ?? accessible;
			object childId = child == null ? (object)index : 0;
			try
			{
				string action = "";
				try { action = node.get_accDefaultAction(childId) ?? ""; }
				catch { }
				int role = Convert.ToInt32(node.get_accRole(childId));
				node.accLocation(out int left, out int top, out int width,
					out int height, childId);
				long area = (long)width * height;
				if ((InteractiveRoles.Contains(role) || !string.IsNullOrWhiteSpace(action)) &&
					width > 0 && height > 0 &&
					new Rectangle(left, top, width, height).Contains(screenPoint) &&
					(best == null || depth > best.Depth ||
						depth == best.Depth && area < best.Area))
				{
					best = new ActionCandidate
					{
						Depth = depth,
						Area = area,
						Bounds = new Rectangle(left, top, width, height)
					};
				}
			}
			catch { }
			if (child != null)
			{
				FindPointActionCandidate(child, screenPoint, depth + 1,
					ref visited, ref best);
			}
		}
	}

	private static void FindActionCandidate(IAccessible accessible,
		Rectangle preferredArea, string[] descriptions, int depth, ref int visited,
		ref ActionCandidate best)
	{
		if (depth > MaxDepth || visited++ > MaxNodes) return;
		int childCount;
		try { childCount = accessible.accChildCount; }
		catch { return; }
		for (int index = 1; index <= childCount; index++)
		{
			IAccessible child = null;
			try { child = accessible.get_accChild(index) as IAccessible; }
			catch { }
			IAccessible node = child ?? accessible;
			object childId = child == null ? (object)index : 0;
			try
			{
				string name = node.get_accName(childId);
				string description = node.get_accDescription(childId);
				if (MatchesAny(name, descriptions) || MatchesAny(description, descriptions))
				{
					string action = "";
					try { action = node.get_accDefaultAction(childId) ?? ""; }
					catch { }
					int score = string.IsNullOrWhiteSpace(action) ? 0 : 100;
					node.accLocation(out int left, out int top, out int width,
						out int height, childId);
					if (width <= 0 || height <= 0) continue;
					if (preferredArea.Contains(
						left + width / 2, top + height / 2)) score += 50;
					if (!IsHidden(node, childId)) score += 10;
					if (best == null || score > best.Score)
					{
						best = new ActionCandidate
						{
							Score = score,
							Bounds = new Rectangle(left, top, width, height)
						};
					}
				}
			}
			catch { }
			if (child != null)
			{
				FindActionCandidate(child, preferredArea, descriptions, depth + 1,
					ref visited, ref best);
			}
		}
	}

	private static bool MatchesAny(string value, string[] candidates)
	{
		if (string.IsNullOrWhiteSpace(value)) return false;
		for (int index = 0; index < candidates.Length; index++)
		{
			if (string.Equals(value, candidates[index], StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	private static Rectangle[] GetControlBounds(IntPtr window,
		string description, Rectangle searchArea, bool includeHidden)
	{
		List<Rectangle> bounds = new List<Rectangle>();
		object accessibleObject = null;
		Guid interfaceId = typeof(IAccessible).GUID;
		try
		{
			if (AccessibleObjectFromWindow(window, ObjIdClient, ref interfaceId,
				ref accessibleObject) != 0 || !(accessibleObject is IAccessible accessible))
			{
				return bounds.ToArray();
			}
			int visited = 0;
			CollectControlBounds(accessible, description, searchArea, includeHidden, 0,
				ref visited, bounds);
		}
		catch { }
		return bounds.ToArray();
	}

	public static Rectangle[] GetInteractiveBounds(IntPtr window, Rectangle visibleArea)
	{
		return GetInteractiveBounds(window, visibleArea, includeHidden: false);
	}

	public static Rectangle[] GetInteractiveBoundsIncludingHidden(IntPtr window,
		Rectangle searchArea)
	{
		return GetInteractiveBounds(window, searchArea, includeHidden: true);
	}

	private static Rectangle[] GetInteractiveBounds(IntPtr window,
		Rectangle visibleArea, bool includeHidden)
	{
		List<Rectangle> bounds = new List<Rectangle>();
		object accessibleObject = null;
		Guid interfaceId = typeof(IAccessible).GUID;
		try
		{
			if (AccessibleObjectFromWindow(window, ObjIdClient, ref interfaceId, ref accessibleObject) != 0 ||
				!(accessibleObject is IAccessible accessible))
			{
				return bounds.ToArray();
			}
			int visited = 0;
			CollectInteractiveBounds(accessible, visibleArea, includeHidden, 0,
				ref visited, bounds);
		}
		catch
		{
		}
		return bounds.ToArray();
	}

	public static AccessibleTimeLabel[] GetVisibleTimeLabels(IntPtr window,
		Rectangle visibleArea)
	{
		List<AccessibleTimeLabel> labels = new List<AccessibleTimeLabel>();
		object accessibleObject = null;
		Guid interfaceId = typeof(IAccessible).GUID;
		try
		{
			if (AccessibleObjectFromWindow(window, ObjIdClient, ref interfaceId,
				ref accessibleObject) != 0 || !(accessibleObject is IAccessible accessible))
			{
				return labels.ToArray();
			}
			int visited = 0;
			CollectTimeLabels(accessible, visibleArea, 0, ref visited, labels);
		}
		catch { }
		return labels.ToArray();
	}

	private static void CollectTimeLabels(IAccessible accessible, Rectangle visibleArea,
		int depth, ref int visited, List<AccessibleTimeLabel> labels)
	{
		if (depth > MaxDepth || visited++ > MaxNodes) return;
		int childCount;
		try { childCount = accessible.accChildCount; }
		catch { return; }
		for (int index = 1; index <= childCount; index++)
		{
			IAccessible child = null;
			try { child = accessible.get_accChild(index) as IAccessible; }
			catch { }
			IAccessible node = child ?? accessible;
			object childId = child == null ? (object)index : 0;
			if (IsHidden(node, childId)) continue;
			string text = "";
			try { text = node.get_accName(childId) ?? ""; }
			catch { }
			if (string.IsNullOrWhiteSpace(text))
			{
				try { text = node.get_accDescription(childId) ?? ""; }
				catch { }
			}
			if (DesktopInputPolicy.TryParseTimestamp(text, out int seconds))
			{
				try
				{
					node.accLocation(out int left, out int top, out int width,
						out int height, childId);
					Rectangle bounds = Rectangle.Intersect(visibleArea,
						new Rectangle(left, top, width, height));
					if (bounds.Width > 0 && bounds.Height > 0)
						labels.Add(new AccessibleTimeLabel(bounds, seconds));
				}
				catch { }
			}
			if (child != null)
			{
				CollectTimeLabels(child, visibleArea, depth + 1, ref visited, labels);
			}
		}
	}

	private static void CollectInteractiveBounds(IAccessible accessible,
		Rectangle visibleArea, bool includeHidden, int depth, ref int visited,
		List<Rectangle> bounds)
	{
		if (depth > MaxDepth || visited++ > MaxNodes)
		{
			return;
		}
		int childCount;
		try { childCount = accessible.accChildCount; }
		catch { return; }

		for (int i = 1; i <= childCount; i++)
		{
			IAccessible child = null;
			try { child = accessible.get_accChild(i) as IAccessible; }
			catch { }
			IAccessible node = child ?? accessible;
			object childId = child == null ? (object)i : 0;
			if (!includeHidden && IsHidden(node, childId))
			{
				continue;
			}

			try
			{
				int role = Convert.ToInt32(node.get_accRole(childId));
				string action = "";
				try { action = node.get_accDefaultAction(childId) ?? ""; }
				catch { }
				if (InteractiveRoles.Contains(role) || !string.IsNullOrWhiteSpace(action))
				{
					node.accLocation(out int left, out int top, out int width, out int height, childId);
					Rectangle item = Rectangle.Intersect(visibleArea, new Rectangle(left, top, width, height));
					long itemArea = (long)item.Width * item.Height;
					long visibleAreaSize = (long)visibleArea.Width * visibleArea.Height;
					if (item.Width > 1 && item.Height > 1 && itemArea < visibleAreaSize / 3)
					{
						bounds.Add(item);
					}
				}
			}
			catch { }

			if (child != null)
			{
				CollectInteractiveBounds(child, visibleArea, includeHidden, depth + 1,
					ref visited, bounds);
			}
		}
	}

	private static bool TryFindVisibleControl(IAccessible accessible, string description, Rectangle visibleArea, int depth, ref int visited, out Rectangle bounds)
	{
		bounds = Rectangle.Empty;
		if (depth > 16 || visited++ > 4096)
		{
			return false;
		}
		int accChildCount;
		try
		{
			accChildCount = accessible.accChildCount;
		}
		catch
		{
			return false;
		}
		for (int i = 1; i <= accChildCount; i++)
		{
			object obj2 = null;
			try
			{
				obj2 = accessible.get_accChild((object)i);
			}
			catch
			{
			}
			IAccessible accessible2 = obj2 as IAccessible;
			IAccessible accessible3 = accessible2 ?? accessible;
			object obj4 = ((accessible2 == null) ? i : 0);
			if (IsHidden(accessible3, obj4))
			{
				continue;
			}
			try
			{
				if (string.Equals(accessible3.get_accDescription(obj4), description, StringComparison.Ordinal))
				{
					accessible3.accLocation(out var pxLeft, out var pyTop, out var pcxWidth, out var pcyHeight, obj4);
					if (pcxWidth > 0 && pcyHeight > 0)
					{
						Rectangle rectangle = new Rectangle(pxLeft, pyTop, pcxWidth, pcyHeight);
						Point pt = new Point(pxLeft + pcxWidth / 2, pyTop + pcyHeight / 2);
						if (visibleArea.Contains(pt))
						{
							bounds = rectangle;
							return true;
						}
					}
				}
			}
			catch
			{
			}
			if (accessible2 != null && TryFindVisibleControl(accessible2, description, visibleArea, depth + 1, ref visited, out bounds))
			{
				return true;
			}
		}
		return false;
	}

	private static void CollectControlBounds(IAccessible accessible,
		string description, Rectangle searchArea, bool includeHidden, int depth,
		ref int visited, List<Rectangle> bounds)
	{
		if (depth > MaxDepth || visited++ > MaxNodes) return;
		int childCount;
		try { childCount = accessible.accChildCount; }
		catch { return; }
		for (int index = 1; index <= childCount; index++)
		{
			IAccessible child = null;
			try { child = accessible.get_accChild(index) as IAccessible; }
			catch { }
			IAccessible node = child ?? accessible;
			object childId = child == null ? (object)index : 0;
			if (!includeHidden && IsHidden(node, childId)) continue;
			try
			{
				string name = node.get_accName(childId);
				string itemDescription = node.get_accDescription(childId);
				if (string.Equals(name, description, StringComparison.Ordinal) ||
					string.Equals(itemDescription, description, StringComparison.Ordinal))
				{
					node.accLocation(out int left, out int top, out int width,
						out int height, childId);
					if (width > 0 && height > 0 &&
						searchArea.Contains(left + width / 2, top + height / 2))
					{
						bounds.Add(new Rectangle(left, top, width, height));
					}
				}
			}
			catch { }
			if (child != null)
			{
				CollectControlBounds(child, description, searchArea, includeHidden,
					depth + 1, ref visited, bounds);
			}
		}
	}

	private static bool IsHidden(IAccessible accessible, object childId)
	{
		try
		{
			return (Convert.ToInt64(accessible.get_accState(childId)) & 0x18000) != 0;
		}
		catch
		{
			return false;
		}
	}
}
