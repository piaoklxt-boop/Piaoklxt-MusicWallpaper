using System;

namespace QQMusicWallpaper;

internal static class MouseHookWatchdog
{
	public const long CallbackTimeoutTicks = 2 * TimeSpan.TicksPerSecond;

	public static bool ShouldRestart(bool cursorMovedSincePoll,
		bool hasCallbackPoint, bool cursorMatchesLastCallback, long callbackAgeTicks)
	{
		if (!cursorMovedSincePoll || callbackAgeTicks <= CallbackTimeoutTicks)
			return false;
		// A delayed maintenance tick can observe old movement after the hook
		// already delivered it. Only an unobserved cursor position is evidence
		// of a callback gap; an unknown initial position retains recovery.
		return !hasCallbackPoint || !cursorMatchesLastCallback;
	}
}
