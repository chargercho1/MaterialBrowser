using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MaterialBrowser
{
    /// <summary>Shared chrome for the app's frameless dialogs.</summary>
    public class DialogBase : Form
    {
        protected const int TitleHeight = 56;

        public DialogBase(int width, int height)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ClientSize = new Size(width, height);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                Native.SetImmersiveDarkMode(Handle, Ui.T.Dark);
                Native.SetCornerPreference(Handle, true);
                IntPtr rgn = Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, Ui.PX(28) * 2, Ui.PX(28) * 2);
                Native.SetWindowRgn(Handle, rgn, true);
            }
            catch { }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < TitleHeight)
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Scheme s = Ui.T;
            using (var b = new SolidBrush(s.SurfaceHigh)) g.FillRectangle(b, ClientRectangle);
            using (var pen = new Pen(Scheme.WithAlpha(s.OutlineVariant, 160)))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Filled action button used by every dialog.</summary>
    public class FilledButton : Control
    {
        bool _hover;
        readonly Rippler _ripple;
        bool _primary;

        public bool Primary
        {
            get { return _primary; }
            set { _primary = value; Invalidate(); }
        }

        public FilledButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Height = Ui.PX(40);
            _ripple = new Rippler(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Scheme s = Ui.T;
            Color bg = _primary ? s.Accent() : s.SurfaceHighest;
            if (_hover && Enabled) bg = Ui.Hover(bg, s.Dark, s.Dark ? 0.14 : -0.06);
            if (!Enabled) bg = Scheme.WithAlpha(s.OnSurface, s.Dark ? 24 : 18);
            Ui.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), Height / 2, bg);
            Color fg = _primary ? (s.Dark ? s.Primary.Tone(20) : Color.White) : s.OnSurface;
            if (!Enabled) fg = Scheme.WithAlpha(s.OnSurfaceVariant, 110);
            using (var b = new SolidBrush(fg))
                g.DrawString(Text, Ui.Label, b, ClientRectangle,
                    new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center });
            _ripple.Draw(g);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) _ripple.Trigger(e.Location, ClientRectangle, Ui.T.OnSurface);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    }

    /// <summary>Single-line text input with inline validation, used for rename and new folder.</summary>
    public class InputDialog : DialogBase
    {
        readonly MaterialTextField _input;
        readonly FilledButton _ok;
        readonly FilledButton _cancel;
        readonly Label _error = new Label();
        readonly Label _title = new Label();
        readonly string _titleText;
        readonly Func<string, string> _validate;

        public string Result1 { get { return _input.Text2.Trim(); } }

        public InputDialog(string title, string prompt, string initial, Func<string, string> validate)
            : base(Ui.PX(420), Ui.PX(216))
        {
            _titleText = title;
            _validate = validate;

            _title.Text = title;
            _title.Font = Ui.Title;
            _title.ForeColor = Ui.T.OnSurface;
            _title.BackColor = Color.Transparent;
            _title.SetBounds(Ui.PX(24), Ui.PX(20), Ui.PX(360), Ui.PX(26));

            var promptLabel = new Label();
            promptLabel.Text = prompt;
            promptLabel.Font = Ui.Caption;
            promptLabel.ForeColor = Ui.T.OnSurfaceVariant;
            promptLabel.BackColor = Color.Transparent;
            promptLabel.SetBounds(Ui.PX(24), Ui.PX(52), Ui.PX(370), Ui.PX(20));

            _input = new MaterialTextField();
            _input.Font = Ui.Body;
            _input.ForeColor = Ui.T.OnSurface;
            _input.SetBounds(Ui.PX(24), Ui.PX(76), Width - Ui.PX(48), Ui.PX(26));
            _input.SetText(initial ?? "");
            _input.TextEdited += delegate { CheckInput(); };
            _input.EnterPressed += delegate { if (Accept()) Close(); };

            _error.Font = Ui.Caption;
            _error.ForeColor = Ui.T.OnError;
            _error.BackColor = Color.Transparent;
            _error.SetBounds(Ui.PX(24), Ui.PX(106), Width - Ui.PX(48), Ui.PX(20));

            _cancel = new FilledButton();
            _cancel.Text = "Отмена";
            _cancel.SetBounds(Width - Ui.PX(124), Height - Ui.PX(60), Ui.PX(100), Ui.PX(40));
            _cancel.Click += delegate { Close(); };

            _ok = new FilledButton();
            _ok.Text = "ОК";
            _ok.Primary = true;
            _ok.SetBounds(Width - Ui.PX(212), Height - Ui.PX(60), Ui.PX(100), Ui.PX(40));
            _ok.Click += delegate { if (Accept()) Close(); };

            Controls.Add(_title);
            Controls.Add(promptLabel);
            Controls.Add(_input);
            Controls.Add(_error);
            Controls.Add(_cancel);
            Controls.Add(_ok);

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _input.Focus();
            _input.SetText(_input.Text2);
            CheckInput();
        }

        bool Accept()
        {
            CheckInput();
            return _error.Text.Length == 0;
        }

        void CheckInput()
        {
            string text = _input.Text2.Trim();
            string message = "";
            if (text.Length == 0) message = "Введите имя";
            else if (_validate != null) message = _validate(text);
            _error.Text = message;
            _ok.Enabled = message.Length == 0;
            _ok.Invalidate();
        }
    }

    /// <summary>Yes/no prompt in the app's style, with an optional danger tone.</summary>
    public class ConfirmDialog : DialogBase
    {
        readonly FilledButton _ok;
        readonly FilledButton _cancel;

        public bool Confirmed { get; private set; }

        public ConfirmDialog(string title, string message, string okText, string cancelText, string icon, bool danger)
            : base(Ui.PX(430), Ui.PX(190))
        {
            var iconLabel = new Label();
            iconLabel.Text = "";
            iconLabel.BackColor = Color.Transparent;
            iconLabel.SetBounds(Ui.PX(24), Ui.PX(26), Ui.PX(36), Ui.PX(36));
            PaintIcon(iconLabel, icon);

            var titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Font = Ui.Title;
            titleLabel.ForeColor = Ui.T.OnSurface;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.SetBounds(Ui.PX(72), Ui.PX(24), Width - Ui.PX(96), Ui.PX(26));

            var body = new Label();
            body.Text = message;
            body.Font = Ui.Body;
            body.ForeColor = Ui.T.OnSurfaceVariant;
            body.BackColor = Color.Transparent;
            body.SetBounds(Ui.PX(72), Ui.PX(56), Width - Ui.PX(96), Ui.PX(60));

            _cancel = new FilledButton();
            _cancel.Text = cancelText;
            _cancel.SetBounds(Width - Ui.PX(212), Height - Ui.PX(60), Ui.PX(100), Ui.PX(40));
            _cancel.Click += delegate { Close(); };

            _ok = new FilledButton();
            _ok.Text = okText;
            _ok.Primary = !danger;
            _ok.SetBounds(Width - Ui.PX(100), Height - Ui.PX(60), Ui.PX(76), Ui.PX(40));
            _ok.Click += delegate { Confirmed = true; Close(); };

            Controls.Add(iconLabel);
            Controls.Add(titleLabel);
            Controls.Add(body);
            Controls.Add(_cancel);
            Controls.Add(_ok);

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
        }

        void PaintIcon(Control host, string icon)
        {
            host.Paint += delegate (object s, PaintEventArgs e)
            {
                Scheme sc = Ui.T;
                Color color = sc.Accent();
                if (icon == "warning") color = sc.Error.Tone(sc.Dark ? 70 : 40);
                Icons.Draw(e.Graphics, icon, host.ClientRectangle, color, Ui.PX(1.7f));
            };
        }

        public static bool Ask(Form owner, string title, string message, string okText, string cancelText, string icon, bool danger)
        {
            using (var dlg = new ConfirmDialog(title, message, okText, cancelText, icon, danger))
            {
                dlg.ShowDialog(owner);
                return dlg.Confirmed;
            }
        }
    }

    /// <summary>File or folder properties, including a recursive size measured off the UI thread.</summary>

    /// <summary>Determinate progress window for copy and move, with a working cancel button.</summary>
}
