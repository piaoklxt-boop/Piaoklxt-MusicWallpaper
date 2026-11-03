using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;

namespace QQMusicWallpaper;

// Owns native accessory discovery, presentation and input-surface lifetime.
// The controller supplies the current source and click identity, never a second
// copy of the coordinator's request or surface state.
internal sealed class AccessoryWindowCoordinator
{
	private readonly Func<IntPtr> source;
	private readonly Func<long> click;
	private readonly Action<string> log;
	private long popupPresentationSequence;
	private volatile AccessibleControlSurface[] surfaces = new AccessibleControlSurface[0];
	public AccessibleControlSurface[] Surfaces { get { return surfaces; } }
	private IntPtr qqWindow { get { return source(); } }
	private const int SwShowNoActivate = 4;
	private const uint SwpShowWindow = 64, SwpNoActivate = 16;
	private const uint WmCancelMode = 31, WmKeyDown = 256, WmKeyUp = 257;
	private const int VkEscape = 27;
	public AccessoryWindowCoordinator(Func<IntPtr> source, Func<long> click, Action<string> log)
	{
		this.source = source;
		this.click = click;
		this.log = log;
	}
	private void Log(string value) { log(value); }

	internal sealed class PopupPresentationRequest
	{
		public readonly long Sequence;
		public readonly long Click;
		public readonly IntPtr Source;
		public readonly Native.RECT SourceRect;
		public readonly long Started = Stopwatch.GetTimestamp();
		public PopupPresentationRequest(long sequence, long click, IntPtr source, Native.RECT sourceRect)
		{
			Sequence = sequence;
			Click = click;
			Source = source;
			SourceRect = sourceRect;
		}
	}


	public HashSet<IntPtr> GetVisibleQQTopLevelWindows()
	{
		HashSet<IntPtr> result = new HashSet<IntPtr>();
		Native.GetWindowThreadProcessId(qqWindow, out var sourceProcessId);
		Native.EnumWindows(delegate(IntPtr candidate, IntPtr data)
		{
			Native.GetWindowThreadProcessId(candidate, out var processId);
			if (processId == sourceProcessId && Native.IsWindowVisible(candidate))
				result.Add(candidate);
			return true;
		}, IntPtr.Zero);
		return result;
	}

	public PopupPresentationRequest BeginQQPopupPresentation(long click)
	{
		if (click != this.click()) return null;
		IntPtr source = qqWindow;
		if (!Native.GetWindowRect(source, out var sourceRect)) return null;
		return new PopupPresentationRequest(Interlocked.Increment(ref popupPresentationSequence),
			click, source, sourceRect);
	}

	public void QueuePresentQQPopup(PopupPresentationRequest request,
		HashSet<IntPtr> visibleBefore, Rectangle work, string controlName)
	{
		if (!IsQQPopupPresentationCurrent(request)) return;
		// Already published windows may be closing in response to a toggle. Only
		// recover a pre-existing visible window when it has not yet been published.
		HashSet<IntPtr> publishedBefore = new HashSet<IntPtr>();
		foreach (AccessibleControlSurface surface in surfaces)
			if (visibleBefore.Contains(surface.Window)) publishedBefore.Add(surface.Window);
		ThreadPool.QueueUserWorkItem(delegate
		{
			// Let the posted launcher click be processed before considering a reused
			// visible window. A hidden result must never be shown again by this worker.
			Thread.Sleep(100);
			while ((Stopwatch.GetTimestamp() - request.Started) * 1000.0 / Stopwatch.Frequency < 5000)
			{
				if (!IsQQPopupPresentationCurrent(request)) return;
				HashSet<IntPtr> current = GetVisibleQQTopLevelWindows();
				IntPtr selected = IntPtr.Zero;
				int bestScore = 0;
				foreach (IntPtr candidate in current)
				{
					if (candidate == request.Source || publishedBefore.Contains(candidate)) continue;
					int score = GetQQPopupCandidateScore(candidate, controlName,
						visibleBefore.Contains(candidate));
					if (score <= bestScore) continue;
					selected = candidate;
					bestScore = score;
				}
				if (selected != IntPtr.Zero &&
					IsQQPopupPresentationCurrent(request) &&
					Native.IsWindowVisible(selected) && TryPositionQQPopup(selected, work))
				{
					if (!IsQQPopupPresentationCurrent(request)) return;
					PublishQQPopupSurface(selected, work);
					Log(controlName + " popup positioned in the working area; hwnd=" +
						selected.ToInt64() + "; reused=" + visibleBefore.Contains(selected));
					return;
				}
				Thread.Sleep(50);
			}
			if (IsQQPopupPresentationCurrent(request))
				Log(controlName + " popup was not observed within the presentation deadline.");
		});
	}

