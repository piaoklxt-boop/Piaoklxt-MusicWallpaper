using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QQMusicWallpaper;

internal sealed class PlaybackModeOverlayForm : Form
{
	private static readonly string[] Modes =
	{
		"随机播放", "顺序播放", "单曲循环", "列表循环"
	};

	private readonly Timer hideTimer;
	private string currentMode = "列表循环";
	private int hotIndex = -1;

	public event Action<string> ModeRequested;

	public PlaybackModeOverlayForm()
	{
		FormBorderStyle = FormBorderStyle.None;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.Manual;
		TopMost = true;
		DoubleBuffered = true;
		BackColor = Color.FromArgb(250, 250, 250);
		ClientSize = new Size(144, 184);
		hideTimer = new Timer { Interval = 3500 };
		hideTimer.Tick += delegate { hideTimer.Stop(); Hide(); };
		MouseMove += delegate(object sender, MouseEventArgs e)
		{
			int next = Math.Max(0, Math.Min(Modes.Length - 1, (e.Y - 8) / 42));
			if (next != hotIndex) { hotIndex = next; Invalidate(); }
		};
		MouseLeave += delegate { hotIndex = -1; Invalidate(); };
		MouseDown += delegate(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left) return;
			int index = (e.Y - 8) / 42;
			if (index < 0 || index >= Modes.Length) return;
			hideTimer.Stop();
			Hide();
			Action<string> handler = ModeRequested;
			if (handler != null) handler(Modes[index]);
		};
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

	public void ShowModes(Rectangle anchor, string selected)
	{
		if (!string.IsNullOrWhiteSpace(selected)) currentMode = selected;
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
		using (var font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular,
			GraphicsUnit.Point))
		using (var normal = new SolidBrush(Color.FromArgb(38, 38, 42)))
		using (var selected = new SolidBrush(Color.FromArgb(211, 44, 91)))
		using (var hover = new SolidBrush(Color.FromArgb(238, 238, 241)))
		using (var separator = new Pen(Color.FromArgb(232, 232, 235)))
		{
			for (int index = 0; index < Modes.Length; index++)
			{
				Rectangle row = new Rectangle(7, 8 + index * 42, Width - 14, 42);
				if (index == hotIndex) FillRounded(e.Graphics, hover, row, 7);
				if (string.Equals(Modes[index], currentMode, StringComparison.Ordinal))
					e.Graphics.FillEllipse(selected, 15, row.Top + 17, 8, 8);
				e.Graphics.DrawString(Modes[index], font, normal, 31, row.Top + 11);
				if (index < Modes.Length - 1)
					e.Graphics.DrawLine(separator, 13, row.Bottom, Width - 13, row.Bottom);
			}
		}
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

	private static void FillRounded(Graphics graphics, Brush brush,
		Rectangle rectangle, int radius)
	{
		using (var path = RoundedPath(rectangle, radius)) graphics.FillPath(brush, path);
	}

	private static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
	{
		var path = new GraphicsPath();
		int diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
		path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
		path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
		path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter,
			diameter, diameter, 0, 90);
		path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
		path.CloseFigure();
		return path;
	}
}
