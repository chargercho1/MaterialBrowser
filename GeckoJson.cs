using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MaterialBrowser
{
    /// <summary>
    /// Tiny JSON reader/writer. The Gecko engine talks WebDriver BiDi over a
    /// WebSocket, and the process has no other JSON available, so the same
    /// hand-written-parser approach as the HTML and CSS engines is used here.
    /// Values map to Dictionary&lt;string,object&gt;, List&lt;object&gt;, string,
    /// double, bool and null.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) return null;
            int i = 0;
            object value = ReadValue(text, ref i);
            return value;
        }

        static object ReadValue(string s, ref int i)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (c == 't' && Match(s, i, "true")) { i += 4; return true; }
            if (c == 'f' && Match(s, i, "false")) { i += 5; return false; }
            if (c == 'n' && Match(s, i, "null")) { i += 4; return null; }
            return ReadNumber(s, ref i);
        }

        static bool Match(string s, int i, string word)
        {
            if (i + word.Length > s.Length) return false;
            for (int k = 0; k < word.Length; k++)
                if (s[i + k] != word[k]) return false;
            return true;
        }

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            i++;                                     // {
            while (i < s.Length)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == '}') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                if (s[i] != '"') { i++; continue; }   // skip junk rather than throw
                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                map[key] = ReadValue(s, ref i);
            }
            return map;
        }

        static List<object> ReadArray(string s, ref int i)
        {
            var list = new List<object>();
            i++;                                     // [
            while (i < s.Length)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == ']') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                list.Add(ReadValue(s, ref i));
            }
            return list;
        }

        static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;                                     // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '/': sb.Append('/'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case 'u':
                        if (i + 4 > s.Length) { i = s.Length; break; }
                        int code;
                        if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            sb.Append((char)code);
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        static object ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E' || (c >= '0' && c <= '9')) i++;
                else break;
            }
            double value;
            if (start == i) { i++; return null; }
            if (double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;
            return null;
        }

        // ─── writing ───────────────────────────────────────────────
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string) { WriteString(sb, (string)value); return; }
            if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }
            if (value is int || value is long || value is double || value is float || value is decimal)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var map = value as IDictionary<string, object>;
            if (map != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, pair.Key);
                    sb.Append(':');
                    WriteValue(sb, pair.Value);
                }
                sb.Append('}');
                return;
            }
            var list = value as IEnumerable<object>;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string text)
        {
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ─── small helpers ─────────────────────────────────────────
        public static Dictionary<string, object> Object(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static string Text(Dictionary<string, object> map, string key)
        {
            if (map == null) return "";
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return "";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static Dictionary<string, object> Child(Dictionary<string, object> map, string key)
        {
            if (map == null) return null;
            object value;
            if (!map.TryGetValue(key, out value)) return null;
            return value as Dictionary<string, object>;
        }
    }
}