	public void CancelPendingQQPopupPresentation()
	{
		Interlocked.Increment(ref popupPresentationSequence);
	}

	public bool IsQQPopupPresentationCurrent(PopupPresentationRequest request)
	{
		return request != null && request.Sequence == Interlocked.Read(ref popupPresentationSequence) &&
			request.Click == this.click() && request.Source == qqWindow &&
			Native.IsWindow(request.Source) && Native.GetWindowRect(request.Source, out var current) &&
			current.Left == request.SourceRect.Left && current.Top == request.SourceRect.Top &&
			current.Right == request.SourceRect.Right && current.Bottom == request.SourceRect.Bottom;
	}

	public static int GetQQPopupCandidateScore(IntPtr candidate, string controlName,
		bool wasVisible)
	{
		StringBuilder className = new StringBuilder(128);
		Native.GetClassName(candidate, className, className.Capacity);
		string windowClass = className.ToString();
		// TXGFLayerMask is a decoration created before the actual dialog. Taking
		// it as the input surface ends discovery before any controls exist.
		if (windowClass != "TXGuiFoundation" && windowClass != "#32770" &&
			windowClass != "Chrome_WidgetWin_0" && windowClass != "Chrome_WidgetWin_1") return 0;
		StringBuilder title = new StringBuilder(128);
		Native.GetWindowText(candidate, title, title.Capacity);
		string value = title.ToString();
		if (value == "评论" || string.Equals(value, "Comments", StringComparison.OrdinalIgnoreCase)) return 0;
		bool expected = IsExpectedQQPopupTitle(value, controlName);
		if (wasVisible) return expected ? 2 : 0;
		// Reject a known, unrelated panel even if another QQ action opened it
		// concurrently. Other newly visible native panels keep their existing
		// discovery path, including QQ panels with skin-dependent titles.
		if (!expected && (IsExpectedQQPopupTitle(value, "Accompaniment") ||
			IsExpectedQQPopupTitle(value, "Audio quality") ||
			IsExpectedQQPopupTitle(value, "Sound effects") ||
			IsExpectedQQPopupTitle(value, "Player style") ||
			IsExpectedQQPopupTitle(value, "Native playlist"))) return 0;
		return expected ? 3 : 1;
	}

	public static bool IsExpectedQQPopupTitle(string title, string controlName)
	{
		switch (controlName)
		{
		case "Accompaniment":
			return title == "伴唱" || title == "伴奏" || title == "Accompaniment";
		case "Audio quality":
			return title == "切换品质" || title == "Change quality" ||
				title == "伴唱" || title == "伴奏" || title == "Accompaniment";
		case "Sound effects":
			return title == "音效" || title == "音效盒子" || title == "QQ音效" || title == "Sound effects";
		case "Player style":
			return title == "播放器样式" || title == "Player style";
		case "Native playlist":
			return title == "播放队列" || title == "播放列表" || title == "Playlist" || title == "Play queue";
		default:
			return false;
		}
	}

