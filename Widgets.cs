using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace MaterialBrowser
{
    /// <summary>
    /// Panel with a self-drawn vertical scrollbar. WinForms' own scrollbars cannot be
    /// themed, so every scrolling surface in the app derives from this.
    /// </summary>
    public class ScrollHost : Control
    {
        private int _scroll;
        private int _content;
        private bool _draggingBar;
        private int _dragOffset;
        private bool _hoverBar;

        public int ScrollOffset { get { return _scroll; } }
        public int ContentHeight { get { return _content; } }
        public bool HasBar { get { return _content > Height && _content > 0; } }
        public int BarWidth { get { return HasBar ? Ui.PX(10) : 0; } }

        public event EventHandler Scrolled;

        public ScrollHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void SetContentHeight(int height)
        {
            if (_content == height) return;
            _content = height;
            ClampScroll();
            Invalidate();
        }

        public void ScrollBy(int delta)
        {
            SetScroll(_scroll + delta);
        }

        public void SetScroll(int value)
        {
            int max = Math.Max(0, _content - Height);
            int clamped = Math.Max(0, Math.Min(max, value));
            if (clamped == _scroll) return;
            _scroll = clamped;
            Invalidate();
            EventHandler h = Scrolled;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void ScrollToTop() { SetScroll(0); }

        public void ScrollIntoView(int top, int bottom)
        {
            if (top < _scroll) SetScroll(top);
            else if (bottom > _scroll + Height) SetScroll(bottom - Height);
        }

        protected void ClampScroll()
        {
            int max = Math.Max(0, _content - Height);
            if (_scroll > max) _scroll = max;
            if (_scroll < 0) _scroll = 0;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-e.Delta / 120 * Ui.PX(60));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!HasBar || e.Button != MouseButtons.Left) return;
            if (e.X < Width - BarWidth - Ui.PX(4)) return;
            int top = Ui.PX(3);
            int track = Height - top * 2;
            if (track <= 0) return;
            int thumb = Math.Max(Ui.PX(30), track * Height / Math.Max(1, _content));
            int travel = Math.Max(0, track - thumb);
            int pos = top + (_content <= Height ? 0 : (int)((long)_scroll * travel / Math.Max(1, _content - Height)));
            if (e.Y >= pos && e.Y <= pos + thumb) { _draggingBar = true; _dragOffset = e.Y - pos; }
            else if (e.Y < pos) SetScroll(_scroll - Height / 2);
            else SetScroll(_scroll + Height / 2);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_draggingBar)
            {
                int top = Ui.PX(3);
                int track = Height - top * 2;
                int thumb = Math.Max(Ui.PX(30), track * Height / Math.Max(1, _content));
                int travel = Math.Max(0, track - thumb);
                int y = e.Y - _dragOffset - top;
                if (travel > 0) SetScroll((int)((long)y * Math.Max(0, _content - Height) / travel));
            }
            bool hover = HasBar && e.X >= Width - BarWidth - Ui.PX(4);
            if (hover != _hoverBar) { _hoverBar = hover; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _draggingBar = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverBar) { _hoverBar = false; Invalidate(); }
        }

        /// <summary>Content viewport, excluding the scrollbar gutter.</summary>
        public Rectangle Viewport
        {
            get { return new Rectangle(0, 0, Math.Max(0, Width - BarWidth), Height); }
        }

        protected void DrawBar(Graphics g)
        {
            if (!HasBar) return;
            Scheme s = Ui.T;
            int trackTop = Ui.PX(3);
            int trackHeight = Height - trackTop * 2;
            int thumb = Math.Max(Ui.PX(30), trackHeight * Height / Math.Max(1, _content));
            int travel = Math.Max(0, trackHeight - thumb);
            int y = _content <= Height ? 0 : (int)((long)_scroll * travel / Math.Max(1, _content - Height));
            int x = Width - BarWidth;
            Color color = Scheme.WithAlpha(s.Dark ? s.OnSurfaceVariant : s.OnSurface,
                _draggingBar ? 110 : (_hoverBar ? 80 : 50));
            Ui.FillRound(g, new Rectangle(x, trackTop + y, BarWidth - Ui.PX(3), thumb), BarWidth / 2, color);
        }
    }

    /// <summary>Browser-style tab strip: activate, close, reorder by dragging, overflow scroll.</summary>
    public class TabStrip : Control
    {
        public class TabInfo
        {
            public string Title;
            public string Path;
            public Rectangle Bounds;
            public Rectangle CloseBounds;
        }

        readonly List<TabInfo> _tabs = new List<TabInfo>();
        int _active = -1;
        int _offset;
        int _hover = -1;
        int _hoverClose = -1;
        int _pressClose = -1;
        int _dragFrom = -1;
        int _dragTo = -1;
        int _pressIndex = -1;
        bool _plusHover;
        bool _plusPress;

        public event EventHandler ActiveChanged;
        public event EventHandler TabClosed;
        public event EventHandler NewTabRequested;

        public int Count { get { return _tabs.Count; } }
        public int ActiveIndex { get { return _active; } }
        public string ActivePath { get { return _active >= 0 && _active < _tabs.Count ? _tabs[_active].Path : ""; } }

        /// <summary>
        /// True when the point sits on a tab itself. The empty part of the strip
        /// is free chrome, so the window can be dragged from there.
        /// </summary>
        public bool IsOnTab(Point p)
        {
            foreach (TabInfo t in _tabs) if (t.Bounds.Contains(p)) return true;
            return false;
        }

        /// <summary>Hands the press to the window frame, which starts the move.</summary>
        void DragWindowFromChrome()
        {
            try
            {
                Native.ReleaseCapture();
                Native.SendMessage(Parent.Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
            }
            catch { }
        }

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Default;
        }

        public string ActiveTitle
        {
            get { return _active >= 0 && _active < _tabs.Count ? _tabs[_active].Title : ""; }
        }

        public string TabPath(int index)
        {
            return index >= 0 && index < _tabs.Count ? _tabs[index].Path : "";
        }

        public void SetTabs(IList<string> paths, int active)
        {
            _tabs.Clear();
            for (int i = 0; i < paths.Count; i++)
            {
                var t = new TabInfo();
                t.Path = paths[i];
                t.Title = WebText.TabTitle(paths[i]);
                _tabs.Add(t);
            }
            _active = active;
            LayoutTabs();
            Invalidate();
        }

        public void SetTitle(int index, string title)
        {
            if (index < 0 || index >= _tabs.Count) return;
            _tabs[index].Title = title;
            LayoutTabs();
            Invalidate();
        }

        public void AddTab(string path, bool activate)
        {
            var t = new TabInfo();
            t.Path = path;
            t.Title = WebText.TabTitle(path);
            _tabs.Add(t);
            if (activate || _active < 0)
            {
                _active = _tabs.Count - 1;
                EventHandler h = ActiveChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
            LayoutTabs();
            Invalidate();
        }

        public void CloseTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            _tabs.RemoveAt(index);
            if (_active >= _tabs.Count) _active = _tabs.Count - 1;
            EventHandler h = TabClosed;
            if (h != null) h(this, EventArgs.Empty);
            Invalidate();
        }

        public void Activate(int index)
        {
            if (index < 0 || index >= _tabs.Count || index == _active) return;
            _active = index;
            LayoutTabs();
            EventHandler h = ActiveChanged;
            if (h != null) h(this, EventArgs.Empty);
            Invalidate();
        }

        void LayoutTabs()
        {
            if (!IsHandleCreated) return;
            using (Graphics g = CreateGraphics())
            {
                int x = Ui.PX(10);
                foreach (TabInfo t in _tabs)
                {
                    int w = (int)g.MeasureString(Ui.Ellipsis(t.Title, Ui.Label, Ui.PX(200), g), Ui.Label).Width + Ui.PX(58);
                    t.Bounds = new Rectangle(x, 0, Math.Max(Ui.PX(90), w), Height);
                    x += t.Bounds.Width;
                }
                int max = Math.Max(0, x - Width + Ui.PX(50));
                if (_offset > max) _offset = max;
                if (_offset < 0) _offset = 0;
            }
        }

        int PlusX { get { return Math.Min(Width - Ui.PX(44), _offset + (_tabs.Count > 0 ? _tabs[_tabs.Count - 1].Bounds.Right + Ui.PX(6) : Ui.PX(10))); } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutTabs();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            LayoutTabs();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = HitTab(e.Location);
            int hoverClose = -1;
            if (hover >= 0 && _tabs[hover].CloseBounds.Contains(e.Location)) hoverClose = hover;
            bool plus = e.X >= PlusX && e.X < PlusX + Ui.PX(38);
            if (hover != _hover || hoverClose != _hoverClose || plus != _plusHover) Invalidate();
            _hover = hover; _hoverClose = hoverClose; _plusHover = plus;
            Cursor = (hover >= 0 || plus) ? Cursors.Hand : Cursors.Default;

            if (_pressIndex >= 0 && e.Button == MouseButtons.Left)
            {
                int target = HitTab(e.Location);
                if (target >= 0 && target != _dragFrom)
                {
                    _dragFrom = _pressIndex;
                    _dragTo = target;
                    Invalidate();
                }
            }
        }

        int HitTab(Point p)
        {
            foreach (TabInfo t in _tabs)
                if (t.Bounds.Contains(p)) return _tabs.IndexOf(t);
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Middle)
            {
                int i = HitTab(e.Location);
                if (i >= 0) CloseTab(i);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (e.X >= PlusX && e.X < PlusX + Ui.PX(38)) { _plusPress = true; Invalidate(); return; }
            int index = HitTab(e.Location);
            if (index < 0)
            {
                // Empty part of the strip is title bar chrome, so the window can
                // be moved from it. The form never sees this click, it lands here.
                DragWindowFromChrome();
                return;
            }
            if (_hoverClose == index) { _pressClose = index; Invalidate(); return; }
            _pressIndex = index;
            _dragFrom = -1;
            _dragTo = -1;
            Activate(index);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (_plusPress)
            {
                _plusPress = false;
                Invalidate();
                if (e.X >= PlusX && e.X < PlusX + Ui.PX(38))
                {
                    EventHandler h = NewTabRequested;
                    if (h != null) h(this, EventArgs.Empty);
                }
                return;
            }
            if (_pressClose >= 0)
            {
                int i = _pressClose;
                _pressClose = -1;
                Invalidate();
                if (_hoverClose == i && i >= 0) CloseTab(i);
                return;
            }
            if (_dragFrom >= 0 && _dragTo >= 0 && _dragFrom != _dragTo)
            {
                TabInfo moved = _tabs[_dragFrom];
                _tabs.RemoveAt(_dragFrom);
                _tabs.Insert(_dragTo, moved);
                if (_active == _dragFrom) _active = _dragTo;
                else if (_dragFrom < _active && _dragTo >= _active) _active--;
                else if (_dragFrom > _active && _dragTo <= _active) _active++;
            }
            _pressIndex = -1;
            _dragFrom = -1;
            _dragTo = -1;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1; _hoverClose = -1; _plusHover = false;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int i = HitTab(e.Location);
            if (i >= 0 && _hoverClose != i) CloseTab(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Scheme s = Ui.T;
            int radius = Ui.PX(12);

            for (int i = 0; i < _tabs.Count; i++)
            {
                TabInfo t = _tabs[i];
                var box = new Rectangle(t.Bounds.X - _offset, 0, t.Bounds.Width, Height);
                if (box.Right < -Ui.PX(20) || box.Left > Width + Ui.PX(20)) continue;
                bool active = i == _active;
                bool hover = i == _hover;
                bool dragging = i == _dragFrom && _dragTo >= 0;

                Color fill;
                if (dragging) fill = s.SecondaryContainer;
                else if (active) fill = s.Surface;
                else if (hover) fill = Ui.Hover(s.SurfaceVariant, s.Dark, s.Dark ? 0.10 : -0.04);
                else fill = Color.Transparent;

                if (fill.A > 0)
                {
                    // Only the top corners are rounded: the tab melts into the content below.
                    var r = new Rectangle(box.X, box.Y - radius, box.Width, radius * 2);
                    using (var path = new GraphicsPath())
                    {
                        int d = radius * 2;
                        path.AddArc(r.X, r.Y, d, d, 180, 90);
                        path.AddArc(r.Right - 1 - d, r.Y, d, d, 270, 90);
                        path.AddLine(r.Right - 1, r.Y + radius, r.Right - 1, r.Bottom);
                        path.AddLine(r.Right - 1, r.Bottom, r.X, r.Bottom);
                        path.AddLine(r.X, r.Bottom, r.X, r.Y + radius);
                        path.CloseFigure();
                        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    }
                }

                Color fg = active ? s.OnSurface : s.OnSurfaceVariant;
                using (var b = new SolidBrush(fg))
                    g.DrawString(Ui.Ellipsis(t.Title, Ui.Label, box.Width - Ui.PX(50), g), Ui.Label, b,
                        new RectangleF(box.X + Ui.PX(16), 0, box.Width - Ui.PX(50), Height),
                        new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });

                if (active)
                {
                    using (var b = new SolidBrush(s.Accent()))
                        g.FillRectangle(b, box.X + Ui.PX(12), Height - Ui.PX(2), box.Width - Ui.PX(24), Ui.PX(2));
                }

                // Close affordance
                t.CloseBounds = new Rectangle(box.Right - Ui.PX(30), (Height - Ui.PX(18)) / 2, Ui.PX(18), Ui.PX(18));
                if (active || hover || i == _pressClose)
                {
                    bool hot = i == _hoverClose || i == _pressClose;
                    if (hot) Ui.FillRound(g, t.CloseBounds, t.CloseBounds.Width / 2, Ui.StateLayer(s, s.Dark ? 0.22 : 0.10));
                    Icons.Draw(g, "close", t.CloseBounds, fg, Ui.PX(1.4f));
                }
                else t.CloseBounds = new Rectangle(-10, -10, 4, 4);

                if (i == _dragTo && _dragFrom >= 0 && i != _dragFrom)
                {
                    using (var b = new SolidBrush(s.Accent()))
                        g.FillRectangle(b, box.X - Ui.PX(2), Ui.PX(6), Ui.PX(3), Height - Ui.PX(12));
                }
            }

            // New tab button
            var plus = new Rectangle(PlusX, (Height - Ui.PX(24)) / 2, Ui.PX(24), Ui.PX(24));
            if (_plusHover || _plusPress)
                Ui.FillRound(g, new Rectangle(plus.X - Ui.PX(4), plus.Y - Ui.PX(4), plus.Width + Ui.PX(8), plus.Height + Ui.PX(8)),
                    Ui.PX(10), Ui.StateLayer(s, s.Dark ? 0.20 : 0.08));
            Icons.Draw(g, "add", plus, s.OnSurfaceVariant, Ui.PX(1.6f));
        }
    }

    /// <summary>Clickable path segments with a collapsing overflow item.</summary>

    /// <summary>Left navigation rail: quick access, drives, recycle bin.</summary>

    /// <summary>Sortable column headings for the details view.</summary>

    /// <summary>Bottom strip with item counts, selection size and free space.</summary>
    public class StatusStrip : Control
    {
        public string TextLeft = "";
        public string TextMiddle = "";
        public string TextRight = "";

        public StatusStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = Ui.PX(30);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Scheme s = Ui.T;
            using (var b = new SolidBrush(s.SurfaceHigh)) g.FillRectangle(b, new Rectangle(0, 0, Width, Height));
            using (var b = new SolidBrush(Scheme.WithAlpha(s.OutlineVariant, 200)))
                g.FillRectangle(b, new Rectangle(0, 0, Width, Ui.PX(1)));

            var fmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            using (var b = new SolidBrush(s.OnSurfaceVariant))
            {
                g.DrawString(Ui.Ellipsis(TextLeft, Ui.Caption, Width / 3, g), Ui.Caption, b,
                    new Rectangle(Ui.PX(18), 0, Width / 3, Height), fmt);
                g.DrawString(Ui.Ellipsis(TextMiddle, Ui.Caption, Width / 3, g), Ui.Caption, b,
                    new Rectangle(Width / 3, 0, Width / 3, Height),
                    new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
                g.DrawString(Ui.Ellipsis(TextRight, Ui.Caption, Width / 3, g), Ui.Caption, b,
                    new Rectangle(Width * 2 / 3, 0, Width / 3 - Ui.PX(18), Height),
                    new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter });
            }
        }
    }
}
