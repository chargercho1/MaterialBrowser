using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MaterialBrowser
{
    // ─── Design tokens ─────────────────────────────────────────────
    public static class Ui
    {
        public static float S = 1f;
        public static string Family = "Segoe UI";
        public static string FamilyDisplay = "Segoe UI";

        public static Font Display;
        public static Font Title;
        public static Font Body;
        public static Font Label;
        public static Font Caption;
        public static Font Mono;

        public static Scheme T { get { return AppTheme.Current; } }

        public static void Init(Control reference)
        {
            try { S = Math.Max(1f, reference.DeviceDpi / 96f); } catch { S = 1f; }

            var installed = new InstalledFontCollection();
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FontFamily ff in installed.Families) names.Add(ff.Name);

            string[] displayCandidates = { "Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI", "Tahoma" };
            string[] bodyCandidates = { "Segoe UI Variable Text", "Segoe UI", "Tahoma" };
            FamilyDisplay = Pick(names, displayCandidates);
            Family = Pick(names, bodyCandidates);

            Display = new Font(FamilyDisplay, 21f * S, FontStyle.Regular, GraphicsUnit.Point);
            Title = new Font(FamilyDisplay, 12f * S, FontStyle.Regular, GraphicsUnit.Point);
            Body = new Font(Family, 9.5f * S, FontStyle.Regular, GraphicsUnit.Point);
            Label = new Font(Family, 9.5f * S, FontStyle.Regular, GraphicsUnit.Point);
            Caption = new Font(Family, 8f * S, FontStyle.Regular, GraphicsUnit.Point);
            Mono = new Font("Consolas", 9f * S, FontStyle.Regular, GraphicsUnit.Point);
        }

        static string Pick(System.Collections.Generic.HashSet<string> names, string[] candidates)
        {
            foreach (string c in candidates) if (names.Contains(c)) return c;
            return candidates[candidates.Length - 1];
        }

        public static int PX(int v) { return (int)Math.Round(v * S); }
        public static int PX(float v) { return (int)Math.Round(v * S); }
        public static float FX(float v) { return v * S; }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = Math.Max(1, radius * 2);
            if (radius * 2 > r.Width) d = Math.Max(1, r.Width);
            if (radius * 2 > r.Height) d = Math.Max(1, r.Height);
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - 1 - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - 1 - d, r.Bottom - 1 - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - 1 - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            return Round(Rectangle.Round(r), (int)Math.Round(radius));
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            using (var b = new SolidBrush(color))
            using (GraphicsPath p = Round(r, radius))
                g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            using (var pen = new Pen(color, width))
            using (GraphicsPath p = Round(r, radius))
                g.DrawPath(pen, p);
        }

        public static Color Hover(Color baseColor, bool dark, double amount)
        {
            return Scheme.Mix(baseColor, dark ? Color.White : Color.Black, amount);
        }

        /// <summary>
        /// Hover/state-layer tint for a surface. Must be derived from the surface colour,
        /// not from OnSurface - in a dark theme OnSurface is nearly white and mixing it
        /// towards white paints an opaque light block over the row.
        /// </summary>
        public static Color StateLayer(Scheme s, double amount)
        {
            return Scheme.Mix(s.SurfaceHigh, s.Dark ? Color.White : Color.Black, amount);
        }

        /// <summary>Routes the mouse wheel from a child control to the nearest scrollable host.</summary>
        public static void BubbleWheel(Control from, MouseEventArgs e)
        {
            Control c = from.Parent;
            while (c != null)
            {
                var host = c as ScrollHost;
                if (host != null) { host.ScrollBy(-e.Delta / 120 * PX(64)); return; }
                c = c.Parent;
            }
        }

        public static string Ellipsis(string text, Font font, int width, Graphics g)
        {
            if (string.IsNullOrEmpty(text)) return "";
            SizeF s = g.MeasureString(text, font);
            if (s.Width <= width) return text;
            while (text.Length > 1)
            {
                text = text.Substring(0, text.Length - 1);
                s = g.MeasureString(text + "…", font);
                if (s.Width <= width) break;
            }
            return text + "…";
        }
    }

    // ─── Ripple feedback ───────────────────────────────────────────
    public class Rippler
    {
        private readonly Control _owner;
        private readonly Timer _timer;
        private float _progress;
        private Point _origin;
        private Rectangle _bounds;
        private Color _color;
        private bool _active;

        public Rippler(Control owner)
        {
            _owner = owner;
            _timer = new Timer();
            _timer.Interval = 16;
            _timer.Tick += OnTick;
        }

        public void Trigger(Point local, Rectangle bounds, Color color)
        {
            _origin = local;
            _bounds = bounds;
            _color = color;
            _progress = 0f;
            _active = true;
            _timer.Start();
            _owner.Invalidate();
        }

        private void OnTick(object sender, EventArgs e)
        {
            _progress += 0.08f;
            if (_progress >= 1f)
            {
                _progress = 1f;
                _active = false;
                _timer.Stop();
            }
            _owner.Invalidate();
        }

        public bool Active { get { return _active; } }

        public void Draw(Graphics g)
        {
            if (_progress <= 0f) return;
            float maxRadius = Math.Max(_bounds.Width, _bounds.Height) * 1.2f;
            float radius = maxRadius * EaseOut(_progress);
            int alpha = (int)(52 * (1 - _progress));
            if (alpha <= 0) return;
            using (var brush = new SolidBrush(Color.FromArgb(alpha, _color.R, _color.G, _color.B)))
                g.FillEllipse(brush, _origin.X - radius, _origin.Y - radius, radius * 2, radius * 2);
        }

        static float EaseOut(float t)
        {
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }
    }

    // ─── Material switch ───────────────────────────────────────────
    public class MaterialSwitch : Control
    {
        private float _progress;
        private bool _checked;
        private readonly Rippler _ripple;
        private readonly Timer _anim;
        private float _target;

        public event EventHandler ValueChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                _target = value ? 1f : 0f;
                _anim.Start();
                Invalidate();
                EventHandler h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public MaterialSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            _ripple = new Rippler(this);
            _anim = new Timer();
            _anim.Interval = 16;
            _anim.Tick += OnAnim;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        private void OnAnim(object sender, EventArgs e)
        {
            float delta = _target - _progress;
            if (Math.Abs(delta) < 0.06f)
            {
                _progress = _target;
                _anim.Stop();
            }
            else _progress += delta * 0.28f;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Scheme s = Ui.T;

            int h = Math.Min(Height, Ui.PX(32));
            int w = Ui.PX(52);
            int y = (Height - h) / 2;
            var track = new Rectangle(0, y, w, h);

            Color offTrack = s.Dark ? s.SurfaceHighest : s.SurfaceVariant;
            Color offBorder = s.Outline;
            Color onTrack = s.Accent();
            Color trackColor = Scheme.Mix(offTrack, onTrack, _progress);

            GraphicsPath path = Ui.Round(track, h / 2);
            using (var b = new SolidBrush(trackColor)) g.FillPath(b, path);
            using (var pen = new Pen(Scheme.Mix(offBorder, onTrack, _progress), 2f)) g.DrawPath(pen, path);

            int thumb = Ui.PX(16);
            int pad = Ui.PX(4);
            int travel = w - thumb - pad * 2;
            float x = pad + travel * _progress;
            var thumbRect = new RectangleF(x, track.Y + pad, thumb, thumb);
            Color thumbColor = s.Dark ? Scheme.Mix(s.OnSurface, s.Accent(), _progress)
                                      : Scheme.Mix(Color.White, s.Accent(), _progress);

            if (_progress > 0.55f)
            {
                using (var b = new SolidBrush(Scheme.WithAlpha(onTrack, 60)))
                    g.FillEllipse(b, new RectangleF(thumbRect.X - Ui.PX(3), thumbRect.Y - Ui.PX(3),
                        thumbRect.Width + Ui.PX(6), thumbRect.Height + Ui.PX(6)));
            }
            using (var b = new SolidBrush(thumbColor)) g.FillEllipse(b, thumbRect);
            using (var pen = new Pen(Scheme.Mix(offBorder, onTrack, _progress), 2f))
                g.DrawEllipse(pen, thumbRect);

            _ripple.Draw(g);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int h = Math.Min(Height, Ui.PX(32));
            var track = new Rectangle(0, (Height - h) / 2, Ui.PX(52), h);
            _ripple.Trigger(new Point(e.X, e.Y), track, Ui.T.Accent());
            Checked = !Checked;
        }
    }

    // ─── Material slider ───────────────────────────────────────────
    public class MaterialSlider : Control
    {
        private int _value;
        private int _min = 10;
        private int _max = 100;
        private int _step = 1;
        private bool _dragging;
        private bool _hover;
        private readonly Rippler _ripple;
        private readonly Timer _anim;
        private float _visual;
        private float _visualTarget;

        public event EventHandler ValueChanged;

        public int Minimum { get { return _min; } set { _min = value; Invalidate(); } }
        public int Maximum { get { return _max; } set { _max = value; Invalidate(); } }
        public int StepSize { get { return _step; } set { _step = Math.Max(1, value); Invalidate(); } }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(_min, Math.Min(_max, value));
                if (v == _value) return;
                _value = v;
                _visualTarget = Percent();
                _anim.Start();
                Invalidate();
                EventHandler h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public MaterialSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            _ripple = new Rippler(this);
            _anim = new Timer();
            _anim.Interval = 16;
            _anim.Tick += OnAnim;
            Height = Ui.PX(48);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            TabStop = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _visual = _visualTarget = Percent();
        }

        private float Percent()
        {
            if (_max <= _min) return 0f;
            return (float)(_value - _min) / (_max - _min);
        }

        private void OnAnim(object sender, EventArgs e)
        {
            float d = _visualTarget - _visual;
            if (Math.Abs(d) < 0.004f) { _visual = _visualTarget; _anim.Stop(); }
            else _visual += d * 0.3f;
            Invalidate();
        }

        private Rectangle TrackBounds()
        {
            int pad = Ui.PX(10);
            int cy = Height / 2 + Ui.PX(6);
            return new Rectangle(pad, cy - Ui.PX(3), Width - pad * 2, Ui.PX(6));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Scheme s = Ui.T;
            Rectangle track = TrackBounds();

            using (var b = new SolidBrush(s.Dark ? s.SurfaceHighest : s.SurfaceVariant))
            using (GraphicsPath p = Ui.Round(track, track.Height / 2))
                g.FillPath(b, p);

            int active = track.X + (int)(track.Width * _visual);
            if (active > track.X)
            {
                var act = new Rectangle(track.X, track.Y, active - track.X, track.Height);
                using (var b = new SolidBrush(s.Accent()))
                using (GraphicsPath p = Ui.Round(act, track.Height / 2))
                    g.FillPath(b, p);
            }

            int thumbSize = _hover || _dragging ? Ui.PX(22) : Ui.PX(18);
            var thumb = new Rectangle(active - thumbSize / 2, track.Y + track.Height / 2 - thumbSize / 2,
                thumbSize, thumbSize);
            using (var b = new SolidBrush(s.Accent())) g.FillEllipse(b, thumb);
            using (var pen = new Pen(s.Dark ? s.OnSurface : Color.White, Ui.PX(2.5f))) g.DrawEllipse(pen, thumb);

            if (_dragging || _hover)
            {
                using (var b = new SolidBrush(Scheme.WithAlpha(s.Accent(), 40)))
                    g.FillEllipse(b, Rectangle.Inflate(thumb, Ui.PX(5), Ui.PX(5)));
            }
            _ripple.Draw(g);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _dragging = true;
            _ripple.Trigger(new Point(e.X, e.Y), TrackBounds(), Ui.T.Accent());
            SetFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        private void SetFromX(int x)
        {
            Rectangle track = TrackBounds();
            double t = (x - track.X) / (double)Math.Max(1, track.Width);
            t = Math.Max(0, Math.Min(1, t));
            int raw = _min + (int)Math.Round(t * (_max - _min));
            Value = _min + (int)(Math.Round((double)(raw - _min) / _step) * _step);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) { Value -= _step; e.Handled = true; }
            if (e.KeyCode == Keys.Right) { Value += _step; e.Handled = true; }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    }

    // ─── Window control button (minimize / maximize / close) ────────
    public class WindowButton : Control
    {
        private string _icon = "minimize";
        private bool _hover;
        private bool _isClose;

        public event EventHandler Clicked;

        public string IconName
        {
            get { return _icon; }
            set { _icon = value; Invalidate(); }
        }

        public bool IsCloseButton
        {
            get { return _isClose; }
            set { _isClose = value; Invalidate(); }
        }

        public WindowButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(Ui.PX(46), Ui.PX(36));
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Scheme s = Ui.T;

            Color bg = Color.Transparent;
            Color fg = s.OnSurfaceVariant;
            if (_hover)
            {
                if (_isClose)
                {
                    bg = s.Dark ? Scheme.Mix(s.ErrorContainer, Color.Black, 0.35) : s.ErrorContainer;
                    fg = s.Dark ? s.OnError : s.OnErrorContainer;
                }
                else
                {
                    bg = Ui.Hover(s.SurfaceHigh, s.Dark, 0.14);
                    fg = s.OnSurface;
                }
            }
            if (bg.A > 0) Ui.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), Ui.PX(10), bg);

            int pad = Ui.PX(12);
            Icons.Draw(g, _icon, new Rectangle(pad, (Height - (Width - pad * 2)) / 2, Width - pad * 2, Width - pad * 2),
                fg, Ui.PX(1.7f));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            {
                EventHandler h = Clicked;
                if (h != null) h(this, EventArgs.Empty);
            }
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    }

    // ─── Ripple icon button ────────────────────────────────────────
    public class IconButton : Control
    {
        private string _icon = "info";
        private bool _hover;
        private bool _active;
        private readonly Rippler _ripple;

        public event EventHandler Clicked;

        public string IconName
        {
            get { return _icon; }
            set { _icon = value; Invalidate(); }
        }

        public bool ActiveState
        {
            get { return _active; }
            set { _active = value; Invalidate(); }
        }

        public IconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            _ripple = new Rippler(this);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Scheme s = Ui.T;
            Color bg = _active ? s.SecondaryContainer : (_hover ? Ui.Hover(s.SurfaceHigh, s.Dark, 0.10) : Color.Transparent);
            if (bg.A > 0) Ui.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), Height / 2, bg);

            Color fg = _active ? s.OnSecondaryContainer : s.OnSurfaceVariant;
            if (!Enabled) fg = Scheme.WithAlpha(s.OnSurfaceVariant, 70);
            int pad = Ui.PX(9);
            Icons.Draw(g, _icon, new Rectangle(pad, pad, Width - pad * 2, Height - pad * 2), fg, Ui.PX(1.8f));
            _ripple.Draw(g);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _ripple.Trigger(new Point(e.X, e.Y), new Rectangle(0, 0, Width, Height),
                _active ? Ui.T.Accent() : Ui.T.OnSurface);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            {
                EventHandler h = Clicked;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    }

    // ─── Search field ──────────────────────────────────────────────
    public class SearchBox : Panel
    {
        private readonly MaterialTextField _input;
        private readonly IconButton _clear;
        private bool _hover;
        private bool _focused;

        public event EventHandler QueryChanged;

        public string Query
        {
            get { return _input == null ? "" : _input.Text2; }
        }

        public SearchBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Height = Ui.PX(46);
            BackColor = Color.Transparent;

            _input = new MaterialTextField();
            _input.ForeColor = Ui.T.OnSurface;
            _input.Font = Ui.Body;
            _input.Location = new Point(Ui.PX(46), Ui.PX(11));
            _input.Size = new Size(Math.Max(10, Width - Ui.PX(96)), Ui.PX(26));
            _input.TextEdited += delegate
            {
                if (_clear != null) _clear.Visible = _input.Text2.Length > 0;
                Invalidate();
                EventHandler h = QueryChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            _input.GotFocus += delegate { _focused = true; Invalidate(); };
            _input.LostFocus += delegate { _focused = false; Invalidate(); };

            _clear = new IconButton();
            _clear.IconName = "close";
            _clear.Size = new Size(Ui.PX(34), Ui.PX(34));
            _clear.Location = new Point(Width - Ui.PX(42), Ui.PX(6));
            _clear.Visible = false;
            _clear.Clicked += delegate
            {
                _input.SetText("");
                _input.Focus();
            };

            Controls.Add(_input);
            Controls.Add(_clear);
        }

        public void FocusInput() { if (_input != null) _input.Focus(); }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        public void Clear() { if (_input != null) _input.SetText(""); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_input == null || _clear == null) return;
            _input.Width = Math.Max(10, Width - Ui.PX(100));
            _clear.Location = new Point(Width - Ui.PX(42), Ui.PX(6));
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Scheme s = Ui.T;
            if (_input == null) return;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill = _focused ? s.SurfaceHigh : (_hover ? Ui.Hover(s.SurfaceHigh, s.Dark, 0.05) : s.SurfaceHigh);
            Ui.FillRound(g, r, Height / 2, Scheme.WithAlpha(fill, _focused ? 235 : 215));
            if (_focused) Ui.StrokeRound(g, r, Height / 2, s.Accent(), Ui.PX(1.6f));

            Icons.Draw(g, "search", new Rectangle(Ui.PX(14), Height / 2 - Ui.PX(9), Ui.PX(18), Ui.PX(18)),
                s.OnSurfaceVariant, Ui.PX(1.7f));

            if (_input.Text2.Length == 0 && !_focused)
            {
                using (var b = new SolidBrush(s.OnSurfaceVariant))
                    g.DrawString("Фильтр по имени", Ui.Body, b,
                        new Rectangle(Ui.PX(46), 0, Width - Ui.PX(60), Height),
                        new StringFormat { LineAlignment = StringAlignment.Center });
            }
        }
    }

    // ─── Snackbar ──────────────────────────────────────────────────
    public class Snackbar : Control
    {
        private readonly Timer _timer;
        private readonly Label _label;
        private string _text = "";
        private int _alpha;

        public void Show(string text)
        {
            _text = text ?? "";
            _alpha = 255;
            _label.Text = _text;
            _timer.Stop();
            _timer.Start();
            Visible = true;
            Invalidate();
        }

        public Snackbar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Height = Ui.PX(46);
            BackColor = Color.Transparent;
            _label = new Label();
            _label.AutoSize = false;
            _label.TextAlign = ContentAlignment.MiddleCenter;
            _label.BackColor = Color.Transparent;
            _label.Font = Ui.Body;
            _label.ForeColor = Ui.T.OnSurface;
            _label.Dock = DockStyle.Fill;
            _label.Padding = new Padding(Ui.PX(20), 0, Ui.PX(20), 0);
            Controls.Add(_label);

            _timer = new Timer();
            _timer.Interval = 60;
            _timer.Tick += OnTick;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_alpha <= 0) { _timer.Stop(); Visible = false; Invalidate(); return; }
            _alpha -= 12;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Scheme s = Ui.T;
            Color bg = Scheme.WithAlpha(s.Dark ? Color.FromArgb(60, 60, 66) : Color.FromArgb(238, 240, 246), _alpha);
            Ui.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), Ui.PX(14), bg);
            _label.ForeColor = Scheme.WithAlpha(s.OnSurface, _alpha);
        }
    }
}