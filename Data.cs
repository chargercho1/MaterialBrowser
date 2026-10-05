using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MaterialBrowser
{
    /// <summary>Where the browser keeps its data.</summary>
    public static class SettingsFile
    {
        public static string Folder
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
                return Path.Combine(root, "MaterialBrowser");
            }
        }

        public static string BookmarksFile { get { return Path.Combine(Folder, "bookmarks.txt"); } }
        public static string HistoryFile { get { return Path.Combine(Folder, "history.txt"); } }
        public static string DownloadsFile { get { return Path.Combine(Folder, "downloads.txt"); } }
        public static string SessionFile { get { return Path.Combine(Folder, "session.txt"); } }
        public static string CacheFolder { get { return Path.Combine(Folder, "cache"); } }
        public static string DownloadFolder
        {
            get
            {
                string downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string path = Path.Combine(downloads, "Downloads");
                try { if (!Directory.Exists(path)) Directory.CreateDirectory(path); } catch { }
                return path;
            }
        }

        public const int MaxBookmarks = 500;
        public const int MaxHistory = 2000;
        public static int MaxPages = 24;

        public static void Ensure()
        {
            try
            {
                if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
                if (!Directory.Exists(CacheFolder)) Directory.CreateDirectory(CacheFolder);
            }
            catch { }
        }

        /// <summary>Fields are tab separated; tabs and newlines inside values are escaped.</summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        public static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    char c = value[++i];
                    if (c == 't') sb.Append('\t');
                    else if (c == 'r') sb.Append('\r');
                    else if (c == 'n') sb.Append('\n');
                    else if (c == '\\') sb.Append('\\');
                    else sb.Append(c);
                }
                else sb.Append(value[i]);
            }
            return sb.ToString();
        }

        public static List<string[]> ReadRows(string path)
        {
            var rows = new List<string[]>();
            try
            {
                if (!File.Exists(path)) return rows;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Length == 0) continue;
                    string[] parts = line.Split('\t');
                    for (int i = 0; i < parts.Length; i++) parts[i] = Unescape(parts[i]);
                    rows.Add(parts);
                }
            }
            catch { }
            return rows;
        }

        public static void WriteRows(string path, IEnumerable<string[]> rows)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (string[] row in rows)
                {
                    for (int i = 0; i < row.Length; i++)
                    {
                        if (i > 0) sb.Append('\t');
                        sb.Append(Escape(row[i]));
                    }
                    sb.Append('\n');
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    public class Bookmark
    {
        public string Url = "";
        public string Title = "";
        public DateTime Added = DateTime.Now;

        public string Display
        {
            get { return string.IsNullOrEmpty(Title) ? WebText.TabTitle(Url) : Title; }
        }
    }

    public static class Bookmarks
    {
        static readonly List<Bookmark> _items = new List<Bookmark>();
        public static event EventHandler Changed;

        public static IList<Bookmark> Items { get { return _items; } }

        public static void Load()
        {
            _items.Clear();
            foreach (string[] row in SettingsFile.ReadRows(SettingsFile.BookmarksFile))
            {
                if (row.Length < 2) continue;
                var b = new Bookmark();
                b.Url = row[0];
                b.Title = row[1];
                if (row.Length > 2)
                {
                    long ticks;
                    if (long.TryParse(row[2], out ticks) && ticks > 0) b.Added = new DateTime(ticks);
                }
                if (b.Url.Length > 0) _items.Add(b);
            }
            Raise();
        }

        public static void Save()
        {
            var rows = new List<string[]>();
            for (int i = 0; i < _items.Count; i++)
            {
                Bookmark b = _items[i];
                rows.Add(new[] { b.Url, b.Title, b.Added.Ticks.ToString() });
            }
            SettingsFile.WriteRows(SettingsFile.BookmarksFile, rows);
        }

        public static bool Has(string url)
        {
            foreach (Bookmark b in _items) if (string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static void Add(string url, string title)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (url.StartsWith("about:") || url.StartsWith("mb:")) return;
            foreach (Bookmark b in _items)
                if (string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase)) { b.Title = title ?? b.Title; Save(); Raise(); return; }
            var added = new Bookmark();
            added.Url = url;
            added.Title = title ?? "";
            _items.Insert(0, added);
            while (_items.Count > SettingsFile.MaxBookmarks) _items.RemoveAt(_items.Count - 1);
            Save();
            Raise();
        }

        public static void Remove(string url)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (!string.Equals(_items[i].Url, url, StringComparison.OrdinalIgnoreCase)) continue;
                _items.RemoveAt(i);
                Save();
                Raise();
                return;
            }
        }

        public static void Toggle(string url, string title)
        {
            if (Has(url)) Remove(url); else Add(url, title);
        }

        public static void Clear()
        {
            _items.Clear();
            Save();
            Raise();
        }

        static void Raise()
        {
            EventHandler h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }
    }

    public class HistoryEntry
    {
        public string Url = "";
        public string Title = "";
        public DateTime Visited = DateTime.Now;

        public string Display
        {
            get { return string.IsNullOrEmpty(Title) ? WebText.TabTitle(Url) : Title; }
        }
    }

    public static class History
    {
        static readonly List<HistoryEntry> _items = new List<HistoryEntry>();
        public static event EventHandler Changed;

        /// <summary>Set for a private window: nothing is written to disk.</summary>
        public static bool VolatileMode;

        public static IList<HistoryEntry> Items { get { return _items; } }

        public static void Load()
        {
            if (VolatileMode) return;
            _items.Clear();
            foreach (string[] row in SettingsFile.ReadRows(SettingsFile.HistoryFile))
            {
                if (row.Length < 3) continue;
                var e = new HistoryEntry();
                e.Url = row[0];
                e.Title = row[1];
                long ticks;
                if (long.TryParse(row[2], out ticks) && ticks > 0) e.Visited = new DateTime(ticks);
                if (e.Url.Length > 0) _items.Add(e);
            }
        }

        public static void Visit(string url, string title)
        {
            if (string.IsNullOrEmpty(url) || url.StartsWith("about:") || url.StartsWith("mb:")) return;
            _items.Insert(0, new HistoryEntry { Url = url, Title = title ?? "", Visited = DateTime.Now });
            while (_items.Count > SettingsFile.MaxHistory) _items.RemoveAt(_items.Count - 1);
            if (VolatileMode)
            {
                EventHandler h = Changed;
                if (h != null) h(null, EventArgs.Empty);
                return;
            }
            Save();
            EventHandler c = Changed;
            if (c != null) c(null, EventArgs.Empty);
        }

        public static void Save()
        {
            if (VolatileMode) return;
            var rows = new List<string[]>();
            for (int i = 0; i < _items.Count; i++)
            {
                HistoryEntry e = _items[i];
                rows.Add(new[] { e.Url, e.Title, e.Visited.Ticks.ToString() });
            }
            SettingsFile.WriteRows(SettingsFile.HistoryFile, rows);
        }

        public static void Clear()
        {
            _items.Clear();
            Save();
            EventHandler h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }

        public static void Remove(string url)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
                if (string.Equals(_items[i].Url, url, StringComparison.OrdinalIgnoreCase)) _items.RemoveAt(i);
            Save();
            EventHandler h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }
    }

    public class DownloadItem
    {
        public string Url = "";
        public string FileName = "";
        public string Path = "";
        public long Bytes;
        public long Total;
        public DateTime Started = DateTime.Now;
        public string Status = "в очереди";
        public bool Done;
        public bool Failed;
        public volatile bool Cancel;

        public int Percent
        {
            get { return Total > 0 ? (int)Math.Min(100, Bytes * 100 / Total) : (Done ? 100 : 0); }
        }

        public string SizeText
        {
            get
            {
                string have = Format.Bytes(Bytes);
                return Total > 0 ? have + " из " + Format.Bytes(Total) : have;
            }
        }
    }

    public static class Downloads
    {
        static readonly List<DownloadItem> _items = new List<DownloadItem>();
        public static event EventHandler Changed;

        public static IList<DownloadItem> Items { get { return _items; } }

        public static DownloadItem Add(string url, string fileName)
        {
            var item = new DownloadItem();
            item.Url = url;
            item.FileName = Sanitize(fileName);
            item.Path = Path.Combine(SettingsFile.DownloadFolder, item.FileName);
            item.Path = Unique(item.Path);
            item.FileName = Path.GetFileName(item.Path);
            _items.Insert(0, item);
            Save();
            Raise();
            return item;
        }

        public static void Update(DownloadItem item, bool save)
        {
            if (save) Save();
            Raise();
        }

        public static void Raise()
        {
            EventHandler h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }

        public static void Remove(DownloadItem item)
        {
            _items.Remove(item);
            Save();
            Raise();
        }

        public static void Clear()
        {
            _items.Clear();
            Save();
            Raise();
        }

        public static void Load()
        {
            foreach (string[] row in SettingsFile.ReadRows(SettingsFile.DownloadsFile))
            {
                if (row.Length < 5) continue;
                var item = new DownloadItem();
                item.Url = row[0];
                item.FileName = row[1];
                item.Path = row[2];
                long value;
                if (long.TryParse(row[3], out value)) item.Bytes = value;
                if (long.TryParse(row[4], out value)) item.Total = value;
                item.Status = row.Length > 5 ? row[5] : "завершено";
                item.Done = item.Status == "завершено";
                item.Failed = item.Status == "ошибка";
                long ticks;
                if (row.Length > 6 && long.TryParse(row[6], out ticks) && ticks > 0) item.Started = new DateTime(ticks);
                _items.Add(item);
            }
        }

        public static void Save()
        {
            var rows = new List<string[]>();
            for (int i = 0; i < _items.Count; i++)
            {
                DownloadItem d = _items[i];
                rows.Add(new[] { d.Url, d.FileName, d.Path, d.Bytes.ToString(), d.Total.ToString(), d.Status, d.Started.Ticks.ToString() });
            }
            SettingsFile.WriteRows(SettingsFile.DownloadsFile, rows);
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "download";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim();
            if (name.Length == 0) name = "download";
            if (name.Length > 120) name = name.Substring(name.Length - 120);
            return name;
        }

        public static string Unique(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 999; i++)
            {
                string candidate = Path.Combine(dir, stem + " (" + i + ")" + ext);
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, stem + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ext);
        }
    }

    /// <summary>Byte formatting shared by the status bar and the downloads page.</summary>
    public static class Format
    {
        static readonly string[] Units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };

        public static string Bytes(double bytes)
        {
            if (bytes < 0) return "";
            if (bytes < 1024) return ((long)bytes).ToString() + " Б";
            int i = 0;
            while (bytes >= 1024 && i < Units.Length - 1) { bytes /= 1024; i++; }
            string s = bytes >= 100 ? bytes.ToString("0") : bytes >= 10 ? bytes.ToString("0.0") : bytes.ToString("0.00");
            return s.Replace('.', ',') + " " + Units[i];
        }

        public static string Time(DateTime when)
        {
            if (when == DateTime.MinValue) return "";
            DateTime today = DateTime.Today;
            if (when.Date == today) return when.ToString("HH:mm");
            if (when.Date == today.AddDays(-1)) return "вчера, " + when.ToString("HH:mm");
            return when.ToString("dd.MM.yyyy HH:mm");
        }
    }
}
