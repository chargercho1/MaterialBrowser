using System;
using System.Collections.Generic;
using System.Text;

namespace MaterialBrowser
{
    /// <summary>A parsed DOM node: elements, text and comments.</summary>
    public class Node
    {
        public string Tag;                       // lower case, null for text nodes
        public string Text = "";
        public readonly Dictionary<string, string> Attr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<Node> Children = new List<Node>();
        public Node Parent;
        public bool BlockFromCss;

        public Node()
        {
        }

        public Node(string tag) { Tag = tag; }

        public bool IsText { get { return Tag == null; } }

        public bool IsElement { get { return Tag != null; } }

        public string AttrValue(string name)
        {
            string value;
            return Attr.TryGetValue(name, out value) ? value : null;
        }

        public string AttrValue(string name, string fallback)
        {
            string value;
            return Attr.TryGetValue(name, out value) ? value : fallback;
        }

        public bool HasAttr(string name) { return Attr.ContainsKey(name); }

        public void SetAttr(string name, string value) { Attr[name] = value; }

        public string ClassList
        {
            get { return AttrValue("class", ""); }
        }

        public string Id { get { return AttrValue("id", ""); } }

        public Node Append(Node child)
        {
            child.Parent = this;
            Children.Add(child);
            return child;
        }

        public Node AppendText(string text)
        {
            var node = new Node();
            node.Text = text;
            return Append(node);
        }

        public Node AppendElement(string tag)
        {
            return Append(new Node(tag));
        }

        /// <summary>Depth-first walk over this node and its descendants.</summary>
        public void Walk(Action<Node> visit)
        {
            if (visit != null) visit(this);
            for (int i = 0; i < Children.Count; i++) Children[i].Walk(visit);
        }

        public List<Node> FindAll(string tag)
        {
            var list = new List<Node>();
            Walk(delegate (Node n)
            {
                if (string.Equals(n.Tag, tag, StringComparison.OrdinalIgnoreCase)) list.Add(n);
            });
            return list;
        }

        public Node FindFirst(string tag)
        {
            Node found = null;
            Walk(delegate (Node n)
            {
                if (found == null && string.Equals(n.Tag, tag, StringComparison.OrdinalIgnoreCase)) found = n;
            });
            return found;
        }

        /// <summary>Concatenated text of this subtree, entities already decoded.</summary>
        public string InnerText
        {
            get
            {
                var sb = new StringBuilder();
                CollectText(sb);
                return sb.ToString();
            }
        }

        void CollectText(StringBuilder sb)
        {
            if (IsText) { sb.Append(Text); return; }
            for (int i = 0; i < Children.Count; i++) Children[i].CollectText(sb);
        }

        public string TitleText
        {
            get
            {
                Node title = FindFirst("title");
                if (title == null) return "";
                return Html.Decode(title.InnerText).Trim();
            }
        }
    }

    /// <summary>
    /// A forgiving HTML tokenizer. It handles void elements, raw-text elements,
    /// the usual implicit closings and numeric plus named character references,
    /// which is enough to build a usable tree out of real-world pages.
    /// </summary>
    public static class Html
    {
        static readonly HashSet<string> Void = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta",
            "param", "source", "track", "wbr", "basefont", "frame", "isindex"
        };

