using System;
using System.Collections.Generic;
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

internal enum TransientDismissalAction
{
	CancelOnly,
	CancelAndEscape
}

internal enum DesktopClickRoute
{
	None,
	SpecialControl,
	LyricTimestamp,
	ProgressControl,
	MediaControl,
	GenericControl
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

internal readonly struct AccessibleTimeLabel
{
	public AccessibleTimeLabel(Rectangle bounds, int seconds)
	{
		Bounds = bounds;
		Seconds = seconds;
	}

	public Rectangle Bounds { get; }

	public int Seconds { get; }
}

internal readonly struct AccessibleNodeSnapshot
{
	public AccessibleNodeSnapshot(string name, string description, string action,
		int role, bool hidden, int depth, Rectangle bounds)
	{
		Name = name ?? "";
		Description = description ?? "";
		Action = action ?? "";
		Role = role;
		Hidden = hidden;
		Depth = depth;
		Bounds = bounds;
	}

	public string Name { get; }
	public string Description { get; }
	public string Action { get; }
	public int Role { get; }
	public bool Hidden { get; }
	public int Depth { get; }
	public Rectangle Bounds { get; }
}

internal sealed class PopupLauncherMap
{
	public readonly IntPtr Source;
	public readonly Rectangle SourceBounds;
	public readonly Rectangle Bounds;
	public readonly AccessibleTreeSnapshot Snapshot;
	public readonly AccessibleNodeSnapshot Target;
	public readonly Point ActionPoint;
	public readonly DesktopSpecialAction Action;

	public PopupLauncherMap(IntPtr source, Rectangle sourceBounds, Rectangle bounds,
		AccessibleTreeSnapshot snapshot, AccessibleNodeSnapshot target, Point actionPoint,
		DesktopSpecialAction action)
	{
		Source = source;
		SourceBounds = sourceBounds;
		Bounds = bounds;
		Snapshot = snapshot;
		Target = target;
		ActionPoint = actionPoint;
		Action = action;
	}
}

internal sealed class AccessibleTreeSnapshot
{
	private static readonly HashSet<int> InteractiveRoles = new HashSet<int>
	{
		12, 30, 43, 44, 45, 46, 47, 51
	};

	private readonly AccessibleNodeSnapshot[] nodes;

	public AccessibleTreeSnapshot(AccessibleNodeSnapshot[] nodes)
	{
		this.nodes = nodes ?? new AccessibleNodeSnapshot[0];
	}

	public bool IsEmpty => nodes.Length == 0;

	public Rectangle[] GetInteractiveBounds(Rectangle searchArea,
		bool includeHidden)
	{
		List<Rectangle> result = new List<Rectangle>();
		long searchAreaSize = (long)searchArea.Width * searchArea.Height;
		for (int index = 0; index < nodes.Length; index++)
		{
			AccessibleNodeSnapshot node = nodes[index];
			if (!includeHidden && node.Hidden) continue;
			if (!InteractiveRoles.Contains(node.Role) &&
				string.IsNullOrWhiteSpace(node.Action)) continue;
			Rectangle item = Rectangle.Intersect(searchArea, node.Bounds);
			long itemArea = (long)item.Width * item.Height;
			if (item.Width > 1 && item.Height > 1 &&
				itemArea < searchAreaSize / 3) result.Add(item);
		}
		return result.ToArray();
	}

	public Rectangle[] GetControlBounds(string description, Rectangle searchArea,
		bool includeHidden)
	{
		List<Rectangle> result = new List<Rectangle>();
		for (int index = 0; index < nodes.Length; index++)
		{
			AccessibleNodeSnapshot node = nodes[index];
			if ((!includeHidden && node.Hidden) ||
				!Matches(node, description)) continue;
			Rectangle item = Rectangle.Intersect(searchArea, node.Bounds);
			if (item.Width > 0 && item.Height > 0 && !result.Contains(item))
				result.Add(item);
		}
		return result.ToArray();
	}

	public bool TryFindVisibleControl(string description, Rectangle searchArea,
		out Rectangle bounds)
	{
		for (int index = 0; index < nodes.Length; index++)
		{
			AccessibleNodeSnapshot node = nodes[index];
			if (node.Hidden || !Matches(node, description) ||
				!searchArea.Contains(Center(node.Bounds))) continue;
			bounds = node.Bounds;
			return true;
		}
		bounds = Rectangle.Empty;
		return false;
	}

