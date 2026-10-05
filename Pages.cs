using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MaterialBrowser
{
    // ─── generic list page (bookmarks, history, downloads) ──────────────
    public class ListItemData
    {
        public string Title = "";
        public string Subtitle = "";
        public string Trailing = "";
        public string Icon = "globe";
        public string Payload = "";
        public Color TrailingColor;
        public double Progress = -1;
    }

    public class ListPage : ScrollHost
    {
        public class ContextEventArgs : EventArgs
        {
            public Point Location;
            public ListItemData Item;
            public ContextEventArgs(Point p, ListItemData item) { Location = p; Item = item; }
        }

        readonly List<ListItemData> _items = new List<ListItemData>();
        int _hover = -1;
        int _selected = -1;

        public event EventHandler<string> ItemActivated;
        public event EventHandler<ContextEventArgs> ContextRequested;

        public ListPage()
        {
            TabStop = true;
        }

        public int Count { get { return _items.Count; } }

        public void SetItems(List<ListItemData> items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            _selected = -1;
            _hover = -1;
            SetContentHeight(Ui.PX(10) + _items.Count * RowHeight + Ui.PX(16));
            Invalidate();
        }

        int RowHeight { get { return Ui.PX(58); } }

        public ListItemData Selected { get { return _selected >= 0 && _selected < _items.Count ? _items[_selected] : null; } }

        Rectangle RowBounds(int index)
        {
            return new Rectangle(Ui.PX(8), Ui.PX(10) + index * RowHeight - ScrollOffset, Viewport.Width - Ui.PX(16), RowHeight - Ui.PX(4));
        }

        int Hit(Point p)
        {
            for (int i = 0; i < _items.Count; i++)
                if (RowBounds(i).Contains(p)) return i;
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Right)
            {
                int index = Hit(e.Location);
                if (index < 0) return;
                _selected = index;
                Invalidate();
                EventHandler<ContextEventArgs> h = ContextRequested;
                if (h != null) h(this, new ContextEventArgs(e.Location, _items[index]));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int i = Hit(e.Location);
            _selected = i;
            Invalidate();
            if (i >= 0 && e.Clicks >= 2)
            {
                EventHandler<string> h = ItemActivated;
                if (h != null) h(this, _items[i].Payload);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Scheme s = Ui.T;

            if (_items.Count == 0)
            {
                using (var brush = new SolidBrush(s.OnSurfaceVariant))
                    g.DrawString("Здесь пока пусто", Ui.Body, brush, Viewport,
                        new StringFormat { LineAlignment = StringAlignment.Near, Alignment = StringAlignment.Center });
            }

            int first = Math.Max(0, (ScrollOffset - Ui.PX(10)) / RowHeight);
            int last = Math.Min(_items.Count - 1, (ScrollOffset + Height - Ui.PX(10)) / RowHeight);

            for (int i = first; i <= last; i++)
            {
                ListItemData item = _items[i];
                Rectangle box = RowBounds(i);
                if (i == _selected) Ui.FillRound(g, box, Ui.PX(12), s.SecondaryContainer);
                else if (i == _hover) Ui.FillRound(g, box, Ui.PX(12), Ui.StateLayer(s, s.Dark ? 0.12 : 0.05));

                Color fg = i == _selected ? s.OnSecondaryContainer : s.OnSurface;
                int iconSize = Ui.PX(22);
                Icons.Draw(g, item.Icon, new Rectangle(box.X + Ui.PX(16), box.Y + (box.Height - iconSize) / 2, iconSize, iconSize),
                    i == _selected ? s.OnSecondaryContainer : s.OnSurfaceVariant, Ui.PX(1.6f));

                int trailingWidth = 0;
                if (item.Trailing.Length > 0)
                {
                    trailingWidth = (int)g.MeasureString(item.Trailing, Ui.Caption).Width + Ui.PX(16);
                    using (var brush = new SolidBrush(item.TrailingColor.A > 0 ? item.TrailingColor : s.OnSurfaceVariant))
                        g.DrawString(item.Trailing, Ui.Caption, brush,
                            new RectangleF(box.Right - trailingWidth, box.Y, trailingWidth, box.Height),
                            new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far });
                }

                int textX = box.X + Ui.PX(50);
                int textW = box.Width - Ui.PX(66) - trailingWidth;
                bool twoLine = item.Subtitle.Length > 0;
                using (var brush = new SolidBrush(fg))
                    g.DrawString(Ui.Ellipsis(item.Title, Ui.Label, textW, g), Ui.Label, brush,
                        new Rectangle(textX, twoLine ? box.Y + Ui.PX(8) : box.Y, textW, Ui.PX(22)),
                        new StringFormat { LineAlignment = twoLine ? StringAlignment.Near : StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });

                if (twoLine)
                {
                    using (var brush = new SolidBrush(s.OnSurfaceVariant))
                        g.DrawString(Ui.Ellipsis(item.Subtitle, Ui.Caption, textW, g), Ui.Caption, brush,
                            new Rectangle(textX, box.Y + Ui.PX(30), textW, Ui.PX(18)),
                            new StringFormat { LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter });
                }

                if (item.Progress >= 0)
                {
                    var track = new Rectangle(textX, box.Bottom - Ui.PX(10), textW, Ui.PX(4));
                    Ui.FillRound(g, track, 2, Ui.StateLayer(s, s.Dark ? 0.20 : 0.10));
                    int w = (int)(track.Width * Math.Max(0, Math.Min(1, item.Progress)));
                    if (w > 0) Ui.FillRound(g, new Rectangle(track.X, track.Y, Math.Max(4, w), track.Height), 2, s.Accent());
                }
            }

            DrawBar(g);
        }
    }

    // ─── settings rows ────────────────────────────────────────────────
    public class SettingsRow : Control
    {
        bool _hover;
        readonly Rippler _ripple;

        public string Title = "";
        public string Subtitle = "";
        public string Icon = "info";

        public SettingsRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Height = Ui.PX(72);
            _ripple = new Rippler(this);
        }

        protected virtual void PaintContent(Graphics g, Scheme s, int contentLeft, int contentWidth)
        {
            int iconSize = Ui.PX(22);
            Icons.Draw(g, Icon, new Rectangle(Ui.PX(18), (Height - iconSize) / 2, iconSize, iconSize), s.OnSurfaceVariant, Ui.PX(1.6f));

            bool twoLine = Subtitle.Length > 0;
            using (var brush = new SolidBrush(s.OnSurface))
                g.DrawString(Ui.Ellipsis(Title, Ui.Label, contentWidth, g), Ui.Label, brush,
                    new Rectangle(contentLeft, twoLine ? Ui.PX(12) : 0, contentWidth, Ui.PX(22)),
                    new StringFormat { LineAlignment = twoLine ? StringAlignment.Near : StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
            if (twoLine)
            {
                using (var brush = new SolidBrush(s.OnSurfaceVariant))
                    g.DrawString(Ui.Ellipsis(Subtitle, Ui.Caption, contentWidth, g), Ui.Caption, brush,
                        new Rectangle(contentLeft, Ui.PX(34), contentWidth, Ui.PX(18)),
                        new StringFormat { LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Scheme s = Ui.T;

            if (_hover) Ui.FillRound(g, new Rectangle(0, 2, Width - 1, Height - 5), Ui.PX(12), Ui.StateLayer(s, s.Dark ? 0.10 : 0.045));
            PaintContent(g, s, Ui.PX(52), Width - Ui.PX(76));
            _ripple.Draw(g);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.X > Width - Ui.PX(90)) { if (_hover) { _hover = false; Invalidate(); } return; }
            if (!_hover) { _hover = true; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover) { _hover = false; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) _ripple.Trigger(e.Location, ClientRectangle, Ui.T.OnSurface);
        }
    }

    public class SwitchRow : SettingsRow
    {
        readonly MaterialSwitch _switch;

        public SwitchRow(string title, string subtitle, string icon, bool value, Action<bool> onChange)
            : base()
        {
            Title = title; Subtitle = subtitle; Icon = icon;
            _switch = new MaterialSwitch();
            _switch.Checked = value;
            _switch.Location = new Point(Width - Ui.PX(74), (Height - Ui.PX(40)) / 2);
            _switch.ValueChanged += delegate { if (onChange != null) onChange(_switch.Checked); };
            Controls.Add(_switch);
        }

        public void SetValue(bool value)
        {
            _switch.Checked = value;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _switch.SetBounds(Width - Ui.PX(76), (Height - Ui.PX(40)) / 2, Ui.PX(52), Ui.PX(40));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && e.X < Width - Ui.PX(84) && ClientRectangle.Contains(e.Location))
                _switch.Checked = !_switch.Checked;
        }
    }

    public class SliderRow : SettingsRow
    {
        readonly MaterialSlider _slider;
        Label _value;
        Action<int> _onChange;

        public SliderRow(string title, string subtitle, string icon, int min, int max, int step, int value, Action<int> onChange)
            : base()
        {
            Title = title; Subtitle = subtitle; Icon = icon; _onChange = onChange;
            _slider = new MaterialSlider();
            _slider.Minimum = min; _slider.Maximum = max; _slider.StepSize = step; _slider.Value = value;
            _slider.ValueChanged += delegate { UpdateValue(); if (_onChange != null) _onChange(_slider.Value); };
            Controls.Add(_slider);

            _value = new Label();
            _value.AutoSize = false;
            _value.BackColor = Color.Transparent;
            _value.Font = Ui.Caption;
            _value.ForeColor = Ui.T.OnSurfaceVariant;
            _value.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_value);
            UpdateValue();
        }

        void UpdateValue()
        {
            if (_value != null) _value.Text = _slider.Value + "%";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _slider.SetBounds(Width - Ui.PX(230), Height - Ui.PX(40), Ui.PX(170), Ui.PX(32));
            _value.SetBounds(Width - Ui.PX(56), (Height - Ui.PX(20)) / 2, Ui.PX(40), Ui.PX(20));
        }
    }

    public class ChoiceRow : SettingsRow
    {
        public string Value = "";
        readonly Action _onClick;

        public ChoiceRow(string title, string subtitle, string icon, string value, Action onClick)
            : base()
        {
            Title = title; Subtitle = subtitle; Icon = icon; Value = value; _onClick = onClick;
        }

        public ChoiceRow(string title, string icon, Action onClick)
            : base()
        {
            Title = title; Subtitle = ""; Icon = icon; Value = ""; _onClick = onClick;
        }

        public ChoiceRow(string title, string value, string icon, Action onClick)
            : base()
        {
            Title = title; Subtitle = ""; Icon = icon; Value = value; _onClick = onClick;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Scheme s = Ui.T;
            using (var brush = new SolidBrush(s.Accent()))
                g.DrawString(Value, Ui.Caption, brush,
                    new RectangleF(Width - Ui.PX(230), 0, Ui.PX(170), Height),
                    new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far });
            Icons.Draw(g, "chevron-right", new Rectangle(Width - Ui.PX(48), Height / 2 - Ui.PX(8), Ui.PX(16), Ui.PX(16)), s.OnSurfaceVariant, Ui.PX(1.6f));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && _onClick != null) _onClick();
        }
    }

    public class ActionRow : SettingsRow
    {
        readonly Action _onClick;

        public ActionRow(string title, string subtitle, string icon, Action onClick, bool danger = false)
            : base()
        {
            Title = title; Subtitle = subtitle; Icon = icon; _onClick = onClick; Danger = danger;
        }

        public bool Danger;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Danger)
            {
                Scheme s = Ui.T;
                using (var brush = new SolidBrush(s.Error.Tone(s.Dark ? 70 : 40)))
                    e.Graphics.DrawString(Title, Ui.Label, brush, new Rectangle(Ui.PX(52), 0, Width - Ui.PX(76), Height),
                        new StringFormat { LineAlignment = StringAlignment.Center });
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && _onClick != null) _onClick();
        }
    }

    /// <summary>Scrolling stack of cards that hosts the settings rows.</summary>
    /// <summary>Transparent, self-painted, double-buffered panel (SetStyle is protected on Control).</summary>
    public class PaintedPanel : Control
    {
        public PaintedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            ResizeRedraw = true;
        }
    }

    public class SettingsPage : ScrollHost
    {
        readonly List<Control> _blocks = new List<Control>();

        public SettingsPage()
        {
            TabStop = true;
        }

        public void Clear()
        {
            foreach (Control block in _blocks) Controls.Remove(block);
            _blocks.Clear();
        }

        public void AddHeader(string title, string subtitle)
        {
            var header = new PaintedPanel();
            header.BackColor = Color.Transparent;
            header.Height = Ui.PX(64);
            header.Paint += delegate (object s, PaintEventArgs e)
            {
                Scheme sc = Ui.T;
                using (var brush = new SolidBrush(sc.OnSurface))
                    e.Graphics.DrawString(title, Ui.Display, brush, new Point(Ui.PX(16), Ui.PX(6)));
                using (var brush = new SolidBrush(sc.OnSurfaceVariant))
                    e.Graphics.DrawString(Ui.Ellipsis(subtitle, Ui.Caption, Width - Ui.PX(40), e.Graphics), Ui.Caption, brush,
                        new Rectangle(Ui.PX(16), Ui.PX(38), Width - Ui.PX(40), Ui.PX(20)));
            };
            _blocks.Add(header);
            Controls.Add(header);
        }

        public void AddCard(string title)
        {
            var card = new Card();
            if (title.Length > 0) card.Header = title;
            _blocks.Add(card);
            Controls.Add(card);
        }

        public void AddRow(SettingsRow row)
        {
            if (_blocks.Count == 0) AddCard("");
            var card = _blocks[_blocks.Count - 1] as Card;
            if (card == null) { AddCard(""); card = _blocks[_blocks.Count - 1] as Card; }
            card.Add(row);
        }

        public void LayoutCards()
        {
            int y = Ui.PX(8);
            foreach (Control block in _blocks)
            {
                var card = block as Card;
                if (card != null)
                {
                    card.SetBounds(Ui.PX(12), y, Width - Ui.PX(24), card.MeasureHeight());
                }
                else
                {
                    block.SetBounds(Ui.PX(20), y, Width - Ui.PX(40), block.Height);
                }
                y += block.Height + Ui.PX(10);
            }
            SetContentHeight(y + Ui.PX(24));
        }

        /// <summary>Rounded surface that stacks rows with hairline separators.</summary>
        public class Card : Control
        {
            public string Header = "";
            readonly List<SettingsRow> _rows = new List<SettingsRow>();

            public Card()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            public void Add(SettingsRow row)
            {
                _rows.Add(row);
                Controls.Add(row);
            }

            public int MeasureHeight()
            {
                int h = Header.Length > 0 ? Ui.PX(34) : Ui.PX(6);
                foreach (SettingsRow row in _rows) h += row.Height;
                return h + Ui.PX(6);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Scheme s = Ui.T;
                Ui.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), Ui.PX(16), s.SurfaceHigh);

                int y = Ui.PX(6);
                if (Header.Length > 0)
                {
                    using (var brush = new SolidBrush(s.OnSurfaceVariant))
                        g.DrawString(Header.ToUpperInvariant(), Ui.Caption, brush, new Rectangle(Ui.PX(18), y + Ui.PX(6), Width - Ui.PX(36), Ui.PX(20)));
                    y += Ui.PX(34);
                }

                foreach (SettingsRow row in _rows)
                {
                    row.SetBounds(Ui.PX(6), y, Width - Ui.PX(12), row.Height);
                    y += row.Height;
                    if (row != _rows[_rows.Count - 1])
                    {
                        using (var brush = new SolidBrush(Scheme.WithAlpha(s.OutlineVariant, 150)))
                            g.FillRectangle(brush, Ui.PX(20), y - 1, Width - Ui.PX(40), Ui.PX(1));
                    }
                }
            }
        }
    }
}
