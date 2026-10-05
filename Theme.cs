using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Win32;

namespace MaterialBrowser
{
    /// <summary>Opt-in trace log, enabled with the MBR_TRACE environment variable.</summary>
    public static class Debug
    {
        static string _path;
        static bool _resolved;

        public static bool Enabled
        {
            get
            {
                if (!_resolved)
                {
                    _resolved = true;
                    string env = Environment.GetEnvironmentVariable("MBR_TRACE");
                    if (!string.IsNullOrEmpty(env)) _path = env;
                }
                return _path != null;
            }
        }

        public static void Log(string message)
        {
            try
            {
                if (!_resolved)
                {
                    _resolved = true;
                    string env = Environment.GetEnvironmentVariable("MBR_TRACE");
                    if (string.IsNullOrEmpty(env)) return;
                    _path = env;
                }
                if (_path == null) return;
                System.IO.File.AppendAllText(_path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\r\n");
            }
            catch { }
        }
    }

    /// <summary>Material You source color and the generated scheme.</summary>
    public static class AppTheme
    {
        public const string RegPath = @"SOFTWARE\MaterialBrowser";

        public static Color Source;
        public static bool FollowSystem = true;
        public static Color CustomSource = Color.FromArgb(255, 103, 80, 164);
        public static int Contrast;

        // Optional override used by the self-test to render both schemes
        public static bool? DarkOverride;

        public static Scheme Current;
        public static event EventHandler Changed;

        public static void Load()
        {
            FollowSystem = Reg.GetDword(RegPath, "FollowSystem", 1) == 1;
            Contrast = Reg.GetDword(RegPath, "Contrast", 0);
            int abgr = Reg.GetDword(RegPath, "SourceColor", 0);
            if (abgr != 0)
                CustomSource = Color.FromArgb(255, (abgr >> 16) & 0xFF, (abgr >> 8) & 0xFF, abgr & 0xFF);
            else
                CustomSource = WinOps.AccentFromRegistry();

            Source = FollowSystem ? WinOps.AccentFromRegistry() : CustomSource;
            bool dark = DarkOverride.HasValue ? DarkOverride.Value : WinOps.SystemInDarkMode();
            Current = Scheme.FromSource(Source, dark, Contrast);
        }

        public static void Build()
        {
            Current = Scheme.FromSource(Source, DarkOverride ?? WinOps.SystemInDarkMode(), Contrast);
        }

        public static void SetSource(Color color, bool follow)
        {
            FollowSystem = follow;
            CustomSource = color;
            Source = follow ? WinOps.AccentFromRegistry() : color;
            Reg.SetDword(RegPath, "FollowSystem", follow ? 1 : 0);
            Reg.SetDword(RegPath, "SourceColor",
                unchecked((int)(0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | (uint)color.B)));
            Build();
            Raise();
        }

        public static void SetDark(bool dark)
        {
            DarkOverride = dark;
            Build();
            Raise();
        }

        public static void RefreshFromSystem()
        {
            Source = FollowSystem ? WinOps.AccentFromRegistry() : CustomSource;
            Build();
            Raise();
        }

        public static void Raise()
        {
            EventHandler h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }
    }

    /// <summary>User-visible browser settings; everything here is edited on the settings page.</summary>
    public static class Settings
    {
        public static string SearchEngine = "Google";
        public static string HomePage = "https://ru.wikipedia.org/wiki/Заглавная_страница";
        public static int ThemeMode = -1;              // -1 follow system, 0 light, 1 dark
        public static int EngineMode = DefaultEngine(); // 0 built-in, 1 system (WebBrowser), 2 Firefox (Gecko)
        public static int FontScale = 100;             // percent
        public static bool ShowBookmarksBar = true;
        public static bool ShowStatusBar = true;
        public static bool LoadImages = true;
        public static bool ShowImagesBroken = true;
        public static bool BlockScripts = true;        // the built-in engine never runs JS
        public static int MaxImages = 60;
        public static bool RestoreSession = true;
        public static string UserAgent = "";

        public static readonly string[] UserAgents =
        {
            "",
            "Material Browser/1.0 (Windows NT 10.0; Win64; x64)",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0"
        };

        public static readonly string[] UserAgentNames =
        {
            "По умолчанию",
            "Material Browser",
            "Chrome 124 (Windows)",
            "Firefox 126 (Windows)"
        };

        /// <summary>
        /// The program ships its own Gecko runtime, so Firefox is the engine a new
        /// installation starts on. Without a runtime there is nothing to fall back
        /// to but the built-in reader.
        /// </summary>
        public static int DefaultEngine()
        {
            try
            {
                List<GeckoInstall> found = GeckoEngine.Installed();
                return found != null && found.Count > 0 ? 2 : 0;
            }
            catch { return 0; }
        }

