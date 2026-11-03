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
	private readonly InteractionDiagnostics diagnostics;
	private readonly object surfaceSync = new object();
	private readonly Dictionary<IntPtr, Rectangle> workAreas = new Dictionary<IntPtr, Rectangle>();
	private IntPtr surfaceSource;
	private long popupPresentationSequence;
	private volatile AccessibleControlSurface[] surfaces = new AccessibleControlSurface[0];
	public AccessibleControlSurface[] Surfaces { get { return surfaces; } }
	private IntPtr qqWindow { get { return source(); } }
	private const int SwShowNoActivate = 4;
	private const uint SwpShowWindow = 64, SwpNoActivate = 16;
	private const uint WmCancelMode = 31, WmKeyDown = 256, WmKeyUp = 257;
	private const int VkEscape = 27;
	public AccessoryWindowCoordinator(Func<IntPtr> source, Func<long> click, Action<string> log,
		InteractionDiagnostics diagnostics)
	{
		this.source = source;
		this.click = click;
		this.log = log;
		this.diagnostics = diagnostics;
	}
	private void Log(string value) { log(value); }

	internal sealed class PopupPresentationRequest
	{
		public readonly long Sequence;
		public readonly long Click;
		public readonly IntPtr Source;
		public readonly Native.RECT SourceRect;
		public readonly Func<bool> AdditionalCurrent;
		public int Completed;
		public readonly long Started = Stopwatch.GetTimestamp();
		public PopupPresentationRequest(long sequence, long click, IntPtr source, Native.RECT sourceRect,
			Func<bool> additionalCurrent)
		{
			Sequence = sequence;
			Click = click;
			Source = source;
			SourceRect = sourceRect;
			AdditionalCurrent = additionalCurrent;
		}
	}


	public HashSet<IntPtr> GetVisibleQQTopLevelWindows()
	{
		HashSet<IntPtr> result = new HashSet<IntPtr>();
		Native.GetWindowThreadProcessId(qqWindow, out var sourceProcessId);
		if (sourceProcessId == 0) return result;
		Native.EnumWindows(delegate(IntPtr candidate, IntPtr data)
		{
			Native.GetWindowThreadProcessId(candidate, out var processId);
			if (processId == sourceProcessId && Native.IsWindowVisible(candidate))
				result.Add(candidate);
			return true;
		}, IntPtr.Zero);
		return result;
	}

	public PopupPresentationRequest BeginQQPopupPresentation(long click, Func<bool> additionalCurrent = null)
	{
		if (click != this.click()) return null;
		IntPtr source = qqWindow;
		if (!Native.GetWindowRect(source, out var sourceRect)) return null;
		return new PopupPresentationRequest(Interlocked.Increment(ref popupPresentationSequence),
			click, source, sourceRect, additionalCurrent);
	}

	public void QueuePresentQQPopup(PopupPresentationRequest request,
		HashSet<IntPtr> visibleBefore, Rectangle work, string controlName)
	{
		if (!IsQQPopupPresentationCurrent(request)) return;
		// Already published windows may be closing in response to a toggle. Only
		// recover a pre-existing visible window when it has not yet been published.
		HashSet<IntPtr> publishedBefore = new HashSet<IntPtr>();
		HashSet<IntPtr> matchingPublished = new HashSet<IntPtr>();
		foreach (AccessibleControlSurface surface in surfaces)
			if (visibleBefore.Contains(surface.Window))
			{
				publishedBefore.Add(surface.Window);
				if (GetQQPopupCandidateScore(surface.Window, controlName, true) > 0)
					matchingPublished.Add(surface.Window);
			}
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
			bool ambiguous = false;
			// Let the posted launcher click be processed before considering a reused
			// visible window. A hidden result must never be shown again by this worker.
			Thread.Sleep(100);
			while ((Stopwatch.GetTimestamp() - request.Started) * 1000.0 / Stopwatch.Frequency < 5000)
			{
				if (!IsQQPopupPresentationCurrent(request)) return;
				HashSet<IntPtr> current = GetVisibleQQTopLevelWindows();
				IntPtr selected = IntPtr.Zero;
				int bestScore = 0;
				bool tied = false;
				foreach (IntPtr candidate in current)
				{
					if (candidate == request.Source || publishedBefore.Contains(candidate)) continue;
					int score = GetQQPopupCandidateScore(candidate, controlName,
						visibleBefore.Contains(candidate));
					if (score == 1) { ambiguous = true; continue; }
					if (score == bestScore && score > 0) { tied = true; continue; }
					if (score < bestScore || score == 0) continue;
					selected = candidate;
					bestScore = score;
					tied = false;
				}
				ambiguous |= tied;
				if (!tied && selected != IntPtr.Zero &&
					IsQQPopupPresentationCurrent(request) &&
					Native.IsWindowVisible(selected) && (controlName == "Comments"
						? TryPositionCommentWindow(selected, work) : TryPositionQQPopup(selected, work)))
				{
					if (!IsQQPopupPresentationCurrent(request)) return;
					PublishQQPopupSurface(selected, work);
					Finish(request, InteractionOutcome.Presented);
					Log(controlName + " popup positioned in the working area; hwnd=" +
						selected.ToInt64() + "; reused=" + visibleBefore.Contains(selected));
					return;
				}
				Thread.Sleep(50);
			}
			if (IsQQPopupPresentationCurrent(request))
			{
				bool closed = !ambiguous && matchingPublished.Count > 0 &&
					!GetVisibleQQTopLevelWindows().Overlaps(matchingPublished);
				Finish(request, closed ? InteractionOutcome.Closed :
					ambiguous ? InteractionOutcome.AmbiguousTarget : InteractionOutcome.PopupTimeout);
				Log(controlName + (closed ? " popup toggle closed the tracked window." :
					" popup was not observed within the presentation deadline."));
			}
			}
			catch { Finish(request, InteractionOutcome.InternalFailure); }
		});
	}

	public void CancelPendingQQPopupPresentation()
	{
		Interlocked.Increment(ref popupPresentationSequence);
	}

	public bool IsQQPopupPresentationCurrent(PopupPresentationRequest request)
	{
		if (request == null) return false;
		if (request.Sequence != Interlocked.Read(ref popupPresentationSequence) || request.Click != this.click() ||
			request.AdditionalCurrent != null && !request.AdditionalCurrent())
		{ Finish(request, InteractionOutcome.Cancelled); return false; }
		if (request.Source != qqWindow || !Native.IsWindow(request.Source) ||
			!Native.GetWindowRect(request.Source, out var current) ||
			current.Left != request.SourceRect.Left || current.Top != request.SourceRect.Top ||
			current.Right != request.SourceRect.Right || current.Bottom != request.SourceRect.Bottom)
		{ Finish(request, InteractionOutcome.SourceChanged); return false; }
		return true;
	}

	public void Finish(PopupPresentationRequest request, InteractionOutcome outcome)
	{
		if (request != null && Interlocked.CompareExchange(ref request.Completed, 1, 0) == 0)
			diagnostics.Complete(request.Click, request.Sequence, outcome);
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
		if (controlName != "Comments" && (value == "评论" || value == "Comments")) return 0;
		bool expected = IsExpectedQQPopupTitle(value, controlName);
		if (wasVisible) return expected ? 2 : 0;
		// Reject a known, unrelated panel even if another QQ action opened it
		// concurrently. Unknown new native panels are reported as ambiguous;
		// their appearance alone is not sufficient evidence of input ownership.
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
		case "Comments":
			return title == "评论" || title == "Comments";
		case "More":
			return title == "更多" || title == "More";
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
		lock (surfaceSync)
		{
		if (surfaceSource != IntPtr.Zero && surfaceSource != qqWindow)
		{ surfaces = new AccessibleControlSurface[0]; workAreas.Clear(); }
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
		workAreas[popup] = work;
		surfaceSource = qqWindow;
		foreach (IntPtr old in new List<IntPtr>(workAreas.Keys))
			if (!updated.Exists(item => item.Window == old)) workAreas.Remove(old);
		}
	}

	public void PruneQQPopupSurfaces()
	{
		lock (surfaceSync)
		{
		AccessibleControlSurface[] current = surfaces;
		if (current.Length == 0) return;
		List<AccessibleControlSurface> visible =
			new List<AccessibleControlSurface>(current.Length);
		for (int index = 0; index < current.Length; index++)
		{
			AccessibleControlSurface surface = current[index];
			if (surfaceSource == qqWindow && Native.IsWindow(surface.Window) &&
				Native.IsWindowVisible(surface.Window) && Native.GetWindowRect(surface.Window, out var rect) &&
				workAreas.TryGetValue(surface.Window, out var work))
			{
				Rectangle bounds = Rectangle.Intersect(work, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
				if (bounds.Width > 1 && bounds.Height > 1)
					visible.Add(new AccessibleControlSurface(surface.Window, bounds,
						new AccessibleTreeSnapshot(new AccessibleNodeSnapshot[0])));
			}
		}
		foreach (AccessibleControlSurface old in current)
			if (!visible.Exists(item => item.Window == old.Window)) workAreas.Remove(old.Window);
		surfaces = visible.ToArray();
		}
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
		bool ambiguous = false;
		Native.GetWindowThreadProcessId(qqWindow, out var sourceProcessId);
		if (sourceProcessId == 0) return result;
		Native.EnumWindows(delegate(IntPtr candidate, IntPtr data)
		{
			if (candidate == qqWindow) return true;
			Native.GetWindowThreadProcessId(candidate, out var processId);
			if (processId != sourceProcessId) return true;
			StringBuilder className = new StringBuilder(128);
			Native.GetClassName(candidate, className, className.Capacity);
			string name = className.ToString();
			if (name != "TXGuiFoundation" && name != "#32770" &&
				name != "Chrome_WidgetWin_0" && name != "Chrome_WidgetWin_1") return true;
			StringBuilder title = new StringBuilder(64);
			Native.GetWindowText(candidate, title, title.Capacity);
			string value = title.ToString();
			for (int index = 0; index < titles.Length; index++)
			{
				if (string.Equals(value, titles[index], StringComparison.OrdinalIgnoreCase))
				{
					if (result != IntPtr.Zero) { ambiguous = true; return false; }
					result = candidate;
					return true;
				}
			}
			return true;
		}, IntPtr.Zero);
		return ambiguous ? IntPtr.Zero : result;
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
		PopupPresentationRequest request = BeginQQPopupPresentation(click(), requestCurrent);
		if (!IsQQPopupPresentationCurrent(request)) return;
		IntPtr existingComments = FindQQMusicPopupWindow("评论", "Comments");
		if (existingComments != IntPtr.Zero &&
			IsQQPopupPresentationCurrent(request) &&
			TryPositionCommentWindow(existingComments, work))
		{
			Finish(request, InteractionOutcome.Presented);
			Log("Existing native comment window restored to the working area.");
			return;
		}
		HashSet<IntPtr> before = GetVisibleQQTopLevelWindows();
		bool invoked = invoke();
		if (!IsQQPopupPresentationCurrent(request)) return;
		Log("Native comments invoked through its MSAA control; applied=" + invoked);
		// Retrying discovery is safe. Replaying a toggle click is not: the first
		// action may have succeeded while its window was still being created.
		if (invoked) QueuePresentQQPopup(request, before, work, "Comments");
		else Finish(request, InteractionOutcome.DispatchFailed);
	}
	public void DismissNativeSurfaces()
	{
		if (!Native.IsWindow(qqWindow)) return;
		PostTransientDismissal(qqWindow,
			DesktopInputPolicy.ResolveTransientDismissalAction(isMainSurface: true));
		Native.EnumChildWindows(qqWindow, delegate(IntPtr child, IntPtr data)
		{
			PostTransientDismissal(child,
				DesktopInputPolicy.ResolveTransientDismissalAction(isMainSurface: true));
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

	public void Recover()
	{
		CancelPendingQQPopupPresentation();
		DismissNativeSurfaces();
		IntPtr comments = FindQQMusicPopupWindow("评论", "Comments");
		if (comments != IntPtr.Zero) Native.ShowWindow(comments, 0);
		lock (surfaceSync) { surfaces = new AccessibleControlSurface[0]; workAreas.Clear(); }
	}
}