        static readonly HashSet<string> RawText = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style", "textarea", "title"
        };

        static readonly HashSet<string> BlockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "address", "article", "aside", "blockquote", "div", "dl", "fieldset", "figure", "footer",
            "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "main", "nav", "ol", "p",
            "pre", "section", "table", "ul", "li", "dt", "dd", "tr", "td", "th", "thead", "tbody",
            "tfoot", "figure", "figcaption", "details", "summary", "dialog"
        };

        static readonly Dictionary<string, string> Entities = BuildEntities();

        static Dictionary<string, string> BuildEntities()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] basic =
            {
                "amp", "&", "lt", "<", "gt", ">", "quot", "\"", "apos", "'", "nbsp", " ",
                "laquo", "«", "raquo", "»", "ldquo", "“", "rdquo", "”", "lsquo", "‘", "rsquo", "’",
                "mdash", "—", "ndash", "–", "hellip", "…", "middot", "·", "bull", "•",
                "copy", "©", "reg", "®", "trade", "™", "deg", "°", "plusmn", "±",
                "times", "×", "divide", "÷", "frac12", "½", "frac14", "¼", "frac34", "¾",
                "sup2", "²", "sup3", "³", "micro", "µ", "para", "¶", "sect", "§",
                "dagger", "†", "permil", "‰", "prime", "′", "Prime", "″",
                "euro", "€", "pound", "£", "yen", "¥", "cent", "¢", "curren", "¤",
                "larr", "←", "uarr", "↑", "rarr", "→", "darr", "↓", "harr", "↔",
                "lArr", "⇐", "uArr", "⇑", "rArr", "⇒", "dArr", "⇓", "hArr", "⇔",
                "shy", "­", "ensp", " ", "emsp", " ", "thinsp", " ", "zwnj", "‌", "zwj", "‍",
                "alpha", "α", "beta", "β", "gamma", "γ", "delta", "δ", "pi", "π",
                "sigma", "σ", "omega", "ω", "Omega", "Ω", "infin", "∞", "ne", "≠",
                "le", "≤", "ge", "≥", "asymp", "≈", "equiv", "≡", "sum", "∑", "radic", "√",
                "int", "∫", "part", "∂", "nabla", "∇", "isin", "∈", "notin", "∉", "cap", "∩",
                "cup", "∪", "sub", "⊂", "sup", "⊃", "sube", "⊆", "supe", "⊇",
                "hearts", "♥", "diams", "♦", "clubs", "♣", "spades", "♠", "check", "✓",
                "star", "☆", "phone", "☎", "larrb", "⇤", "rarrb", "⇥",
                "AElig", "Æ", "aelig", "æ", "Oslash", "Ø", "oslash", "ø", "Aring", "Å",
                "aring", "å", "Aacute", "Á", "aacute", "á", "Auml", "Ä", "auml", "ä",
                "Ccedil", "Ç", "ccedil", "ç", "Eacute", "É", "eacute", "é", "Egrave", "È",
                "egrave", "è", "szlig", "ß", "Ntilde", "Ñ", "ntilde", "ñ", "Oacute", "Ó",
                "oacute", "ó", "Ouml", "Ö", "ouml", "ö", "Uuml", "Ü", "uuml", "ü"
            };
            for (int i = 0; i + 1 < basic.Length; i += 2) map[basic[i]] = basic[i + 1];
            return map;
        }

        public static Node Parse(string html)
        {
            var root = new Node("#document");
            if (string.IsNullOrEmpty(html)) return root;

            var stack = new Stack<Node>();
            stack.Push(root);
            int i = 0;
            int length = html.Length;

            var pending = new StringBuilder();

            Action flush = delegate ()
            {
                if (pending.Length == 0) return;
                string text = Decode(pending.ToString());
                pending.Length = 0;
                if (text.Length == 0) return;
                if (stack.Count == 0) stack.Push(root);
                stack.Peek().AppendText(text);
            };

            while (i < length)
            {
                char c = html[i];
                if (c != '<')
                {
                    pending.Append(c);
                    i++;
                    continue;
                }

                // Comment / doctype / CDATA
                if (string.Compare(html, i, "<!--", 0, 4, StringComparison.Ordinal) == 0)
                {
                    flush();
                    int end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = end < 0 ? length : end + 3;
                    continue;
                }
                if (string.Compare(html, i, "<!", 0, 2, StringComparison.Ordinal) == 0 || string.Compare(html, i, "<?", 0, 2, StringComparison.Ordinal) == 0)
                {
                    flush();
                    int end = html.IndexOf('>', i);
                    i = end < 0 ? length : end + 1;
                    continue;
                }
                if (string.Compare(html, i, "</", 0, 2, StringComparison.Ordinal) == 0)
                {
                    flush();
                    int end = html.IndexOf('>', i);
                    if (end < 0) break;
                    string name = html.Substring(i + 2, end - i - 2).Trim().ToLowerInvariant();
                    int space = name.IndexOfAny(new[] { ' ', '\t', '\n', '\r' });
                    if (space > 0) name = name.Substring(0, space);
                    CloseTag(stack, name);
                    i = end + 1;
                    continue;
                }

                // Opening tag
                int tagEnd = FindTagEnd(html, i);
                if (tagEnd < 0)
                {
                    pending.Append(c);
                    i++;
                    continue;
                }

                flush();
                string body = html.Substring(i + 1, tagEnd - i - 1);
                bool selfClosing = body.EndsWith("/");
                if (selfClosing) body = body.Substring(0, body.Length - 1);

                int nameEnd = 0;
                while (nameEnd < body.Length && !char.IsWhiteSpace(body[nameEnd])) nameEnd++;
                string tag = body.Substring(0, nameEnd).ToLowerInvariant();
                string rest = nameEnd < body.Length ? body.Substring(nameEnd) : "";

                if (tag.Length == 0)
                {
                    pending.Append(c);
                    i++;
                    continue;
                }

                ImplicitClose(stack, tag);
                if (stack.Count == 0) stack.Push(root);

                var node = new Node(tag);
                ParseAttributes(rest, node.Attr);
                stack.Peek().Append(node);

                if (Void.Contains(tag) || selfClosing) { i = tagEnd + 1; continue; }

                if (RawText.Contains(tag))
                {
                    string closing = "</" + tag;
                    int index = IndexOfIgnoreCase(html, closing, tagEnd + 1);
                    string inner = index < 0 ? html.Substring(tagEnd + 1) : html.Substring(tagEnd + 1, index - tagEnd - 1);
                    if (tag == "script" || tag == "style" || tag == "textarea" || tag == "title")
                        node.AppendText(Decode(inner));
                    i = index < 0 ? length : html.IndexOf('>', index) + 1;
                    if (index < 0) i = length;
                    continue;
                }

                stack.Push(node);
                i = tagEnd + 1;
            }

            flush();
            return root;
        }

        static int FindTagEnd(string html, int start)
        {
            bool inQuote = false;
            char quote = '\0';
            for (int i = start + 1; i < html.Length; i++)
            {
                char c = html[i];
                if (inQuote)
                {
                    if (c == quote) inQuote = false;
                    continue;
                }
                if (c == '"' || c == '\'') { inQuote = true; quote = c; continue; }
                if (c == '>') return i;
            }
            return -1;
        }

        static int IndexOfIgnoreCase(string haystack, string needle, int start)
        {
            if (start >= haystack.Length) return -1;
            return haystack.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Applies the closing rules browsers use when markup omits end tags.</summary>
        static void ImplicitClose(Stack<Node> stack, string tag)
        {
            if (stack.Count < 2) return;
            string top = stack.Peek().Tag;

            if (top == "p" && BlockTags.Contains(tag) && tag != "p") { stack.Pop(); return; }
            if (top == "li" && tag == "li") { stack.Pop(); return; }
            if ((top == "td" || top == "th") && (tag == "td" || tag == "th" || tag == "tr")) { stack.Pop(); return; }
            if (top == "tr" && (tag == "tr" || tag == "tbody" || tag == "tfoot" || tag == "thead")) { stack.Pop(); return; }
            if (top == "option" && (tag == "option" || tag == "optgroup")) { stack.Pop(); return; }
            if ((top == "dt" || top == "dd") && (tag == "dt" || tag == "dd")) { stack.Pop(); return; }
            if (top == "head" && tag == "body") { stack.Pop(); return; }
        }

        static void CloseTag(Stack<Node> stack, string name)
        {
            if (name.Length == 0) return;
            // Stack<T>.ToArray() returns the top of the stack first.
            var items = stack.ToArray();
            int pops = -1;
            for (int i = 0; i < items.Length; i++)
            {
                if (string.Equals(items[i].Tag, name, StringComparison.OrdinalIgnoreCase)) { pops = i + 1; break; }
            }
            if (pops < 0) return;
            // Never unwind past the document root.
            while (stack.Count > 1 && pops-- > 0) stack.Pop();
        }

        public static void ParseAttributes(string text, Dictionary<string, string> into)
        {
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == '/')) i++;
                int nameStart = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '=' && text[i] != '/') i++;
                if (i == nameStart) break;
                string name = text.Substring(nameStart, i - nameStart).Trim();
                if (name.Length == 0) break;

                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                string value = "";
                if (i < text.Length && text[i] == '=')
                {
                    i++;
                    while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                    if (i < text.Length && (text[i] == '"' || text[i] == '\''))
                    {
                        char quote = text[i++];
                        int valueStart = i;
                        while (i < text.Length && text[i] != quote) i++;
                        value = text.Substring(valueStart, i - valueStart);
                        if (i < text.Length) i++;
                    }
                    else
                    {
                        int valueStart = i;
                        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '>') i++;
                        value = text.Substring(valueStart, i - valueStart);
                    }
                }
                into[name] = Decode(value);
            }
        }

        public static string Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.IndexOf('&') < 0) return text;

            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c != '&') { sb.Append(c); i++; continue; }

                int semicolon = text.IndexOf(';', i + 1);
                if (semicolon < 0 || semicolon - i > 12) { sb.Append(c); i++; continue; }

                string name = text.Substring(i + 1, semicolon - i - 1);
                string replacement = null;

                if (name.Length > 0 && name[0] == '#')
                {
                    long code;
                    string digits = name.Substring(1);
                    bool hex = digits.Length > 0 && (digits[0] == 'x' || digits[0] == 'X');
                    if (hex) digits = digits.Substring(1);
                    if (digits.Length == 0 || digits.Length > 8) { sb.Append(c); i++; continue; }
                    if (long.TryParse(hex ? digits : digits, hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out code))
                    {
                        if (code > 0 && code <= 0x10FFFF) replacement = char.ConvertFromUtf32((int)code);
                    }
                }
                else
                {
                    if (!Entities.TryGetValue(name, out replacement))
                    {
                        sb.Append(c); i++; continue;
                    }
                }

                if (replacement == null) { sb.Append(c); i++; continue; }
                sb.Append(replacement);
                i = semicolon + 1;
            }
            return sb.ToString();
        }

        /// <summary>Resolves href/src against the document URL.</summary>
        public static string ResolveUrl(string documentUrl, string reference)
        {
            if (string.IsNullOrEmpty(reference)) return "";
            reference = reference.Trim();
            if (reference.Length == 0) return "";
            if (reference.StartsWith("#")) return "";
            if (reference.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return "";
            if (reference.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return reference;
            if (reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return reference;

            try
            {
                if (reference.StartsWith("//"))
                    return new Uri(new Uri(documentUrl), "http:" + reference).AbsoluteUri;
                return new Uri(new Uri(documentUrl), reference).AbsoluteUri;
            }
            catch { return reference; }
        }
    }
}