        public static void Load()
        {
            SearchEngine = Reg.GetString(RegPath(), "SearchEngine", "Google");
            HomePage = Reg.GetString(RegPath(), "HomePage", HomePage);
            ThemeMode = Reg.GetDword(RegPath(), "ThemeMode", -1);
            EngineMode = Reg.GetDword(RegPath(), "EngineMode", DefaultEngine());
            FontScale = Reg.GetDword(RegPath(), "FontScale", 100);
            ShowBookmarksBar = Reg.GetDword(RegPath(), "ShowBookmarksBar", 1) == 1;
            ShowStatusBar = Reg.GetDword(RegPath(), "ShowStatusBar", 1) == 1;
            LoadImages = Reg.GetDword(RegPath(), "LoadImages", 1) == 1;
            ShowImagesBroken = Reg.GetDword(RegPath(), "ShowImagesBroken", 1) == 1;
            MaxImages = Reg.GetDword(RegPath(), "MaxImages", 60);
            RestoreSession = Reg.GetDword(RegPath(), "RestoreSession", 1) == 1;
            UserAgent = Reg.GetString(RegPath(), "UserAgent", "");

            if (SettingsFile.MaxPages < 4) SettingsFile.MaxPages = 4;
        }

        static string RegPath() { return AppTheme.RegPath; }

        public static void Save()
        {
            Reg.SetString(RegPath(), "SearchEngine", SearchEngine);
            Reg.SetString(RegPath(), "HomePage", HomePage);
            Reg.SetDword(RegPath(), "ThemeMode", ThemeMode);
            Reg.SetDword(RegPath(), "EngineMode", EngineMode);
            Reg.SetDword(RegPath(), "FontScale", FontScale);
            Reg.SetDword(RegPath(), "ShowBookmarksBar", ShowBookmarksBar ? 1 : 0);
            Reg.SetDword(RegPath(), "ShowStatusBar", ShowStatusBar ? 1 : 0);
            Reg.SetDword(RegPath(), "LoadImages", LoadImages ? 1 : 0);
            Reg.SetDword(RegPath(), "ShowImagesBroken", ShowImagesBroken ? 1 : 0);
            Reg.SetDword(RegPath(), "MaxImages", MaxImages);
            Reg.SetDword(RegPath(), "RestoreSession", RestoreSession ? 1 : 0);
            Reg.SetString(RegPath(), "UserAgent", UserAgent);
        }

        /// <summary>Effective page font scale, clamped to something readable.</summary>
        public static float FontFactor
        {
            get { return Math.Max(0.6f, Math.Min(2.0f, FontScale / 100f)); }
        }
    }

    /// <summary>Small helpers for turning URLs into tab titles and back.</summary>
    public static class WebText
    {
        public static string TabTitle(string url)
        {
            if (string.IsNullOrEmpty(url)) return "Новая вкладка";
            if (url.StartsWith("about:")) return url.Substring(6);
            if (url.StartsWith("mb:")) return url.Substring(3);
            try
            {
                Uri uri = new Uri(url);
                string host = uri.Host;
                if (host.StartsWith("www.")) host = host.Substring(4);
                string path = uri.AbsolutePath;
                if (path.Length > 1)
                {
                    string last = path.Substring(path.LastIndexOf('/') + 1);
                    if (last.Length > 0) return last;
                }
                return host;
            }
            catch { return url; }
        }

        public static string Host(string url)
        {
            try { return new Uri(url).Host; }
            catch { return ""; }
        }

        public static string Elide(string text, Font font, int width, Graphics g)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (g == null) return text.Length > 48 ? text.Substring(0, 45) + "…" : text;
            if (g.MeasureString(text, font).Width <= width) return text;
            while (text.Length > 1)
            {
                text = text.Substring(0, text.Length - 1);
                if (g.MeasureString(text + "…", font).Width <= width) break;
            }
            return text + "…";
        }
    }

    public static class WinOps
    {
        public static Color AccentFromRegistry()
        {
            byte[] b = Reg.GetBinary(Reg.Explorer + "\\Accent", "AccentColor");
            if (b != null && b.Length >= 4)
            {
                int abgr = b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24);
                int r = (abgr >> 16) & 0xFF, g = (abgr >> 8) & 0xFF, bl = abgr & 0xFF;
                if (r + g + bl > 0) return Color.FromArgb(255, r, g, bl);
            }
            int cc = Reg.GetDwordMachine(Reg.Dwm, "ColorizationColor", unchecked((int)0xFF707070));
            return Color.FromArgb(255, (cc >> 16) & 0xFF, (cc >> 8) & 0xFF, cc & 0xFF);
        }

        public static bool SystemInDarkMode()
        {
            try
            {
                object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return value != null && Convert.ToInt32(value) == 0;
            }
            catch { return false; }
        }
    }
}
