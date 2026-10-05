using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MaterialBrowser
{
    /// <summary>
    /// Paints a laid-out <see cref="Page"/> and turns clicks into navigation,
    /// hover into a status-bar hint and form controls into submissions.
    /// </summary>
    public class PageView : ScrollHost
    {
        Page _page;
        Box _hoverBox;
        string _hoverHref = "";

        public event EventHandler<string> LinkActivated;
        public event EventHandler<string> StatusChanged;
        public event EventHandler RelayoutNeeded;

        public Page Current { get { return _page; } }

        public PageView()
        {
            TabStop = true;
            BackColor = Color.White;
        }

        public void SetPage(Page page)
        {
            _page = page;
            ScrollToTop();
            Invalidate();
        }

        public void RefreshLayout()
        {
            if (_page == null) return;
            // CreateGraphics throws until the handle exists, and the first layout pass can
            // run before that. Fall back to an offscreen surface for text measurement.
            Graphics measure = MeasureGraphics();
            _page.Layout(measure, Viewport.Width);
            SetContentHeight(_page.Root == null ? 0 : (int)Math.Ceiling(_page.Root.Rect.Bottom));
            Invalidate();
        }

        Bitmap _measureBitmap;
        int _measureForWidth = -1;

        /// <summary>A Graphics usable for measuring, valid even before the handle is created.</summary>
        Graphics MeasureGraphics()
        {
            int width = Math.Max(16, Viewport.Width);
            if (IsHandleCreated)
            {
                try { return CreateGraphics(); } catch { }
            }
            if (_measureBitmap == null || _measureForWidth != width)
            {
                if (_measureBitmap != null) _measureBitmap.Dispose();
                // MeasureString throws "invalid parameter" on a surface smaller than the
                // text being measured, so this one has to be generously tall.
                _measureBitmap = new Bitmap(width, Math.Max(1024, Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                _measureForWidth = width;
            }
            return Graphics.FromImage(_measureBitmap);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            EventHandler h = RelayoutNeeded;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ─── painting ─────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (_page == null || _page.Root == null)
            {
                using (var brush = new SolidBrush(Ui.T.Surface)) g.FillRectangle(brush, ClientRectangle);
                DrawBar(g);
                return;
            }

            int offset = ScrollOffset;
            g.TranslateTransform(0, -offset);
            var clip = new RectangleF(0, offset, Viewport.Width, Height);

            PaintBox(g, _page.Root, clip);
            g.ResetTransform();

            if (offset > 0)
            {
                var shade = new LinearGradientBrush(new Rectangle(0, 0, Viewport.Width, 10), Color.FromArgb(60, 0, 0, 0), Color.Transparent, LinearGradientMode.Vertical);
                g.FillRectangle(shade, new Rectangle(0, 0, Viewport.Width, 10));
                shade.Dispose();
            }

            DrawBar(g);
        }

        void PaintBox(Graphics g, Box box, RectangleF clip)
        {
            if (box.Rect.Bottom < clip.Top - 40 || box.Rect.Top > clip.Bottom + 40) return;

            if (box.Background.A > 0 && box.Rect.Width > 0 && box.Rect.Height > 0)
            {
                if (box.Radius > 0.5f) Ui.FillRound(g, Rectangle.Round(box.Rect), (int)box.Radius, box.Background);
                else using (var brush = new SolidBrush(box.Background)) g.FillRectangle(brush, box.Rect);
            }

            if (box.BorderTop > 0 || box.BorderLeft > 0 || box.BorderBottom > 0 || box.BorderRight > 0)
                PaintBorder(g, box);

            switch (box.Kind)
            {
                case BoxKind.Text:
                    PaintText(g, box);
                    break;
                case BoxKind.Image:
                    PaintImage(g, box);
                    break;
                case BoxKind.Input:
                    PaintInput(g, box);
                    break;
                case BoxKind.Button:
                    PaintButton(g, box);
                    break;
                case BoxKind.Marker:
                    PaintText(g, box);
                    break;
                case BoxKind.Placeholder:
                    PaintPlaceholder(g, box);
                    break;
            }

            for (int i = 0; i < box.Children.Count; i++) PaintBox(g, box.Children[i], clip);
        }

        void PaintBorder(Graphics g, Box box)
        {
            var pen = new Pen(box.BorderColor, 1);
            float x = box.Rect.Left, y = box.Rect.Top;
            float w = box.Rect.Width, h = box.Rect.Height;
            if (box.BorderLeft > 0) { pen.Width = box.BorderLeft; g.DrawLine(pen, x + box.BorderLeft / 2, y, x + box.BorderLeft / 2, y + h); }
            if (box.BorderTop > 0) { pen.Width = box.BorderTop; g.DrawLine(pen, x, y + box.BorderTop / 2, x + w, y + box.BorderTop / 2); }
            if (box.BorderRight > 0) { pen.Width = box.BorderRight; g.DrawLine(pen, x + w - box.BorderRight / 2, y, x + w - box.BorderRight / 2, y + h); }
            if (box.BorderBottom > 0) { pen.Width = box.BorderBottom; g.DrawLine(pen, x, y + h - box.BorderBottom / 2, x + w, y + h - box.BorderBottom / 2); }
            pen.Dispose();
        }

        void PaintText(Graphics g, Box box)
        {
            if (box.Text.Length == 0 || box.Font == null) return;
            Color color = box.TextColor;
            if (box.IsLink && ReferenceEquals(box, _hoverBox))
                color = Ui.T.Accent();

            using (var brush = new SolidBrush(color))
                g.DrawString(box.Text, box.Font, brush, box.Rect.X, box.Rect.Y, StringFormat.GenericTypographic);

            if (box.Underline || (box.IsLink && !ReferenceEquals(box, _hoverBox)))
            {
                float width = g.MeasureString(box.Text, box.Font, SizeF.Empty, StringFormat.GenericTypographic).Width;
                using (var pen = new Pen(color, Math.Max(1f, Ui.FX(0.8f))))
                    g.DrawLine(pen, box.Rect.X, box.Rect.Bottom - 1.5f, box.Rect.X + width, box.Rect.Bottom - 1.5f);
            }
        }

        void PaintImage(Graphics g, Box box)
        {
            if (box.Image != null)
            {
                g.DrawImage(box.Image, box.Rect);
                if (box.IsLink && ReferenceEquals(box, _hoverBox))
                {
                    using (var pen = new Pen(Ui.T.Accent(), 2f)) g.DrawRectangle(pen, box.Rect.X, box.Rect.Y, box.Rect.Width, box.Rect.Height);
                }
                return;
            }

            if (!Settings.ShowImagesBroken) return;

            var area = Rectangle.Round(box.Rect);
            using (var brush = new SolidBrush(Color.FromArgb(24, 24, 27))) g.FillRectangle(brush, area);
            using (var pen = new Pen(Color.FromArgb(60, 60, 66)))
                g.DrawRectangle(pen, area.X, area.Y, area.Width, area.Height);
            string caption = box.AltText.Length > 0 ? box.AltText : "изображение";
            Font font = Fonts.Get("Segoe UI", 12, false, false);
            SizeF size = g.MeasureString(caption, font, Math.Max(20, area.Width - 12));
            using (var brush = new SolidBrush(Color.FromArgb(150, 150, 158)))
            {
                g.DrawString(caption, font, brush,
                    new RectangleF(area.X + 6, area.Y + (area.Height - size.Height) / 2, area.Width - 12, size.Height),
                    new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
            }
        }

        void PaintInput(Graphics g, Box box)
        {
            var area = Rectangle.Round(box.Rect);
            string kind = box.InputKind;

            if (kind == "checkbox" || kind == "radio")
            {
                Ui.FillRound(g, area, 3, Color.White);
                using (var pen = new Pen(Color.FromArgb(110, 110, 118), 1.4f)) g.DrawRectangle(pen, area.X, area.Y, area.Width, area.Height);
                if (box.Checked)
                {
                    using (var pen = new Pen(Ui.T.Accent(), 2f))
                    {
                        if (kind == "checkbox")
                        {
                            g.DrawLines(pen, new PointF[] {
                                new PointF(area.X + 3, area.Y + 8), new PointF(area.X + 6.5f, area.Y + 12), new PointF(area.X + 13, area.Y + 4) });
                        }
                        else
                        {
                            g.FillEllipse(new SolidBrush(Ui.T.Accent()), area.X + 3.5f, area.Y + 3.5f, area.Width - 7, area.Height - 7);
                        }
                    }
                }
                return;
            }

            // Fields keep the light page colour even in a dark chrome, matching how the
            // rest of the document is drawn.
            Ui.FillRound(g, area, (int)(box.Radius > 0 ? box.Radius : 4),
                box.Background.A > 0 ? box.Background : Color.White);
            using (var pen = new Pen(box.BorderColor, 1)) g.DrawRectangle(pen, area.X, area.Y, area.Width, area.Height);

            string text = box.InputValue;
            if (kind == "search" && text.Length == 0) text = "Поиск";
            if (kind == "password") text = new string('•', Math.Max(4, text.Length));

            using (var brush = new SolidBrush(text.Length == 0 ? Color.FromArgb(140, 140, 148) : box.TextColor))
                g.DrawString(text, box.Font ?? Fonts.Get("Segoe UI", 13, false, false), brush,
                    new RectangleF(area.X + 8, area.Y, area.Width - 12, area.Height),
                    new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });

            if (kind == "submit" || kind == "button")
            {
                // Filled look for button-type inputs
            }
        }

        void PaintButton(Graphics g, Box box)
        {
            var area = Rectangle.Round(box.Rect);
            // A button without an explicit background falls back to the themed one; painting
            // Color.Empty would cover the page with an opaque black block.
            Color background = box.Background.A > 0 ? box.Background
                : (Ui.T.Dark ? Ui.T.SurfaceVariant : Color.FromArgb(238, 238, 243));
            if (ReferenceEquals(box, _hoverBox)) background = Ui.Hover(background, Ui.T.Dark, Ui.T.Dark ? 0.16 : -0.08);
            Ui.FillRound(g, area, (int)(box.Radius > 0 ? box.Radius : 8), background);
            SizeF size = g.MeasureString(box.Text, box.Font);
            using (var brush = new SolidBrush(box.TextColor))
                g.DrawString(box.Text, box.Font, brush,
                    new RectangleF(area.X, area.Y + (area.Height - size.Height) / 2, area.Width, size.Height),
                    new StringFormat { Alignment = StringAlignment.Center });
        }

        void PaintPlaceholder(Graphics g, Box box)
        {
            var area = Rectangle.Round(box.Rect);
            Ui.FillRound(g, area, Ui.PX(12), Ui.T.SurfaceVariant);
            using (var brush = new SolidBrush(Ui.T.OnSurfaceVariant))
                g.DrawString(box.Text, Fonts.Get("Segoe UI", 14, false, false), brush,
                    new RectangleF(area.X + Ui.PX(20), area.Y, area.Width - Ui.PX(40), area.Height),
                    new StringFormat { LineAlignment = StringAlignment.Center });
        }

        // ─── interaction ──────────────────────────────────────────
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_page == null || _page.Root == null) return;
            var point = new PointF(e.X, e.Y + ScrollOffset);

            Box box = _page.Root.HitLink(point);
            string href = box != null ? box.Url : "";
            if (box != null && box.Kind == BoxKind.Input && box.InputKind == "checkbox") href = "";

            if (!string.Equals(href, _hoverHref, StringComparison.Ordinal))
            {
                _hoverHref = href;
                Invalidate();
                EventHandler<string> h = StatusChanged;
                if (h != null) h(this, _hoverHref);
            }

            _hoverBox = box;
            Cursor = href.Length > 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverHref = "";
            _hoverBox = null;
            Invalidate();
            EventHandler<string> h = StatusChanged;
            if (h != null) h(this, "");
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_page == null || _page.Root == null || e.Button != MouseButtons.Left) return;
            var point = new PointF(e.X, e.Y + ScrollOffset);

            Box control = FindControl(point);
            if (control != null)
            {
                HandleControl(control);
                return;
            }

            Box link = _page.Root.HitLink(point);
            if (link != null && link.Url.Length > 0)
            {
                string target = link.Url;
                EventHandler<string> h = LinkActivated;
                if (h != null) h(this, (ModifierKeys & Keys.Control) == Keys.Control ? "tab\t" + target : target);
            }
        }

        Box FindControl(PointF point)
        {
            Box found = null;
            _page.Root.Walk(delegate (Box box)
            {
                if ((box.Kind == BoxKind.Input || box.Kind == BoxKind.Button) && box.IsUnder(point)) found = box;
            });
            return found;
        }

        void HandleControl(Box control)
        {
            if (control.Kind == BoxKind.Button || (control.InputKind == "submit"))
            {
                SubmitForm(control.Node);
                return;
            }
            if (control.InputKind == "checkbox" || control.InputKind == "radio")
            {
                if (control.InputKind == "radio") ClearRadioGroup(control.InputName);
                control.Checked = !control.Checked;
                Invalidate();
                return;
            }
            if (control.InputKind == "button")
            {
                string action = control.Node.AttrValue("onclick", "");
                if (action.StartsWith("search:", StringComparison.OrdinalIgnoreCase))
                {
                    EventHandler<string> h = LinkActivated;
                    if (h != null) h(this, Engines.Search(control.InputValue));
                }
                return;
            }
            if (control.InputKind == "text" || control.InputKind == "search")
            {
                if (IsSearchField(control.Node))
                {
                    EventHandler<string> h = LinkActivated;
                    if (h != null) h(this, Engines.Search(control.InputValue));
                }
            }
        }

        bool IsSearchField(Node node)
        {
            if (node == null) return false;
            string id = (node.AttrValue("id", "") + " " + node.AttrValue("name", "") + " " + node.AttrValue("class", "")).ToLowerInvariant();
            return id.Contains("search") || id.Contains("query") || id.Contains("q=");
        }

        void ClearRadioGroup(string name)
        {
            if (name.Length == 0 || _page.Root == null) return;
            _page.Root.Walk(delegate (Box box)
            {
                if (box.Kind == BoxKind.Input && box.InputKind == "radio" && box.InputName == name) box.Checked = false;
            });
        }

        /// <summary>Emulates a GET form submit through the same event the links use.</summary>
        void SubmitForm(Node node)
        {
            if (node == null) return;
            Node form = FindAncestor(node, "form");
            string action = form != null ? form.AttrValue("action", "") : "";
            string method = form != null ? form.AttrValue("method", "get").ToLowerInvariant() : "get";
            if (method != "get") return;
            if (action.Length == 0) return;

            var query = new System.Text.StringBuilder();
            if (form != null)
            {
                form.Walk(delegate (Node child)
                {
                    if (!child.IsElement) return;
                    if (child.Tag != "input" && child.Tag != "select") return;
                    string type = child.AttrValue("type", "text").ToLowerInvariant();
                    if (type == "checkbox" || type == "radio" || type == "hidden" || type == "submit" || type == "button") return;
                    if (!child.HasAttr("checked") && (type == "checkbox" || type == "radio")) return;
                    string name = child.AttrValue("name", "");
                    if (name.Length == 0) return;
                    if (query.Length > 0) query.Append('&');
                    query.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(child.AttrValue("value", "")));
                });
            }

            string url = Html.ResolveUrl(_page.Url, action);
            if (query.Length > 0) url += (url.IndexOf('?') >= 0 ? "&" : "?") + query;
            EventHandler<string> h = LinkActivated;
            if (h != null) h(this, url);
        }

        public static Node FindAncestor(Node node, string tag)
        {
            Node current = node.Parent;
            while (current != null)
            {
                if (string.Equals(current.Tag, tag, StringComparison.OrdinalIgnoreCase)) return current;
                current = current.Parent;
            }
            return null;
        }

        public string HoverUrl { get { return _hoverHref; } }
    }
}
