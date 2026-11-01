using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Windows.Forms;

namespace QQMusicWallpaper;

internal sealed class PlaybackModeOverlayForm : Form
{
	private static readonly string[] Modes =
	{
		"随机播放", "顺序播放", "单曲循环", "列表循环"
	};

	private readonly Timer hideTimer;
	private readonly Dictionary<string, Button> modeButtons =
		new Dictionary<string, Button>(StringComparer.Ordinal);
	private string currentMode = "列表循环";

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

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = Modes.Length,
			Padding = new Padding(7, 8, 7, 8),
			BackColor = BackColor
		};
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		for (int index = 0; index < Modes.Length; index++)
		{
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
			string mode = Modes[index];
			Button button = new Button
			{
				Dock = DockStyle.Fill,
				FlatStyle = FlatStyle.Flat,
				FlatAppearance = { BorderSize = 0 },
				BackColor = Color.Transparent,
				ForeColor = Color.FromArgb(38, 38, 42),
				Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular,
					GraphicsUnit.Point),
				Text = mode,
				TextAlign = ContentAlignment.MiddleLeft,
				Padding = new Padding(18, 0, 0, 0),
				Margin = Padding.Empty,
				Tag = mode,
				TabStop = false,
				UseVisualStyleBackColor = false
			};
			button.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 238, 241);
			button.Click += OnModeButtonClick;
			modeButtons.Add(mode, button);
			layout.Controls.Add(button, 0, index);
		}
		Controls.Add(layout);
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
		UpdateButtonStyles();
		Rectangle work = Screen.FromPoint(anchor.IsEmpty ? Cursor.Position :
			new Point(anchor.Left + anchor.Width / 2, anchor.Top)).WorkingArea;
		int x = anchor.IsEmpty ? work.Left + (work.Width - Width) / 2 :
			anchor.Left + (anchor.Width - Width) / 2;
		int y = anchor.IsEmpty ? work.Bottom - Height - 70 : anchor.Top - Height - 10;
		Location = new Point(Math.Max(work.Left + 4, Math.Min(x, work.Right - Width - 4)),
			Math.Max(work.Top + 4, Math.Min(y, work.Bottom - Height - 4)));
		ApplyRoundedRegion();
		if (!Visible) Show();
		hideTimer.Stop();
		hideTimer.Start();
	}

	private void OnModeButtonClick(object sender, EventArgs e)
	{
		if (!(sender is Button button) || !(button.Tag is string mode)) return;
		hideTimer.Stop();
		Hide();
		Action<string> handler = ModeRequested;
		if (handler != null) handler(mode);
	}

	private void UpdateButtonStyles()
	{
		foreach (KeyValuePair<string, Button> item in modeButtons)
		{
			bool selected = string.Equals(item.Key, currentMode,
				StringComparison.Ordinal);
			item.Value.Text = selected ? "●  " + item.Key : "    " + item.Key;
			item.Value.ForeColor = selected ? Color.FromArgb(211, 44, 91) :
				Color.FromArgb(38, 38, 42);
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
