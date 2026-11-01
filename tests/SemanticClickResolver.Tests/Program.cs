using System;
using System.Drawing;
using QQMusicWallpaper;

internal static class Program
{
	private static int assertions;
	private static int failures;

	private static int Main()
	{
		Rectangle source = new Rectangle(-2076, 77, 1920, 1032);
		AccessibleNodeSnapshot staleComment = Node("评论999+", "评论", 6,
			new Rectangle(-1742, 976, 51, 35));
		AccessibleNodeSnapshot activeComment = Node("评论999+", "评论", 5,
			new Rectangle(-1971, 1065, 51, 35));
		AccessibleNodeSnapshot volume = Node("调节音量", "调节音量", 7,
			new Rectangle(-1870, 1065, 51, 35));
		AccessibleTreeSnapshot snapshot = new AccessibleTreeSnapshot(
			new[] { staleComment, activeComment, volume });
		Check("the active semantic comment target is promoted",
			SemanticClickResolver.TryGetCommentAtPoint(snapshot, source,
				Center(activeComment.Bounds), out AccessibleNodeSnapshot chosen) &&
				chosen.Bounds == activeComment.Bounds);
		Check("a stale player-bar comment is not promoted",
			!SemanticClickResolver.TryGetCommentAtPoint(snapshot, source,
				Center(staleComment.Bounds), out _));
		Check("a volume control is not promoted to comments",
			!SemanticClickResolver.TryGetCommentAtPoint(snapshot, source,
				Center(volume.Bounds), out _));
		Check("blank source space is not promoted",
			!SemanticClickResolver.TryGetCommentAtPoint(snapshot, source,
				new Point(source.Left + 20, source.Top + 20), out _));
		Check("a point outside the source is not promoted",
			!SemanticClickResolver.TryGetCommentAtPoint(snapshot, source,
				new Point(5, 5), out _));
		Check("missing accessibility data does not create a comment action",
			!SemanticClickResolver.TryGetCommentAtPoint(null, source,
				Center(activeComment.Bounds), out _));
		Check("an empty tree does not create a comment action",
			!SemanticClickResolver.TryGetCommentAtPoint(
				new AccessibleTreeSnapshot(new AccessibleNodeSnapshot[0]), source,
				Center(activeComment.Bounds), out _));
		AccessibleNodeSnapshot english = Node("Comments", "", 2,
			activeComment.Bounds);
		Check("an exact English comment semantic is recognized",
			SemanticClickResolver.TryGetCommentAtPoint(
				new AccessibleTreeSnapshot(new[] { english }), source,
				Center(english.Bounds), out _));
		AccessibleNodeSnapshot unrelated = Node("Read comments in this article", "", 2,
			activeComment.Bounds);
		Check("unrelated text mentioning comments is not a launcher",
			!SemanticClickResolver.TryGetCommentAtPoint(
				new AccessibleTreeSnapshot(new[] { unrelated }), source,
				Center(unrelated.Bounds), out _));

		IntPtr window = new IntPtr(10);
		long young = TimeSpan.FromMilliseconds(100).Ticks;
		Check("a recent request for the same source geometry remains current",
			SemanticClickResolver.IsRequestCurrent(1, 1, young, window, window,
				source, source));
		Check("a newer click cancels the previous request",
			!SemanticClickResolver.IsRequestCurrent(1, 2, young, window, window,
				source, source));
		Check("default request identifiers cannot dispatch",
			!SemanticClickResolver.IsRequestCurrent(0, 0, young, window, window,
				source, source));
		Check("an expired request cannot dispatch",
			!SemanticClickResolver.IsRequestCurrent(1, 1,
				SemanticClickResolver.MaximumRequestAgeTicks + 1, window, window,
				source, source));
		Check("the documented maximum age remains eligible",
			SemanticClickResolver.IsRequestCurrent(1, 1,
				SemanticClickResolver.MaximumRequestAgeTicks, window, window,
				source, source));
		Check("negative elapsed time is rejected",
			!SemanticClickResolver.IsRequestCurrent(1, 1, -1, window, window,
				source, source));
		Check("a different source window cancels the request",
			!SemanticClickResolver.IsRequestCurrent(1, 1, young, window,
				new IntPtr(11), source, source));
		Check("a missing source window cancels the request",
			!SemanticClickResolver.IsRequestCurrent(1, 1, young, IntPtr.Zero,
				IntPtr.Zero, source, source));
		Rectangle moved = source;
		moved.Offset(1, 0);
		Check("source movement cancels a pending request",
			!SemanticClickResolver.IsRequestCurrent(1, 1, young, window, window,
				source, moved));
		Rectangle resized = source;
		resized.Width += 1;
		Check("source resizing cancels a pending request",
			!SemanticClickResolver.IsRequestCurrent(1, 1, young, window, window,
				source, resized));
		Check("invalid source geometry cannot dispatch",
			!SemanticClickResolver.IsRequestCurrent(1, 1, young, window, window,
				Rectangle.Empty, Rectangle.Empty));
		Console.WriteLine(assertions + " assertions; " + failures + " failures.");
		return failures == 0 ? 0 : 1;
	}

	private static AccessibleNodeSnapshot Node(string name, string description,
		int depth, Rectangle bounds)
	{
		return new AccessibleNodeSnapshot(name, description, "", 43, false, depth,
			bounds);
	}

	private static Point Center(Rectangle bounds)
	{
		return new Point(bounds.Left + bounds.Width / 2,
			bounds.Top + bounds.Height / 2);
	}

	private static void Check(string description, bool passed)
	{
		assertions++;
		if (!passed) failures++;
		Console.WriteLine((passed ? "PASS: " : "FAIL: ") + description);
	}
}
