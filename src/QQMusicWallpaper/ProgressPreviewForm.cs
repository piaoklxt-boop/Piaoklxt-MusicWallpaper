using System;
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

	public ProgressPreviewForm()
	{
		FormBorderStyle = FormBorderStyle.None;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.Manual;
		AutoScaleMode = AutoScaleMode.None;
		TopMost = true;
		DoubleBuffered = true;
		TabStop = false;
		SetStyle(ControlStyles.Selectable, false);
		BackColor = Color.FromArgb(26, 26, 28);
		// Cover the old playhead only inside the timeline, leaving the rest of
		// the player untouched. Layered + transparent also makes hit testing pass
		// through to the desktop across process/thread boundaries.
		Opacity = 0.98;
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
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using (var track = new Pen(Color.FromArgb(102, 104, 107), 3f))
		using (var active = new Pen(Color.FromArgb(49, 194, 124), 3f))
		using (var thumb = new SolidBrush(Color.FromArgb(248, 248, 248)))
		{
			e.Graphics.DrawLine(track, 0f, centerY, width - 1, centerY);
			if (pointerX > 0f) e.Graphics.DrawLine(active, 0f, centerY, pointerX, centerY);
			float diameter = Math.Min(10f, Math.Min(height - 2f, width));
			float thumbX = Math.Max(0f, Math.Min(width - diameter, pointerX - diameter / 2f));
			e.Graphics.FillEllipse(thumb, thumbX, centerY - diameter / 2f, diameter, diameter);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) paintTimer.Dispose();
		base.Dispose(disposing);
	}
}