	public bool TryGetControlTarget(Rectangle preferredArea,
		out AccessibleNodeSnapshot target, params string[] descriptions)
	{
		int bestScore = int.MinValue;
		long bestArea = long.MaxValue;
		target = default;
		bool found = false;
		for (int index = 0; index < nodes.Length; index++)
		{
			AccessibleNodeSnapshot node = nodes[index];
			if (node.Bounds.Width <= 0 || node.Bounds.Height <= 0) continue;
			int matchScore = GetSemanticMatchScore(node, descriptions);
			if (matchScore == 0) continue;
			int score = matchScore;
			if (!string.IsNullOrWhiteSpace(node.Action)) score += 100;
			if (preferredArea.Contains(Center(node.Bounds))) score += 50;
			if (!node.Hidden) score += 10;
			long area = (long)node.Bounds.Width * node.Bounds.Height;
			if (!found || score > bestScore ||
				score == bestScore && node.Depth > target.Depth ||
				score == bestScore && node.Depth == target.Depth && area < bestArea)
			{
				found = true;
				bestScore = score;
				bestArea = area;
				target = node;
			}
		}
		return found;
	}

	public bool TryGetPopupLauncherTarget(Rectangle preferredArea,
		DesktopSpecialAction action, out AccessibleNodeSnapshot target)
	{
		target = default;
		if (action == DesktopSpecialAction.None) return false;
		int bestScore = int.MinValue;
		bool found = false;
		for (int index = 0; index < nodes.Length; index++)
		{
			AccessibleNodeSnapshot node = nodes[index];
			if (node.Bounds.Width <= 0 || node.Bounds.Height <= 0 ||
				DesktopInputPolicy.GetPopupLauncherAction(string.IsNullOrWhiteSpace(
					node.Description) ? node.Name : node.Description) != action) continue;
			int score = string.IsNullOrWhiteSpace(node.Description) ? 200 : 300;
			if (preferredArea.Contains(Center(node.Bounds))) score += 50;
			if (!node.Hidden) score += 10;
			if (node.Role == 43) score += 20;
			bool overlappingDuplicate = found && node.Bounds.IntersectsWith(target.Bounds);
			// Repeated player bars overlap with a small offset. Within that duplicate
			// group, depth identifies the retained old tree, not the active launcher.
			bool betterTie = overlappingDuplicate && node.Bounds.Bottom != target.Bounds.Bottom
				? node.Bounds.Bottom > target.Bounds.Bottom
				: node.Depth > target.Depth;
			if (!found || score > bestScore || score == bestScore && betterTie)
			{
				found = true;
				bestScore = score;
				target = node;
			}
		}
		return found;
	}

	public bool TryGetUnambiguousControlTarget(Rectangle preferredArea,
		out AccessibleNodeSnapshot target, params string[] descriptions)
	{
		int bestScore = int.MinValue;
		long bestArea = long.MaxValue;
		target = default;
		bool found = false;
		for (int index = 0; index < nodes.Length; index++)
		{
			AccessibleNodeSnapshot node = nodes[index];
			if (node.Bounds.Width <= 0 || node.Bounds.Height <= 0) continue;
			int matchScore = GetSemanticMatchScore(node, descriptions);
			if (matchScore == 0 || HasConflictingSemanticAtSameBounds(index,
				descriptions)) continue;
			int score = matchScore;
			if (!string.IsNullOrWhiteSpace(node.Action)) score += 100;
			if (preferredArea.Contains(Center(node.Bounds))) score += 50;
			if (!node.Hidden) score += 10;
			long area = (long)node.Bounds.Width * node.Bounds.Height;
			if (!found || score > bestScore ||
				score == bestScore && node.Depth > target.Depth ||
				score == bestScore && node.Depth == target.Depth && area < bestArea)
			{
				found = true;
				bestScore = score;
				bestArea = area;
				target = node;
			}
		}
		return found;
	}

	public bool TryGetBottommostControlTarget(Rectangle preferredArea,
		out AccessibleNodeSnapshot target, params string[] descriptions)
	{
		target = default;
		int bestScore = int.MinValue;
		bool found = false;
		for (int index = 0; index < nodes.Length; index++)
		{
			AccessibleNodeSnapshot node = nodes[index];
			if (node.Bounds.Width <= 0 || node.Bounds.Height <= 0 ||
				!preferredArea.Contains(Center(node.Bounds))) continue;
			int score = GetSemanticMatchScore(node, descriptions);
			if (score == 0) continue;
			if (!string.IsNullOrWhiteSpace(node.Action)) score += 100;
			if (!node.Hidden) score += 10;
			if (!found || node.Bounds.Bottom > target.Bounds.Bottom ||
				node.Bounds.Bottom == target.Bounds.Bottom && score > bestScore ||
				node.Bounds.Bottom == target.Bounds.Bottom && score == bestScore &&
					node.Depth > target.Depth)
			{
				found = true;
				bestScore = score;
				target = node;
			}
		}
		return found;
	}

