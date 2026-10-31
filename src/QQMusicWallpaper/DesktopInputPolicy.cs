using System;
using System.Drawing;
using System.Globalization;

namespace QQMusicWallpaper;

internal enum DesktopSpecialAction
{
	None,
	ShowVolume,
	ShowPlaybackModes,
	ShowNativeComments,
	ClickLyricTranslation,
	ClickAccompaniment,
	ClickPlayerStyle,
	ClickQuality,
	ClickSoundEffects,
	ToggleNativePlaylist
}

internal enum ProgressPointerAction
{
	None,
	BeginTracking,
	UpdatePendingPosition,
	CommitSeek
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
		if (message == 513u) return ProgressPointerAction.BeginTracking;
		if (progressInteractionActive && message == 512u)
			return ProgressPointerAction.UpdatePendingPosition;
		if (progressInteractionActive && message == 514u)
			return ProgressPointerAction.CommitSeek;
		return ProgressPointerAction.None;
	}

	public static bool ShouldConsumeProgressPointerEvent(uint message)
	{
		return message != 512u;
	}

	public static bool ShouldDismissComments(uint message, bool isDesktopSurface,
		bool isDesktopIcon)
	{
		return message == 513u && isDesktopSurface && !isDesktopIcon;
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
		bool foregroundIsQQMusic)
	{
		return sourcePresented && foregroundIsQQMusic;
	}

	public static Rectangle GetVisibleCommentBounds(Rectangle commentWindow,
		Rectangle workingArea)
	{
		const int margin = 12;
		int width = Math.Min(Math.Max(1, commentWindow.Width),
			Math.Max(1, workingArea.Width - margin * 2));
		int height = Math.Min(Math.Max(1, commentWindow.Height),
			Math.Max(1, workingArea.Height - margin * 2));
		return new Rectangle(workingArea.Right - width - margin,
			workingArea.Top + (workingArea.Height - height) / 2, width, height);
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

	public static bool TryInferOffscreenControlCorrection(Rectangle[] candidates,
		Rectangle visibleArea, out Point correction)
	{
		correction = Point.Empty;
		int bestDistance = int.MaxValue;
		for (int visibleIndex = 0; visibleIndex < candidates.Length; visibleIndex++)
		{
			Rectangle visible = candidates[visibleIndex];
			if (!visibleArea.Contains(visible.Left + visible.Width / 2,
				visible.Top + visible.Height / 2)) continue;
			for (int staleIndex = 0; staleIndex < candidates.Length; staleIndex++)
			{
				Rectangle stale = candidates[staleIndex];
				if (stale.Top < visibleArea.Bottom || stale.Width != visible.Width ||
					stale.Height != visible.Height) continue;
				Point candidate = new Point(visible.Left - stale.Left,
					visible.Top - stale.Top);
				int distance = Math.Abs(candidate.X) + Math.Abs(candidate.Y);
				if (distance == 0 || distance >= bestDistance) continue;
				bestDistance = distance;
				correction = candidate;
			}
		}
		return bestDistance != int.MaxValue;
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
		bool isPlaybackMode, bool isComment, bool isLyricTranslation,
		bool isAccompaniment, bool isPlayerStyle, bool isQuality,
		bool isSoundEffects, bool isPlaylist)
	{
		if (isVolume) return DesktopSpecialAction.ShowVolume;
		if (isPlaybackMode) return DesktopSpecialAction.ShowPlaybackModes;
		if (isComment) return DesktopSpecialAction.ShowNativeComments;
		if (isLyricTranslation) return DesktopSpecialAction.ClickLyricTranslation;
		if (isAccompaniment) return DesktopSpecialAction.ClickAccompaniment;
		if (isPlayerStyle) return DesktopSpecialAction.ClickPlayerStyle;
		if (isQuality) return DesktopSpecialAction.ClickQuality;
		if (isSoundEffects) return DesktopSpecialAction.ClickSoundEffects;
		if (isPlaylist) return DesktopSpecialAction.ToggleNativePlaylist;
		return DesktopSpecialAction.None;
	}
}
