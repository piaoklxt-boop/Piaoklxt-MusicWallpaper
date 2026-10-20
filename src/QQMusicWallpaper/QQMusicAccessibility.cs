using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Accessibility;

namespace QQMusicWallpaper;

internal static class QQMusicAccessibility
{
	private const uint ObjIdClient = 4294967292u;

	private const int MaxDepth = 16;

	private const int MaxNodes = 4096;

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

	public static bool TryInvokeVisibleControl(IntPtr window, string description,
		Rectangle visibleArea)
	{
		object accessibleObject = null;
		Guid interfaceId = typeof(IAccessible).GUID;
		try
		{
			if (AccessibleObjectFromWindow(window, ObjIdClient, ref interfaceId,
				ref accessibleObject) != 0 || !(accessibleObject is IAccessible accessible))
			{
				return false;
			}
			int visited = 0;
			return TryInvokeVisibleControl(accessible, description, visibleArea, 0,
				ref visited);
		}
		catch
		{
			return false;
		}
	}

	public static Rectangle[] GetVisibleControlBounds(IntPtr window,
		string description, Rectangle visibleArea)
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
			CollectVisibleControlBounds(accessible, description, visibleArea, 0,
				ref visited, bounds);
		}
		catch { }
		return bounds.ToArray();
	}

	public static Rectangle[] GetInteractiveBounds(IntPtr window, Rectangle visibleArea)
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
			CollectInteractiveBounds(accessible, visibleArea, 0, ref visited, bounds);
		}
		catch
		{
		}
		return bounds.ToArray();
	}

	private static void CollectInteractiveBounds(IAccessible accessible, Rectangle visibleArea,
		int depth, ref int visited, List<Rectangle> bounds)
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
			if (IsHidden(node, childId))
			{
				continue;
			}

			try
			{
				int role = Convert.ToInt32(node.get_accRole(childId));
				string action = node.get_accDefaultAction(childId);
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
				CollectInteractiveBounds(child, visibleArea, depth + 1, ref visited, bounds);
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

	private static void CollectVisibleControlBounds(IAccessible accessible,
		string description, Rectangle visibleArea, int depth, ref int visited,
		List<Rectangle> bounds)
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
						visibleArea.Contains(left + width / 2, top + height / 2))
					{
						bounds.Add(new Rectangle(left, top, width, height));
					}
				}
			}
			catch { }
			if (child != null)
			{
				CollectVisibleControlBounds(child, description, visibleArea,
					depth + 1, ref visited, bounds);
			}
		}
	}

	private static bool TryInvokeVisibleControl(IAccessible accessible,
		string description, Rectangle visibleArea, int depth, ref int visited)
	{
		if (depth > MaxDepth || visited++ > MaxNodes) return false;
		int childCount;
		try { childCount = accessible.accChildCount; }
		catch { return false; }
		for (int index = 1; index <= childCount; index++)
		{
			IAccessible child = null;
			try { child = accessible.get_accChild(index) as IAccessible; }
			catch { }
			IAccessible node = child ?? accessible;
			object childId = child == null ? (object)index : 0;
			if (IsHidden(node, childId)) continue;
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
						visibleArea.Contains(left + width / 2, top + height / 2))
					{
						node.accDoDefaultAction(childId);
						return true;
					}
				}
			}
			catch { }
			if (child != null && TryInvokeVisibleControl(child, description,
				visibleArea, depth + 1, ref visited)) return true;
		}
		return false;
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
