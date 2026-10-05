using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;

namespace MaterialBrowser
{
    public enum BoxKind
    {
        Block, Text, Line, Image, Input, Button, Marker, Rule, Placeholder, ListItem
    }

    /// <summary>A laid-out rectangle that knows how to paint itself and where its links are.</summary>
    public class Box
    {
        public BoxKind Kind = BoxKind.Block;
        public RectangleF Rect;
        public Color Background = Color.Transparent;
        public Color BorderColor = Color.Transparent;
        public float BorderTop, BorderRight, BorderBottom, BorderLeft;
        public float Radius;

        public string Text = "";
        public Font Font;
        public Color TextColor = Color.Black;

        public string Url = "";              // link target
        public bool IsLink;
        public bool Underline;
        public bool IsHover;

        public Bitmap Image;
        public bool ImageBroken;
        public string AltText = "";

        public string InputKind = "";        // text | checkbox | radio | submit | button | search
        public string InputValue = "";
        public string InputName = "";
        public bool Checked;
        public bool NoWrap;

        public Node Node;
        public readonly List<Box> Children = new List<Box>();

        public void Add(Box child)
        {
            if (child == null) return;
            Children.Add(child);
        }

        public bool Contains(PointF point)
        {
            return Rect.Contains(point);
        }

        /// <summary>Deepest link box under the point, or null.</summary>
        public Box HitLink(PointF point)
        {
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                Box found = Children[i].HitLink(point);
                if (found != null) return found;
            }
            if (IsLink && IsUnder(point) && !Children.Exists(delegate (Box b) { return b.Kind == BoxKind.Input || b.Kind == BoxKind.Button; }))
                return this;
            return null;
        }

        public bool IsUnder(PointF point)
        {
            return point.X >= Rect.Left && point.X < Rect.Right && point.Y >= Rect.Top && point.Y < Rect.Bottom;
        }

        public void Walk(Action<Box> visit)
        {
            if (visit == null) return;
            visit(this);
            for (int i = 0; i < Children.Count; i++) Children[i].Walk(visit);
        }