	public bool TryPositionQQPopup(IntPtr popup, Rectangle work)
	{
		if (!Native.GetWindowRect(popup, out var popupRect) ||
			!Native.GetWindowRect(qqWindow, out var sourceRect)) return false;
		int width = popupRect.Right - popupRect.Left;
		int height = popupRect.Bottom - popupRect.Top;
		if (width < 2 || height < 2) return false;
		Rectangle current = Rectangle.FromLTRB(popupRect.Left, popupRect.Top,
			popupRect.Right, popupRect.Bottom);
		Rectangle source = Rectangle.FromLTRB(sourceRect.Left, sourceRect.Top,
			sourceRect.Right, sourceRect.Bottom);
		Rectangle target = DesktopInputPolicy.GetVisiblePopupBounds(current, source, work);
		Native.ShowWindow(popup, SwShowNoActivate);
		return Native.SetWindowPos(popup, new IntPtr(-1), target.Left, target.Top,
			target.Width, target.Height, SwpShowWindow | SwpNoActivate);
	}

	public void PublishQQPopupSurface(IntPtr popup, Rectangle work)
	{
		if (!Native.IsWindow(popup) ||
			!Native.GetWindowRect(popup, out var popupRect)) return;
		Rectangle bounds = Rectangle.Intersect(work, Rectangle.FromLTRB(
			popupRect.Left, popupRect.Top, popupRect.Right, popupRect.Bottom));
		if (bounds.Width < 2 || bounds.Height < 2) return;
		// The top-level bounds are enough to give the popup ownership of pointer
		// input. Reading the popup's complete MSAA tree here synchronously blocks
		// QQ Music's Chromium UI thread and makes newly opened panels feel stuck.
		AccessibleControlSurface surface = new AccessibleControlSurface(popup,
			bounds, new AccessibleTreeSnapshot(new AccessibleNodeSnapshot[0]));
		List<AccessibleControlSurface> updated =
			new List<AccessibleControlSurface> { surface };
		AccessibleControlSurface[] current = surfaces;
		for (int index = 0; index < current.Length; index++)
		{
			if (current[index].Window == popup ||
				!Native.IsWindowVisible(current[index].Window)) continue;
			updated.Add(current[index]);
		}
		surfaces = updated.ToArray();
	}

	public void PruneQQPopupSurfaces()
	{
		AccessibleControlSurface[] current = surfaces;
		if (current.Length == 0) return;
		List<AccessibleControlSurface> visible =
			new List<AccessibleControlSurface>(current.Length);
		for (int index = 0; index < current.Length; index++)
		{
			AccessibleControlSurface surface = current[index];
			if (Native.IsWindow(surface.Window) &&
				Native.IsWindowVisible(surface.Window)) visible.Add(surface);
		}
		if (visible.Count == current.Length) return;
		surfaces = visible.ToArray();
	}


	public bool TryPositionCommentWindow(IntPtr comments, Rectangle work)
	{
		if (!Native.GetWindowRect(comments, out var rect)) return false;
		Rectangle current = new Rectangle(rect.Left, rect.Top,
			rect.Right - rect.Left, rect.Bottom - rect.Top);
		Rectangle target = DesktopInputPolicy.GetVisibleCommentBounds(current, work);
		Native.ShowWindow(comments, SwShowNoActivate);
		bool positioned = Native.SetWindowPos(comments, new IntPtr(-1), target.Left,
			target.Top, target.Width, target.Height, SwpShowWindow | SwpNoActivate);
		if (positioned) PublishQQPopupSurface(comments, work);
		return positioned;
	}

	public IntPtr FindQQMusicPopupWindow(params string[] titles)
	{
		IntPtr result = IntPtr.Zero;
		Native.GetWindowThreadProcessId(qqWindow, out var sourceProcessId);
		Native.EnumWindows(delegate(IntPtr candidate, IntPtr data)
		{
			if (candidate == qqWindow) return true;
			Native.GetWindowThreadProcessId(candidate, out var processId);
			if (processId != sourceProcessId) return true;
			StringBuilder title = new StringBuilder(64);
			Native.GetWindowText(candidate, title, title.Capacity);
			string value = title.ToString();
			for (int index = 0; index < titles.Length; index++)
			{
				if (string.Equals(value, titles[index], StringComparison.OrdinalIgnoreCase))
				{
					result = candidate;
					return false;
				}
			}
			return true;
		}, IntPtr.Zero);
		return result;
	}

