using System;
using System.Drawing;
using QQMusicWallpaper;

internal static class Program
{
	private static int failures;

	private static void Main()
	{
		Rectangle work = new Rectangle(0, 0, 1920, 1040);
		Rectangle progress = new Rectangle(360, 994, 1200, 12);
		Point progressCenter = new Point(960, 1000);

		AssertFalse("progress does not trigger play/pause",
			DesktopInputPolicy.TryGetMediaKey(progressCenter, work,
				Array.Empty<MediaControlArea>(), new[] { progress }, out _));

		MediaControlArea play = new MediaControlArea(
			new Rectangle(938, 970, 44, 36), 179);
		AssertTrue("discovered play control is used",
			DesktopInputPolicy.TryGetMediaKey(new Point(960, 980), work,
				new[] { play }, Array.Empty<Rectangle>(), out byte key) && key == 179);
		AssertFalse("discovered play control has no oversized fallback",
			DesktopInputPolicy.TryGetMediaKey(new Point(960, 1015), work,
				new[] { play }, Array.Empty<Rectangle>(), out _));

		AssertFalse("player fallback is limited to the real bottom control strip",
			DesktopInputPolicy.IsPlayerFallbackPoint(new Point(1000, work.Bottom - 100), work));

		AssertEqual("comment opens native QQ Music",
			DesktopSpecialAction.ShowNativeComments,
			DesktopInputPolicy.ResolveSpecialAction(false, false, true));

		if (failures != 0)
		{
			Console.Error.WriteLine($"Desktop input policy: {failures} failure(s).");
			Environment.Exit(1);
		}
		Console.WriteLine("Desktop input policy: all regression checks passed.");
	}

	private static void AssertTrue(string name, bool value)
	{
		if (value) return;
		failures++;
		Console.Error.WriteLine("FAIL: " + name);
	}

	private static void AssertFalse(string name, bool value)
	{
		AssertTrue(name, !value);
	}

	private static void AssertEqual<T>(string name, T expected, T actual)
	{
		AssertTrue(name, Equals(expected, actual));
	}
}
