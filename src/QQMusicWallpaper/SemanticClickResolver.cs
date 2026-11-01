using System;
using System.Drawing;

namespace QQMusicWallpaper;

internal static class SemanticClickResolver
{
	public const long MaximumRequestAgeTicks = 750 * TimeSpan.TicksPerMillisecond;

	public static bool TryGetCommentAtPoint(AccessibleTreeSnapshot snapshot,
		Rectangle sourceBounds, Point sourcePoint, out AccessibleNodeSnapshot target)
	{
		target = default;
		if (snapshot == null || !sourceBounds.Contains(sourcePoint) ||
			!snapshot.TryGetBottommostControlTarget(sourceBounds, out target,
				"评论", "Comments")) return false;
		// A generic click may discover the active comment launcher before the
		// specialized hit map does. Do not promote clicks on a stale duplicate
		// player bar or on unrelated content elsewhere in the source window.
		if (target.Bounds.Contains(sourcePoint)) return true;
		target = default;
		return false;
	}

	public static bool IsRequestCurrent(long requestId, long latestRequestId,
		long elapsedTicks, IntPtr requestedSource, IntPtr currentSource,
		Rectangle requestedBounds, Rectangle currentBounds)
	{
		return requestId > 0 && requestId == latestRequestId &&
			elapsedTicks >= 0 && elapsedTicks <= MaximumRequestAgeTicks &&
			requestedSource != IntPtr.Zero && requestedSource == currentSource &&
			requestedBounds.Width > 0 && requestedBounds.Height > 0 &&
			requestedBounds == currentBounds;
	}
}
