using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;

namespace MaterialBrowser
{
    /// <summary>
    /// Bounded in-memory image cache. Images are fetched on background threads before
    /// layout so the first paint already has the right sizes where possible.
    /// </summary>
    public static class Images
    {
        class Entry
        {
            public Bitmap Bitmap;
            public string Url;
            public int Width;
            public int Height;
            public bool Failed;
        }

        static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        static readonly object _lock = new object();
        static readonly Queue<string> _order = new Queue<string>();
        static readonly Dictionary<string, bool> _pending = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        const int MaxEntries = 160;

        public static event EventHandler Changed;

        public static int Loaded
        {
            get { lock (_lock) { return _cache.Count; } }
        }

        public static Bitmap Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            lock (_lock)
            {
                Entry entry;
                return _cache.TryGetValue(url, out entry) ? entry.Bitmap : null;
            }
        }

        public static bool IsKnown(string url)
        {
            lock (_lock) return _cache.ContainsKey(url);
        }

        public static void Clear()
        {
            lock (_lock)
            {
                foreach (Entry e in _cache.Values)
                    if (e.Bitmap != null) e.Bitmap.Dispose();
                _cache.Clear();
                _order.Clear();
            }
        }

        /// <summary>Queues a background download; the page is notified when something arrives.</summary>
        public static void Prefetch(string url, CookieContainer cookies)
        {
            if (!Settings.LoadImages) return;
            if (string.IsNullOrEmpty(url)) return;
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

            lock (_lock)
            {
                if (_cache.ContainsKey(url) || _pending.ContainsKey(url)) return;
                if (_order.Count >= MaxEntries) return;
                _pending[url] = true;
            }

            var thread = new Thread(delegate ()
            {
                try { Load(url, cookies); }
                catch { }
                finally
                {
                    lock (_lock) _pending.Remove(url);
                    EventHandler h = Changed;
                    if (h != null) h(null, EventArgs.Empty);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        public static void Load(string url, CookieContainer cookies)
        {
            byte[] bytes;
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                bytes = DecodeDataUrl(url);
            }
            else if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                string path = Http.ToLocalPath(url);
                if (!File.Exists(path)) { Remember(url, null); return; }
                var info = new FileInfo(path);
                if (info.Length > Http.MaxImageBytes) { Remember(url, null); return; }
                bytes = File.ReadAllBytes(path);
            }
            else
            {
                Http.Prepare();
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(url);
                    request.UserAgent = Http.UserAgent;
                    request.Timeout = 12000;
                    request.ReadWriteTimeout = 12000;
                    request.AllowAutoRedirect = true;
                    request.MaximumAutomaticRedirections = 5;
                    request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    request.Referer = Referer;
                    if (cookies != null) request.CookieContainer = cookies;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    {
                        if (stream == null) { Remember(url, null); return; }
                        long read;
                        bytes = Http.ReadLimited(stream, Http.MaxImageBytes, out read);
                    }
                }
                catch { Remember(url, null); return; }
            }

            if (bytes == null || bytes.Length == 0) { Remember(url, null); return; }

            Bitmap bitmap = null;
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                    bitmap = new Bitmap(ms);
            }
            catch { }

            Remember(url, bitmap);
        }

        public static string Referer = "";

        static void Remember(string url, Bitmap bitmap)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(url)) return;
                var entry = new Entry();
                entry.Url = url;
                entry.Bitmap = bitmap;
                if (bitmap != null) { entry.Width = bitmap.Width; entry.Height = bitmap.Height; }
                else entry.Failed = true;
                _cache[url] = entry;
                _order.Enqueue(url);
                while (_order.Count > MaxEntries)
                {
                    string oldest = _order.Dequeue();
                    Entry old;
                    if (_cache.TryGetValue(oldest, out old))
                    {
                        if (old.Bitmap != null) old.Bitmap.Dispose();
                        _cache.Remove(oldest);
                    }
                }
            }
        }

        static byte[] DecodeDataUrl(string url)
        {
            int comma = url.IndexOf(',');
            if (comma < 0) return null;
            string meta = url.Substring(5, comma - 5);
            string payload = url.Substring(comma + 1);
            try
            {
                if (meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                    return Convert.FromBase64String(payload);
                return System.Text.Encoding.GetEncoding(1251).GetBytes(Uri.UnescapeDataString(payload));
            }
            catch { return null; }
        }
    }

    /// <summary>Cached font construction so layout does not leak GDI handles.</summary>
    public static class Fonts
    {
        static readonly Dictionary<string, Font> _cache = new Dictionary<string, Font>();

        public static Font Get(string family, float sizePx, bool bold, bool italic)
        {
            sizePx = Math.Max(6f, Math.Min(96f, sizePx));
            string familyName = Normalize(family);
            string key = familyName + "|" + sizePx.ToString("0") + "|" + (bold ? "b" : "") + (italic ? "i" : "");
            lock (_cache)
            {
                Font font;
                if (_cache.TryGetValue(key, out font)) return font;
                FontStyle style = FontStyle.Regular;
                if (bold && italic) style = FontStyle.Bold | FontStyle.Italic;
                else if (bold) style = FontStyle.Bold;
                else if (italic) style = FontStyle.Italic;
                try { font = new Font(familyName, sizePx, style, GraphicsUnit.Pixel); }
                catch { font = new Font("Segoe UI", sizePx, style, GraphicsUnit.Pixel); }
                _cache[key] = font;
                return font;
            }
        }

        static string Normalize(string family)
        {
            if (string.IsNullOrEmpty(family)) return "Segoe UI";
            string first = family.Split(',')[0].Trim().Trim('"', '\'');
            switch (first.ToLowerInvariant())
            {
                case "sans-serif": case "arial": case "helvetica": case "system-ui": case "verdana": case "tahoma":
                    return "Segoe UI";
                case "serif": case "times": case "times new roman": case "georgia":
                    return "Times New Roman";
                case "monospace": case "courier": case "courier new": case "consolas":
                    return "Consolas";
                case "cursive": case "fantasy":
                    return "Segoe UI";
                default:
                    return first;
            }
        }
    }
}
