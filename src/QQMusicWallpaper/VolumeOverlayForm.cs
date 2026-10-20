using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QQMusicWallpaper;

internal sealed class VolumeOverlayForm : Form
{
	private readonly Timer hideTimer;
	private float level = 1f;
	private bool dragging;

	public event Action<float> VolumeRequested;

	public VolumeOverlayForm()
	{
		FormBorderStyle = FormBorderStyle.None;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.Manual;
		TopMost = true;
		DoubleBuffered = true;
		BackColor = Color.FromArgb(248, 248, 248);
		ClientSize = new Size(66, 184);
		hideTimer = new Timer { Interval = 1800 };
		hideTimer.Tick += delegate { hideTimer.Stop(); if (!dragging) Hide(); };
		MouseDown += OnPointerDown;
		MouseMove += OnPointerMove;
		MouseUp += OnPointerUp;
	}

	protected override bool ShowWithoutActivation { get { return true; } }

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams parameters = base.CreateParams;
			parameters.ExStyle |= 0x08000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
			return parameters;
		}
	}

	public void ShowLevel(Rectangle anchor, float value)
	{
		level = Clamp(value);
		Rectangle work = Screen.FromPoint(anchor.IsEmpty ? Cursor.Position :
			new Point(anchor.Left + anchor.Width / 2, anchor.Top)).WorkingArea;
		int x = anchor.IsEmpty ? work.Left + (work.Width - Width) / 2 :
			anchor.Left + (anchor.Width - Width) / 2;
		int y = anchor.IsEmpty ? work.Bottom - Height - 70 : anchor.Top - Height - 10;
		Location = new Point(Math.Max(work.Left + 4, Math.Min(x, work.Right - Width - 4)),
			Math.Max(work.Top + 4, Math.Min(y, work.Bottom - Height - 4)));
		ApplyRoundedRegion();
		Invalidate();
		if (!Visible) Show();
		hideTimer.Stop();
		hideTimer.Start();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using (var track = new SolidBrush(Color.FromArgb(220, 220, 224)))
			FillRounded(e.Graphics, track, new Rectangle(27, 20, 12, 122), 6);
		int fill = (int)Math.Round(122 * level);
		using (var active = new SolidBrush(Color.FromArgb(30, 30, 34)))
			FillRounded(e.Graphics, active, new Rectangle(27, 142 - fill, 12, fill), 6);
		using (var text = new SolidBrush(Color.FromArgb(35, 35, 38)))
		using (var font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point))
		{
			string percent = ((int)Math.Round(level * 100)).ToString() + "%";
			SizeF size = e.Graphics.MeasureString(percent, font);
			e.Graphics.DrawString(percent, font, text, (Width - size.Width) / 2f, 153f);
		}
	}

	private void OnPointerDown(object sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left) return;
		dragging = true;
		Capture = true;
		hideTimer.Stop();
		SetFromPointer(e.Y);
	}

	private void OnPointerMove(object sender, MouseEventArgs e)
	{
		if (dragging) SetFromPointer(e.Y);
	}

	private void OnPointerUp(object sender, MouseEventArgs e)
	{
		if (!dragging) return;
		SetFromPointer(e.Y);
		dragging = false;
		Capture = false;
		hideTimer.Start();
	}

	private void SetFromPointer(int y)
	{
		level = Clamp((142f - y) / 122f);
		Invalidate();
		Action<float> handler = VolumeRequested;
		if (handler != null) handler(level);
	}

	private void ApplyRoundedRegion()
	{
		using (var path = RoundedPath(ClientRectangle, 12))
		{
			Region old = Region;
			Region = new Region(path);
			if (old != null) old.Dispose();
		}
	}

	private static void FillRounded(Graphics graphics, Brush brush, Rectangle rectangle, int radius)
	{
		if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
		using (var path = RoundedPath(rectangle, radius)) graphics.FillPath(brush, path);
	}

	private static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
	{
		var path = new GraphicsPath();
		int diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
		if (diameter <= 0) { path.AddRectangle(rectangle); return path; }
		path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
		path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
		path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
		path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
		path.CloseFigure();
		return path;
	}

	private static float Clamp(float value)
	{
		return Math.Max(0f, Math.Min(1f, value));
	}
}
