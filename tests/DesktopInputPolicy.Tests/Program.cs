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
			DesktopInputPolicy.TryGetMediaKey(progressCenter,
				Array.Empty<MediaControlArea>(), new[] { progress }, out _));

		MediaControlArea play = new MediaControlArea(
			new Rectangle(938, 970, 44, 36), 179);
		AssertTrue("discovered play control is used",
			DesktopInputPolicy.TryGetMediaKey(new Point(960, 980),
				new[] { play }, Array.Empty<Rectangle>(), out byte key) && key == 179);
		AssertFalse("discovered play control has no oversized fallback",
			DesktopInputPolicy.TryGetMediaKey(new Point(960, 1015),
				new[] { play }, Array.Empty<Rectangle>(), out _));

		AssertFalse("undiscovered player controls have no coordinate fallback",
			DesktopInputPolicy.TryGetMediaKey(new Point(960, 980),
				Array.Empty<MediaControlArea>(), Array.Empty<Rectangle>(), out _));

		AssertEqual("comment opens native QQ Music",
			DesktopSpecialAction.ShowNativeComments,
			DesktopInputPolicy.ResolveSpecialAction(false, false, true,
				false, false, false, false, false, false));
		AssertEqual("translation uses an atomic control click",
			DesktopSpecialAction.ClickLyricTranslation,
			DesktopInputPolicy.ResolveSpecialAction(false, false, false,
				true, false, false, false, false, false));
		AssertEqual("accompaniment uses an atomic control click",
			DesktopSpecialAction.ClickAccompaniment,
			DesktopInputPolicy.ResolveSpecialAction(false, false, false,
				false, true, false, false, false, false));
		AssertEqual("player style uses an atomic control click",
			DesktopSpecialAction.ClickPlayerStyle,
			DesktopInputPolicy.ResolveSpecialAction(false, false, false,
				false, false, true, false, false, false));
		AssertEqual("quality uses an atomic control click",
			DesktopSpecialAction.ClickQuality,
			DesktopInputPolicy.ResolveSpecialAction(false, false, false,
				false, false, false, true, false, false));
		AssertEqual("sound effects use an atomic control click",
			DesktopSpecialAction.ClickSoundEffects,
			DesktopInputPolicy.ResolveSpecialAction(false, false, false,
				false, false, false, false, true, false));
		AssertEqual("playlist uses the native popup lifecycle",
			DesktopSpecialAction.ToggleNativePlaylist,
			DesktopInputPolicy.ResolveSpecialAction(false, false, false,
				false, false, false, false, false, true));
		AssertEqual("volume wins when its bounds overlap a media control",
			DesktopClickRoute.SpecialControl,
			DesktopInputPolicy.ResolveDesktopClickRoute(
				DesktopSpecialAction.ShowVolume, hasMediaControl: true,
				hasLyricTimestamp: false, hasProgressControl: false,
				hasGenericControl: true));
		AssertEqual("playback mode wins when its bounds overlap a media control",
			DesktopClickRoute.SpecialControl,
			DesktopInputPolicy.ResolveDesktopClickRoute(
				DesktopSpecialAction.ShowPlaybackModes, hasMediaControl: true,
				hasLyricTimestamp: false, hasProgressControl: false,
				hasGenericControl: true));

		Rectangle goodQualityBounds = new Rectangle(1719, 969, 39, 24);
		Rectangle staleQualityBounds = new Rectangle(1731, 1058, 39, 24);
		AssertTrue("duplicate QQ bounds reveal the stale non-client offset",
			DesktopInputPolicy.TryInferOffscreenControlCorrection(
				new[] { goodQualityBounds, staleQualityBounds }, work,
				out Point correction) && correction == new Point(-12, -89));

		AssertEqual("progress down starts local tracking",
			ProgressPointerAction.BeginTracking,
			DesktopInputPolicy.ResolveProgressPointerAction(513u, false));
		AssertEqual("progress held move updates the pending point",
			ProgressPointerAction.UpdatePendingPosition,
			DesktopInputPolicy.ResolveProgressPointerAction(512u, true));
		AssertEqual("progress up commits one seek",
			ProgressPointerAction.CommitSeek,
			DesktopInputPolicy.ResolveProgressPointerAction(514u, true));
		AssertFalse("progress move remains available to the system cursor",
			DesktopInputPolicy.ShouldConsumeProgressPointerEvent(512u));
		AssertTrue("progress down stays away from Explorer",
			DesktopInputPolicy.ShouldConsumeProgressPointerEvent(513u));
		AssertTrue("progress up stays away from Explorer",
			DesktopInputPolicy.ShouldConsumeProgressPointerEvent(514u));
		AssertTrue("blank desktop click dismisses transient QQ UI",
			DesktopInputPolicy.ShouldDismissTransientUi(513u, true, false));
		AssertFalse("desktop icon click does not dismiss transient QQ UI",
			DesktopInputPolicy.ShouldDismissTransientUi(513u, true, true));
		AssertTrue("desktop icon lookup is limited to press events",
			DesktopInputPolicy.ShouldCheckDesktopIcon(513u));
		AssertFalse("button release avoids a cross-process desktop icon lookup",
			DesktopInputPolicy.ShouldCheckDesktopIcon(514u));
		AssertFalse("pointer movement avoids a cross-process desktop icon lookup",
			DesktopInputPolicy.ShouldCheckDesktopIcon(512u));
		AssertEqual("QQ main surface cancellation never navigates back",
			TransientDismissalAction.CancelOnly,
			DesktopInputPolicy.ResolveTransientDismissalAction(isMainSurface: true));
		AssertEqual("independent QQ popup cancellation may use Escape",
			TransientDismissalAction.CancelAndEscape,
			DesktopInputPolicy.ResolveTransientDismissalAction(isMainSurface: false));
		uint[] activationMessages = DesktopInputPolicy.GetControlActivationMessages();
		AssertTrue("control activation preserves hover, press, and release order",
			activationMessages.Length == 3 && activationMessages[0] == 512u &&
			activationMessages[1] == 513u && activationMessages[2] == 514u);
		AssertTrue("menu controls retain a real press interval",
			DesktopInputPolicy.ControlActivationHoldMilliseconds >= 50);

		Rectangle commentWindow = new Rectangle(-490, 12, 478, 943);
		Rectangle commentWork = new Rectangle(0, 0, 1920, 1032);
		AssertEqual("comment window is restored inside the working area",
			new Rectangle(1430, 44, 478, 943),
			DesktopInputPolicy.GetVisibleCommentBounds(commentWindow, commentWork));
		AssertEqual("offscreen QQ popup follows the source window onto the desktop",
			new Rectangle(1422, 508, 432, 478),
			DesktopInputPolicy.GetVisiblePopupBounds(
				new Rectangle(-654, 508, 432, 478),
				new Rectangle(-2076, 0, 1920, 1032), commentWork));

		AssertTrue("lyric timestamp is parsed",
			DesktopInputPolicy.TryParseTimestamp(" 03:32 ", out int lyricSeconds) &&
			lyricSeconds == 212);
		AssertFalse("overflowing timestamp is rejected",
			DesktopInputPolicy.TryParseTimestamp("999999999:59", out _));
		AssertTrue("bottom time labels define the real timeline",
			DesktopInputPolicy.TryBuildTimeline(new Rectangle(557, 1007, 50, 14), 78,
				new Rectangle(1313, 1007, 50, 14), 244, 1032,
				out Rectangle timeline) &&
			timeline == new Rectangle(615, 1005, 690, 18));

		AssertFalse("comment launch never keeps the main player over the desktop",
			DesktopInputPolicy.ShouldKeepNativeSurfacePresented(true, false));

		AccessibleNodeSnapshot pageComment = new AccessibleNodeSnapshot(
			"评论", "", "", 43, false, 8,
			new Rectangle(120, 180, 30, 38));
		AccessibleNodeSnapshot playerComment = new AccessibleNodeSnapshot(
			"评论999+", "评论", "", 43, false, 6,
			new Rectangle(120, 970, 51, 35));
		AccessibleNodeSnapshot staleComment = new AccessibleNodeSnapshot(
			"评论999+", "评论", "", 43, false, 5,
			new Rectangle(120, 1060, 51, 35));
		AccessibleTreeSnapshot controlSnapshot = new AccessibleTreeSnapshot(
			new[] { pageComment, playerComment, staleComment });
		AssertTrue("semantic description selects the player comment control",
			controlSnapshot.TryGetControlTarget(work, out AccessibleNodeSnapshot commentTarget,
				"评论", "Comments") && commentTarget.Name == "评论999+" &&
				commentTarget.Bounds == playerComment.Bounds);

		AccessibleTreeSnapshot timelineSnapshot = new AccessibleTreeSnapshot(new[]
		{
			new AccessibleNodeSnapshot("01:18", "", "", 41, false, 6,
				new Rectangle(557, 1007, 50, 14)),
			new AccessibleNodeSnapshot("04:04", "", "", 41, false, 6,
				new Rectangle(1313, 1007, 50, 14)),
			new AccessibleNodeSnapshot("01:18", "", "", 41, false, 4,
				new Rectangle(557, 1084, 50, 14)),
			new AccessibleNodeSnapshot("04:04", "", "", 41, false, 4,
				new Rectangle(1313, 1084, 50, 14))
		});
		AccessibleTimeLabel[] activeTimes = timelineSnapshot.GetTimeLabels(work,
			includeHidden: true);
		AssertTrue("only the active player control group supplies timeline labels",
			activeTimes.Length == 2 && activeTimes[0].Bounds.Top < work.Bottom &&
			activeTimes[1].Bounds.Top < work.Bottom);

		AccessibleNodeSnapshot popupButton = new AccessibleNodeSnapshot(
			"顺序播放", "顺序播放", "", 43, false, 3,
			new Rectangle(1380, 700, 120, 36));
		AccessibleControlSurface popupSurface = new AccessibleControlSurface(
			new IntPtr(2), new Rectangle(1340, 650, 240, 260),
			new AccessibleTreeSnapshot(new[] { popupButton }));
		AssertTrue("popup clicks resolve against the popup's own control tree",
			AccessibleSurfaceRouter.TryGetControlTargetAtPoint(
				new Point(1440, 718), new[] { popupSurface }, out IntPtr popupWindow,
				out AccessibleNodeSnapshot popupTarget) &&
				popupWindow == new IntPtr(2) && popupTarget.Name == "顺序播放");

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