        public int Depth
        {
            get
            {
                int max = 0;
                for (int i = 0; i < Children.Count; i++)
                {
                    int d = Children[i].Depth;
                    if (d > max) max = d;
                }
                return max + 1;
            }
        }
    }

    /// <summary>Colours and metrics shared by one document layout.</summary>
    public class PageStyle
    {
        public Color Text = Color.FromArgb(32, 33, 36);
        public Color Background = Color.White;
        public Color Link = Color.FromArgb(26, 115, 232);
        public Color Visited = Color.FromArgb(130, 84, 200);
        public Color Border = Color.FromArgb(218, 220, 224);
        public Color Muted = Color.FromArgb(95, 99, 104);
        public string FontFamily = "Segoe UI";
        public float FontScale = 1f;
        public double RootFontSize = 16;
        public int ViewportWidth = 800;
        public string BaseUrl = "";
    }

    /// <summary>
    /// Block-flow layout: block stacking, inline wrapping, lists, tables, images and
    /// form controls. Only the subset of CSS a reader actually needs is honoured.
    /// </summary>
    public class LayoutEngine
    {
        readonly Graphics _g;
        readonly List<CssRule> _rules;
        readonly PageStyle _page;
        readonly Dictionary<string, bool> _visited = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<Node, double> _fontCache = new Dictionary<Node, double>();
        readonly Dictionary<Node, Style> _inheritCache = new Dictionary<Node, Style>();
        double _rootFontSize = 16;
        int _imageBudget;

        public LayoutEngine(Graphics g, List<CssRule> rules, PageStyle page)
        {
            _g = g;
            _rules = rules ?? new List<CssRule>();
            _page = page;
            _imageBudget = Settings.MaxImages;
        }

        static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style", "head", "meta", "link", "title", "noscript", "template", "base", "param", "source", "track"
        };

        /// <summary>Class names that mark content as visually hidden but present for screen readers.</summary>
        static readonly HashSet<string> HiddenClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a11y-hidden", "visually-hidden", "visuallyhidden", "sr-only", "screen-reader-only",
            "screenreader-only", "screen-reader-text", "accessibility-hidden", "hide-visually",
            "hidden-visually", "u-hidden", "v-hidden", "hidden"
        };

        static bool IsHiddenElement(Node node)
        {
            if (node.HasAttr("hidden")) return true;
            if (string.Equals(node.AttrValue("aria-hidden", ""), "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (node.Tag == "input" && string.Equals(node.AttrValue("type", ""), "hidden", StringComparison.OrdinalIgnoreCase)) return true;
            string classes = node.ClassList;
            if (classes.Length == 0) return false;
            foreach (string token in classes.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                if (HiddenClasses.Contains(token)) return true;
            return false;
        }

        static readonly HashSet<string> InlineTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "abbr", "b", "bdi", "bdo", "big", "cite", "code", "data", "del", "dfn", "em", "font",
            "i", "img", "input", "button", "label", "mark", "q", "s", "samp", "small", "span", "strike",
            "strong", "sub", "sup", "time", "u", "var", "wbr", "select", "textarea", "output", "ins", "kbd"
        };

        // ─── entry point ─────────────────────────────────────────
        public Box BuildDocument(Node root)
        {
            Node body = root.FindFirst("body") ?? root;
            var rootBox = new Box { Kind = BoxKind.Block, Rect = new RectangleF(0, 0, _page.ViewportWidth, 0) };
            ReadRootFontSize(root);
            _fontCache.Clear();
            _inheritCache.Clear();
            Style bodyStyle = StyleFor(body, true);
            if (!bodyStyle.Has("background-color")) bodyStyle.Values["background-color"] = ColorToCss(_page.Background);

            float y = 0;
            var inlineRun = new List<Node>();
            foreach (Node child in body.Children)
            {
                if (child.IsText || (child.IsElement && IsInline(child)))
                {
                    inlineRun.Add(child);
                    continue;
                }
                if (inlineRun.Count > 0)
                {
                    Box line = LayoutInlineRun(inlineRun, bodyStyle, 0, y, _page.ViewportWidth, rootBox);
                    if (line != null) y += line.Rect.Height;
                    inlineRun.Clear();
                }
                y = LayoutNode(child, bodyStyle, 0, y, _page.ViewportWidth, rootBox);
            }
            if (inlineRun.Count > 0)
            {
                Box line = LayoutInlineRun(inlineRun, bodyStyle, 0, y, _page.ViewportWidth, rootBox);
                if (line != null) y += line.Rect.Height;
            }

            rootBox.Rect = new RectangleF(0, 0, _page.ViewportWidth, y);
            return rootBox;
        }

        // ─── style resolution ─────────────────────────────────────
        /// <summary>html { font-size } is the reference every rem resolves against.</summary>
        void ReadRootFontSize(Node root)
        {
            _rootFontSize = 16;
            Node html = root.IsElement && root.Tag == "html" ? root : root.FindFirst("html");
            if (html == null) return;
            string declared = Css.Compute(html, _rules).Get("font-size", "");
            if (declared.Length == 0) return;
            double value;
            if (Css.TryParseLength(declared, CssMetrics.Default(16), out value) && !double.IsNaN(value) && value >= 6 && value <= 200)
                _rootFontSize = value;
            _page.RootFontSize = _rootFontSize;
        }

        /// <summary>Resolved font size of an element, following the parent chain for em and %.</summary>
        double FontSizeOf(Node node)
        {
            double cached;
            if (node.IsElement && _fontCache.TryGetValue(node, out cached)) return cached;
            double own = ComputeFontSize(node, 0);
            if (node.IsElement) _fontCache[node] = own;
            return own;
        }

        double ComputeFontSize(Node node, int depth)
        {
            if (node == null || depth > 24) return _rootFontSize;
            double parent = _rootFontSize;
            if (node.Parent != null) parent = FontSizeOf(node.Parent);
            if (!node.IsElement) return parent;

            Style raw = Css.Compute(node, _rules);
            string declared = raw.Get("font-size", "");
            if (declared.Length > 0)
            {
                CssMetrics m = CssMetrics.Default(16);
                m.FontSize = parent;
                m.RootFontSize = _rootFontSize;
                double value;
                if (Css.TryParseLength(declared, m, out value) && !double.IsNaN(value) && value > 0)
                    return value * _page.FontScale;
            }
            return RelativeFontSize(node.Tag, parent) * _page.FontScale;
        }

        /// <summary>The UA default sizes, relative to the parent font size as CSS requires.</summary>
        static double RelativeFontSize(string tag, double parent)
        {
            switch (tag)
            {
                case "h1": return parent * 2;
                case "h2": return parent * 1.5;
                case "h3": return parent * 1.17;
                case "h4": return parent;
                case "h5": return parent * 0.83;
                case "h6": return parent * 0.67;
                case "small": return parent * 0.8;
                case "big": return parent * 1.25;
                case "code": case "kbd": case "samp": case "pre": return parent * 0.85;
                case "sup": case "sub": return parent * 0.72;
                case "input": case "button": case "select": case "textarea": return parent * 0.9;
                default: return parent;
            }
        }

        Style StyleFor(Node node, bool isRoot)
        {
            if (node.IsText) return InheritedStyle(node);

            Style style = Css.Compute(node, _rules);

            if (node.Parent == null && !style.Has("color")) style.Values["color"] = ColorToCss(_page.Text);

            string tag = node.Tag;
            if (!style.Has("display"))
            {
                if (Hidden.Contains(tag)) style.Values["display"] = "none";
                else if (tag == "br") style.Values["display"] = "inline";
                else if (tag == "hr") style.Values["display"] = "block";
                else if (InlineTags.Contains(tag)) style.Values["display"] = "inline";
                else style.Values["display"] = "block";
            }

            string display = style.Get("display", "block");
            if (display == "inline-block") style.Values["display"] = "inline";
            else if (display == "inline-flex") style.Values["display"] = "flex";
            else if (display == "grid" || display == "inline-grid")
            {
                // A grid we cannot place is far better approximated by a single column.
                style.Values["display"] = "flex";
                if (!style.Has("flex-direction")) style.Values["flex-direction"] = "column";
            }

            if (Hidden.Contains(tag) || IsHiddenElement(node)) style.Values["display"] = "none";
            display = style.Get("display", "block");
            if (display == "none") return style;

            if (!style.Has("font-family")) style.Values["font-family"] = _page.FontFamily;

            double defaultSize = FontSizeOf(node);
            style.Values["font-size"] = defaultSize.ToString("0.##") + "px";

            if (!style.Has("font-weight"))
            {
                if (tag == "b" || tag == "strong" || tag == "h1" || tag == "h2" || tag == "h3" || tag == "h4" || tag == "h5" || tag == "h6" || tag == "th")
                    style.Values["font-weight"] = "bold";
                else if (tag == "small") style.Values["font-weight"] = "normal";
            }
            if (!style.Has("font-style") && (tag == "i" || tag == "em" || tag == "cite" || tag == "var")) style.Values["font-style"] = "italic";
            if (!style.Has("text-decoration") && (tag == "u" || tag == "ins")) style.Values["text-decoration"] = "underline";
            if (!style.Has("white-space") && (tag == "pre" || tag == "code" || tag == "kbd" || tag == "samp" || tag == "textarea"))
                style.Values["white-space"] = "pre";
            if (!style.Has("font-family") == false && (tag == "code" || tag == "kbd" || tag == "samp" || tag == "pre"))
                style.Values["font-family"] = "Consolas";

            if (!style.Has("color"))
            {
                if (tag == "a" && node.HasAttr("href")) style.Values["color"] = ColorToCss(LinkColor(node));
                else style.Values["color"] = ColorToCss(_page.Text);
            }

            if (!style.Has("background-color") && !style.Has("background"))
            {
                if (tag == "mark") style.Values["background-color"] = "#FFF59D";
                else if (tag == "pre" || tag == "code" || tag == "kbd") style.Values["background-color"] = "#F4F4F6";
            }

            if (tag == "a" && node.HasAttr("href") && !style.Has("text-decoration")) style.Values["text-decoration"] = "underline";

            if (tag == "ol" || tag == "ul")
            {
                if (!style.Has("list-style-type"))
                    style.Values["list-style-type"] = tag == "ol" ? node.AttrValue("type", "decimal") : (node.AttrValue("type", "disc"));
                if (!style.Has("padding-left")) style.Values["padding-left"] = "28px";
            }
            else if (tag == "li")
            {
                if (!style.Has("padding-left")) style.Values["padding-left"] = "4px";
            }
            else if (tag == "blockquote")
            {
                if (!style.Has("margin-left")) style.Values["margin-left"] = "24px";
                if (!style.Has("padding-left")) style.Values["padding-left"] = "12px";
                if (!style.Has("border-left")) style.Values["border-left"] = "4px solid " + ColorToCss(_page.Border);
                if (!style.Has("color")) style.Values["color"] = ColorToCss(_page.Muted);
            }
            else if (tag == "hr")
            {
                if (!style.Has("border")) style.Values["border"] = "1px solid " + ColorToCss(_page.Border);
                if (!style.Has("margin")) style.Values["margin"] = "12px 0";
            }
            else if (tag == "table")
            {
                if (!style.Has("border-spacing")) style.Values["border-spacing"] = "0px";
            }
            else if ((tag == "td" || tag == "th"))
            {
                if (!style.Has("padding")) style.Values["padding"] = "6px 10px";
                if (!style.Has("border") && node.Parent != null && node.Parent.Tag == "table")
                    style.Values["border"] = "1px solid " + ColorToCss(_page.Border);
                if (tag == "th" && !style.Has("text-align")) style.Values["text-align"] = "left";
            }
            else if (tag == "h1" || tag == "h2" || tag == "h3" || tag == "h4" || tag == "h5" || tag == "h6")
            {
                if (!style.Has("margin")) style.Values["margin"] = "14px 0 8px";
            }
            else if (tag == "p" || tag == "div" || tag == "section" || tag == "article" || tag == "header" ||
                     tag == "footer" || tag == "nav" || tag == "main" || tag == "aside" || tag == "form" ||
                     tag == "figure" || tag == "ul" || tag == "ol" || tag == "dl" || tag == "table" || tag == "pre")
            {
                if (!style.Has("margin")) style.Values["margin"] = tag == "p" ? "0 0 10px" : "0 0 12px";
            }

            if (tag == "input" || tag == "button" || tag == "select" || tag == "textarea")
            {
                if (!style.Has("font-family")) style.Values["font-family"] = "Segoe UI";
            }

            string position = style.Get("position", "").ToLowerInvariant();
            if (position == "absolute" || position == "fixed" || style.Get("visibility", "") == "hidden")
                style.Values["display"] = "none";

            return style;
        }

        /// <summary>Text nodes take the computed style of the element that contains them.</summary>
        Style InheritedStyle(Node node)
        {
            Node parent = node.Parent;
            int depth = 0;
            while (parent != null && !parent.IsElement && depth++ < 24) parent = parent.Parent;
            if (parent == null) return new Style();
            Style cached;
            if (_inheritCache.TryGetValue(parent, out cached)) return cached;
            Style style = StyleFor(parent, false);
            _inheritCache[parent] = style;
            return style;
        }

        Color LinkColor(Node node)
        {
            string href = node.AttrValue("href", "");
            if (href.Length > 0)
            {
                bool seen;
                if (_visited.TryGetValue(href, out seen) && seen) return _page.Visited;
            }
            return _page.Link;
        }

        public void MarkVisited(string url)
        {
            if (!string.IsNullOrEmpty(url)) _visited[url] = true;
        }

        // ─── block layout ─────────────────────────────────────────
        float LayoutNode(Node node, Style parentStyle, float x, float y, float availWidth, Box parent)
        {
            if (node.IsText)
            {
                string text = Collapse(node.Text, parentStyle);
                if (text.Length == 0) return y;
                Box line = LayoutInlineRun(new List<Node>() { node }, parentStyle, x, y, availWidth, null);
                if (line != null) { parent.Add(line); return y + line.Rect.Height; }
                return y;
            }

            Style style = StyleFor(node, false);
            if (style.Get("display", "block") == "none") return y;

            string tag = node.Tag;
            if (tag == "br")
            {
                parent.Add(new Box { Kind = BoxKind.Text, Rect = new RectangleF(x, y, availWidth, LineHeight(parentStyle) * 0.6f), Font = FontOf(parentStyle), TextColor = ColorOf(parentStyle) });
                return y + LineHeight(parentStyle) * 0.6f;
            }

            if (tag == "hr")
            {
                double border;
                Css.ParseBorder(style.Get("border", "1px solid #ccc"), out border, out _unusedColor);
                var rule = new Box
                {
                    Kind = BoxKind.Rule,
                    Rect = new RectangleF(x + Length(style, "margin-left", availWidth), y, availWidth, Math.Max(1, (float)border)),
                    BorderColor = ColorOfBorder(style, _page.Border),
                    Node = node
                };
                parent.Add(rule);
                return y + rule.Rect.Height + Length(style, "margin-top", availWidth) + Length(style, "margin-bottom", availWidth);
            }

            if (tag == "img" || tag == "input" || tag == "button" || tag == "select" || tag == "textarea")
            {
                Box leaf = LayoutLeaf(node, style, x, y, availWidth, parentStyle);
                if (leaf == null) return y;
                parent.Add(leaf);
                return y + leaf.Rect.Height + Length(style, "margin-bottom", availWidth);
            }

            if (tag == "table") return LayoutTable(node, style, x, y, availWidth, parent);
            if (tag == "ul" || tag == "ol") return LayoutList(node, style, x, y, availWidth, parent);
            if (style.Get("display", "block") == "flex") return LayoutFlex(node, style, x, y, availWidth, parent);

            // Generic block container
            float[] margin = BoxEdges(style, "margin", "margin", availWidth);
            float[] padding = BoxEdges(style, "padding", "padding", availWidth);
            float marginLeft = margin[3], marginRight = margin[1];
            float marginTop = margin[0], marginBottom = margin[2];
            float paddingLeft = padding[3], paddingRight = padding[1];
            float paddingTop = padding[0], paddingBottom = padding[2];

            CssMetrics metrics = Metrics(style, availWidth);
            float declaredWidth = (float)Css.TryParseLength(style.Get("width", ""), metrics, double.NaN);
            float contentWidth = availWidth - marginLeft - marginRight - paddingLeft - paddingRight;
            if (!double.IsNaN(declaredWidth)) contentWidth = Math.Min(contentWidth, declaredWidth);
            if (BorderBox(style) && !double.IsNaN(declaredWidth))
                contentWidth = Math.Max(0, declaredWidth - paddingLeft - paddingRight);
            if (contentWidth < 1) contentWidth = 1;
            // A collapsed box still has to show its content: fall back to the minimum width
            // the content needs instead of shredding it into a one pixel column.
            if (contentWidth < 24)
            {
                float needed = MeasureMinWidth(node, style);
                float ceiling = Math.Max(24, availWidth);
                if (needed > contentWidth) contentWidth = Math.Min(Math.Max(24, needed), ceiling);
            }

            float maxWidth = (float)Css.TryParseLength(style.Get("max-width", ""), metrics, double.NaN);
            if (!double.IsNaN(maxWidth) && contentWidth > maxWidth) contentWidth = maxWidth;

            float minWidth = (float)Css.TryParseLength(style.Get("min-width", ""), metrics, double.NaN);
            if (!double.IsNaN(minWidth) && contentWidth < minWidth) contentWidth = Math.Min(minWidth, availWidth);

            float boxX = x + marginLeft;
            if (HasAutoMargin(style) && !double.IsNaN(declaredWidth) && declaredWidth + paddingLeft + paddingRight < availWidth)
                boxX = x + (availWidth - declaredWidth - paddingLeft - paddingRight) / 2;

            float cursorY = y + marginTop + paddingTop;
            Box box = NewBlockBox(node, style, new RectangleF(boxX, y + marginTop, contentWidth + paddingLeft + paddingRight, 0), availWidth);
            parent.Add(box);

            cursorY = LayoutFlow(node, style, box, boxX + paddingLeft, cursorY, contentWidth);
            FinishBlock(box, style, cursorY, y + marginTop, paddingBottom, boxX, contentWidth, paddingLeft, paddingRight);
            return box.Rect.Bottom + marginBottom;
        }

        Box NewBlockBox(Node node, Style style, RectangleF rect, float availWidth)
        {
            var box = new Box
            {
                Kind = BoxKind.Block,
                Rect = rect,
                Background = style.Color("background-color", Color.Transparent),
                Node = node
            };
            string backgroundShorthand = style.Get("background", "");
            if (backgroundShorthand.Length > 0 && !IsNone(backgroundShorthand))
            {
                Color parsed;
                if (Css.TryParseColor(Css.ResolveBackground(backgroundShorthand, Color.Empty), out parsed)) box.Background = parsed;
            }
            ApplyBorders(box, style, _page.Border);
            box.Radius = (float)Length(style, "border-radius", availWidth);
            return box;
        }

        static bool IsAuto(Style style, string name)
        {
            string value = style.Get(name, "").Trim();
            return value == "auto";
        }

        static bool BorderBox(Style style)
        {
            return string.Equals(style.Get("box-sizing", "").Trim(), "border-box", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the margin shorthand asks for automatic horizontal centring.</summary>
        static bool HasAutoMargin(Style style)
        {
            string margin = style.Get("margin", "");
            return margin.Length > 0 && margin.IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Lays out the children of a block, alternating block boxes and inline runs.</summary>
        float LayoutFlow(Node node, Style style, Box owner, float x, float y, float width)
        {
            var inlineRun = new List<Node>();
            foreach (Node child in node.Children)
            {
                if (child.IsText || (child.IsElement && IsInline(child)))
                {
                    inlineRun.Add(child);
                    continue;
                }
                if (inlineRun.Count > 0)
                {
                    Box line = LayoutInlineRun(inlineRun, style, x, y, width, owner);
                    if (line != null) y += line.Rect.Height;
                    inlineRun.Clear();
                }
                y = LayoutNode(child, style, x, y, width, owner);
            }
            if (inlineRun.Count > 0)
            {
                Box line = LayoutInlineRun(inlineRun, style, x, y, width, owner);
                if (line != null) y += line.Rect.Height;
            }
            return y;
        }

        void FinishBlock(Box box, Style style, float cursorY, float top, float paddingBottom, float boxX, float contentWidth, float paddingLeft, float paddingRight)
        {
            float height = (cursorY - top) + paddingBottom;
            height = AtLeast(height, DeclaredHeight(style, contentWidth, height));
            box.Rect = new RectangleF(boxX, top, contentWidth + paddingLeft + paddingRight, height);
        }

        /// <summary>An explicit height (or min-height) wins over the height the content asked for.</summary>
        static float AtLeast(float computed, float declared)
        {
            return declared > computed ? declared : computed;
        }

        static float DeclaredHeight(Style style, float reference, float computed)
        {
            CssMetrics metrics = CssMetrics.Default(reference);
            metrics.ViewportHeight = Math.Max(360, reference * 1.5);
            double minHeight = Css.TryParseLength(style.Get("min-height", ""), metrics, 0);
            double height = Css.TryParseLength(style.Get("height", ""), metrics, 0);
            float result = (float)Math.Max(minHeight, height);
            return result > computed ? result : computed;
        }

        // ─── flex ────────────────────────────────────────────────
        class FlexItem
        {
            public Node Node;
            public List<Node> Run;          // non-null for an anonymous item made of inline content
            public Style Style;
            public float Natural;
            public float MinWidth;
            public float Grow;
            public float Shrink;
            public float Basis;
            public float Width;
            public float Height;
            public Box Box;
        }

        /// <summary>
        /// A one-dimensional flexbox: row, row-reverse, column and wrapping. Items are
        /// measured naturally, then grown or shrunk into the line and aligned vertically.
        /// </summary>
        float LayoutFlex(Node node, Style style, float x, float y, float availWidth, Box parent)
        {
            float[] margin = BoxEdges(style, "margin", "margin", availWidth);
            float[] padding = BoxEdges(style, "padding", "padding", availWidth);
            float marginLeft = margin[3], marginTop = margin[0];
            float marginBottom = margin[2];
            float paddingLeft = padding[3], paddingRight = padding[1];
            float paddingTop = padding[0], paddingBottom = padding[2];

            CssMetrics metrics = Metrics(style, availWidth);
            float declaredWidth = (float)Css.TryParseLength(style.Get("width", ""), metrics, double.NaN);
            float contentWidth = availWidth - marginLeft - paddingLeft - paddingRight;
            if (!double.IsNaN(declaredWidth)) contentWidth = Math.Min(contentWidth, declaredWidth);
            if (BorderBox(style) && !double.IsNaN(declaredWidth))
                contentWidth = Math.Max(0, declaredWidth - paddingLeft - paddingRight);
            float maxWidth = (float)Css.TryParseLength(style.Get("max-width", ""), metrics, double.NaN);
            if (!double.IsNaN(maxWidth) && contentWidth > maxWidth) contentWidth = maxWidth;
            contentWidth = Math.Max(20, contentWidth);
            float boxX = x + marginLeft;
            float top = y + marginTop;
            Box box = NewBlockBox(node, style, new RectangleF(boxX, top, contentWidth + paddingLeft + paddingRight, 0), availWidth);
            parent.Add(box);

            var items = new List<FlexItem>();
            CollectFlexItems(node, style, items);
            if (items.Count == 0)
            {
                box.Rect = new RectangleF(boxX, top, contentWidth + paddingLeft + paddingRight, paddingTop + paddingBottom);
                return box.Rect.Bottom + marginBottom;
            }

            string direction = style.Get("flex-direction", "row").Trim().ToLowerInvariant();
            bool row = direction != "column" && direction != "column-reverse";
            bool reverse = direction.EndsWith("reverse", StringComparison.Ordinal);
            float gap = ResolveGap(style, "gap", contentWidth);
            float gapX = row ? ResolveGap(style, "column-gap", contentWidth) : 0;
            float gapY = row ? ResolveGap(style, "row-gap", contentWidth) : gap;
            if (gap > 0) { if (gapX <= 0) gapX = gap; if (gapY <= 0) gapY = gap; }

            var lines = new List<List<FlexItem>>();
            bool wrap = false;
            if (row)
            {
                wrap = style.Get("flex-wrap", "nowrap").Trim().ToLowerInvariant() == "wrap";
                var current = new List<FlexItem>();
                float used = 0;
                foreach (FlexItem item in items)
                {
                    float step = item.Natural + gapX;
                    if (wrap && current.Count > 0 && used + step > contentWidth + 0.5f)
                    {
                        lines.Add(current);
                        current = new List<FlexItem>();
                        used = 0;
                    }
                    current.Add(item);
                    used += step;
                }
                if (current.Count > 0) lines.Add(current);
            }
            else
            {
                lines.Add(new List<FlexItem>(items));
            }

            float cursorY = top + paddingTop;
            float innerX = boxX + paddingLeft;
            string justify = style.Get("justify-content", row ? "flex-start" : "stretch").Trim().ToLowerInvariant();
            string align = style.Get("align-items", "stretch").Trim().ToLowerInvariant();

            foreach (List<FlexItem> line in lines)
            {
                if (reverse) line.Reverse();

                float used = 0;
                for (int i = 0; i < line.Count; i++) used += line[i].Natural;
                used += gapX * Math.Max(0, line.Count - 1);

                if (row && !wrap)
                {
                    if (used < contentWidth) Distribute(line, contentWidth - used, true);
                    else if (used > contentWidth) Distribute(line, used - contentWidth, false);
                }

                float offset = 0;
                if (row)
                {
                    float placed = 0;
                    for (int i = 0; i < line.Count; i++) placed += line[i].Width;
                    placed += gapX * Math.Max(0, line.Count - 1);
                    float slack = contentWidth - placed;
                    if (slack > 0.5f)
                    {
                        if (justify == "center") offset = slack / 2;
                        else if (justify == "flex-end" || justify == "end" || justify == "right") offset = slack;
                        else if (justify == "space-between" && line.Count > 1) offset = slack / (line.Count - 1);
                        else if (justify == "space-around" && line.Count > 0) offset = slack / line.Count / 2;
                        else if (justify == "space-evenly" && line.Count > 0) offset = slack / (line.Count + 1);
                    }
                }

                float lineTop = cursorY;
                float tallest = 0;
                float used_main = 0;
                for (int i = 0; i < line.Count; i++)
                {
                    FlexItem item = line[i];
                    float width = row ? Math.Max(1, item.Width) : contentWidth;
                    float itemX = row ? innerX + offset + used_main : innerX;
                    float itemY = row ? lineTop : lineTop + used_main;
                    var itemBox = new Box
                    {
                        Kind = BoxKind.Block,
                        Rect = new RectangleF(itemX, itemY, width, 0),
                        Node = item.Node
                    };
                    box.Add(itemBox);
                    item.Box = itemBox;
                    float height;
                    if (item.Run != null)
                    {
                        Box lineBox = LayoutInlineRun(item.Run, style, itemX, itemY, Math.Max(20, width), itemBox);
                        height = lineBox == null ? 0 : lineBox.Rect.Height;
                    }
                    else
                    {
                        height = LayoutNode(item.Node, style, itemX, itemY, Math.Max(20, width), itemBox) - itemY;
                        if (height < 0) height = 0;
                    }
                    item.Height = height;
                    if (height > tallest) tallest = height;
                    used_main += (row ? width : height) + (row ? gapX : gapY);
                }

                for (int i = 0; i < line.Count; i++)
                {
                    FlexItem item = line[i];
                    RectangleF rect = item.Box.Rect;
                    if (row)
                    {
                        float offsetY = 0;
                        if (align == "center") offsetY = (tallest - item.Height) / 2;
                        else if (align == "flex-end" || align == "end") offsetY = tallest - item.Height;
                        float boxHeight = align == "stretch" ? tallest : item.Height;
                        item.Box.Rect = new RectangleF(rect.X, lineTop + offsetY, rect.Width, boxHeight);
                    }
                    else
                    {
                        // Cross axis of a column: stretch by default, otherwise keep the natural width.
                        float width = contentWidth;
                        float itemX = innerX;
                        if (align == "center" || align == "flex-end" || align == "end" || align == "right")
                        {
                            width = Math.Min(contentWidth, Math.Max(1, item.Natural));
                            itemX = align == "center" ? innerX + (contentWidth - width) / 2 : innerX + contentWidth - width;
                        }
                        item.Box.Rect = new RectangleF(itemX, rect.Y, width, item.Height);
                    }
                }

                float lineSpan = row ? tallest : Math.Max(0, used_main - gapY);
                cursorY = lineTop + lineSpan + gapY;
            }

            box.Rect = new RectangleF(boxX, top, contentWidth + paddingLeft + paddingRight,
                AtLeast(cursorY - top + paddingBottom, DeclaredHeight(style, contentWidth, cursorY - top + paddingBottom)));
            return box.Rect.Bottom + marginBottom;
        }

        float ResolveGap(Style style, string name, float reference)
        {
            string value = style.Get(name, "");
            if (value.Length > 0)
            {
                double parsed = Css.TryParseLength(value, Metrics(style, reference), 0);
                if (parsed > 0) return (float)parsed;
            }
            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) return (float)Css.TryParseLength(parts[0], Metrics(style, reference), 0);
            return 0;
        }

        /// <summary>Hands out the free space (or takes it away) according to flex-grow / flex-shrink.</summary>
        static void Distribute(List<FlexItem> line, float amount, bool grow)
        {
            float total = 0;
            foreach (FlexItem item in line) total += grow ? item.Grow : item.Shrink;
            if (total <= 0)
            {
                foreach (FlexItem item in line) item.Width = Math.Max(item.Natural, item.MinWidth);
                return;
            }
            foreach (FlexItem item in line)
            {
                float share = (grow ? item.Grow : item.Shrink) / total;
                item.Width = grow
                    ? item.Natural + amount * share
                    : Math.Max(item.MinWidth, item.Natural - amount * share);
            }
        }

        /// <summary>
        /// The width below which the content starts breaking: the widest single word, or the
        /// widest leaf, for everything the subtree contains.
        /// </summary>
        float MeasureMinWidth(Node node, Style parentStyle)
        {
            if (node == null) return 0;
            if (node.IsText)
            {
                Font font = FontOf(InheritedStyle(node));
                string[] words = node.Text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) return _g.MeasureString(node.Text, font).Width;
                float widest = 0;
                foreach (string word in words)
                {
                    float width = _g.MeasureString(word, font).Width;
                    if (width > widest) widest = width;
                }
                return widest;
            }
            if (!node.IsElement) return 0;

            Style style = StyleFor(node, false);
            if (style.Get("display", "block") == "none") return 0;
            if (node.Tag == "br") return 0;
            if (node.Tag == "img" || node.Tag == "input" || node.Tag == "button" ||
                node.Tag == "select" || node.Tag == "textarea")
                return LeafWidth(node, style);

            float best = 0;
            foreach (Node child in node.Children)
            {
                float width = MeasureMinWidth(child, style);
                if (width > best) best = width;
            }
            return best;
        }

        void CollectFlexItems(Node container, Style containerStyle, List<FlexItem> into)
        {
            var pending = new List<Node>();
            foreach (Node child in container.Children)
            {
                if (child.IsElement && (child.Tag == "script" || child.Tag == "style" || child.Tag == "template" || child.Tag == "noscript")) continue;

                bool inline = child.IsText || (child.IsElement && IsInline(child));
                if (inline)
                {
                    if (child.IsElement)
                    {
                        Style s = StyleFor(child, false);
                        if (s.Get("display", "inline") == "none") continue;
                    }
                    pending.Add(child);
                    continue;
                }

                if (pending.Count > 0) { into.Add(AnonymousItem(pending, containerStyle)); pending = new List<Node>(); }

                Style childStyle = StyleFor(child, false);
                if (childStyle.Get("display", "block") == "none") continue;
                into.Add(Measure(child, childStyle));
            }
            if (pending.Count > 0) into.Add(AnonymousItem(pending, containerStyle));
        }

        FlexItem AnonymousItem(List<Node> run, Style containerStyle)
        {
            var item = new FlexItem { Run = run, Style = containerStyle };
            float width = 0;
            bool anyVisible = false;
            foreach (Node node in run)
            {
                if (node.IsText && Collapse(node.Text, containerStyle).Trim().Length == 0) continue;
                anyVisible = true;
                width += MeasureNodeWidth(node, containerStyle);
            }
            if (width <= 0 && anyVisible) width = 24;
            item.Natural = width;
            item.Grow = 0;
            item.Shrink = 1;
            item.Width = width;
            item.MinWidth = MinWidthOfRun(run, containerStyle);
            return item;
        }

        float MinWidthOfRun(List<Node> run, Style containerStyle)
        {
            float widest = 0;
            foreach (Node node in run)
            {
                float width = MeasureMinWidth(node, containerStyle);
                if (width > widest) widest = width;
            }
            return widest;
        }

        FlexItem Measure(Node child, Style childStyle)
        {
            float grow, shrink;
            string basisText;
            ParseFlex(childStyle, out grow, out shrink, out basisText);

            float basis = (float)Css.TryParseLength(basisText, Metrics(childStyle, _page.ViewportWidth), double.NaN);
            if (double.IsNaN(basis)) basis = MeasureNodeWidth(child, childStyle);
            if (basis < 0) basis = 0;
            if (basis > _page.ViewportWidth) basis = _page.ViewportWidth;

            // flex items refuse to shrink below their content, exactly like min-width: auto.
            float declaredMin = (float)Css.TryParseLength(childStyle.Get("min-width", ""), Metrics(childStyle, _page.ViewportWidth), 0);
            float minWidth = Math.Max(declaredMin, MeasureMinWidth(child, childStyle));

            return new FlexItem
            {
                Node = child,
                Style = childStyle,
                Natural = basis,
                Width = Math.Max(basis, minWidth),
                MinWidth = minWidth,
                Grow = grow,
                Shrink = shrink,
                Basis = basis
            };
        }

        /// <summary>
        /// Reads flex-grow / flex-shrink / flex-basis, understanding the shorthand and its
        /// keywords: none is 0 0 auto, auto is 1 1 auto, and a bare number is 1 1 0%.
        /// </summary>
        static void ParseFlex(Style style, out float grow, out float shrink, out string basis)
        {
            grow = 0;
            shrink = 1;
            basis = "";

            string shorthand = style.Get("flex", "").Trim().ToLowerInvariant();
            if (shorthand.Length > 0)
            {
                string[] parts = shorthand.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string first = parts[0];
                if (first == "none") { grow = 0; shrink = 0; basis = "auto"; return; }
                if (first == "auto") { grow = 1; shrink = 1; basis = "auto"; return; }
                if (first == "initial") { grow = 0; shrink = 1; basis = "auto"; return; }

                double value;
                if (TryNumber(parts[0], out value))
                {
                    grow = (float)value;
                    basis = "0px";
                    if (parts.Length > 1 && TryNumber(parts[1], out value)) shrink = (float)value;
                    if (parts.Length > 2) basis = parts[2];
                    return;
                }
                return;
            }

            double growValue, shrinkValue;
            if (style.Get("flex-grow", "").Length > 0 && TryNumber(style.Get("flex-grow", ""), out growValue)) grow = (float)growValue;
            if (style.Get("flex-shrink", "").Length > 0 && TryNumber(style.Get("flex-shrink", ""), out shrinkValue)) shrink = (float)shrinkValue;
            basis = style.Get("flex-basis", "");
        }

        static bool TryNumber(string text, out double value)
        {
            return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        bool IsInline(Node node)
        {
            if (!node.IsElement) return true;
            if (node.Tag == "br") return true;
            if (Hidden.Contains(node.Tag)) return true;
            if (!InlineTags.Contains(node.Tag)) return false;
            Style style = StyleFor(node, false);
            return style.Get("display", "inline") != "none";
        }

        static bool IsNone(string value)
        {
            string lower = value.Trim().ToLowerInvariant();
            return lower == "none" || lower == "transparent" || lower.StartsWith("url(");
        }

        // ─── lists ────────────────────────────────────────────────
        float LayoutList(Node list, Style style, float x, float y, float availWidth, Box parent)
        {
            float marginLeft = Length(style, "margin-left", availWidth);
            float marginRight = Length(style, "margin-right", availWidth);
            float marginTop = Length(style, "margin-top", availWidth);
            float marginBottom = Length(style, "margin-bottom", availWidth);
            float paddingLeft = Padding(style, "padding-left", availWidth);
            float contentWidth = Math.Max(40, availWidth - marginLeft - marginRight - paddingLeft);

            var box = new Box
            {
                Kind = BoxKind.Block,
                Rect = new RectangleF(x + marginLeft, y + marginTop, contentWidth + paddingLeft, 0),
                Background = style.Color("background-color", Color.Transparent),
                Node = list
            };
            parent.Add(box);

            string listType = style.Get("list-style-type", list.Tag == "ol" ? "decimal" : "disc");
            int index = 1;
            float cursorY = y + marginTop;

            foreach (Node child in list.Children)
            {
                if (!child.IsElement || child.Tag != "li") continue;
                Style itemStyle = StyleFor(child, false);
                string marker = MarkerText(listType, index++);
                float markerWidth = 22;

                var itemBox = new Box
                {
                    Kind = BoxKind.ListItem,
                    Rect = new RectangleF(x + marginLeft + paddingLeft, cursorY, contentWidth, 0),
                    Node = child
                };
                box.Add(itemBox);

                if (marker.Length > 0)
                {
                    itemBox.Add(new Box
                    {
                        Kind = BoxKind.Marker,
                        Rect = new RectangleF(x + marginLeft + paddingLeft - markerWidth + 2, cursorY, markerWidth - 6, 20),
                        Text = marker,
                        Font = FontOf(itemStyle),
                        TextColor = ColorOf(itemStyle)
                    });
                }

                float innerWidth = Math.Max(30, contentWidth - markerWidth);
                float innerY = cursorY;
                var inlineRun = new List<Node>();
                foreach (Node grand in child.Children)
                {
                    if (grand.IsText || (grand.IsElement && IsInline(grand))) { inlineRun.Add(grand); continue; }
                    if (inlineRun.Count > 0)
                    {
                        Box line = LayoutInlineRun(inlineRun, itemStyle, x + marginLeft + paddingLeft + markerWidth, innerY, innerWidth, itemBox);
                        if (line != null) innerY += line.Rect.Height;
                        inlineRun.Clear();
                    }
                    innerY = LayoutNode(grand, itemStyle, x + marginLeft + paddingLeft + markerWidth, innerY, innerWidth, itemBox);
                }
                if (inlineRun.Count > 0)
                {
                    Box line = LayoutInlineRun(inlineRun, itemStyle, x + marginLeft + paddingLeft + markerWidth, innerY, innerWidth, itemBox);
                    if (line != null) innerY += line.Rect.Height;
                }

                float itemMargin = Length(itemStyle, "margin-bottom", availWidth);
                itemBox.Rect = new RectangleF(x + marginLeft + paddingLeft, cursorY, contentWidth, Math.Max(LineHeight(itemStyle), innerY - cursorY));
                cursorY += itemBox.Rect.Height + itemMargin;
            }

            box.Rect = new RectangleF(x + marginLeft, y + marginTop, contentWidth + paddingLeft, Math.Max(0, cursorY - y - marginTop));
            return y + marginTop + box.Rect.Height + marginBottom;
        }

        static string MarkerText(string type, int index)
        {
            switch (type)
            {
                case "none": return "";
                case "disc": return "•";
                case "circle": return "◦";
                case "square": return "▪";
                case "decimal": return index + ".";
                case "decimal-leading-zero": return index.ToString("00") + ".";
                case "lower-alpha": return ((char)('a' + (index - 1) % 26)).ToString() + ".";
                case "upper-alpha": return ((char)('A' + (index - 1) % 26)).ToString() + ".";
                case "lower-roman": return ToRoman(index).ToLowerInvariant() + ".";
                case "upper-roman": return ToRoman(index) + ".";
                default: return "•";
            }
        }

        static string ToRoman(int number)
        {
            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length && number > 0; i++)
            {
                while (number >= values[i]) { sb.Append(symbols[i]); number -= values[i]; }
            }
            return sb.ToString();
        }

        // ─── tables ───────────────────────────────────────────────
        float LayoutTable(Node table, Style style, float x, float y, float availWidth, Box parent)
        {
            var rows = new List<Node>();
            CollectRows(table, rows);
            if (rows.Count == 0) return y;

            int columns = 0;
            foreach (Node row in rows) columns = Math.Max(columns, CellsOf(row).Count);
            if (columns == 0) return y;

            float marginLeft = Length(style, "margin-left", availWidth);
            float contentWidth = availWidth - marginLeft - Length(style, "margin-right", availWidth);
            float spacing = (float)Css.TryParseLength(style.Get("border-spacing", "0"), 16, 0);
            float available = contentWidth - spacing * (columns + 1);

            var columnWidths = new float[columns];
            var columnNatural = new float[columns];
            for (int c = 0; c < columns; c++) columnWidths[c] = 60;

            foreach (Node row in rows)
            {
                List<Node> cells = CellsOf(row);
                int spanSum = 0;
                for (int c = 0; c < cells.Count && spanSum < columns; c++)
                {
                    Node cell = cells[c];
                    Style cellStyle = StyleFor(cell, false);
                    float natural = MeasureNaturalWidth(cell, cellStyle);
                    float span = Math.Max(1, (int)Css.TryParseLength(cell.AttrValue("colspan", "1"), 16, 1));
                    if (spanSum + span > columns) span = columns - spanSum;
                    float share = natural / span;
                    if (share > columnNatural[spanSum]) columnNatural[spanSum] = share;
                    spanSum += (int)span;
                }
            }

            float total = 0;
            for (int c = 0; c < columns; c++) { columnWidths[c] = Math.Min(columnNatural[c], 240); total += columnWidths[c]; }

            if (total > available && total > 0)
            {
                float scale = available / total;
                for (int c = 0; c < columns; c++) columnWidths[c] = Math.Max(40, columnWidths[c] * scale);
                total = 0;
                for (int c = 0; c < columns; c++) total += columnWidths[c];
            }
            else if (total < available)
            {
                float extra = (available - total) / columns;
                for (int c = 0; c < columns; c++) columnWidths[c] += extra;
                total = available;
            }

            var tableBox = new Box
            {
                Kind = BoxKind.Block,
                Rect = new RectangleF(x + marginLeft + spacing, y + spacing, total, 0),
                Node = table
            };
            parent.Add(tableBox);

            float cursorY = y + spacing;
            foreach (Node row in rows)
            {
                Style rowStyle = StyleFor(row, false);
                List<Node> cells = CellsOf(row);
                float rowHeight = 0;
                float cellX = x + marginLeft + spacing;
                int columnIndex = 0;

                for (int c = 0; c < cells.Count; c++)
                {
                    Node cell = cells[c];
                    int span = Math.Max(1, (int)Css.TryParseLength(cell.AttrValue("colspan", "1"), 16, 1));
                    if (columnIndex >= columns) break;
                    float width = 0;
                    for (int k = 0; k < span && columnIndex + k < columns; k++) width += columnWidths[columnIndex + k];
                    span = Math.Min(span, columns - columnIndex);

                    Style cellStyle = StyleFor(cell, false);
                    var cellBox = new Box
                    {
                        Kind = BoxKind.Block,
                        Rect = new RectangleF(cellX, cursorY, width, 0),
                        Background = cellStyle.Color("background-color", Color.Transparent),
                        Node = cell
                    };
                    ApplyBorders(cellBox, cellStyle, _page.Border);

                    float paddingLeft = Padding(cellStyle, "padding-left", width);
                    float paddingRight = Padding(cellStyle, "padding-right", width);
                    float paddingTop = Padding(cellStyle, "padding-top", width);
                    float paddingBottom = Padding(cellStyle, "padding-bottom", width);
                    float innerWidth = Math.Max(20, width - paddingLeft - paddingRight);

                    float innerY = cursorY + paddingTop;
                    var inlineRun = new List<Node>();
                    foreach (Node grand in cell.Children)
                    {
                        if (grand.IsText || (grand.IsElement && IsInline(grand))) { inlineRun.Add(grand); continue; }
                        if (inlineRun.Count > 0)
                        {
                            Box line = LayoutInlineRun(inlineRun, cellStyle, cellX + paddingLeft, innerY, innerWidth, cellBox);
                            if (line != null) innerY += line.Rect.Height;
                            inlineRun.Clear();
                        }
                        innerY = LayoutNode(grand, cellStyle, cellX + paddingLeft, innerY, innerWidth, cellBox);
                    }
                    if (inlineRun.Count > 0)
                    {
                        Box line = LayoutInlineRun(inlineRun, cellStyle, cellX + paddingLeft, innerY, innerWidth, cellBox);
                        if (line != null) innerY += line.Rect.Height;
                    }

                    float height = (innerY - cursorY) + paddingBottom;
                    cellBox.Rect = new RectangleF(cellX, cursorY, width, height);
                    tableBox.Add(cellBox);
                    rowHeight = Math.Max(rowHeight, height);

                    cellX += width + spacing;
                    columnIndex += span;
                }

                cursorY += rowHeight + spacing;
            }

            tableBox.Rect = new RectangleF(x + marginLeft + spacing, y + spacing, total, Math.Max(0, cursorY - y - spacing * 2));
            return cursorY + Length(style, "margin-bottom", availWidth);
        }

        static void CollectRows(Node table, List<Node> into)
        {
            foreach (Node child in table.Children)
            {
                if (!child.IsElement) continue;
                if (child.Tag == "tr") into.Add(child);
                else if (child.Tag == "thead" || child.Tag == "tbody" || child.Tag == "tfoot") CollectRows(child, into);
            }
        }

        static List<Node> CellsOf(Node row)
        {
            var list = new List<Node>();
            foreach (Node child in row.Children)
                if (child.IsElement && (child.Tag == "td" || child.Tag == "th")) list.Add(child);
            return list;
        }

        float MeasureNaturalWidth(Node cell, Style style)
        {
            float widest = 0;
            foreach (Node child in cell.Children)
            {
                float width = MeasureNodeWidth(child, style);
                if (width > widest) widest = width;
            }
            return Math.Min(600, widest + 24);
        }

        float MeasureNodeWidth(Node node, Style parentStyle)
        {
            if (node.IsText) return _g.MeasureString(node.Text, FontOf(parentStyle)).Width;
            if (!node.IsElement) return 0;
            Style style = StyleFor(node, false);
            if (style.Get("display", "block") == "none") return 0;

            float declared = (float)Css.TryParseLength(style.Get("width", ""), _page.ViewportWidth, double.NaN);
            if (!double.IsNaN(declared)) return declared;

            if (node.Tag == "img" || node.Tag == "input" || node.Tag == "button")
                return LeafWidth(node, style);

            float widest = 0;
            bool anyBlock = false;
            foreach (Node child in node.Children)
            {
                float width = MeasureNodeWidth(child, style);
                if (width > widest) widest = width;
                if (child.IsElement && !IsInline(child)) anyBlock = true;
            }
            if (anyBlock) return widest;
            if (node.Tag == "br") return 0;
            return widest;
        }

        // ─── leaf elements ────────────────────────────────────────
        Box LayoutLeaf(Node node, Style style, float x, float y, float availWidth, Style parentStyle)
        {
            string tag = node.Tag;
            if (tag == "img")
            {
                string src = Html.ResolveUrl(_page.BaseUrl, node.AttrValue("src", node.AttrValue("data-src", "")));
                if (src.Length == 0) return null;
                Bitmap bitmap = Images.Get(src);
                if (bitmap == null && _imageBudget > 0) { _imageBudget--; }

                int naturalWidth = bitmap != null ? bitmap.Width : 0;
                int naturalHeight = bitmap != null ? bitmap.Height : 0;

                int declaredWidth, declaredHeight;
                bool hasWidth = int.TryParse(node.AttrValue("width", ""), out declaredWidth);
                bool hasHeight = int.TryParse(node.AttrValue("height", ""), out declaredHeight);

                int width = naturalWidth;
                int height = naturalHeight;
                if (hasWidth && declaredWidth > 0) width = declaredWidth;
                if (hasHeight && declaredHeight > 0) height = declaredHeight;
                if (width <= 0 && height > 0 && naturalWidth > 0) width = height * naturalWidth / naturalHeight;
                if (height <= 0 && width > 0 && naturalHeight > 0) height = width * naturalHeight / naturalWidth;
                if (width <= 0) width = bitmap == null ? 160 : 160;
                if (height <= 0) height = bitmap == null ? 120 : 100;
                if (width > availWidth) { height = (int)(height * (availWidth / (float)width)); width = (int)availWidth; }

                var box = new Box
                {
                    Kind = BoxKind.Image,
                    Rect = new RectangleF(x, y, width, height),
                    Image = bitmap,
                    ImageBroken = bitmap == null,
                    AltText = node.AttrValue("alt", ""),
                    Node = node,
                    Url = Html.ResolveUrl(_page.BaseUrl, node.AttrValue("src", "")),
                    IsLink = node.Parent != null && node.Parent.Tag == "a" && node.Parent.HasAttr("href"),
                    TextColor = ColorOf(style),
                    Font = FontOf(style)
                };
                if (box.IsLink) box.Url = Html.ResolveUrl(_page.BaseUrl, node.Parent.AttrValue("href", ""));
                return box;
            }

            if (tag == "input")
            {
                string kind = node.AttrValue("type", "text").ToLowerInvariant();
                var box = new Box
                {
                    Kind = BoxKind.Input,
                    Rect = new RectangleF(x, y, LeafWidth(node, style), LeafHeight(node, style)),
                    InputKind = kind,
                    InputValue = node.AttrValue("value", ""),
                    InputName = node.AttrValue("name", ""),
                    Checked = node.HasAttr("checked"),
                    TextColor = ColorOf(style),
                    Font = FontOf(style),
                    Background = style.Color("background-color", Color.White),
                    BorderColor = _page.Border,
                    BorderTop = BorderWidth(style),
                    BorderRight = BorderWidth(style),
                    BorderBottom = BorderWidth(style),
                    BorderLeft = BorderWidth(style),
                    Radius = 4,
                    Node = node
                };
                return box;
            }

            if (tag == "button" || tag == "select")
            {
                string label = node.InnerText.Trim();
                if (label.Length == 0) label = node.AttrValue("value", tag == "select" ? "выбрать" : "OK");
                Font font = FontOf(style);
                float width = Math.Max(64, _g.MeasureString(label, font).Width + 28);
                var box = new Box
                {
                    Kind = BoxKind.Button,
                    Rect = new RectangleF(x, y, width, LeafHeight(node, style)),
                    Text = label,
                    Font = font,
                    TextColor = Color.White,
                    Background = _page.Link,
                    Radius = 6,
                    InputName = node.AttrValue("name", ""),
                    Node = node
                };
                return box;
            }

            if (tag == "textarea")
            {
                var box = new Box
                {
                    Kind = BoxKind.Input,
                    Rect = new RectangleF(x, y, Math.Min(availWidth, 320), 70),
                    InputKind = "textarea",
                    InputValue = node.InnerText,
                    TextColor = ColorOf(style),
                    Font = FontOf(style),
                    Background = Color.White,
                    BorderColor = _page.Border,
                    BorderTop = BorderWidth(style),
                    BorderRight = BorderWidth(style),
                    BorderBottom = BorderWidth(style),
                    BorderLeft = BorderWidth(style),
                    Radius = 4,
                    Node = node
                };
                return box;
            }

            return null;
        }

        float LeafWidth(Node node, Style style)
        {
            string tag = node.Tag;
            if (tag == "button") return 90;
            if (tag == "input")
            {
                string kind = node.AttrValue("type", "text").ToLowerInvariant();
                if (kind == "checkbox" || kind == "radio") return 16;
                if (kind == "hidden") return 0;
                if (kind == "submit" || kind == "button" || kind == "reset")
                    return Math.Max(70, _g.MeasureString(node.AttrValue("value", "OK"), FontOf(style)).Width + 26);
                int size;
                if (int.TryParse(node.AttrValue("size", ""), out size) && size > 0) return size * 8;
                return 180;
            }
            return 100;
        }

        float LeafHeight(Node node, Style style)
        {
            if (node.Tag == "input")
            {
                string kind = node.AttrValue("type", "text").ToLowerInvariant();
                if (kind == "checkbox" || kind == "radio") return 16;
                if (kind == "hidden") return 0;
            }
            return 26;
        }

        float TextHeight(Font font)
        {
            return font.SizeInPoints * 0.75f * 1.35f + 1f;
        }

        float BorderWidth(Style style)
        {
            string border = style.Get("border", "");
            if (border.Length == 0) return 1;
            double width;
            Color color;
            Css.ParseBorder(border, out width, out color);
            return (float)width;
        }

        // ─── inline run ───────────────────────────────────────────
        class InlineItem
        {
            public string Text = "";
            public Font Font;
            public Color Color;
            public bool Underline;
            public bool NoWrap;
            public string Url = "";
            public Box Leaf;
            public float MarginLeft;
            public float MarginRight;
            public Node Node;
        }

        Box LayoutInlineRun(List<Node> nodes, Style parentStyle, float x, float y, float availWidth, Box parent)
        {
            var items = new List<InlineItem>();
            foreach (Node node in nodes) CollectInline(node, parentStyle, items, true);
            if (items.Count == 0) return null;

            // A box squeezed down to a sliver must not shred its text one character per
            // line: wrap against a sane minimum and let the overflow be clipped instead.
            float wrapWidth = availWidth < 24 ? 24 : availWidth;
            var lines = new List<Box>();
            float cursorY = y;

            var current = new List<InlineItem>();
            float lineWidth = 0;
            float lineHeight = LineHeight(parentStyle);

            Action flush = delegate ()
            {
                if (current.Count == 0) return;
                float height = 0;
                foreach (InlineItem item in current)
                {
                    float h = item.Leaf != null ? item.Leaf.Rect.Height : _g.MeasureString(item.Text, item.Font).Height;
                    if (h > height) height = h;
                }
                height = Math.Max(height, lineHeight);
                var lineBox = new Box { Kind = BoxKind.Line, Rect = new RectangleF(x, cursorY, availWidth, height) };
                float cursorX = x;
                foreach (InlineItem item in current)
                {
                    cursorX += item.MarginLeft;
                    if (item.Leaf != null)
                    {
                        item.Leaf.Rect = new RectangleF(cursorX, cursorY + Math.Max(0, (height - item.Leaf.Rect.Height) / 2), item.Leaf.Rect.Width, item.Leaf.Rect.Height);
                        lineBox.Add(item.Leaf);
                    }
                    else if (item.Text.Length > 0)
                    {
                        float textWidth = _g.MeasureString(item.Text, item.Font).Width;
                        float baseline = (height - TextHeight(item.Font) * 0.82f);
                        var text = new Box
                        {
                            Kind = BoxKind.Text,
                            Rect = new RectangleF(cursorX, cursorY + baseline - 2, textWidth, TextHeight(item.Font)),
                            Text = item.Text,
                            Font = item.Font,
                            TextColor = item.Color,
                            Underline = item.Underline,
                            IsLink = item.Url.Length > 0,
                            Url = item.Url,
                            NoWrap = item.NoWrap,
                            Node = item.Node
                        };
                        lineBox.Add(text);
                        cursorX += textWidth;
                    }
                    cursorX += item.MarginRight;
                }
                current.Clear();
                lineWidth = 0;
                cursorY += height;
                lines.Add(lineBox);
            };

            foreach (InlineItem item in items)
            {
                if (item.Leaf != null)
                {
                    float width = item.Leaf.Rect.Width + item.MarginLeft + item.MarginRight;
                    if (lineWidth + width > wrapWidth && current.Count > 0) flush();
                    current.Add(item);
                    lineWidth += width;
                    continue;
                }

                string text = item.Text;
                if (text.Length == 0) continue;

                int index = 0;
                while (index < text.Length)
                {
                    if (text[index] == '\n' || text[index] == '\r')
                    {
                        flush();
                        index++;
                        continue;
                    }

                    int end = text.Length;
                    if (!item.NoWrap)
                    {
                        end = index;
                        float spaceWidth = _g.MeasureString(" ", item.Font).Width;
                        float limit = wrapWidth - lineWidth - item.MarginLeft;
                        while (end < text.Length)
                        {
                            if (text[end] == ' ') { end++; break; }
                            if (text[end] == '\n' || text[end] == '\r') { end++; break; }
                            float probe = _g.MeasureString(text.Substring(index, end - index + 1), item.Font).Width;
                            if (probe > limit && end > index) break;
                            end++;
                            if (item.NoWrap && probe > limit) break;
                        }
                    }
                    else
                    {
                        while (end < text.Length && text[end] != '\n' && text[end] != '\r') end++;
                        if (end < text.Length) end++;
                    }

                    string chunk = text.Substring(index, end - index);
                    index = end;

                    if (chunk.Trim().Length == 0 && current.Count == 0) continue;   // drop leading spaces
                    chunk = chunk.Replace("\n", "").Replace("\r", "");
                    if (chunk.Length == 0) continue;

                    float width = _g.MeasureString(chunk, item.Font).Width;
                    if (lineWidth + width > wrapWidth && current.Count > 0)
                    {
                        bool allSpaces = chunk.Trim().Length == 0;
                        if (!allSpaces) flush();
                    }

                    // A single unbreakable word wider than the line: cut it by characters.
                    if (!item.NoWrap && width > wrapWidth)
                    {
                        int cut = chunk.Length;
                        while (cut > 1 && _g.MeasureString(chunk.Substring(0, cut), item.Font).Width > wrapWidth - lineWidth) cut--;
                        current.Add(Clone(item, chunk.Substring(0, cut)));
                        flush();
                        chunk = chunk.Substring(cut);
                        while (_g.MeasureString(chunk, item.Font).Width > wrapWidth && chunk.Length > 1)
                        {
                            cut = chunk.Length;
                            while (cut > 1 && _g.MeasureString(chunk.Substring(0, cut), item.Font).Width > wrapWidth) cut--;
                            current.Add(Clone(item, chunk.Substring(0, cut)));
                            flush();
                            chunk = chunk.Substring(cut);
                        }
                        if (chunk.Length == 0) continue;
                        width = _g.MeasureString(chunk, item.Font).Width;
                    }

                    current.Add(Clone(item, chunk));
                    lineWidth += width;
                }
            }

            if (current.Count > 0) flush();
            if (lines.Count == 0) return null;
            if (parent != null) foreach (Box line in lines) parent.Add(line);
            // The returned box only reports the total height of the run.
            return new Box { Kind = BoxKind.Line, Rect = new RectangleF(x, y, availWidth, cursorY - y) };
        }

        static InlineItem Clone(InlineItem source, string text)
        {
            var copy = new InlineItem();
            copy.Text = text;
            copy.Font = source.Font;
            copy.Color = source.Color;
            copy.Underline = source.Underline;
            copy.NoWrap = source.NoWrap;
            copy.Url = source.Url;
            copy.MarginLeft = source.MarginLeft;
            copy.MarginRight = source.MarginRight;
            copy.Node = source.Node;
            return copy;
        }

        void CollectInline(Node node, Style parentStyle, List<InlineItem> into, bool topLevel)
        {
            if (node.IsText)
            {
                Style style = StyleFor(node, false);
                string text = node.Text;
                if (text.Length == 0) return;
                into.Add(new InlineItem
                {
                    Text = text,
                    Font = FontOf(style),
                    Color = ColorOf(style),
                    Underline = style.Underline,
                    NoWrap = style.NoWrap,
                    MarginLeft = 0,
                    MarginRight = 0,
                    Node = node
                });
                return;
            }

            if (!node.IsElement) return;
            string tag = node.Tag;
            if (Hidden.Contains(tag)) return;

            Style nodeStyle = StyleFor(node, false);
            if (nodeStyle.Get("display", "inline") == "none") return;

            if (nodeStyle.Get("display", "") == "flex" && !nodeStyle.Get("flex-direction", "row").Trim().ToLowerInvariant().StartsWith("column", StringComparison.Ordinal))
            {
                CollectFlexInline(node, nodeStyle, into);
                return;
            }

            if (tag == "br")
            {
                into.Add(new InlineItem { Text = "\n", Font = FontOf(nodeStyle), Color = ColorOf(nodeStyle), Node = node });
                return;
            }

            if (tag == "img" || tag == "input" || tag == "button" || tag == "select" || tag == "textarea")
            {
                Box leaf = LayoutLeaf(node, nodeStyle, 0, 0, _page.ViewportWidth, parentStyle);
                if (leaf == null) return;
                into.Add(new InlineItem
                {
                    Leaf = leaf,
                    Font = leaf.Font,
                    Color = leaf.TextColor,
                    MarginLeft = Length(nodeStyle, "margin-left", 400),
                    MarginRight = Length(nodeStyle, "margin-right", 400),
                    Node = node
                });
                return;
            }

            // <a href> turns everything inside into a link
            string href = tag == "a" ? node.AttrValue("href", "") : null;
            if (href != null && href.Length > 0)
            {
                string url = Html.ResolveUrl(_page.BaseUrl, href);
                int start = into.Count;
                foreach (Node child in node.Children) CollectInline(child, nodeStyle, into, false);
                // Mark every run the anchor produced, and attribute it to the anchor node.
                for (int i = start; i < into.Count; i++)
                {
                    into[i].Url = url;
                    into[i].Node = node;
                    if (into[i].Leaf != null) into[i].Leaf.Node = node;
                }
                if (into.Count == start)
                {
                    // An anchor with no visible content stands for its own label - never for
                    // the raw href, which on real sites is a hundred character tracking URL.
                    string label = node.InnerText.Trim();
                    if (label.Length == 0) label = WebText.TabTitle(url);
                    if (label.Length == 0 || label.Length > 60) return;
                    into.Add(new InlineItem { Text = label, Font = FontOf(nodeStyle), Color = ColorOf(nodeStyle), Url = url, Node = node });
                }
                return;
            }

            if (tag == "sup" || tag == "sub")
            {
                string text = node.InnerText;
                if (text.Length == 0) return;
                Font small = Fonts.Get("Segoe UI", Math.Max(8, nodeStyle.FontSize * 0.75f), nodeStyle.FontWeight >= 600, false);
                into.Add(new InlineItem { Text = text, Font = small, Color = ColorOf(nodeStyle), Url = null, Node = node });
                return;
            }

            foreach (Node child in node.Children) CollectInline(child, nodeStyle, into, false);
        }

        /// <summary>An inline flex container becomes a row of inline items with gaps.</summary>
        void CollectFlexInline(Node node, Style style, List<InlineItem> into)
        {
            float gap = ResolveGap(style, "gap", _page.ViewportWidth);
            var items = new List<FlexItem>();
            CollectFlexItems(node, style, items);
            foreach (FlexItem item in items)
            {
                if (gap > 0 && into.Count > 0) into[into.Count - 1].MarginRight += gap;
                if (item.Run != null)
                {
                    foreach (Node inline in item.Run) CollectInline(inline, style, into, false);
                    continue;
                }
                Node child = item.Node;
                if (child.IsElement && (child.Tag == "img" || child.Tag == "input" || child.Tag == "button" ||
                                        child.Tag == "select" || child.Tag == "textarea"))
                {
                    Style leafStyle = StyleFor(child, false);
                    Box leaf = LayoutLeaf(child, leafStyle, 0, 0, _page.ViewportWidth, style);
                    if (leaf == null) continue;
                    into.Add(new InlineItem
                    {
                        Leaf = leaf,
                        Font = leaf.Font,
                        Color = leaf.TextColor,
                        MarginLeft = Length(leafStyle, "margin-left", 400),
                        MarginRight = Length(leafStyle, "margin-right", 400),
                        Node = child
                    });
                    continue;
                }
                CollectInline(child, style, into, false);
            }
        }

        // ─── helpers ──────────────────────────────────────────────
        string Collapse(string text, Style style)
        {
            if (text.Length == 0) return "";
            bool preserve = style.Get("white-space", "") == "pre" || style.Get("white-space", "") == "pre-wrap";
            if (preserve) return text;
            var sb = new System.Text.StringBuilder(text.Length);
            bool space = false;
            foreach (char c in text)
            {
                if (c == '\n' || c == '\r' || c == '\t' || c == ' ') { space = true; continue; }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            if (space && sb.Length > 0) sb.Append(' ');
            return sb.ToString();
        }

        float LineHeight(Style style)
        {
            Font font = FontOf(style);
            float height = TextHeight(font);
            string declared = style.Get("line-height", "");
            if (declared.Length > 0)
            {
                double unitless;
                if (double.TryParse(declared.Trim(), System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out unitless) && unitless > 0)
                    return (float)(height * unitless);
                double length = Css.TryParseLength(declared, Metrics(style, height), double.NaN);
                if (!double.IsNaN(length) && length > 0) return (float)length + 1f;
            }
            return height + 2;
        }

        Font FontOf(Style style)
        {
            float size = style.FontSize;
            string family = style.Get("font-family", _page.FontFamily);
            return Fonts.Get(family, size, style.FontWeight >= 600, style.Get("font-style", "") == "italic");
        }

        static Color ColorOf(Style style)
        {
            return style.Color("color", Color.Black);
        }

        static Color ColorOfBorder(Style style, Color fallback)
        {
            string border = style.Get("border", "");
            if (border.Length == 0) return fallback;
            double width;
            Color color;
            Css.ParseBorder(border, out width, out color);
            return color;
        }

        float Length(Style style, string name, float reference)
        {
            return (float)Css.TryParseLength(style.Get(name, ""), Metrics(style, reference), 0);
        }

        /// <summary>The reference lengths of one element: its own font size, the root size and the viewport.</summary>
        CssMetrics Metrics(Style style, double reference)
        {
            CssMetrics m = CssMetrics.Default(reference);
            m.RootFontSize = _rootFontSize;
            m.ViewportHeight = Math.Max(360, _page.ViewportWidth * 1.5);
            m.FontSize = style.FontSize;
            return m;
        }

        float Padding(Style style, string name, float reference)
        {
            string value = style.Get(name, "");
            CssMetrics metrics = Metrics(style, reference);
            if (value.Length == 0)
            {
                string shorthand = style.Get("padding", "");
                if (shorthand.Length == 0) return 0;
                string[] parts = shorthand.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return 0;
                if (name == "padding-top" || name == "padding-bottom")
                    return (float)Css.TryParseLength(parts.Length > 1 ? parts[1] : parts[0], metrics, 0);
                return (float)Css.TryParseLength(parts[0], metrics, 0);
            }
            return (float)Css.TryParseLength(value, metrics, 0);
        }

        /// <summary>Resolves a shorthand of one to four space separated lengths.</summary>
        float[] BoxEdges(Style style, string shorthandName, string sideName, float reference)
        {
            string value = style.Get(sideName, "");
            if (value.Length == 0) value = style.Get(shorthandName, "");
            if (value.Length == 0) return new float[] { 0, 0, 0, 0 };
            CssMetrics metrics = Metrics(style, reference);
            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var edges = new float[4];
            for (int i = 0; i < 4; i++)
            {
                int index = i < parts.Length ? i : parts.Length - 1;
                if (index < 0) return new float[] { 0, 0, 0, 0 };
                edges[i] = (float)Css.TryParseLength(parts[index], metrics, 0);
            }
            return edges;
        }

        void ApplyBorders(Box box, Style style, Color fallback)
        {
            Color borderColor = ColorOfBorder(style, fallback);
            float defaultWidth = BorderWidth(style);
            string border = style.Get("border", "");

            box.BorderColor = borderColor;
            box.BorderTop = defaultWidth;
            box.BorderRight = defaultWidth;
            box.BorderBottom = defaultWidth;
            box.BorderLeft = defaultWidth;

            ApplySide(style, "border-top", ref box.BorderTop, border);
            ApplySide(style, "border-bottom", ref box.BorderBottom, border);
            ApplySide(style, "border-left", ref box.BorderLeft, border);
            ApplySide(style, "border-right", ref box.BorderRight, border);
            if (border.Length == 0 && !style.Has("border-width")) { box.BorderTop = box.BorderRight = box.BorderBottom = box.BorderLeft = 0; }
        }

        void ApplySide(Style style, string name, ref float target, string shorthand)
        {
            string value = style.Get(name, "");
            if (value.Length == 0) return;
            double width;
            Color color;
            Css.ParseBorder(value, out width, out color);
            if (!double.IsNaN(width)) target = (float)width;
        }

        static Color _unusedColor;

        public static string ColorToCss(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }
    }
}
