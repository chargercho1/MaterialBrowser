using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace MaterialBrowser
{
    public class CssRule
    {
        public string[] Selectors = new string[0];
        public readonly List<KeyValuePair<string, string>> Declarations = new List<KeyValuePair<string, string>>();
        /// <summary>Custom properties (--name) declared by this rule.</summary>
        public readonly Dictionary<string, string> Custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int Order;
        public bool Important;
    }

    /// <summary>The reference values a length needs: em, rem, %, vw and vh are not self-contained.</summary>
    public struct CssMetrics
    {
        public double FontSize;
        public double RootFontSize;
        public double ViewportWidth;
        public double ViewportHeight;

        public static CssMetrics Default(double reference)
        {
            CssMetrics m;
            m.FontSize = 16;
            m.RootFontSize = 16;
            m.ViewportWidth = reference <= 0 ? 800 : reference;
            m.ViewportHeight = 800;
            return m;
        }
    }

    /// <summary>Typed access to the declarations that apply to one element.</summary>
    public class Style
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool Has(string name) { return Values.ContainsKey(name); }

        public string Get(string name, string fallback)
        {
            string value;
            return Values.TryGetValue(name, out value) && value.Length > 0 ? value : fallback;
        }

        public Color Color(string name, Color fallback)
        {
            string value;
            if (!Values.TryGetValue(name, out value)) return fallback;
            Color parsed;
            return Css.TryParseColor(value, out parsed) ? parsed : fallback;
        }

        public double Length(string name, double fallback, double reference)
        {
            string value;
            if (!Values.TryGetValue(name, out value)) return fallback;
            return Css.TryParseLength(value, reference, fallback);
        }

        public int FontSize { get { return (int)Math.Round(Length("font-size", 16, 16)); } }

        public int FontWeight
        {
            get
            {
                string value = Get("font-weight", "normal").ToLowerInvariant();
                if (value == "bold" || value == "bolder") return 700;
                if (value == "normal" || value == "lighter") return 400;
                int parsed;
                if (int.TryParse(value, out parsed)) return parsed;
                return 400;
            }
        }

        public bool Underline
        {
            get
            {
                string value = Get("text-decoration", "");
                return value.IndexOf("underline", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public bool NoWrap
        {
            get
            {
                string value = Get("white-space", "");
                return value == "nowrap" || value == "pre";
            }
        }
    }

    /// <summary>
    /// A deliberately small CSS engine: type, class, id and descendant selectors,
    /// specificity resolution and inline styles. Unsupported declarations are ignored.
    /// </summary>
    public static class Css
    {
        static readonly string[] Supported =
        {
            "display", "color", "background", "background-color", "font-size", "font-weight", "font-style",
            "font-family", "text-align", "text-decoration", "margin", "margin-top", "margin-bottom",
            "margin-left", "margin-right", "padding", "padding-top", "padding-bottom", "padding-left",
            "padding-right", "border", "border-top", "border-bottom", "border-left", "border-right",
            "border-width", "border-color", "border-radius", "width", "height", "max-width", "min-width",
            "list-style-type", "vertical-align", "white-space", "overflow", "line-height", "opacity",
            "text-transform", "float", "clear", "position", "box-sizing", "visibility", "min-height",
            "max-height", "flex", "flex-direction", "flex-wrap", "flex-grow", "flex-shrink", "flex-basis",
            "justify-content", "align-items", "align-self", "align-content", "gap", "row-gap", "column-gap",
            "grid-template-columns", "grid-template-rows", "grid-column", "grid-row", "order", "text-indent"
        };

        static readonly HashSet<string> Allowed = new HashSet<string>(Supported, StringComparer.OrdinalIgnoreCase);

        public static List<CssRule> ParseSheet(string css)
        {
            var rules = new List<CssRule>();
            int order = 0;
            ParseInto(css, rules, ref order);
            return rules;
        }

        /// <summary>
        /// Walks one block of CSS. Conditional groups (@media, @supports) are read as if
        /// they applied, because guessing the media state wrong is far less harmful than
        /// losing every rule inside them; @keyframes and friends are dropped entirely.
        /// </summary>
        static void ParseInto(string css, List<CssRule> rules, ref int order)
        {
            if (string.IsNullOrEmpty(css)) return;
            int i = 0;
            int length = css.Length;

            while (i < length)
            {
                if (css[i] == '/' && i + 1 < length && css[i + 1] == '*')
                {
                    int commentEnd = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = commentEnd < 0 ? length : commentEnd + 2;
                    continue;
                }

                if (css[i] == '@')
                {
                    int brace = css.IndexOf('{', i);
                    int semicolon = css.IndexOf(';', i);
                    if (brace < 0)
                    {
                        if (semicolon < 0) break;
                        i = semicolon + 1;
                        continue;
                    }
                    if (semicolon >= 0 && semicolon < brace)
                    {
                        i = semicolon + 1;
                        continue;
                    }

                    string prelude = css.Substring(i, brace - i).ToLowerInvariant();
                    int close = FindMatchingBrace(css, brace);
                    if (close < 0) break;
                    if (prelude.IndexOf("keyframes", StringComparison.Ordinal) >= 0 ||
                        prelude.IndexOf("@font-face", StringComparison.Ordinal) >= 0 ||
                        prelude.IndexOf("@page", StringComparison.Ordinal) >= 0 ||
                        prelude.IndexOf("@property", StringComparison.Ordinal) >= 0 ||
                        prelude.IndexOf("@counter-style", StringComparison.Ordinal) >= 0)
                    {
                        i = close + 1;
                        continue;
                    }
                    ParseInto(css.Substring(brace + 1, close - brace - 1), rules, ref order);
                    i = close + 1;
                    continue;
                }

                int open = css.IndexOf('{', i);
                if (open < 0) break;
                int end = FindMatchingBrace(css, open);
                if (end < 0) break;

                string selectorText = css.Substring(i, open - i).Trim();
                string body = css.Substring(open + 1, end - open - 1);

                if (selectorText.Length > 0 && !selectorText.StartsWith("@"))
                {
                    var rule = new CssRule();
                    rule.Order = order++;
                    rule.Selectors = SplitSelectors(selectorText);
                    ParseDeclarations(body, rule);
                    if (rule.Declarations.Count > 0 || rule.Custom.Count > 0) rules.Add(rule);
                }
                i = end + 1;
            }
        }

        static int FindMatchingBrace(string css, int open)
        {
            int depth = 0;
            for (int i = open; i < css.Length; i++)
            {
                if (css[i] == '{') depth++;
                else if (css[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        static string[] SplitSelectors(string text)
        {
            var list = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    string part = text.Substring(start, i - start).Trim();
                    if (part.Length > 0) list.Add(part);
                    start = i + 1;
                }
            }
            string last = text.Substring(start).Trim();
            if (last.Length > 0) list.Add(last);
            return list.ToArray();
        }

        static void ParseDeclarations(string body, CssRule rule)
        {
            int i = 0;
            while (i < body.Length)
            {
                int colon = body.IndexOf(':', i);
                if (colon < 0) break;
                int end = body.IndexOf(';', colon);
                string chunk = end < 0 ? body.Substring(colon + 1) : body.Substring(colon + 1, end - colon - 1);
                string name = body.Substring(i, colon - i).Trim().TrimEnd('*', '!').Trim();

                if (name.Length > 0 && chunk.Trim().Length > 0)
                {
                    string value = chunk.Trim();
                    if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 10).Trim();
                    if (name.StartsWith("--", StringComparison.Ordinal))
                    {
                        rule.Custom[name] = value;
                    }
                    else if (Allowed.Contains(name))
                    {
                        rule.Declarations.Add(new KeyValuePair<string, string>(name, value));
                        if (value.IndexOf("!important", StringComparison.OrdinalIgnoreCase) >= 0) rule.Important = true;
                    }
                }
                if (end < 0) break;
                i = end + 1;
            }
        }

        // ─── matching ─────────────────────────────────────────────
        /// <summary>Collects declarations for one element, honouring specificity and !important.</summary>
        public static Style Compute(Node element, List<CssRule> rules)
        {
            var style = new Style();
            if (rules == null || rules.Count == 0)
            {
                ApplyInline(element, style);
                return style;
            }

            var best = new Dictionary<string, int>();      // property -> specificity*1000 + order
            var important = new HashSet<string>();

            foreach (CssRule rule in rules)
            {
                int bestMatch = MatchOf(rule, element);
                if (bestMatch < 0) continue;

                foreach (KeyValuePair<string, string> declaration in rule.Declarations)
                {
                    int rank = bestMatch * 1000 + rule.Order;
                    int current;
                    bool has = best.TryGetValue(declaration.Key, out current);
                    if (has && current >= rank && !rule.Important) continue;
                    if (has && important.Contains(declaration.Key) && !rule.Important) continue;
                    best[declaration.Key] = rank;
                    if (rule.Important) important.Add(declaration.Key);
                    style.Values[declaration.Key] = declaration.Value;
                }
            }

            ApplyInline(element, style);
            if (HasReferences(style)) ResolveReferences(element, style);
            return style;
        }

        static int MatchOf(CssRule rule, Node element)
        {
            int bestMatch = -1;
            foreach (string selector in rule.Selectors)
            {
                int specificity = Matches(selector, element);
                if (specificity > bestMatch) bestMatch = specificity;
            }
            return bestMatch;
        }

        static bool HasReferences(Style style)
        {
            foreach (string value in style.Values.Values)
                if (value.IndexOf("var(", StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Custom properties cascade, so the remaining var() references are resolved
        /// against everything the element and its ancestors declare, root first.
        /// </summary>
        static void ResolveReferences(Node element, Style style)
        {
            var chain = new List<Node>();
            Node current = element;
            int depth = 0;
            while (current != null && depth++ < 24) { chain.Add(current); current = current.Parent; }
            chain.Reverse();

            var scope = new Dictionary<string, string>(_rootVars, StringComparer.OrdinalIgnoreCase);
            foreach (Node node in chain)
            {
                if (!node.IsElement) continue;

                string inline;
                if (node.Attr.TryGetValue("style", out inline) && inline.IndexOf("--", StringComparison.Ordinal) >= 0)
                {
                    var parsed = new CssRule();
                    ParseDeclarations(inline, parsed);
                    foreach (KeyValuePair<string, string> kv in parsed.Custom)
                        scope[kv.Key] = ExpandVars(kv.Value, scope, 0);
                }

                foreach (CssRule rule in _varRules)
                {
                    if (MatchOf(rule, node) < 0) continue;
                    foreach (KeyValuePair<string, string> kv in rule.Custom)
                        scope[kv.Key] = ExpandVars(kv.Value, scope, 0);
                }
            }

            var keys = new List<string>(style.Values.Keys);
            foreach (string key in keys)
            {
                string value = style.Values[key];
                if (value.IndexOf("var(", StringComparison.Ordinal) >= 0)
                    style.Values[key] = ExpandVars(value, scope, 0);
            }
        }

        static void ApplyInline(Node element, Style style)
        {
            string inline;
            if (!element.Attr.TryGetValue("style", out inline) || string.IsNullOrEmpty(inline)) return;
            var rule = new CssRule();
            ParseDeclarations(inline, rule);
            bool hasVars = rule.Custom.Count > 0;
            foreach (KeyValuePair<string, string> declaration in rule.Declarations)
            {
                string value = declaration.Value;
                if (hasVars || value.IndexOf("var(", StringComparison.Ordinal) >= 0)
                    value = ExpandVars(value, VariablesFor(rule), 0);
                style.Values[declaration.Key] = value;
            }
        }

        /// <summary>Root-level custom properties, plus the ones the element declares inline.</summary>
        static Dictionary<string, string> VariablesFor(CssRule inline)
        {
            var vars = new Dictionary<string, string>(_rootVars, StringComparer.OrdinalIgnoreCase);
            if (inline.Custom.Count > 0)
                foreach (KeyValuePair<string, string> kv in inline.Custom)
                    vars[kv.Key] = ExpandVars(kv.Value, vars, 0);
            return vars;
        }

        static int MatchParen(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        static int TopLevelComma(string text)
        {
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')') depth--;
                else if (text[i] == ',' && depth == 0) return i;
            }
            return -1;
        }

        // ─── custom properties ────────────────────────────────────
        static Dictionary<string, string> _rootVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly List<CssRule> _varRules = new List<CssRule>();

        /// <summary>
        /// Gathers the custom properties declared on :root / html / body and substitutes
        /// every var() reference the sheet can resolve on its own, so most elements never
        /// pay for the cascade at all.
        /// </summary>
        public static void ExpandRules(List<CssRule> rules)
        {
            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _varRules.Clear();
            if (rules != null)
            {
                foreach (CssRule rule in rules)
                {
                    if (rule.Custom.Count == 0) continue;
                    if (!IsGlobalSelector(rule)) { _varRules.Add(rule); continue; }
                    foreach (KeyValuePair<string, string> kv in rule.Custom) vars[kv.Key] = kv.Value;
                }
                var names = new List<string>(vars.Keys);
                foreach (string name in names) vars[name] = ExpandVars(vars[name], vars, 0);

                foreach (CssRule rule in rules)
                {
                    // A rule may define variables it then uses itself.
                    var local = vars;
                    if (rule.Custom.Count > 0)
                    {
                        local = new Dictionary<string, string>(vars, StringComparer.OrdinalIgnoreCase);
                        foreach (KeyValuePair<string, string> kv in rule.Custom)
                            local[kv.Key] = ExpandVars(kv.Value, local, 0);
                    }
                    for (int i = 0; i < rule.Declarations.Count; i++)
                    {
                        string value = rule.Declarations[i].Value;
                        if (value.IndexOf("var(", StringComparison.Ordinal) < 0) continue;
                        rule.Declarations[i] = new KeyValuePair<string, string>(rule.Declarations[i].Key, ExpandVars(value, local, 0));
                    }
                }
            }
            _rootVars = vars;
        }

        static bool IsGlobalSelector(CssRule rule)
        {
            foreach (string selector in rule.Selectors)
            {
                string s = selector.Trim().ToLowerInvariant();
                if (s == ":root" || s == "html" || s == "body" || s == ":root html" || s == "html:root" ||
                    s.StartsWith(":root,") || s.StartsWith("html,") || s.StartsWith("body,") ||
                    s.EndsWith(",:root") || s.EndsWith(",html") || s.EndsWith(",body"))
                    return true;
            }
            return false;
        }

        /// <summary>Replaces var(--name, fallback) occurrences, recursively.</summary>
        public static string ExpandVars(string value, Dictionary<string, string> vars, int depth)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (depth > 8 || value.IndexOf("var(", StringComparison.Ordinal) < 0) return value;

            var sb = new StringBuilder(value.Length);
            int i = 0;
            while (i < value.Length)
            {
                int at = value.IndexOf("var(", i, StringComparison.Ordinal);
                if (at < 0) { sb.Append(value.Substring(i)); break; }

                sb.Append(value.Substring(i, at - i));
                int open = at + 3;
                int close = MatchParen(value, open);
                if (close < 0) { sb.Append(value.Substring(at)); break; }

                string inner = value.Substring(open + 1, close - open - 1);
                int comma = TopLevelComma(inner);
                string name = (comma < 0 ? inner : inner.Substring(0, comma)).Trim();
                string replacement;
                if (!vars.TryGetValue(name, out replacement))
                    replacement = comma < 0 ? "" : inner.Substring(comma + 1).Trim();
                sb.Append(ExpandVars(replacement, vars, depth + 1));
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>Returns the specificity of the selector against the element, or -1 when it does not match.</summary>
        public static int Matches(string selector, Node element)
        {
            if (string.IsNullOrEmpty(selector)) return -1;
            selector = selector.Trim();
            if (selector == "*") return 0;
            if (selector.StartsWith(":root")) return -1;
            // Child and sibling combinators degrade to descendant matching.
            selector = selector.Replace(" > ", " ").Replace(">+", " ").Replace(" + ", " ").Replace(" +", " ");

            string[] parts = selector.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return -1;

            // Match the rightmost part against the element, then walk up for the rest.
            int specificity = 0;
            Node current = element;
            int index = parts.Length - 1;
            int partScore;

            if (!MatchesSimple(parts[index], current, out partScore)) return -1;
            specificity += partScore;
            index--;

            while (index >= 0)
            {
                Node ancestor = current.Parent;
                bool matched = false;
                while (ancestor != null)
                {
                    int score;
                    if (MatchesSimple(parts[index], ancestor, out score))
                    {
                        specificity += score;
                        current = ancestor;
                        matched = true;
                        break;
                    }
                    ancestor = ancestor.Parent;
                }
                if (!matched) return -1;
                index--;
            }
            return specificity;
        }

        static bool MatchesSimple(string selector, Node node, out int specificity)
        {
            specificity = 0;
            if (node == null || !node.IsElement) return false;

            string simple = selector;
            string pseudo = "";
            int colon = simple.IndexOf(':');
            if (colon >= 0)
            {
                pseudo = simple.Substring(colon + 1).ToLowerInvariant();
                simple = simple.Substring(0, colon);
            }
            // Attribute selectors, e.g. [type], [type="submit"], [rel~="noopener"]
            var attributes = new List<string>();
            int bracket = simple.IndexOf('[');
            if (bracket >= 0)
            {
                int closeBracket = simple.IndexOf(']', bracket);
                if (closeBracket < 0) return false;
                attributes.Add(simple.Substring(bracket + 1, closeBracket - bracket - 1));
                simple = simple.Substring(0, bracket) + simple.Substring(closeBracket + 1);
            }

            // id
            string id = "";
            int hash = simple.IndexOf('#');
            if (hash >= 0)
            {
                int next = simple.IndexOf('.', hash);
                id = next > 0 ? simple.Substring(hash + 1, next - hash - 1) : simple.Substring(hash + 1);
                simple = next > 0 ? simple.Substring(0, next) : simple.Substring(0, hash);
            }

            // classes
            var classes = new List<string>();
            int search = 0;
            while (true)
            {
                int dot = simple.IndexOf('.', search);
                if (dot < 0) break;
                int end = dot + 1;
                while (end < simple.Length && simple[end] != '.' && simple[end] != '#') end++;
                classes.Add(simple.Substring(dot + 1, end - dot - 1));
                search = end;
            }

            // The tag name is whatever precedes the first class or id marker.
            string tag = simple.Trim();
            int cut = tag.IndexOfAny(new[] { '.', '#' });
            if (cut >= 0) tag = tag.Substring(0, cut);
            if (tag.Length > 0 && tag != "*")
            {
                if (!string.Equals(tag, node.Tag, StringComparison.OrdinalIgnoreCase)) return false;
                specificity += 1;
            }

            if (id.Length > 0)
            {
                if (!string.Equals(id, node.Id, StringComparison.Ordinal)) return false;
                specificity += 100;
            }

            if (classes.Count > 0)
            {
                string classList = node.ClassList;
                foreach (string wanted in classes)
                {
                    bool found = false;
                    foreach (string actual in classList.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                        if (string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                    if (!found) return false;
                }
                specificity += 10 * classes.Count;
            }

            foreach (string condition in attributes)
            {
                if (!MatchAttribute(condition, node)) return false;
            }
            if (attributes.Count > 0) specificity += 10 * attributes.Count;

            // Pseudo-classes we do understand
            if (pseudo.Length > 0)
            {
                if (pseudo.StartsWith("link") || pseudo.StartsWith("visited") || pseudo == "any-link") { }
                else if (pseudo == "first-child" || pseudo == "last-child") { }
                else return false;
            }

            return true;
        }

        /// <summary>Evaluates one attribute condition such as type="submit" or rel~="noopener".</summary>
        static bool MatchAttribute(string condition, Node node)
        {
            condition = condition.Trim();
            if (condition.Length == 0) return true;
            int i = 0;
            while (i < condition.Length && (char.IsLetterOrDigit(condition[i]) || condition[i] == '-' || condition[i] == '_' || condition[i] == ':')) i++;
            string name = condition.Substring(0, i);
            string rest = condition.Substring(i).Trim();
            if (name.Length == 0) return false;

            string actual;
            if (!node.Attr.TryGetValue(name, out actual)) return false;
            if (rest.Length == 0) return true;

            string op = rest.Substring(0, 1);
            string wanted = rest.Substring(1).Trim().Trim('"', '\'');
            switch (op)
            {
                case "=":
                    return string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase);
                case "~":
                    foreach (string token in actual.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                        if (string.Equals(token, wanted, StringComparison.OrdinalIgnoreCase)) return true;
                    return false;
                case "|":
                    return string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase) ||
                           actual.StartsWith(wanted + "-", StringComparison.OrdinalIgnoreCase);
                case "^":
                    return actual.StartsWith(wanted, StringComparison.OrdinalIgnoreCase);
                case "$":
                    return actual.EndsWith(wanted, StringComparison.OrdinalIgnoreCase);
                case "*":
                    return actual.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
                default:
                    return false;
            }
        }

        // ─── value parsing ────────────────────────────────────────
        static readonly Dictionary<string, Color> NamedColors = BuildNamedColors();

        static Dictionary<string, Color> BuildNamedColors()
        {
            var map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            map["black"] = Color.Black; map["white"] = Color.White; map["red"] = Color.FromArgb(244, 67, 54);
            map["green"] = Color.FromArgb(0, 128, 0); map["blue"] = Color.FromArgb(0, 0, 255);
            map["gray"] = Color.FromArgb(128, 128, 128); map["grey"] = Color.FromArgb(128, 128, 128);
            map["silver"] = Color.FromArgb(192, 192, 192); map["maroon"] = Color.FromArgb(128, 0, 0);
            map["olive"] = Color.FromArgb(128, 128, 0); map["lime"] = Color.FromArgb(0, 255, 0);
            map["aqua"] = Color.FromArgb(0, 255, 255); map["cyan"] = Color.FromArgb(0, 255, 255);
            map["teal"] = Color.FromArgb(0, 128, 128); map["navy"] = Color.FromArgb(0, 0, 128);
            map["fuchsia"] = Color.FromArgb(255, 0, 255); map["magenta"] = Color.FromArgb(255, 0, 255);
            map["purple"] = Color.FromArgb(128, 0, 128); map["orange"] = Color.FromArgb(255, 165, 0);
            map["yellow"] = Color.FromArgb(255, 255, 0); map["pink"] = Color.FromArgb(255, 192, 203);
            map["brown"] = Color.FromArgb(165, 42, 42); map["gold"] = Color.FromArgb(255, 215, 0);
            map["indigo"] = Color.FromArgb(75, 0, 130); map["violet"] = Color.FromArgb(238, 130, 238);
            map["coral"] = Color.FromArgb(255, 127, 80); map["salmon"] = Color.FromArgb(250, 128, 114);
            map["khaki"] = Color.FromArgb(240, 230, 140); map["plum"] = Color.FromArgb(221, 160, 221);
            map["tan"] = Color.FromArgb(210, 180, 140); map["beige"] = Color.FromArgb(245, 245, 220);
            map["ivory"] = Color.FromArgb(255, 255, 240); map["lavender"] = Color.FromArgb(230, 230, 250);
            map["turquoise"] = Color.FromArgb(64, 224, 208); map["darkgray"] = Color.FromArgb(169, 169, 169);
            map["darkgrey"] = Color.FromArgb(169, 169, 169); map["lightgray"] = Color.FromArgb(211, 211, 211);
            map["lightgrey"] = Color.FromArgb(211, 211, 211); map["whitesmoke"] = Color.FromArgb(245, 245, 245);
            map["gainsboro"] = Color.FromArgb(220, 220, 220); map["dimgray"] = Color.FromArgb(105, 105, 105);
            map["transparent"] = Color.Transparent;
            return map;
        }

        public static bool TryParseColor(string text, out Color color)
        {
            color = Color.Black;
            if (string.IsNullOrEmpty(text)) return false;
            string value = text.Trim();
            if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                int open = value.IndexOf('(');
                int close = value.IndexOf(')');
                if (open < 0 || close < 0) return false;
                string[] parts = value.Substring(open + 1, close - open - 1).Split(',');
                if (parts.Length < 3) return false;
                int r, g, b, a = 255;
                if (!int.TryParse(parts[0].Trim(), out r)) return false;
                if (!int.TryParse(parts[1].Trim(), out g)) return false;
                if (!int.TryParse(parts[2].Trim(), out b)) return false;
                if (parts.Length > 3 && !int.TryParse(parts[3].Trim(), out a)) a = 255;
                color = Color.FromArgb(Clamp(a), Clamp(r), Clamp(g), Clamp(b));
                return true;
            }
            if (value.StartsWith("#"))
            {
                string hex = value.Substring(1);
                if (hex.Length == 3) hex = hex[0] + "" + hex[0] + hex[1] + "" + hex[1] + hex[2] + "" + hex[2];
                if (hex.Length == 8)
                {
                    int a = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int r = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(4, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(6, 2), 16);
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
                if (hex.Length != 6) return false;
                try
                {
                    int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                    color = Color.FromArgb(255, r, g, b);
                    return true;
                }
                catch { return false; }
            }
            Color named;
            if (NamedColors.TryGetValue(value, out named)) { color = named; return true; }
            return false;
        }

        static int Clamp(int value) { return value < 0 ? 0 : value > 255 ? 255 : value; }

        public static bool TryParseLength(string text, double reference, out double value)
        {
            return TryParseLength(text, CssMetrics.Default(reference), out value);
        }

        public static bool TryParseLength(string text, CssMetrics m, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim().ToLowerInvariant();
            if (t == "auto") { value = double.NaN; return true; }
            if (t == "inherit" || t == "initial" || t == "none" || t == "unset") { value = double.NaN; return true; }

            // calc() / min() / max() / clamp()
            if (t.StartsWith("calc(", StringComparison.Ordinal) || t.StartsWith("min(", StringComparison.Ordinal) ||
                t.StartsWith("max(", StringComparison.Ordinal) || t.StartsWith("clamp(", StringComparison.Ordinal))
            {
                int open = t.IndexOf('(');
                int close = t.LastIndexOf(')');
                if (open > 0 && close > open)
                {
                    int index = 0;
                    double computed;
                    if (EvalSum(t.Substring(open + 1, close - open - 1), ref index, m, out computed))
                    {
                        value = computed;
                        return true;
                    }
                }
                return false;
            }

            double number;
            // Split the numeric prefix from the unit: "12px", " 2.5em ", "-3%" ...
            string unit = t.Trim();
            int split = 0;
            while (split < unit.Length && (char.IsDigit(unit[split]) || unit[split] == '.' || unit[split] == '-' || unit[split] == '+')) split++;
            string digits = unit.Substring(0, split);
            if (digits.Length == 0 || !double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return false;
            string suffix = unit.Substring(split);

            double scale;
            if (!UnitScale(suffix, m, out scale)) return false;
            value = number * scale;
            return true;
        }

        static bool UnitScale(string suffix, CssMetrics m, out double scale)
        {
            scale = 1;
            if (suffix.Length == 0 || suffix == "px") return true;
            if (suffix == "em") { scale = m.FontSize; return true; }
            if (suffix == "rem") { scale = m.RootFontSize; return true; }
            if (suffix == "ch") { scale = m.FontSize * 0.5; return true; }
            if (suffix == "ex") { scale = m.FontSize * 0.5; return true; }
            if (suffix == "pt") { scale = 96.0 / 72.0; return true; }
            if (suffix == "pc") { scale = 16.0; return true; }
            if (suffix == "in") { scale = 96.0; return true; }
            if (suffix == "cm") { scale = 96.0 / 2.54; return true; }
            if (suffix == "mm") { scale = 96.0 / 25.4; return true; }
            if (suffix == "%") { scale = m.ViewportWidth / 100.0; return true; }
            if (suffix == "vw") { scale = m.ViewportWidth / 100.0; return true; }
            if (suffix == "vh") { scale = m.ViewportHeight / 100.0; return true; }
            if (suffix == "vmin") { scale = Math.Min(m.ViewportWidth, m.ViewportHeight) / 100.0; return true; }
            if (suffix == "vmax") { scale = Math.Max(m.ViewportWidth, m.ViewportHeight) / 100.0; return true; }
            return false;
        }

        // ─── calc() ───────────────────────────────────────────────
        static void SkipBlanks(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static bool EvalSum(string s, ref int i, CssMetrics m, out double value)
        {
            double left;
            if (!EvalProduct(s, ref i, m, out left)) { value = 0; return false; }
            while (true)
            {
                SkipBlanks(s, ref i);
                if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                {
                    char op = s[i];
                    i++;
                    double right;
                    if (!EvalProduct(s, ref i, m, out right)) { value = 0; return false; }
                    left = op == '+' ? left + right : left - right;
                }
                else break;
            }
            value = left;
            return true;
        }

        static bool EvalProduct(string s, ref int i, CssMetrics m, out double value)
        {
            double left;
            if (!EvalTerm(s, ref i, m, out left)) { value = 0; return false; }
            while (true)
            {
                SkipBlanks(s, ref i);
                if (i < s.Length && (s[i] == '*' || s[i] == '/'))
                {
                    char op = s[i];
                    i++;
                    double right;
                    if (!EvalTerm(s, ref i, m, out right)) { value = 0; return false; }
                    if (op == '/')
                    {
                        if (right == 0) { value = 0; return false; }
                        left /= right;
                    }
                    else left *= right;
                }
                else break;
            }
            value = left;
            return true;
        }

        static bool EvalTerm(string s, ref int i, CssMetrics m, out double value)
        {
            value = 0;
            SkipBlanks(s, ref i);
            if (i >= s.Length) return false;

            if (s[i] == '(')
            {
                i++;
                if (!EvalSum(s, ref i, m, out value)) return false;
                SkipBlanks(s, ref i);
                if (i >= s.Length || s[i] != ')') return false;
                i++;
                return true;
            }

            if (char.IsLetter(s[i]))
            {
                int start = i;
                while (i < s.Length && (char.IsLetter(s[i]) || s[i] == '-')) i++;
                string name = s.Substring(start, i - start);
                SkipBlanks(s, ref i);
                if (i >= s.Length || s[i] != '(') return false;
                i++;

                var args = new List<double>();
                while (true)
                {
                    double argument;
                    if (!EvalSum(s, ref i, m, out argument)) return false;
                    args.Add(argument);
                    SkipBlanks(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    break;
                }
                SkipBlanks(s, ref i);
                if (i >= s.Length || s[i] != ')') return false;
                i++;
                if (args.Count == 0) return false;

                double result = args[0];
                for (int a = 1; a < args.Count; a++)
                {
                    if (name == "min") result = Math.Min(result, args[a]);
                    else if (name == "max") result = Math.Max(result, args[a]);
                    else result = args[a];
                }
                value = result;
                return true;
            }

            int digits = i;
            if (s[i] == '+' || s[i] == '-') i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i == digits) return false;
            double number;
            if (!double.TryParse(s.Substring(digits, i - digits), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return false;

            int unitStart = i;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            double scale;
            if (!UnitScale(s.Substring(unitStart, i - unitStart), m, out scale)) return false;
            value = number * scale;
            return true;
        }

        public static double TryParseLength(string text, CssMetrics m, double fallback)
        {
            double value;
            if (!TryParseLength(text, m, out value)) return fallback;
            if (double.IsNaN(value)) return fallback;
            return value;
        }

        public static double TryParseLength(string text, double reference, double fallback)
        {
            double value;
            if (!TryParseLength(text, reference, out value)) return fallback;
            if (double.IsNaN(value)) return fallback;
            return value;
        }

        /// <summary>Splits "1px solid red" into a width, a style and a color.</summary>
        public static void ParseBorder(string text, out double width, out Color color)
        {
            width = 1;
            color = Color.Gray;
            if (string.IsNullOrEmpty(text)) return;
            string[] tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string token in tokens)
            {
                double parsed;
                if (TryParseLength(token, 16, out parsed) && !double.IsNaN(parsed) &&
                    (token.EndsWith("px") || token.EndsWith("pt") || double.TryParse(token, out parsed)))
                {
                    width = Math.Max(0, parsed);
                    continue;
                }
                Color colorValue;
                if (TryParseColor(token, out colorValue)) { color = colorValue; continue; }
            }

            string lower = text.ToLowerInvariant();
            if (lower.IndexOf("none", StringComparison.Ordinal) >= 0) width = 0;
        }

        public static string ResolveBackground(string value, Color fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback.Name;
            string lower = value.ToLowerInvariant();
            if (lower == "transparent" || lower == "none" || lower == "initial") return "transparent";
            int start = value.IndexOf("rgb", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                int end = value.IndexOf(')', start);
                if (end > start) return value.Substring(start, end - start + 1);
            }
            return value;
        }
    }
}
