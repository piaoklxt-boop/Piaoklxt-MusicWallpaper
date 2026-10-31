using System;
using System.Drawing;

namespace QQMusicWallpaper;

internal enum DesktopSpecialAction
{
	None,
	ShowVolume,
	ShowPlaybackModes,
	ShowNativeComments
}

internal readonly struct MediaControlArea
{
	public MediaControlArea(Rectangle bounds, byte virtualKey)
	{
		Bounds = bounds;
		VirtualKey = virtualKey;
	}

	public Rectangle Bounds { get; }

	public byte VirtualKey { get; }
}

internal static class DesktopInputPolicy
{
	public static bool IsPlayerFallbackPoint(Point point, Rectangle work)
	{
		return point.Y >= work.Bottom - 84;
	}

	public static bool IsPointInAny(Point point, Rectangle[] areas)
	{
		for (int index = 0; index < areas.Length; index++)
		{
			if (areas[index].Contains(point)) return true;
		}
		return false;
	}

	public static bool TryGetMediaKey(Point point, Rectangle work,
		MediaControlArea[] controls, Rectangle[] progressAreas, out byte key)
	{
		if (IsPointInAny(point, progressAreas))
		{
			key = 0;
			return false;
		}
		if (controls.Length > 0)
		{
			for (int index = 0; index < controls.Length; index++)
			{
				if (!controls[index].Bounds.Contains(point)) continue;
				key = controls[index].VirtualKey;
				return true;
			}
		}

		// Last-resort geometry for QQ Music builds which expose no media buttons.
		// Keep it deliberately smaller than the visible button so it cannot reach
		// the progress bar above or below the control row.
		int centerY = work.Bottom - 48;
		if (Math.Abs(point.Y - centerY) > 14)
		{
			key = 0;
			return false;
		}
		int centerX = work.Left + work.Width / 2;
		int offsetX = point.X - centerX;
		if (Math.Abs(offsetX) <= 20)
		{
			key = 179;
			return true;
		}
		if (Math.Abs(offsetX + 52) <= 18)
		{
			key = 177;
			return true;
		}
		if (Math.Abs(offsetX - 52) <= 18)
		{
			key = 176;
			return true;
		}
		key = 0;
		return false;
	}

	public static DesktopSpecialAction ResolveSpecialAction(bool isVolume,
		bool isPlaybackMode, bool isComment)
	{
		if (isVolume) return DesktopSpecialAction.ShowVolume;
		if (isPlaybackMode) return DesktopSpecialAction.ShowPlaybackModes;
		if (isComment) return DesktopSpecialAction.ShowNativeComments;
		return DesktopSpecialAction.None;
	}
}
