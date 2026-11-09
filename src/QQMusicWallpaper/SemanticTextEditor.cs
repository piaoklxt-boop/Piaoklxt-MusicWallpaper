using System;
using System.Drawing;
using System.Windows.Forms;

namespace QQMusicWallpaper;

// A real text control provides IME/clipboard/selection without focusing or
// exposing the parked source. Only a verified editable MSAA node can open it.
internal sealed class SemanticTextEditor : Form
{
    private readonly TextBox text;
    private readonly Label status;
    private bool submitting;

    internal SemanticTextEditor(Rectangle anchor, Rectangle work, string value,
        Action<string, Action<bool>> submit)
    {
        Text = "QQ 音乐 · 输入";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        Font = SystemFonts.MessageBoxFont;
        int width = Math.Min(work.Width, Math.Max(240, anchor.Width));
        ClientSize = new Size(width, 64);
        Location = new Point(Math.Max(work.Left, Math.Min(anchor.Left, work.Right - Width)),
            Math.Max(work.Top, Math.Min(anchor.Top, work.Bottom - Height)));
        text = new TextBox { Name = "SourceText", Text = value, Dock = DockStyle.Top };
        status = new Label { Text = "Enter 提交 · Esc 取消", Dock = DockStyle.Bottom, Height = 28 };
        Controls.Add(text); Controls.Add(status);
        KeyPreview = true;
        Shown += delegate { text.Focus(); text.SelectAll(); };
        Deactivate += delegate { if (!submitting) Close(); };
        KeyDown += (sender, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
            if (e.KeyCode != Keys.Enter || submitting) return;
            e.SuppressKeyPress = true;
            submitting = true; text.Enabled = false;
            status.Text = "正在提交…";
            submit(text.Text, success =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed) return;
                        if (success) Close();
                        else { submitting = false; text.Enabled = true; status.Text = "控件已变化或不支持写入，请重新打开输入框。"; }
                    });
                }
                catch (InvalidOperationException) { /* Closed while submitting. */ }
            });
        };
    }
}
