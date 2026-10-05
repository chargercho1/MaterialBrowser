using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MaterialBrowser
{
    /// <summary>
    /// The Firefox engine downloads by itself, straight into the Downloads
    /// folder, so the downloads page would otherwise stay empty. This watches
    /// that folder while the engine runs and turns anything that appears there
    /// into an entry of its own.
    /// </summary>
    public static class GeckoDownloads
    {
        static readonly List<string> Known = new List<string>();
        static readonly Dictionary<string, DownloadItem> Tracked = new Dictionary<string, DownloadItem>(StringComparer.OrdinalIgnoreCase);
        static Thread _worker;
        static volatile bool _running;
        static string _folder = "";

        /// <summary>Temporary files the engine writes while a download is still running.</summary>
        static readonly string[] Noise =
        {
            ".part", ".crdownload", ".tmp", ".download", ".opdownload",
        };

        /// <summary>Starts watching. Safe to call more than once.</summary>
        public static void Start()
        {
            lock (Known)
            {
                if (_running) return;
                _running = true;
                _folder = SettingsFile.DownloadFolder;
            }

            try { if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder); }
            catch { }

            // Whatever is already there was downloaded by something else, so only
            // files that show up from now on belong to the browser.
            try
            {
                foreach (string path in Directory.GetFiles(_folder)) Known.Add(path);
            }
            catch { }

            Debug.Log("downloads: watching " + _folder);
            _worker = new Thread(Loop) { IsBackground = true, Name = "gecko-downloads" };
            _worker.Start();
        }

        /// <summary>Stops watching and forgets the half finished transfers.</summary>
        public static void Stop()
        {
            lock (Known) _running = false;
            lock (Tracked) Tracked.Clear();
        }

        static void Loop()
        {
            while (IsRunning())
            {
                try { Scan(); } catch (Exception ex) { Debug.Log("downloads: scan failed: " + ex.Message); }
                Thread.Sleep(1200);
            }
        }

        static bool IsRunning()
        {
            lock (Known) return _running;
        }

        /// <summary>One pass over the folder: new files in, finished files settled.</summary>
        static void Scan()
        {
            if (!IsRunning()) return;
            string folder = _folder;
            if (folder.Length == 0 || !Directory.Exists(folder)) return;

            foreach (string path in Directory.GetFiles(folder))
            {
                string name = Path.GetFileName(path);
                if (IsTemporary(name)) continue;

                bool tracked;
                lock (Tracked) tracked = Tracked.ContainsKey(path);
                if (tracked)
                {
                    Settle(path);
                    continue;
                }

                bool seen;
                lock (Known) seen = Known.Contains(path);
                if (seen) continue;

                // A file that is still empty is a transfer in progress. Leave it
                // unseen so the next pass picks it up once it has content.
                if (Register(path))
                {
                    lock (Known) Known.Add(path);
                }
            }
        }

        static bool IsTemporary(string name)
        {
            foreach (string tail in Noise)
                if (name.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>A finished file becomes a download entry.</summary>
        static bool Register(string path)
        {
            long size;
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) return false;
                size = file.Length;
                if (size <= 0) return false;      // still being written
            }
            catch { return false; }

            DownloadItem item;
            try { item = Downloads.Add("", Path.GetFileName(path)); }
            catch (Exception ex) { Debug.Log("downloads: add failed for " + path + ": " + ex.Message); return false; }

            item.Url = "file://" + path.Replace("\\", "/");
            item.Path = path;
            item.FileName = Path.GetFileName(path);
            item.Bytes = size;
            item.Total = size;
            item.Started = File.GetCreationTime(path);
            item.Status = "завершено";
            item.Done = true;
            item.Failed = false;
            Downloads.Update(item, true);

            lock (Tracked) Tracked[path] = item;
            Debug.Log("downloads: registered " + Path.GetFileName(path) + " " + size + " bytes");
            return true;
        }

        /// <summary>Keeps the byte count current while a transfer is still growing.</summary>
        static void Settle(string path)
        {
            DownloadItem item;
            lock (Tracked) if (!Tracked.TryGetValue(path, out item)) return;

            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) return;
                long size = file.Length;
                if (size == item.Bytes) return;

                item.Bytes = size;
                item.Total = size;
                item.Status = "загрузка";
                item.Done = false;
                Downloads.Update(item, false);
            }
            catch { }
        }
    }
}