using System;
using System.Collections.Generic;
using System.Text;

namespace MaterialBrowser
{
    public class SearchEngineInfo
    {
        public string Name;
        public string QueryUrl;     // contains %s
        public string HomeUrl;
        public string Host;
        public string Color = "#5C6BC0";

        public string Build(string query)
        {
            return QueryUrl.Replace("%s", Uri.EscapeDataString(query ?? ""));
        }

        public override string ToString() { return Name; }
    }

    /// <summary>The search engines offered on the settings page, plus address-bar heuristics.</summary>
    public static class Engines
    {
        public static readonly List<SearchEngineInfo> All = new List<SearchEngineInfo>
        {
            new SearchEngineInfo { Name = "Google",    QueryUrl = "https://www.google.com/search?q=%s",           HomeUrl = "https://www.google.com/",     Host = "google.com",    Color = "#4285F4" },
            new SearchEngineInfo { Name = "Яндекс",    QueryUrl = "https://yandex.ru/search/?text=%s",             HomeUrl = "https://yandex.ru/",          Host = "yandex.ru",     Color = "#FC3F1D" },
            new SearchEngineInfo { Name = "Bing",      QueryUrl = "https://www.bing.com/search?q=%s",             HomeUrl = "https://www.bing.com/",       Host = "bing.com",      Color = "#008373" },
            new SearchEngineInfo { Name = "DuckDuckGo",QueryUrl = "https://duckduckgo.com/?q=%s",                 HomeUrl = "https://duckduckgo.com/",     Host = "duckduckgo.com",Color = "#DE5833" },
            new SearchEngineInfo { Name = "Brave",     QueryUrl = "https://search.brave.com/search?q=%s",        HomeUrl = "https://search.brave.com/",   Host = "brave.com",     Color = "#FB542B" },
            new SearchEngineInfo { Name = "Wikipedia", QueryUrl = "https://ru.wikipedia.org/w/index.php?search=%s", HomeUrl = "https://ru.wikipedia.org/", Host = "wikipedia.org", Color = "#636466" },
            new SearchEngineInfo { Name = "YouTube",   QueryUrl = "https://www.youtube.com/results?search_query=%s", HomeUrl = "https://www.youtube.com/",  Host = "youtube.com",   Color = "#FF0000" },
            new SearchEngineInfo { Name = "Почта Mail.ru", QueryUrl = "https://go.mail.ru/search?q=%s",            HomeUrl = "https://go.mail.ru/",         Host = "go.mail.ru",    Color = "#2B2F35" },
        };

        public static SearchEngineInfo Default
        {
            get { return Find(Settings.SearchEngine) ?? All[0]; }
        }

        public static SearchEngineInfo Find(string name)
        {
            foreach (SearchEngineInfo e in All)
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        public static string Search(string query)
        {
            return Default.Build(query);
        }

        // ─── address bar ──────────────────────────────────────────
        static readonly string[] Schemes = { "http://", "https://", "file://", "ftp://", "about:", "mb:", "data:", "chrome://" };

        /// <summary>
        /// Decides what the user meant: a URL, a bare host name, or a search query.
        /// </summary>
        public static string Resolve(string input, out bool wasSearch)
        {
            wasSearch = false;
            string text = (input ?? "").Trim();
            if (text.Length == 0) return "";

            string lower = text.ToLowerInvariant();
            foreach (string scheme in Schemes)
                if (lower.StartsWith(scheme)) return Normalize(text);

            if (lower.StartsWith("localhost") || lower.StartsWith("127.0.0.1") || lower.StartsWith("[::1]"))
                return "http://" + text;

            // Windows path such as C:\folder or \\server\share
            if (text.Length > 2 && char.IsLetter(text[0]) && text[1] == ':' &&
                (text[2] == '\\' || text[2] == '/'))
                return "file:///" + text.Replace('\\', '/').TrimStart('/').Insert(0, "/");
            if (text.StartsWith(@"\\")) return "file://" + text.Replace('\\', '/');

            if (LooksLikeHost(text)) return Normalize("https://" + text);

            wasSearch = true;
            return Search(text);
        }

        public static bool LooksLikeHost(string text)
        {
            if (text.Length == 0 || text.Length > 253) return false;
            if (text.Contains(" ")) return false;
            if (text.EndsWith(".") || text.StartsWith(".") || text.Contains("..")) return false;

            int dot = text.IndexOf('.');
            if (dot <= 0 || dot == text.Length - 1) return false;

            string host = text.Substring(0, dot);
            if (host.StartsWith("-") || host.EndsWith("-")) return false;
            foreach (char c in host)
                if (!char.IsLetterOrDigit(c) && c != '-') return false;

            string rest = text.Substring(dot + 1);
            string tail = rest;
            int slash = rest.IndexOf('/');
            if (slash >= 0) tail = rest.Substring(0, slash);
            int colon = tail.IndexOf(':');
            if (colon >= 0)
            {
                string port = tail.Substring(colon + 1);
                if (port.Length == 0) return false;
                int portNumber;
                if (!int.TryParse(port, out portNumber) || portNumber < 1 || portNumber > 65535) return false;
                tail = tail.Substring(0, colon);
            }
            if (tail.Length < 2) return false;
            // A single label is only a host when it is a name we can reasonably dial.
            if (!tail.Contains(".")) return tail.IndexOfAny(new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' }) < 0;

            foreach (string part in tail.Split('.'))
            {
                if (part.Length == 0 || part.Length > 63) return false;
                foreach (char c in part)
                    if (!char.IsLetterOrDigit(c) && c != '-') return false;
            }
            string last = tail.Substring(tail.LastIndexOf('.') + 1);
            if (last.Length < 2) return false;
            foreach (char c in last) if (!char.IsLetter(c)) return false;
            return true;
        }

        static string Normalize(string url)
        {
            if (url.StartsWith("https://") || url.StartsWith("http://")) return url;
            if (url.StartsWith("file:///")) return url;
            if (url.StartsWith("file://")) return url;
            return url;
        }

        /// <summary>Shortens a URL for the status bar: strips the scheme and a trailing slash.</summary>
        public static string PrettyUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string text = url;
            if (text.StartsWith("https://")) text = text.Substring(8);
            else if (text.StartsWith("http://")) text = text.Substring(7);
            else if (text.StartsWith("file:///")) text = text.Substring(8);
            if (text.EndsWith("/") && text.Length > 1) text = text.Substring(0, text.Length - 1);
            return text;
        }
    }
}
