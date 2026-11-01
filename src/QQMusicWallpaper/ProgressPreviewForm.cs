using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace QQMusicWallpaper;

// A visual-only seek preview. The existing input hook continues to own the
// gesture and commits one seek on release; this form never takes mouse input.
internal sealed class ProgressPreviewForm : Form
{
	private readonly System.Windows.Forms.Timer paintTimer;
	private int pendingPointerX;
	private int paintedPointerX;
	private readonly Func<Rectangle, Bitmap> captureBackdrop;
	private Bitmap backdrop;

	public ProgressPreviewForm() : this(CaptureBackdrop) { }

	internal ProgressPreviewForm(Func<Rectangle, Bitmap> captureBackdrop)
	{
		this.captureBackdrop = captureBackdrop;
		FormBorderStyle = FormBorderStyle.None;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.Manual;
		AutoScaleMode = AutoScaleMode.None;
		TopMost = true;
		DoubleBuffered = true;
		TabStop = false;
		SetStyle(ControlStyles.Selectable, false);
		BackColor = Color.Magenta;
		TransparencyKey = BackColor;
		// Match the surrounding player skin instead of covering it with a dark
		// rectangle. The color key is only a capture-failure/offscreen fallback.
		ClientSize = new Size(1, 18);
		paintTimer = new System.Windows.Forms.Timer { Interval = 16 };
		paintTimer.Tick += delegate
		{
			int pointer = Interlocked.CompareExchange(ref pendingPointerX, 0, 0);
			if (pointer == paintedPointerX) return;
			paintedPointerX = pointer;
			Invalidate();
		};
	}

	protected override bool ShowWithoutActivation { get { return true; } }

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams parameters = base.CreateParams;
			parameters.ExStyle |= 0x080800A0;
			// WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT
			return parameters;
		}
	}

	// Called on the controller UI thread with the live timeline captured when
	// this gesture began. All pointer coordinates remain physical screen pixels.
	public void ShowProgress(Rectangle timeline, int pointerX)
	{
		if (IsDisposed) return;
		if (timeline.Width < 2 || timeline.Height < 2)
		{
			HidePreview();
			return;
		}
		if (!Visible || Bounds != timeline)
		{
			if (Visible) Hide();
			if (backdrop != null) backdrop.Dispose();
			backdrop = captureBackdrop(timeline);
		}
		Bounds = timeline;
		UpdatePointer(pointerX);
		paintedPointerX = pointerX;
		if (!Visible) Show();
		Invalidate();
		paintTimer.Start();
	}

	// Safe on the hook thread: do not touch a control, wait, allocate, or queue
	// one UI delegate per mouse event. The UI timer coalesces the latest position.
	public void UpdatePointer(int pointerX)
	{
		Interlocked.Exchange(ref pendingPointerX, pointerX);
	}

	// Called on the controller UI thread when the gesture ends or is cancelled.
	public void HidePreview()
	{
		if (IsDisposed) return;
		paintTimer.Stop();
		Hide();
		if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
	}

	private static Bitmap CaptureBackdrop(Rectangle timeline)
	{
		// Sample once before showing, on the UI thread. Never sample on the
		// mouse hook or per frame, and never keep a screen capture on disk.
		bool onScreen = false;
		foreach (Screen screen in Screen.AllScreens)
			if (screen.Bounds.Contains(timeline)) { onScreen = true; break; }
		if (!onScreen) return null;
		var image = new Bitmap(timeline.Width, timeline.Height);
		try
		{
			using (Graphics graphics = Graphics.FromImage(image))
				graphics.CopyFromScreen(timeline.Location, Point.Empty, timeline.Size);
			RestoreTrackBackground(image);
			return image;
		}
		catch (Win32Exception) { image.Dispose(); return null; }
		catch (ArgumentException) { image.Dispose(); return null; }
	}

	internal static void RestoreTrackBackground(Bitmap image)
	{
		// The 18px semantic hit strip includes clean pixels above and below
		// the native track/thumb. Interpolate those edges column by column to
		// preserve the album's background gradient and remove the old playhead.
		if (image.Height < 2) return;
		for (int x = 0; x < image.Width; x++)
		{
			Color top = image.GetPixel(x, 0);
			Color bottom = image.GetPixel(x, image.Height - 1);
			for (int y = 1; y < image.Height - 1; y++)
			{
				int remainder = image.Height - 1 - y;
				int divisor = image.Height - 1;
				image.SetPixel(x, y, Color.FromArgb(
					(top.R * remainder + bottom.R * y) / divisor,
					(top.G * remainder + bottom.G * y) / divisor,
					(top.B * remainder + bottom.B * y) / divisor));
			}
		}
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		if (backdrop == null) base.OnPaintBackground(e);
		else e.Graphics.DrawImageUnscaled(backdrop, Point.Empty);
	}

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == 0x0084) // WM_NCHITTEST
		{
			message.Result = new IntPtr(-1); // HTTRANSPARENT
			return;
		}
		if (message.Msg == 0x0021) // WM_MOUSEACTIVATE
		{
			message.Result = new IntPtr(3); // MA_NOACTIVATE
			return;
		}
		base.WndProc(ref message);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		int width = ClientSize.Width;
		int height = ClientSize.Height;
		if (width < 2 || height < 2) return;
		float centerY = height / 2f;
		float pointerX = Math.Max(0, Math.Min(width - 1, paintedPointerX - Left));
		e.Graphics.SmoothingMode = backdrop == null ? SmoothingMode.None : SmoothingMode.AntiAlias;
		bool lightSkin = backdrop != null && backdrop.GetPixel(width / 2, 0).GetBrightness() > 0.6f;
		Color trackColor = backdrop == null ? Color.FromArgb(102, 104, 107)
			: Color.FromArgb(65, lightSkin ? Color.Black : Color.White);
		using (var track = new Pen(trackColor, 2f))
		using (var active = new Pen(Color.FromArgb(49, 194, 124), 2f))
		using (var thumb = new SolidBrush(Color.FromArgb(248, 248, 248)))
		{
			e.Graphics.DrawLine(track, 0f, centerY, width - 1, centerY);
			if (pointerX > 0f) e.Graphics.DrawLine(active, 0f, centerY, pointerX, centerY);
			float diameter = Math.Min(6f, Math.Min(height - 2f, width));
			float thumbX = Math.Max(0f, Math.Min(width - diameter, pointerX - diameter / 2f));
			e.Graphics.FillEllipse(thumb, thumbX, centerY - diameter / 2f, diameter, diameter);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			paintTimer.Dispose();
			if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
		}
		base.Dispose(disposing);
	}
}