	public bool TryGetControlTargetAtPoint(Point point,
		out AccessibleNodeSnapshot target)
	{
		target = default;
		long bestArea = long.MaxValue;
		bool found = false;
		for (int index = 0; index < nodes.Length; index++)
		{
			AccessibleNodeSnapshot node = nodes[index];
			if ((!InteractiveRoles.Contains(node.Role) &&
				string.IsNullOrWhiteSpace(node.Action)) ||
				!node.Bounds.Contains(point)) continue;
			long area = (long)node.Bounds.Width * node.Bounds.Height;
			if (!found || node.Depth > target.Depth ||
				node.Depth == target.Depth && area < bestArea)
			{
				found = true;
				bestArea = area;
				target = node;
			}
		}
		return found;
	}

	public AccessibleTimeLabel[] GetTimeLabels(Rectangle activeSurface,
		bool includeHidden)
	{
		List<AccessibleTimeLabel> result = new List<AccessibleTimeLabel>();
		for (int index = 0; index < nodes.Length; index++)
		{
			AccessibleNodeSnapshot node = nodes[index];
			if ((!includeHidden && node.Hidden) ||
				!activeSurface.Contains(Center(node.Bounds))) continue;
			string text = string.IsNullOrWhiteSpace(node.Name)
				? node.Description : node.Name;
			if (DesktopInputPolicy.TryParseTimestamp(text, out int seconds))
				result.Add(new AccessibleTimeLabel(node.Bounds, seconds));
		}
		return result.ToArray();
	}

	private static int GetSemanticMatchScore(AccessibleNodeSnapshot node,
		string[] descriptions)
	{
		int score = 0;
		for (int index = 0; index < descriptions.Length; index++)
		{
			string candidate = descriptions[index];
			if (string.Equals(node.Description, candidate,
				StringComparison.Ordinal)) score = Math.Max(score, 300);
			if (string.Equals(node.Name, candidate,
				StringComparison.Ordinal)) score = Math.Max(score, 200);
		}
		return score;
	}

