using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MaterialBrowser
{
    /// <summary>The omnibox: scheme icon, editable text, bookmark star and private-mode shield.</summary>
    public class AddressBar : Panel
    {
        readonly MaterialTextField _input;
        readonly IconButton _star;
        readonly IconButton _shield;
        bool _hover;
        bool _focused;
        bool _loading;
        string _placeholder = "Поиск или адрес";

        public event EventHandler Submitted;
        public event EventHandler StarClicked;
        public event EventHandler PrivateClicked;

        public AddressBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = Ui.PX(46);

            _input = new MaterialTextField();
            _input.Font = Ui.Body;
            _input.ForeColor = Ui.T.OnSurface;
            _input.Location = new Point(Ui.PX(44), Ui.PX(11));
            _input.Size = new Size(Math.Max(10, Width - Ui.PX(132)), Ui.PX(26));
            _input.EnterPressed += delegate { Submit(); };
            _input.GotFocus += delegate { _focused = true; Invalidate(); };
            _input.LostFocus += delegate { _focused = false; Invalidate(); };
            _input.TextEdited += delegate { Invalidate(); };

            _star = new IconButton();
            _star.IconName = "star";
            _star.Size = new Size(Ui.PX(34), Ui.PX(34));
            _star.Location = new Point(Width - Ui.PX(76), Ui.PX(6));
            _star.Clicked += delegate { EventHandler h = StarClicked; if (h != null) h(this, EventArgs.Empty); };

            _shield = new IconButton();
            _shield.IconName = "lock";
            _shield.Size = new Size(Ui.PX(34), Ui.PX(34));
            _shield.Location = new Point(Width - Ui.PX(40), Ui.PX(6));
            _shield.ActiveState = true;
            _shield.Clicked += delegate { EventHandler h = PrivateClicked; if (h != null) h(this, EventArgs.Empty); };

            Controls.Add(_input);
            Controls.Add(_star);
            Controls.Add(_shield);
        }

        public string Text2 { get { return _input.Text2; } }

        public void SetText(string text)
        {
            _input.SetText(text ?? "");
            _input.SetText(_input.Text2);
        }

        public void SetPlaceholder(string text) { _placeholder = text; Invalidate(); }

        public void FocusInput() { if (_input != null) _input.Focus(); }

        public void SetLoading(bool loading) { _loading = loading; Invalidate(); }

        public void SetStarred(bool starred)
        {
            _star.ActiveState = starred;
            _star.Invalidate();
        }

        public void SetPrivate(bool isPrivate)
        {
            _shield.IconName = isPrivate ? "shield" : "lock";
            _shield.ActiveState = isPrivate;
            _shield.Invalidate();
        }

        void Submit()
        {
            EventHandler h = Submitted;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_input == null) return;
            _input.Width = Math.Max(10, Width - Ui.PX(130));
            _star.Location = new Point(Width - Ui.PX(76), Ui.PX(6));
            _shield.Location = new Point(Width - Ui.PX(40), Ui.PX(6));
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.X < Ui.PX(40)) _input.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Scheme s = Ui.T;
            if (_input == null) return;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill = _focused ? s.SurfaceHigh : (_hover ? Ui.Hover(s.SurfaceHigh, s.Dark, 0.06) : s.SurfaceHigh);
            Ui.FillRound(g, r, Height / 2, Scheme.WithAlpha(fill, _focused ? 240 : 215));
            if (_focused) Ui.StrokeRound(g, r, Height / 2, s.Accent(), Ui.PX(1.6f));
            else if (_hover) Ui.StrokeRound(g, r, Height / 2, Scheme.WithAlpha(s.OutlineVariant, 160), 1f);

            string icon = "globe";
            if (_loading) icon = "refresh2";
            else if (_input.Text2.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) icon = "lock";
            Icons.Draw(g, icon, new Rectangle(Ui.PX(15), Height / 2 - Ui.PX(9), Ui.PX(18), Ui.PX(18)),
                s.OnSurfaceVariant, Ui.PX(1.6f));

            if (_input.Text2.Length == 0 && !_focused)
            {
                using (var brush = new SolidBrush(s.OnSurfaceVariant))
                    g.DrawString(_placeholder, Ui.Body, brush, new Rectangle(Ui.PX(46), 0, Width - Ui.PX(180), Height),
                        new StringFormat { LineAlignment = StringAlignment.Center });
            }
        }
    }

    /// <summary>Thin indeterminate bar shown while a page loads.</summary>
    public class LoadingBar : Control
    {
        float _offset;
        readonly Timer _timer = new Timer();
        bool _active;

        public LoadingBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = Ui.PX(3);
            _timer.Interval = 16;
            _timer.Tick += delegate
            {
                _offset += 0.035f;
                if (_offset > 1.4f) _offset = -0.4f;
                Invalidate();
            };
        }

        public void Start()
        {
            _active = true;
            Visible = true;
            _timer.Start();
            Invalidate();
        }

        public void Stop()
        {
            _active = false;
            _timer.Stop();
            Visible = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_active) return;
            Scheme s = Ui.T;
            float w = Width * 0.35f;
            float x = (_offset - 0.35f) * Width;
            if (x > Width - w) x = Width - w;
            if (x < 0) x = 0;
            Ui.FillRound(e.Graphics, new Rectangle((int)x, 0, (int)w, Height), Height / 2, s.Accent());
        }
    }

    /// <summary>Row of bookmark chips under the toolbar.</summary>
    public class BookmarksBar : Control
    {
        public class Chip
        {
            public string Title = "";
            public string Url = "";
            public Rectangle Bounds;
            public bool Hover;
        }

        readonly List<Chip> _chips = new List<Chip>();
        int _hover = -1;

        public event EventHandler<string> ChipActivated;

        public BookmarksBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = Ui.PX(36);
        }

        public void Rebuild()
        {
            _chips.Clear();
            foreach (Bookmark b in Bookmarks.Items)
            {
                if (_chips.Count >= 12) break;
                _chips.Add(new Chip { Title = b.Display, Url = b.Url });
            }
            LayoutChips();
            Invalidate();
        }

        void LayoutChips()
        {
            using (Graphics g = CreateGraphics())
            {
                int x = Ui.PX(12);
                foreach (Chip chip in _chips)
                {
                    int w = (int)g.MeasureString(chip.Title, Ui.Caption).Width + Ui.PX(38);
                    if (x + w > Width - Ui.PX(12)) { chip.Bounds = Rectangle.Empty; x += 0; continue; }
                    chip.Bounds = new Rectangle(x, Ui.PX(4), w, Height - Ui.PX(8));
                    x += w + Ui.PX(6);
                }
            }
        }

        int Hit(Point p)
        {
            for (int i = 0; i < _chips.Count; i++)
                if (_chips[i].Bounds.Contains(p)) return i;
            return -1;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_chips.Count > 0 && IsHandleCreated) LayoutChips();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int i = Hit(e.Location);
            if (i < 0) return;
            EventHandler<string> h = ChipActivated;
            if (h != null) h(this, _chips[i].Url);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Scheme s = Ui.T;
            using (var brush = new SolidBrush(s.SurfaceHigh)) g.FillRectangle(brush, ClientRectangle);
            using (var brush = new SolidBrush(Scheme.WithAlpha(s.OutlineVariant, 160)))
                g.FillRectangle(brush, new Rectangle(0, 0, Width, Ui.PX(1)));

            foreach (Chip chip in _chips)
            {
                if (chip.Bounds.IsEmpty) continue;
                if (chip.Hover) Ui.FillRound(g, chip.Bounds, chip.Bounds.Height / 2, Ui.StateLayer(s, s.Dark ? 0.14 : 0.06));
                Icons.Draw(g, "star", new Rectangle(chip.Bounds.X + Ui.PX(9), chip.Bounds.Y + chip.Bounds.Height / 2 - Ui.PX(7), Ui.PX(14), Ui.PX(14)),
                    s.OnSurfaceVariant, Ui.PX(1.4f));
                using (var brush = new SolidBrush(s.OnSurface))
                    g.DrawString(Ui.Ellipsis(chip.Title, Ui.Caption, chip.Bounds.Width - Ui.PX(36), g), Ui.Caption, brush,
                        new Rectangle(chip.Bounds.X + Ui.PX(28), chip.Bounds.Y, chip.Bounds.Width - Ui.PX(36), chip.Bounds.Height),
                        new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
            }
        }
    }

    /// <summary>Left navigation: browser pages plus the most used bookmarks.</summary>
    public class Sidebar : ScrollHost
    {
        class Entry
        {
            public string Title;
            public string Subtitle;
            public string Icon;
            public string Page;
            public Rectangle Bounds;
        }

        readonly List<Entry> _entries = new List<Entry>();
        int _hover = -1;
        string _active = "";

        public event EventHandler<string> PageRequested;

        public Sidebar()
        {
            Width = Ui.PX(248);
        }

        public void Rebuild()
        {
            string previous = _active;
            _entries.Clear();
            _entries.Add(new Entry { Title = "Избранное", Icon = "star", Page = "bookmarks" });
            _entries.Add(new Entry { Title = "История", Icon = "clock", Page = "history" });
            _entries.Add(new Entry { Title = "Загрузки", Icon = "download", Page = "downloads" });
            _entries.Add(new Entry { Title = "Настройки", Icon = "tune", Page = "settings" });

            if (Bookmarks.Items.Count > 0)
            {
                _entries.Add(new Entry { Title = "ЗАКЛАДКИ", Icon = "", Page = "" });
                int added = 0;
                foreach (Bookmark b in Bookmarks.Items)
                {
                    if (added++ >= 8) break;
                    _entries.Add(new Entry { Title = b.Display, Subtitle = WebText.Host(b.Url), Icon = "globe", Page = b.Url });
                }
            }

            _active = previous;
            LayoutEntries();
        }

        int RowHeight { get { return Ui.PX(40); } }
        int HeaderHeight { get { return Ui.PX(28); } }

        internal void LayoutEntries()
        {
            int y = Ui.PX(8);
            foreach (Entry entry in _entries)
            {
                entry.Bounds = new Rectangle(Ui.PX(8), y, Width - Ui.PX(16), entry.Page.Length == 0 ? HeaderHeight : RowHeight);
                y += entry.Bounds.Height;
            }
            SetContentHeight(y + Ui.PX(12));
            Invalidate();
        }

        public void SetActive(string page)
        {
            _active = page ?? "";
            Invalidate();
        }

        int Hit(Point p)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Page.Length > 0 && _entries[i].Bounds.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int i = Hit(e.Location);
            if (i < 0) return;
            EventHandler<string> h = PageRequested;
            if (h != null) h(this, _entries[i].Page);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Scheme s = Ui.T;
            int shift = -ScrollOffset;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                Rectangle box = entry.Bounds;
                box.Offset(0, shift);
                if (box.Bottom < 0 || box.Top > Height) continue;

                if (entry.Page.Length == 0)
                {
                    using (var brush = new SolidBrush(s.OnSurfaceVariant))
                        g.DrawString(entry.Title, Ui.Caption, brush, new PointF(box.X + Ui.PX(12), box.Y + Ui.PX(6)),
                            new StringFormat { LineAlignment = StringAlignment.Center });
                    continue;
                }

                bool active = string.Equals(entry.Page, _active, StringComparison.Ordinal);
                if (active) Ui.FillRound(g, box, Ui.PX(18), s.SecondaryContainer);
                else if (i == _hover) Ui.FillRound(g, box, Ui.PX(18), Ui.StateLayer(s, s.Dark ? 0.14 : 0.06));

                Color fg = active ? s.OnSecondaryContainer : s.OnSurfaceVariant;
                int iconSize = Ui.PX(18);
                Icons.Draw(g, entry.Icon, new Rectangle(box.X + Ui.PX(12), box.Y + (box.Height - iconSize) / 2, iconSize, iconSize), fg, Ui.PX(1.6f));

                int textWidth = box.Width - Ui.PX(44);
                using (var brush = new SolidBrush(fg))
                    g.DrawString(Ui.Ellipsis(entry.Title, Ui.Label, textWidth, g), Ui.Label, brush,
                        new Rectangle(box.X + Ui.PX(42), box.Y, textWidth, box.Height),
                        new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
            }

            DrawBar(g);
        }
    }
}