	public static void PostTransientDismissal(IntPtr target,
		TransientDismissalAction action)
	{
		if (target == IntPtr.Zero || !Native.IsWindow(target)) return;
		Native.PostMessage(target, WmCancelMode, IntPtr.Zero, IntPtr.Zero);
		if (action != TransientDismissalAction.CancelAndEscape) return;
		Native.PostMessage(target, WmKeyDown, new IntPtr(VkEscape), IntPtr.Zero);
		Native.PostMessage(target, WmKeyUp, new IntPtr(VkEscape), IntPtr.Zero);
	}
	public void OpenNativeComments(Rectangle work, Func<bool> invoke,
		Func<bool> requestCurrent = null)
	{
		if (requestCurrent != null && !requestCurrent()) return;
		IntPtr existingComments = FindQQMusicPopupWindow("评论", "Comments");
		if (existingComments != IntPtr.Zero &&
			TryPositionCommentWindow(existingComments, work))
		{
			Log("Existing native comment window restored to the working area.");
			return;
		}
		bool invoked = invoke();
		Log("Native comments invoked through its MSAA control; applied=" + invoked);
		if (invoked) QueuePresentCommentWindow(work, invoke, requestCurrent);
	}
	private void QueuePresentCommentWindow(Rectangle work, Func<bool> invoke,
		Func<bool> requestCurrent)
	{
		ThreadPool.QueueUserWorkItem(delegate
		{
			if (TryPresentCommentWindow(work, 12, requestCurrent)) return;
			if (requestCurrent != null && !requestCurrent()) return;
			// QQ keeps hidden popup state internally. A second control action repairs
			// that state after a prior forced hide or playlist transition.
			invoke();
			Log("Native comment state required one synchronized retry.");
			if (TryPresentCommentWindow(work, 20, requestCurrent)) return;
			Log("Native comment window did not become visible after background click.");
		});
	}
	private bool TryPresentCommentWindow(Rectangle work, int attempts, Func<bool> requestCurrent)
	{
		for (int attempt = 0; attempt < attempts; attempt++)
		{
			if (requestCurrent != null && !requestCurrent()) return false;
			IntPtr comments = FindQQMusicPopupWindow("评论", "Comments");
			if (comments != IntPtr.Zero && Native.IsWindowVisible(comments) &&
				TryPositionCommentWindow(comments, work))
			{
				Log("Native comment window positioned in the working area; hwnd=" +
					comments.ToInt64());
				return true;
			}
			Thread.Sleep(50);
		}
		return false;
	}
	public void DismissNativeSurfaces()
	{
		PostTransientDismissal(qqWindow,
			DesktopInputPolicy.ResolveTransientDismissalAction(isMainSurface: true));
		Native.EnumChildWindows(qqWindow, delegate(IntPtr child, IntPtr data)
		{
			PostTransientDismissal(child,
				DesktopInputPolicy.ResolveTransientDismissalAction(isMainSurface: true));
			return true;
		}, IntPtr.Zero);
		Native.GetWindowThreadProcessId(qqWindow, out var sourceProcessId);
		Native.EnumWindows(delegate(IntPtr candidate, IntPtr data)
		{
			if (candidate == qqWindow || !Native.IsWindowVisible(candidate)) return true;
			Native.GetWindowThreadProcessId(candidate, out var processId);
			if (processId == sourceProcessId)
			{
				PostTransientDismissal(candidate,
					DesktopInputPolicy.ResolveTransientDismissalAction(
						isMainSurface: false));
			}
			return true;
		}, IntPtr.Zero);
		AccessibleControlSurface[] popupSurfaces = Surfaces;
		for (int index = 0; index < popupSurfaces.Length; index++)
		{
			PostTransientDismissal(popupSurfaces[index].Window,
				DesktopInputPolicy.ResolveTransientDismissalAction(
					isMainSurface: false));
		}
	}
}