	private bool HasConflictingSemanticAtSameBounds(int nodeIndex,
		string[] descriptions)
	{
		AccessibleNodeSnapshot node = nodes[nodeIndex];
		string semantic = GetSemanticLabel(node);
		for (int index = 0; index < nodes.Length; index++)
		{
			if (index == nodeIndex || nodes[index].Bounds != node.Bounds ||
				GetSemanticMatchScore(nodes[index], descriptions) == 0) continue;
			if (!string.Equals(semantic, GetSemanticLabel(nodes[index]),
				StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static string GetSemanticLabel(AccessibleNodeSnapshot node)
	{
		return string.IsNullOrWhiteSpace(node.Description) ? node.Name :
			node.Description;
	}

	private static bool Matches(AccessibleNodeSnapshot node, string description)
	{
		return string.Equals(node.Name, description, StringComparison.Ordinal) ||
			string.Equals(node.Description, description, StringComparison.Ordinal);
	}

	private static Point Center(Rectangle bounds)
	{
		return new Point(bounds.Left + bounds.Width / 2,
			bounds.Top + bounds.Height / 2);
	}
}

internal readonly struct AccessibleControlSurface
{
	public AccessibleControlSurface(IntPtr window, Rectangle bounds,
		AccessibleTreeSnapshot controls)
	{
		Window = window;
		Bounds = bounds;
		Controls = controls ?? new AccessibleTreeSnapshot(
			new AccessibleNodeSnapshot[0]);
	}

	public IntPtr Window { get; }
	public Rectangle Bounds { get; }
	public AccessibleTreeSnapshot Controls { get; }
}

internal static class AccessibleSurfaceRouter
{
	public static bool TryGetWheelTarget(Point point,
		AccessibleControlSurface[] surfaces, out IntPtr window)
	{
		window = IntPtr.Zero;
		if (!TryGetSurfaceAtPoint(point, surfaces,
			out AccessibleControlSurface surface)) return false;
		window = surface.Window;
		return window != IntPtr.Zero;
	}

	public static bool TryGetControlTargetAtPoint(Point point,
		AccessibleControlSurface[] surfaces, out IntPtr window,
		out AccessibleNodeSnapshot target)
	{
		window = IntPtr.Zero;
		target = default;
		long bestSurfaceArea = long.MaxValue;
		bool found = false;
		for (int index = 0; index < surfaces.Length; index++)
		{
			AccessibleControlSurface surface = surfaces[index];
			if (!surface.Bounds.Contains(point) ||
				!surface.Controls.TryGetControlTargetAtPoint(point,
					out AccessibleNodeSnapshot candidate)) continue;
			long area = (long)surface.Bounds.Width * surface.Bounds.Height;
			if (found && area >= bestSurfaceArea) continue;
			found = true;
			bestSurfaceArea = area;
			window = surface.Window;
			target = candidate;
		}
		return found;
	}

	public static bool TryGetSurfaceAtPoint(Point point,
		AccessibleControlSurface[] surfaces, out AccessibleControlSurface surface)
	{
		surface = default;
		long bestArea = long.MaxValue;
		bool found = false;
		for (int index = 0; index < surfaces.Length; index++)
		{
			AccessibleControlSurface candidate = surfaces[index];
			if (!candidate.Bounds.Contains(point)) continue;
			long area = (long)candidate.Bounds.Width * candidate.Bounds.Height;
			if (found && area >= bestArea) continue;
			found = true;
			bestArea = area;
			surface = candidate;
		}
		return found;
	}
}

internal static class DesktopInputPolicy
{
	public static DesktopSpecialAction GetPopupLauncherAction(string semantic)
	{
		switch (semantic)
		{
		case "伴唱": case "伴奏": case "Accompaniment":
			return DesktopSpecialAction.ClickAccompaniment;
		case "播放器样式": case "Player style":
			return DesktopSpecialAction.ClickPlayerStyle;
		case "切换品质": case "Change quality":
			return DesktopSpecialAction.ClickQuality;
		case "音效": case "Sound effects":
			return DesktopSpecialAction.ClickSoundEffects;
		default: return DesktopSpecialAction.None;
		}
	}

	public const int ControlActivationHoldMilliseconds = 80;
	public const int AccessibilityRefreshIntervalMilliseconds = 5000;

	public static bool ShouldRefreshAccessibility(bool hasVisiblePopup)
	{
		return !hasVisiblePopup;
	}

	public static uint[] GetControlActivationMessages()
	{
		return new[] { 512u, 513u, 514u };
	}

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

	public static bool ShouldDismissTransientUi(uint message, bool isDesktopSurface,
		bool isDesktopIcon)
	{
		return message == 513u && isDesktopSurface && !isDesktopIcon;
	}

	public static DesktopClickRoute ResolveDesktopClickRoute(
		DesktopSpecialAction specialAction, bool hasMediaControl,
		bool hasLyricTimestamp, bool hasProgressControl, bool hasGenericControl)
	{
		if (specialAction != DesktopSpecialAction.None)
			return DesktopClickRoute.SpecialControl;
		if (hasLyricTimestamp) return DesktopClickRoute.LyricTimestamp;
		if (hasProgressControl) return DesktopClickRoute.ProgressControl;
		if (hasMediaControl) return DesktopClickRoute.MediaControl;
		if (hasGenericControl) return DesktopClickRoute.GenericControl;
		return DesktopClickRoute.None;
	}

	public static bool ShouldCheckDesktopIcon(uint message)
	{
		return message == 513u || message == 516u;
	}

	public static TransientDismissalAction ResolveTransientDismissalAction(
		bool isMainSurface)
	{
		return isMainSurface ? TransientDismissalAction.CancelOnly :
			TransientDismissalAction.CancelAndEscape;
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

	public static Rectangle GetVisiblePopupBounds(Rectangle popupWindow,
		Rectangle sourceWindow, Rectangle workingArea)
	{
		int width = Math.Min(Math.Max(1, popupWindow.Width), workingArea.Width);
		int height = Math.Min(Math.Max(1, popupWindow.Height), workingArea.Height);
		int x = popupWindow.Left;
		int y = popupWindow.Top;
		if (!workingArea.IntersectsWith(popupWindow))
		{
			x += workingArea.Left - sourceWindow.Left;
			y += workingArea.Top - sourceWindow.Top;
		}
		x = Math.Max(workingArea.Left, Math.Min(x, workingArea.Right - width));
		y = Math.Max(workingArea.Top, Math.Min(y, workingArea.Bottom - height));
		return new Rectangle(x, y, width, height);
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

	public static bool TryGetMediaKey(Point point, MediaControlArea[] controls,
		Rectangle[] progressAreas, out byte key)
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
