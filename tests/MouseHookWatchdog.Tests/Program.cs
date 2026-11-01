using System;
using QQMusicWallpaper;

internal static class Program
{
	private static int failures;
	private static int assertions;

	private static int Main()
	{
		long stale = TimeSpan.FromSeconds(3).Ticks;
		Check("movement beyond the last callback still recovers a callback gap",
			true, MouseHookWatchdog.ShouldRestart(true, true, false, stale));
		Check("a recent callback does not trigger recovery",
			false, MouseHookWatchdog.ShouldRestart(true, true, false,
				TimeSpan.FromSeconds(1).Ticks));
		Check("the timeout boundary does not trigger recovery",
			false, MouseHookWatchdog.ShouldRestart(true, true, false,
				MouseHookWatchdog.CallbackTimeoutTicks));
		Check("an unchanged poll position does not trigger recovery",
			false, MouseHookWatchdog.ShouldRestart(false, true, false, stale));
		Check("an unknown callback position preserves initial recovery",
			true, MouseHookWatchdog.ShouldRestart(true, false, false, stale));
		Check("unknown position cannot accidentally suppress initial recovery",
			true, MouseHookWatchdog.ShouldRestart(true, false, true, stale));
		Check("a backwards clock reading does not trigger recovery",
			false, MouseHookWatchdog.ShouldRestart(true, true, false, -stale));
		// The UI timer can be delayed while the hook thread receives a move. If
		// the cursor has since stopped, its old event time is not a missed event.
		Check("a delayed poll of an already delivered cursor position stays healthy",
			false, MouseHookWatchdog.ShouldRestart(true, true, true, stale));
		Console.WriteLine(assertions + " assertions; " + failures + " failures.");
		return failures == 0 ? 0 : 1;
	}

	private static void Check(string description, bool expected, bool actual)
	{
		assertions++;
		if (actual == expected)
		{
			Console.WriteLine("PASS: " + description);
			return;
		}
		failures++;
		Console.WriteLine("FAIL: " + description + "; expected=" + expected +
			"; actual=" + actual);
	}
}
