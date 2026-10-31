using System;
using System.Drawing;
using System.Globalization;

namespace QQMusicWallpaper;

internal enum DesktopSpecialAction
{
	None,
	ShowVolume,
	ShowPlaybackModes,
	ShowNativeComments
}

internal enum ProgressPointerAction
{
	None,
	ClickAndBegin,
	Click,
	ClickAndEnd
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

internal readonly struct TimestampControlArea
{
	public TimestampControlArea(Rectangle bounds, int seconds)
	{
		Bounds = bounds;
		Seconds = seconds;
	}

	public Rectangle Bounds { get; }

	public int Seconds { get; }
}

internal static class DesktopInputPolicy
{
	public static ProgressPointerAction ResolveProgressPointerAction(uint message,
		bool progressInteractionActive)
	{
		if (message == 513u) return ProgressPointerAction.ClickAndBegin;
		if (progressInteractionActive && message == 512u)
			return ProgressPointerAction.Click;
		if (progressInteractionActive && message == 514u)
			return ProgressPointerAction.ClickAndEnd;
		return ProgressPointerAction.None;
	}

	public static bool TryParseTimestamp(string text, out int seconds)
	{
		seconds = 0;
		if (string.IsNullOrWhiteSpace(text)) return false;
		string[] parts = text.Trim().Split(':');
		if (parts.Length != 2 && parts.Length != 3) return false;
		int first;
		int second;
		if (!int.TryParse(parts[parts.Length - 2], NumberStyles.None,
			CultureInfo.InvariantCulture, out first) ||
			!int.TryParse(parts[parts.Length - 1], NumberStyles.None,
				CultureInfo.InvariantCulture, out second) ||
			first < 0 || second < 0 || second > 59)
		{
			return false;
		}
		if (parts.Length == 2)
		{
			long total = (long)first * 60 + second;
			if (total > int.MaxValue) return false;
			seconds = (int)total;
			return true;
		}
		int hours;
		if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
			out hours) || hours < 0 || first > 59) return false;
		long duration = (long)hours * 3600 + first * 60 + second;
		if (duration > int.MaxValue) return false;
		seconds = (int)duration;
		return true;
	}

	public static bool TryBuildTimeline(Rectangle currentBounds, int currentSeconds,
		Rectangle totalBounds, int totalSeconds, int windowBottom, out Rectangle timeline)
	{
		timeline = Rectangle.Empty;
		if (currentSeconds < 0 || totalSeconds <= 0 || currentSeconds > totalSeconds ||
			currentBounds.Width <= 0 || totalBounds.Width <= 0 ||
			currentBounds.Top < windowBottom - 120 ||
			totalBounds.Top < windowBottom - 120)
		{
			return false;
		}
		int currentCenterY = currentBounds.Top + currentBounds.Height / 2;
		int totalCenterY = totalBounds.Top + totalBounds.Height / 2;
		if (Math.Abs(currentCenterY - totalCenterY) > 6 ||
			totalBounds.Left <= currentBounds.Right + 24) return false;
		int left = currentBounds.Right + 8;
		int right = totalBounds.Left - 8;
		if (right - left < 120) return false;
		int centerY = (currentCenterY + totalCenterY) / 2;
		timeline = new Rectangle(left, centerY - 9, right - left, 18);
		return true;
	}

	public static bool ShouldKeepNativeSurfacePresented(bool sourcePresented,
		bool foregroundIsQQMusic, DateTime now, DateTime presentationGraceUntil)
	{
		return sourcePresented && (foregroundIsQQMusic || now <= presentationGraceUntil);
	}

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
