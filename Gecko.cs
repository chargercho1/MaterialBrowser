using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MaterialBrowser
{
    /// <summary>An installed Gecko browser that we know how to drive.</summary>
    public class GeckoInstall
    {
        public string Path = "";
        public string Name = "";
        public string Version = "";
        public string Product = "";

        public override string ToString()
        {
            return Version.Length > 0 ? Name + " " + Version : Name;
        }
    }

    /// <summary>
    /// A shortcut the app owns, seen by the engine while the browser window held
    /// the keyboard. The modifiers travel with it: by then the keys live in the
    /// browser's input queue, so the app cannot read them for itself.
    /// </summary>
    public sealed class GeckoShortcut
    {
        public readonly Keys Key;
        public readonly bool Ctrl, Alt, Shift;

        public GeckoShortcut(Keys key, bool ctrl, bool alt, bool shift)
        {
            Key = key; Ctrl = ctrl; Alt = alt; Shift = shift;
        }

        public override string ToString()
        {
            string text = Key.ToString();
            if (Ctrl) text = "Ctrl+" + text;
            if (Alt) text = "Alt+" + text;
            if (Shift) text = "Shift+" + text;
            return text;
        }
    }

    /// <summary>
    /// The Firefox engine. One shared Gecko process serves every tab: it runs in a
    /// throw-away profile, is started with a remote debugging port, and its single
    /// native window is re-parented into whichever panel is currently on screen.
    /// Navigation, title, URL and load state come from WebDriver BiDi over a
    /// WebSocket, so no browser library is needed.
    /// </summary>
    public sealed class GeckoEngine : IDisposable
    {
        const string WindowClass = "MozillaWindowClass";

        static GeckoEngine _shared;
        static readonly object SharedGate = new object();

        GeckoClient _client;
        Process _launcher;
        IntPtr _window = IntPtr.Zero;
        Control _host;
        string _profileDir = "";
        string _context = "";
        string _url = "";
        string _title = "";
        string _exeName = "";
        string _browserName = "";
        string _browserVersion = "";
        string _lastError = "";
        int _port;
        bool _running;
        bool _loading;
        bool _clipped;
        int _windowWidth, _windowHeight;
        readonly HashSet<IntPtr> _knownWindows = new HashSet<IntPtr>();
        readonly HashSet<IntPtr> _ourProcesses = new HashSet<IntPtr>();

        System.Windows.Forms.Timer _pump;

        // ── shared instance ──────────────────────────────────────
        public static GeckoEngine Shared
        {
            get
            {
                lock (SharedGate)
                {
                    if (_shared == null) _shared = new GeckoEngine();
                    return _shared;
                }
            }
        }

        public static void ReleaseShared()
        {
            lock (SharedGate)
            {
                if (_shared == null) return;
                _shared.Dispose();
                _shared = null;
            }
        }

        // ── discovery ────────────────────────────────────────────
        /// <summary>
        /// Every Gecko build on the machine, Firefox first. Nothing here is
        /// specific to a single distribution: any browser that speaks WebDriver
        /// BiDi works, and a copy shipped next to the exe wins over a system one.
        /// </summary>
        public static List<GeckoInstall> Installed()
        {
            var found = new List<GeckoInstall>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string, string> take = delegate (string exe, string product)
            {
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;
                if (!seen.Add(Path.GetFullPath(exe))) return;
                found.Add(Read(exe, product));
            };

            // 1. the runtime shipped with the app. This is the shipped contract,
            //    so it outranks the environment and the registry.
            take(AppDirectoryGecko(), "Gecko");

            // 2. an explicit override for a different runtime
            take(Environment.GetEnvironmentVariable("MBR_GECKO"), "Gecko");

            // 2. the registered handler for the http protocol, if it is Gecko
            string handler = RegistryAppPath("firefox.exe");
            take(handler, "Firefox");

            // 3. the usual install locations
            var order = new string[,]
            {
                { "firefox",   "Mozilla Firefox",     "Firefox"   },
                { "firefox",   "Firefox",             "Firefox"   },
                { "firefox-esr", "Mozilla Firefox ESR", "Firefox" },
                { "waterfox",  "Waterfox",            "Waterfox"  },
                { "librewolf", "LibreWolf",           "LibreWolf" },
                { "palemoon",  "Pale Moon",           "Pale Moon" },
                { "floorp",    "Floorp",              "Floorp"    },
                { "seamonkey", "SeaMonkey",           "SeaMonkey" },
                { "icecat",    "IceCat",              "IceCat"    },
                { "basilisk",  "Basilisk",            "Basilisk"  },
                { "netfox",    "Netfox",              "Netfox"    },
                { "zen",       "Zen Browser",         "Zen"       },
                { "zen",       "Zen Browser Nightly", "Zen"       },
            };
            for (int i = 0; i < order.GetLength(0); i++)
            {
                foreach (string root in Roots())
                {
                    string exe = Path.Combine(Path.Combine(root, order[i, 1]), order[i, 0] + ".exe");
                    take(exe, order[i, 2]);
                }
            }

            return found;
        }

        /// <summary>True when the engine is the copy shipped inside the app folder.</summary>
        static bool IsBundled(string path)
        {
            string own = AppDirectoryGecko();
            return own.Length > 0 && string.Equals(Path.GetFullPath(own), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Folders a runtime may live in, most specific first: the app folder
        /// itself, then a read-only install falling back to the user's data dir.
        /// </summary>
        static List<string> EngineFolders()
        {
            var folders = new List<string>();
            Action<string> add = delegate (string path)
            {
                if (!string.IsNullOrEmpty(path) && !folders.Contains(path)) folders.Add(path);
            };

            try
            {
                string here = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
                if (here.Length > 0)
                {
                    add(Path.Combine(here, "gecko"));
                    add(here);                       // a runtime dropped beside the exe
                }
            }
            catch { }

            try
            {
                string data = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaterialBrowser");
                add(Path.Combine(data, "gecko"));
            }
            catch { }

            return folders;
        }

        /// <summary>A Gecko runtime unpacked next to the exe: gecko\firefox.exe.</summary>
        static string AppDirectoryGecko()
        {
            try
            {
                foreach (string folder in EngineFolders())
                {
                    foreach (string name in new[] { "firefox.exe", "gecko.exe" })
                    {
                        string candidate = Path.Combine(folder, name);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch { }
            return "";
        }

        // ── the engine inside the exe ─────────────────────────────
        /// <summary>Manifest resource name holding the zipped runtime.</summary>
        public const string EngineResource = "MaterialBrowser.gecko.zip";

        /// <summary>True when this build carries a runtime inside the executable.</summary>
        public static bool HasEmbeddedEngine
        {
            get
            {
                try
                {
                    return Assembly.GetExecutingAssembly().GetManifestResourceInfo(EngineResource) != null;
                }
                catch { return false; }
            }
        }

        /// <summary>Size of the embedded runtime in bytes, 0 when there is none.</summary>
        public static long EmbeddedEngineSize
        {
            get
            {
                try
                {
                    Stream stream = Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream(EngineResource);
                    if (stream == null) return 0;
                    using (stream) return stream.Length;
                }
                catch { return 0; }
            }
        }

        static readonly object UnpackLock = new object();
        static bool _unpackTried;

        /// <summary>
        /// Returns the runtime shipped with the program, unpacking the copy built
        /// into the executable the first time it is needed. Returns "" when there
        /// is no gecko folder and nothing to unpack.
        /// </summary>
        public static string EnsureEngine()
        {
            string present = AppDirectoryGecko();
            if (present.Length > 0) return present;
            lock (UnpackLock)
            {
                if (_unpackTried) return AppDirectoryGecko();
                _unpackTried = true;
                Unpack();
            }
            return AppDirectoryGecko();
        }

        static void Unpack()
        {
            if (!HasEmbeddedEngine)
            {
                Debug.Log("gecko: no engine folder and no embedded runtime");
                return;
            }

            List<string> targets = EngineFolders();
            if (targets.Count == 0) return;
            string folder = targets[0];

            foreach (string target in targets)
            {
                try
                {
                    if (!Directory.Exists(target)) Directory.CreateDirectory(target);
                    using (var probe = new FileStream(
                        Path.Combine(target, ".mb-write-test"), FileMode.Create, FileAccess.Write, FileShare.None))
                    { probe.WriteByte(0); }
                    File.Delete(Path.Combine(target, ".mb-write-test"));
                    folder = target;
                    break;
                }
                catch (Exception ex)
                {
                    Debug.Log("gecko: cannot write to " + target + ": " + ex.Message);
                }
            }

            string exe = Path.Combine(folder, "firefox.exe");
            string marker = Path.Combine(folder, ".materialbrowser-engine");
            if (File.Exists(marker) && File.Exists(exe)) return;   // already unpacked

            // Unpack beside the target and swap at the end, so an interrupted
            // run never leaves a half-written runtime behind.
            string staging = folder.TrimEnd('\\', '/') + ".unpack";
            try
            {
                DeleteTree(staging);
                Directory.CreateDirectory(staging);

                Debug.Log("gecko: unpacking the engine from the exe into " + staging);
                Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(EngineResource);
                if (source == null) return;
                int written;
                using (source)
                using (var archive = new ZipArchive(source, ZipArchiveMode.Read))
                {
                    written = ExtractAll(archive, staging);
                }
                Debug.Log("gecko: unpacked " + written + " files");

                if (staging != folder)
                {
                    DeleteTree(folder);
                    Directory.Move(staging, folder);
                }
                File.WriteAllText(marker, "unpacked from " + EngineResource + "\n");
                Debug.Log("gecko: engine ready at " + exe);
            }
            catch (Exception ex)
            {
                Debug.Log("gecko: unpack failed: " + ex.Message);
                DeleteTree(staging);
            }
        }

        /// <summary>Writes every entry of the archive below <paramref name="root"/>.</summary>
        static int ExtractAll(ZipArchive archive, string root)
        {
            string fullRoot = Path.GetFullPath(root);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                fullRoot += Path.DirectorySeparatorChar;

            int count = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

                // Refuse any entry that would escape the target folder.
                string target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) continue;

                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (Stream input = entry.Open())
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 32 * 1024))
                {
                    byte[] buffer = new byte[64 * 1024];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
                }
                count++;
            }
            return count;
        }

        /// <summary>Writes the runtime built into the exe into <paramref name="folder"/>.</summary>
        public static int ExtractEmbeddedTo(string folder)
        {
            if (!HasEmbeddedEngine) return 0;
            try
            {
                Directory.CreateDirectory(folder);
                using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(EngineResource))
                using (var archive = new ZipArchive(source, ZipArchiveMode.Read))
                    return ExtractAll(archive, folder);
            }
            catch (Exception ex)
            {
                Debug.Log("gecko: test extract failed: " + ex.Message);
                return -1;
            }
        }

        /// <summary>The runtime next to the exe, "" when there is none yet.</summary>
        public static string BundledEngine() { return AppDirectoryGecko(); }

        /// <summary>Removes a directory tree, read-only files included.</summary>
        public static void DeleteTree(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                Directory.Delete(path, true);
            }
            catch (Exception ex) { Debug.Log("gecko: delete failed " + path + ": " + ex.Message); }
        }

        static List<string> Roots()
        {
            var roots = new List<string>();
            Action<string> add = delegate (string path)
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!roots.Contains(path)) roots.Add(path);
            };
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            add(Environment.GetEnvironmentVariable("LOCALAPPDATA"));
            return roots;
        }

        static string RegistryAppPath(string exe)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                {
                    if (key != null) return Convert.ToString(key.GetValue("")).Trim('"');
                }
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                {
                    if (key != null) return Convert.ToString(key.GetValue("")).Trim('"');
                }
            }
            catch { }
            return "";
        }

        static GeckoInstall Read(string exe, string product)
        {
            var info = new GeckoInstall();
            info.Path = exe;
            info.Product = product;
            info.Name = Path.GetFileNameWithoutExtension(exe);
            info.Version = "";
            try
            {
                string ini = Path.Combine(Path.GetDirectoryName(exe) ?? "", "application.ini");
                if (File.Exists(ini))
                {
                    foreach (string line in File.ReadAllLines(ini))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string value = line.Substring(eq + 1).Trim();
                        if (key == "version") info.Version = value;
                        else if (key == "name") info.Name = value;
                        else if (key == "product" || key == "vendor") { }
                    }
                }
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exe);
                if (info.Version.Length == 0) info.Version = fvi.ProductVersion ?? "";
            }
            catch { }
            if (info.Version.Length == 0) info.Version = "Gecko";
            return info;
        }

        // ── state ────────────────────────────────────────────────
        public bool Running { get { return _running && _window != IntPtr.Zero; } }
        public bool Available { get { return Running && _client != null && _client.Ready; } }
        public string Name { get { return _browserName.Length > 0 ? _browserName : Path.GetFileNameWithoutExtension(_exeName); } }
        public string Version { get { return _browserVersion.Length > 0 ? _browserVersion : ""; } }
        public string CurrentUrl { get { return _url; } }
        public string CurrentTitle { get { return _title; } }
        public string LastError { get { return _lastError; } }

        /// <summary>True when the browser refuses to shrink below our host panel.</summary>
        public bool Clipped { get { return _clipped; } }
        public string ClipNote
        {
            get { return _clipped ? "окно " + _windowWidth + "×" + _windowHeight + " больше панели" : ""; }
        }

        public event EventHandler<string> TitleChanged;
        public event EventHandler<string> UrlChanged;
        public event EventHandler<bool> LoadingChanged;
        public event EventHandler<string> NavigationRequested;

        // ── start / stop ─────────────────────────────────────────
        /// <summary>
        /// Launches the browser and attaches to its window. Returns false with
        /// LastError set when nothing usable is installed or the browser dies.
        /// </summary>
        public bool Start(int width, int height)
        {
            if (Running) return true;
            if (_pump != null) return _running;

            // A runtime shipped inside the exe is unpacked on the first launch.
            string shipped = EnsureEngine();
            if (shipped.Length > 0) Debug.Log("gecko: engine from the app folder " + shipped);

            List<GeckoInstall> installs = Installed();
            if (installs.Count == 0)
            {
                _lastError = "Firefox не найден";
                return false;
            }

            GeckoInstall install = installs[0];
            _lastError = "";
            bool bundled = IsBundled(install.Path);
            Debug.Log("gecko: launching " + install.Path + " (" + install + ") bundled=" + bundled);
            _pump = new System.Windows.Forms.Timer();
            _pump.Interval = 250;
            _pump.Tick += delegate { Pump(); };

            try
            {
                _port = FreePort();
                _profileDir = Path.Combine(Path.GetTempPath(), "MaterialBrowser-" + _port.ToString(CultureInfo.InvariantCulture));
                if (Directory.Exists(_profileDir)) Directory.Delete(_profileDir, true);
                Directory.CreateDirectory(_profileDir);
                WriteProfile(_profileDir);

                RememberWindows();

                var info = new ProcessStartInfo(install.Path);
                info.Arguments = "-profile \"" + _profileDir + "\" -no-remote -new-instance" +
                                 " --remote-debugging-port " + _port +
                                 " --width " + Math.Max(400, width) + " --height " + Math.Max(300, height) +
                                 " about:blank";
                info.UseShellExecute = false;
                info.WorkingDirectory = Path.GetDirectoryName(install.Path) ?? "";
                _exeName = Path.GetFileNameWithoutExtension(install.Path);
                _launcher = Process.Start(info);
                Debug.Log("gecko: launcher pid=" + _launcher.Id + " args=" + info.Arguments);
            }
            catch (Exception ex)
            {
                _lastError = "Не удалось запустить " + install + ": " + ex.Message;
                Debug.Log("gecko start: " + ex.Message);
                StopPump();
                CleanupProfile();
                return false;
            }

            _window = WaitForWindow(25000);
            if (_window == IntPtr.Zero)
            {
                _lastError = "Окно браузера не появилось";
                Debug.Log("gecko: window never appeared, exe=" + _exeName + " profile=" + _profileDir +
                          " launcherExited=" + (_launcher == null ? "?" : _launcher.HasExited.ToString()) +
                          " running=" + Process.GetProcessesByName(_exeName).Length);
                StopPump();
                CleanupProfile();
                return false;
            }
            _knownWindows.Add(_window);

            // The engine is ours alone, so its window must never appear on the
            // desktop as a separate browser: park it off-screen until the tab
            // panel is ready to take it.
            Native.ShowWindow(_window, Native.SW_HIDE);
            Native.SetWindowPos(_window, IntPtr.Zero, -32000, -32000, 1, 1,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Sleep(60);

            _client = new GeckoClient();
            _client.Event += OnBidiEvent;
            int attempt = 0;
            while (attempt < 40)
            {
                attempt++;
                try
                {
                    _client.Connect(_port, 4000);
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt % 8 == 0) Debug.Log("gecko bidi: attempt " + attempt + " failed: " + ex.Message);
                    Thread.Sleep(250);
                    if (attempt % 8 == 0 && HasExited())
                    {
                        _lastError = "Браузер завершился при запуске";
                        StopPump();
                        CleanupProfile();
                        return false;
                    }
                }
            }
            if (_client == null || !_client.Ready)
            {
                _lastError = "Не удалось открыть канал управления (WebDriver BiDi)";
                Debug.Log("gecko: no bidi session after " + attempt + " attempts");
                StopPump();
                CleanupProfile();
                return false;
            }

            _browserName = _client.BrowserName;
            _browserVersion = _client.BrowserVersion;
            _context = FirstContext();
            if (_context.Length == 0)
            {
                _lastError = "Браузер не открыл вкладку";
                StopPump();
                CleanupProfile();
                return false;
            }

            _running = true;
            _pump.Start();
            CloseOtherContexts();
            Debug.Log("gecko ready: " + Name + " " + Version + " port=" + _port + " context=" + _context);
            return true;
        }

        bool HasExited()
        {
            try
            {
                if (_launcher == null) return true;
                if (!_launcher.HasExited) return false;
            }
            catch { return true; }
            foreach (Process p in Process.GetProcessesByName(_exeName))
            {
                try { if (p.StartTime > DateTime.Now.AddMinutes(-2)) return false; }
                catch { }
            }
            return true;
        }

        void StopPump()
        {
            if (_pump != null) { _pump.Stop(); _pump.Dispose(); _pump = null; }
            _running = false;
        }

        void CleanupProfile()
        {
            if (_profileDir.Length == 0) return;
            try { if (Directory.Exists(_profileDir)) Directory.Delete(_profileDir, true); }
            catch { }
            _profileDir = "";
        }

        static int FreePort()
        {
            try
            {
                var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                probe.Start();
                int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                return port;
            }
            catch { return 45000 + new Random().Next(4000); }
        }

        // ── profile ──────────────────────────────────────────────
        static void WriteProfile(string dir)
        {
            File.WriteAllText(Path.Combine(dir, "user.js"), WritePrefs());

            // Hide the browser chrome: our own toolbar is the only chrome the user needs.
            string chromeDir = Path.Combine(dir, "chrome");
            Directory.CreateDirectory(chromeDir);
            File.WriteAllText(Path.Combine(chromeDir, "userChrome.css"), ChromeCss());
        }

        static string WritePrefs()
        {
            var prefs = new List<string>();
            Action<string, string> pref = delegate (string name, string value) { prefs.Add("user_pref(\"" + name + "\", " + value + ");"); };

            // --- a profile that behaves like a clean, private browser tab ---
            pref("browser.shell.checkDefaultBrowser", "false");
            pref("browser.startup.page", "0");
            pref("browser.startup.homepage", "\"about:blank\"");
            pref("browser.startup.firstrunSkipsHomepage", "true");
            pref("browser.aboutwelcome.enabled", "false");
            pref("browser.newtabpage.enabled", "false");
            pref("browser.newtab.url", "\"about:blank\"");
            pref("browser.newtabpage.activity-stream.feeds.topsites", "false");
            pref("browser.sessionstore.resume_from_crash", "false");
            pref("browser.sessionstore.privacy_level", "2");
            pref("datareporting.policy.dataSubmissionEnabled", "false");
            pref("toolkit.telemetry.enabled", "false");
            pref("toolkit.telemetry.unified", "false");
            // Required since Firefox 69, otherwise userChrome.css is ignored.
            pref("toolkit.legacyUserProfileCustomizations.stylesheets", "true");

            // --- one window, and no questions asked when it closes ---
            pref("browser.warnOnQuit", "false");
            pref("browser.tabs.warnOnClose", "false");
            pref("browser.tabs.warnOnCloseOtherTabs", "false");
            pref("browser.tabs.closeWindowWithLastTab", "false");
            pref("browser.tabs.loadInBackground", "false");
            pref("browser.tabs.drawInTitlebar", "false");
            pref("browser.tabs.inTitlebar", "0");
            pref("browser.uidensity", "1");

            // Pages may open windows; we adopt them instead of losing them.
            pref("browser.link.open_newwindow", "1");
            pref("browser.link.open_newwindow.restriction", "0");
            pref("dom.disable_open_during_load", "false");
            pref("dom.disable_beforeunload", "true");

            // Downloads go to the user's Downloads folder and never open a panel.
            pref("browser.download.useDownloadDir", "true");
            pref("browser.download.folderList", "3");
            pref("browser.download.manager.showWhenStarting", "false");
            pref("browser.download.alwaysOpenPanel", "false");
            pref("browser.download.start_downloads_in_tmp_dir", "false");

            // The remote agent that gives us WebDriver BiDi.
            pref("remote.active-protocols", "3");
            pref("browser.region.network.url", "\"\"");

            // The engine is our own copy: it must never phone home, try to
            // update itself, register a default browser or write shortcuts.
            pref("app.update.enabled", "false");
            pref("app.update.auto", "false");
            pref("app.update.service.enabled", "false");
            pref("app.update.checkInstallTime", "false");
            pref("app.update.background.scheduling.enabled", "false");
            pref("app.normandy.enabled", "false");
            pref("app.normandy.api_url", "\"\"");
            pref("browser.shell.integrationWindows", "false");
            pref("browser.startup.homepage_override.mstone", "\"ignore\"");
            pref("extensions.update.enabled", "false");
            pref("extensions.update.autoDisableDefault", "false");
            pref("extensions.systemAddon.update.enabled", "false");
            pref("browser.discovery.enabled", "false");
            pref("captivedetect.canonicalURL", "\"\"");
            pref("network.captive-portal-service.enabled", "false");
            pref("browser.safebrowsing.malware.enabled", "false");
            pref("browser.safebrowsing.phishing.enabled", "false");
            pref("browser.contentblocking.report.hide_vpn_banner", "true");
            return string.Join("\r\n", prefs.ToArray());
        }

        public static string ChromeCssForTests() { return ChromeCss(); }

        public static string PrefsForTests() { return WritePrefs(); }

        static string ChromeCss()
        {
            return
                "/* Hide the browser's own chrome: MaterialBrowser draws its toolbar. */\r\n" +
                "#navigator-toolbox, #titlebar, #titlebar-container, #TabsToolbar,\r\n" +
                "#nav-bar, #PersonalToolbar, #sidebar-box, #sidebar-header,\r\n" +
                "#back-button, #forward-button, .titlebar-buttonbox {\r\n" +
                "  display: none !important;\r\n" +
                "  height: 0 !important;\r\n" +
                "  min-height: 0 !important;\r\n" +
                "  max-height: 0 !important;\r\n" +
                "  margin: 0 !important;\r\n" +
                "  padding: 0 !important;\r\n" +
                "  border: 0 !important;\r\n" +
                "}\r\n" +
                "#appcontent, #browser, #tabbrowser-tabbox, #tabbrowser-tabpanels {\r\n" +
                "  margin: 0 !important;\r\n" +
                "  border: none !important;\r\n" +
                "  padding: 0 !important;\r\n" +
                "  top: 0 !important;\r\n" +
                "  left: 0 !important;\r\n" +
                "  right: 0 !important;\r\n" +
                "  bottom: 0 !important;\r\n" +
                "  height: 100% !important;\r\n" +
                "}\r\n" +
                "/* Firefox reserves room above the tabs; with no tabs there is nothing\r\n" +
                "   to reserve it for, and the gap shows as a dead strip. */\r\n" +
                ":root { --space-above-tabbar: 0px !important; }\r\n" +
                "#window { margin: 0 !important; padding: 0 !important; border: 0 !important; }\r\n" +
                "toolbox { display: none !important; }\r\n";
        }

        // ── window adoption ──────────────────────────────────────
        static void RememberWindows()
        {
            _snapshot.Clear();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (ClassOf(h) == WindowClass) _snapshot.Add(h);
                return true;
            });
        }

        static readonly HashSet<IntPtr> _snapshot = new HashSet<IntPtr>();

        static string ClassOf(IntPtr hwnd)
        {
            var text = new StringBuilder(256);
            Native.GetClassName(hwnd, text, 256);
            return text.ToString();
        }

        static string TitleOf(IntPtr hwnd)
        {
            int length = Native.GetWindowTextLength(hwnd);
            if (length <= 0) return "";
            var text = new StringBuilder(length + 2);
            Native.GetWindowText(hwnd, text, text.Capacity);
            return text.ToString();
        }

        static List<IntPtr> GeckoWindows()
        {
            var list = new List<IntPtr>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (ClassOf(h) == WindowClass) list.Add(h);
                return true;
            });
            return list;
        }

        static void EnumWindows(Native.EnumWindowsProc callback)
        {
            try { Native.EnumWindows(callback, IntPtr.Zero); }
            catch { }
        }

        IntPtr WaitForWindow(int timeoutMs)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            int reported = 0;
            while (DateTime.Now < deadline)
            {
                foreach (IntPtr h in GeckoWindows())
                {
                    if (_snapshot.Contains(h)) continue;
                    uint pid;
                    Native.GetWindowThreadProcessId(h, out pid);
                    if (pid == 0) continue;
                    Native.RECT rect;
                    if (!Native.TryGetWindowRect(h, out rect)) continue;
                    if (rect.Width < 200 || rect.Height < 150) continue;
                    if (reported == 0)
                    {
                        reported++;
                        Debug.Log("gecko: adopted window 0x" + h.ToInt64().ToString("X") + " pid=" + pid +
                                  " " + rect.Width + "x" + rect.Height);
                    }
                    _ourProcesses.Add(new IntPtr((long)pid));
                    return h;
                }
                Sleep(120);
            }
            return IntPtr.Zero;
        }

        // ── hosting ──────────────────────────────────────────────
        /// <summary>Moves the browser window into <paramref name="host"/> and fits it.</summary>
        public bool Attach(Control host)
        {
            if (!Running || host == null || !host.IsHandleCreated) return false;
            if (_host == host)
            {
                Fit();
                // The panel may have been detached and taken again while another
                // tab was on screen, so the shared state is re-asserted here too.
                ShareInputQueue();
                InstallMouseHook();
                InstallKeyboardHook();
                return true;
            }
            if (_host != null && !_host.IsDisposed) Native.SetParent(_window, IntPtr.Zero);

            Native.SetWindowLongValue(_window, Native.GWL_STYLE,
                new IntPtr(Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS | Native.WS_CLIPCHILDREN));
            // Drop the frame bits the browser window was born with, otherwise the
            // leftover caption and window edge keep eating a few pixels of page.
            Native.SetWindowLongValue(_window, Native.GWL_EXSTYLE, new IntPtr(0));
            Native.SetParent(_window, host.Handle);
            _host = host;
            Native.SetWindowPos(_window, IntPtr.Zero, 0, 0, host.ClientSize.Width, host.ClientSize.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);
            Sleep(120);
            Fit();
            ShareInputQueue();
            GiveKeyboardFocus();
            InstallMouseHook();
            InstallKeyboardHook();
            Native.RECT probe;
            Native.TryGetWindowRect(_window, out probe);
            Debug.Log("gecko attach: parent=" + Native.GetParent(_window) + " host=" + host.Handle +
                      " rect=" + probe.Width + "x" + probe.Height + " client=" + host.ClientSize);
            return true;
        }

        /// <summary>Returns the browser window to the desktop; used when a tab is hidden.</summary>
        public void Detach(Control host)
        {
            if (_window == IntPtr.Zero) return;
            bool wasTheHost = host == null || _host == host;
            Native.SetWindowLongValue(_window, Native.GWL_STYLE, new IntPtr(Native.WS_VISIBLE | 0x00C00000));
            Native.SetParent(_window, IntPtr.Zero);
            Native.SetWindowPos(_window, IntPtr.Zero, 200, 120, _windowWidth, _windowHeight,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_HIDEWINDOW);
            if (!wasTheHost) return;
            // Only the panel that actually holds the window tears the shared input
            // state down. Another tab hiding must not unplug the active one.
            _host = null;
            RemoveMouseHook();
            RemoveKeyboardHook();
            ReleaseInputQueue();
        }

        // ── keyboard hand-off ─────────────────────────────────────
        // The browser window is re-parented into our panel, but it belongs to
        // the browser's own thread. Windows only gives the keyboard to the focus
        // window of the foreground thread's queue, so clicks land in the page
        // while every keystroke stays with us. Sharing the two input queues and
        // pointing focus at the browser window is what makes typing work.
        bool _inputShared;
        uint _browserThread;

        void ShareInputQueue()
        {
            if (_inputShared || _window == IntPtr.Zero) return;
            uint pid;
            uint thread = Native.GetWindowThreadProcessId(_window, out pid);
            uint me = Native.GetCurrentThreadId();
            if (thread == 0 || thread == me) { _browserThread = thread; return; }
            _inputShared = Native.AttachThreadInput(me, thread, true);
            _browserThread = thread;
            Debug.Log("gecko input: share queues me=" + me + " browser=" + thread + " ok=" + _inputShared);
        }

        void ReleaseInputQueue()
        {
            if (!_inputShared) { _browserThread = 0; return; }
            uint me = Native.GetCurrentThreadId();
            if (_browserThread != 0 && _browserThread != me)
                Native.AttachThreadInput(me, _browserThread, false);
            _inputShared = false;
            _browserThread = 0;
            Debug.Log("gecko input: queues released");
        }

        /// <summary>
        /// Hands the keyboard to the browser window. Called whenever the page
        /// becomes the surface the user is looking at, so typing works without
        /// a click first.
        /// </summary>
        public void GiveKeyboardFocus()
        {
            if (!Running || _host == null || _host.IsDisposed || !_host.Visible) return;
            ShareInputQueue();
            try
            {
                Native.SetActiveWindow(_window);
                Native.SetFocus(_window);
                if (Debug.Enabled)
                    Debug.Log("gecko focus: want=" + _window + " have=" + Native.GetFocus());
            }
            catch (Exception ex) { Debug.Log("gecko focus: " + ex.Message); }
        }

        // A click inside the page is the user's clear signal that the browser
        // should own the keyboard. The browser cannot do that itself - focus
        // belongs to our queue - so we watch the mouse and re-point it.
        IntPtr _mouseHook;
        Native.HookProc _mouseProc;

        void InstallMouseHook()
        {
            if (_mouseHook != IntPtr.Zero) return;
            try
            {
                _mouseProc = MouseHook;
                _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
                if (_mouseHook == IntPtr.Zero) Debug.Log("gecko input: mouse hook not installed");
            }
            catch (Exception ex) { Debug.Log("gecko input: mouse hook " + ex.Message); }
        }

        void RemoveMouseHook()
        {
            if (_mouseHook == IntPtr.Zero) return;
            try { Native.UnhookWindowsHookEx(_mouseHook); } catch { }
            _mouseHook = IntPtr.Zero;
            _mouseProc = null;
        }

        /// <summary>
        /// The browser owns the keyboard while a page is on screen, so the app's
        /// own shortcuts would never reach its window. We watch the keyboard and
        /// hand the combinations the app owns back to it, so Ctrl+L still opens
        /// the address bar and Ctrl+T still opens a tab. The modifier state is
        /// passed along because it lives in the browser's queue, not ours.
        /// </summary>
        public event EventHandler<GeckoShortcut> ShortcutPressed;

        IntPtr _keyHook;
        Native.HookProc _keyProc;

        void InstallKeyboardHook()
        {
            if (_keyHook != IntPtr.Zero) return;
            try
            {
                _keyProc = KeyboardHook;
                _keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyProc, IntPtr.Zero, 0);
                if (_keyHook == IntPtr.Zero) Debug.Log("gecko input: keyboard hook not installed");
            }
            catch (Exception ex) { Debug.Log("gecko input: keyboard hook " + ex.Message); }
        }

        void RemoveKeyboardHook()
        {
            if (_keyHook == IntPtr.Zero) return;
            try { Native.UnhookWindowsHookEx(_keyHook); } catch { }
            _keyHook = IntPtr.Zero;
            _keyProc = null;
        }

        IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0)
                {
                    int message = wParam.ToInt32();
                    if (message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN ||
                        message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP)
                    {
                        var info = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                            lParam, typeof(Native.KBDLLHOOKSTRUCT));
                        // Modifier state is tracked here rather than read with
                        // GetKeyState: inside a low-level hook that call reports
                        // the state of the wrong queue and misses the real press.
                        bool down = message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN;
                        TrackModifier((int)info.vkCode, down);
                        if (down && PointerInsidePage())
                        {
                            Keys key = TranslateScan(info.scanCode);
                            if (key != Keys.None && IsAppShortcut(key))
                            {
                                EventHandler<GeckoShortcut> handler = ShortcutPressed;
                                if (handler != null)
                                {
                                    // Swallow the key: the browser is the focus window
                                    // and would otherwise act on it a second time.
                                    handler(this, new GeckoShortcut(key, _ctrl, _alt, _shift));
                                    return new IntPtr(1);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            try { return Native.CallNextHookEx(_keyHook, code, wParam, lParam); }
            catch { return IntPtr.Zero; }
        }

        bool _ctrl, _alt, _shift;

        void TrackModifier(int vk, bool down)
        {
            switch (vk)
            {
                case Native.VK_CONTROL: _ctrl = down; break;
                case Native.VK_MENU: _alt = down; break;
                case Native.VK_SHIFT: _shift = down; break;
                case 0xA2: _ctrl = down; break;     // right Ctrl
                case 0xA3: _ctrl = down; break;     // right Alt
                case 0xA0: _shift = down; break;    // left Shift
                case 0xA1: _shift = down; break;    // right Shift
                case 0x5B: case 0x5C: _alt = down; break;
            }
        }

        /// <summary>The combinations the app handles itself; everything else is the page's.</summary>
        bool IsAppShortcut(Keys key)
        {
            if (_alt && (key == Keys.Left || key == Keys.Right || key == Keys.Home)) return true;
            if (!_ctrl) return false;
            switch (key)
            {
                case Keys.T: case Keys.N: case Keys.W: case Keys.L: case Keys.D:
                case Keys.J: case Keys.B: case Keys.H: case Keys.I: case Keys.R:
                case Keys.F:
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Scan code to key. The virtual key code depends on the active keyboard
        /// layout - under a Russian layout the L key arrives as 0xE7, not 0x4C -
        /// so the physical key is resolved instead, which is layout independent.
        /// </summary>
        static Keys TranslateScan(uint scanCode)
        {
            if (scanCode == 0) return Keys.None;
            switch (scanCode)
            {
                case 0x4B: return Keys.Left;         // the cursor keys share their
                case 0x4D: return Keys.Right;        // code with the numpad, so
                case 0x48: return Keys.Up;           // they are listed by hand
                case 0x50: return Keys.Down;
                case 0x47: return Keys.Home;
                case 0x4F: return Keys.End;
                case 0x53: return Keys.Delete;
                case 0x52: return Keys.Insert;
                case 0x1C: return Keys.Enter;
                case 0x1D: return Keys.Escape;
                case 0x39: return Keys.Space;
                case 0x0E: return Keys.Back;
                case 0x0F: return Keys.Tab;
            }
            char physical = (char)Native.MapVirtualKey(scanCode, Native.MAPVK_VSC_TO_CHAR);
            if (physical < 'A' || physical > 'z') return Keys.None;
            if (physical >= 'A' && physical <= 'Z') physical = char.ToLowerInvariant(physical);
            if (physical >= 'a' && physical <= 'z') return Keys.A + (physical - 'a');
            return Keys.None;
        }

        IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                // Only real presses matter: a click or a wheel turn over the page
                // is the user working with the browser, and moving the mouse past
                // it must not steal focus away from our own controls.
                int message = wParam.ToInt32();
                if (code >= 0 &&
                    (message == Native.WM_LBUTTONDOWN || message == Native.WM_LBUTTONDBLCLK ||
                     message == Native.WM_RBUTTONDOWN || message == Native.WM_MBUTTONDOWN ||
                     message == Native.WM_MOUSEWHEEL) &&
                    PointerInsidePage()) DeferFocus();
            }
            catch { }
            try { return Native.CallNextHookEx(_mouseHook, code, wParam, lParam); }
            catch { return IntPtr.Zero; }
        }

        /// <summary>True when the cursor is over the browser panel, not over our chrome.</summary>
        bool PointerInsidePage()
        {
            if (_host == null || _host.IsDisposed || !_host.Visible) return false;
            Native.POINT point;
            if (!Native.GetCursorPos(out point)) return false;
            Native.RECT frame;
            if (!Native.TryGetWindowRect(_host.Handle, out frame)) return false;
            return point.X >= frame.Left && point.X < frame.Right &&
                   point.Y >= frame.Top && point.Y < frame.Bottom;
        }

        bool _focusQueued;

        /// <summary>Sets focus after the click itself has been delivered.</summary>
        void DeferFocus()
        {
            if (_focusQueued) return;
            Control target = _host;
            if (target == null || target.IsDisposed || !target.IsHandleCreated) return;
            _focusQueued = true;
            try
            {
                target.BeginInvoke((MethodInvoker)delegate
                {
                    _focusQueued = false;
                    GiveKeyboardFocus();
                });
            }
            catch { _focusQueued = false; }
        }

        /// <summary>Matches the browser window to its host panel, honouring any minimum size.</summary>
        public void Fit()
        {
            if (!Running || _host == null || _host.IsDisposed || !_host.IsHandleCreated) return;
            int w = Math.Max(1, _host.ClientSize.Width);
            int h = Math.Max(1, _host.ClientSize.Height);
            if (w < 80 || h < 60) return;

            Native.SetWindowPos(_window, IntPtr.Zero, 0, 0, w, h,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);
            Sleep(40);
            Native.RECT rect;
            if (Native.TryGetWindowRect(_window, out rect))
            {
                if (rect.Width == _windowWidth && rect.Height == _windowHeight &&
                    rect.Width == w && rect.Height == h) return;
                _windowWidth = rect.Width;
                _windowHeight = rect.Height;
                _clipped = rect.Width > w + 4 || rect.Height > h + 4;
                if (Debug.Enabled && (_windowWidth != _lastLoggedWidth || _windowHeight != _lastLoggedHeight))
                {
                    _lastLoggedWidth = _windowWidth;
                    _lastLoggedHeight = _windowHeight;
                    Debug.Log("gecko fit: want " + w + "x" + h + " got " + _windowWidth + "x" +
                              _windowHeight + " parent=" + Native.GetParent(_window) + " host=" +
                              (_host == null ? IntPtr.Zero : _host.Handle));
                }
            }
        }

        // ── navigation ───────────────────────────────────────────
        public void Navigate(string url)
        {
            if (!Available) return;
            url = (url ?? "").Trim();
            if (url.Length == 0) return;
            Run(delegate { Command("navigate", url); });
        }

        public void Reload()
        {
            if (!Available) return;
            Run(delegate { Command("reload", ""); });
        }

        public void Stop()
        {
            if (!Available) return;
            Run(delegate { Evaluate("window.stop()"); });
        }

        void Command(string kind, string url)
        {
            var parameters = new Dictionary<string, object>();
            parameters["context"] = _context;
            parameters["wait"] = "complete";
            if (kind == "navigate") parameters["url"] = url;
            Dictionary<string, object> answer = _client.Call("browsingContext." + kind, parameters, 60000);
            if (answer == null)
            {
                Debug.Log("gecko " + kind + " failed on " + _context + ": " + _client.LastErrorPublic);
                RefreshContext();
                return;
            }
            if (kind == "navigate") SetUrl(Json.Text(answer, "url"));
        }

        /// <summary>BiDi calls can block for seconds, so they never run on the UI thread.</summary>
        void Run(ThreadStart work)
        {
            var worker = new Thread(delegate ()
            {
                try { work(); }
                catch (Exception ex) { Debug.Log("gecko " + ex.Message); }
            });
            worker.IsBackground = true;
            worker.Name = "gecko-command";
            worker.Start();
        }

        public void SetZoom(double level)
        {
            if (!Available) return;
            Evaluate("document.documentElement.style.setProperty('--mbr-zoom','" +
                     level.ToString("0.##", CultureInfo.InvariantCulture) + "');");
        }

        string Evaluate(string expression)
        {
            if (!Available) return "";
            var target = new Dictionary<string, object>();
            target["context"] = _context;
            var parameters = new Dictionary<string, object>();
            parameters["expression"] = expression;
            parameters["target"] = target;
            parameters["awaitPromise"] = false;
            parameters["resultOwnership"] = "none";
            Dictionary<string, object> answer = _client.Call("script.evaluate", parameters, 8000);
            if (answer == null) return "";
            Dictionary<string, object> result = Json.Child(answer, "result");
            return result == null ? "" : Json.Text(result, "value");
        }

        string FirstContext()
        {
            Dictionary<string, object> tree = _client.Call("browsingContext.getTree", null, 8000);
            string context = FirstOf(tree);
            if (Debug.Enabled) Debug.Log("gecko tree: contexts=" + CountOf(tree) + " picked=" + context);
            return context;
        }

        static int CountOf(Dictionary<string, object> tree)
        {
            if (tree == null) return 0;
            var list = tree["contexts"] as List<object>;
            return list == null ? 0 : list.Count;
        }

        void RefreshContext()
        {
            string next = FirstContext();
            if (next.Length > 0) _context = next;
        }

        /// <summary>
        /// Forks such as Zen open a start page in a second tab, which would keep
        /// sitting on top of ours. Only our context should be left.
        /// </summary>
        void CloseOtherContexts()
        {
            Dictionary<string, object> tree = _client.Call("browsingContext.getTree", null, 8000);
            if (tree == null) return;
            var list = tree["contexts"] as List<object>;
            if (list == null) return;
            foreach (object item in list)
            {
                var context = Json.Object(item);
                if (context == null) continue;
                string id = Json.Text(context, "context");
                if (string.Equals(id, _context, StringComparison.OrdinalIgnoreCase)) continue;
                var parameters = new Dictionary<string, object>();
                parameters["context"] = id;
                _client.Fire("browsingContext.close", parameters);
            }
            var activate = new Dictionary<string, object>();
            activate["context"] = _context;
            if (_client.Call("browsingContext.activate", activate, 4000) == null)
                Debug.Log("gecko: activate unsupported: " + _client.LastErrorPublic);
        }

        /// <summary>
        /// The browsing context we drive. Firefox forks like Zen open a welcome
        /// tab first, so a plain about:blank tab wins when one is present.
        /// </summary>
        static string FirstOf(Dictionary<string, object> tree)
        {
            if (tree == null) return "";
            object raw;
            if (!tree.TryGetValue("contexts", out raw)) return "";
            var list = raw as List<object>;
            if (list == null) return "";
            string best = "", bestUrl = null, last = "";
            foreach (object item in list)
            {
                var context = Json.Object(item);
                if (context == null) continue;
                string id = Json.Text(context, "context");
                string url = Json.Text(context, "url");
                last = id;
                if (url.StartsWith("about:welcome", StringComparison.OrdinalIgnoreCase)) continue;
                if (url == "about:blank") { best = id; bestUrl = url; continue; }
                if (best.Length == 0) { best = id; bestUrl = url; }
            }
            return best.Length > 0 ? best : last;
        }

        // ── the BiDi bridge ──────────────────────────────────────
        void OnBidiEvent(string method, Dictionary<string, object> parameters)
        {
            if (parameters == null) return;
            string context = Json.Text(parameters, "context");
            switch (method)
            {
                case "browsingContext.navigationStarted":
                    Post(delegate { SetUrl(Json.Text(parameters, "url")); SetLoading(true); });
                    break;
                case "browsingContext.domContentLoaded":
                case "browsingContext.load":
                    Post(delegate
                    {
                        if (ContextMatches(context))
                        {
                            HardenPage();
                            SetLoading(false);
                            RefreshTitle();
                            if (Debug.Enabled) Debug.Log("gecko metrics: " + ViewportMetrics());
                        }
                    });
                    break;
                case "browsingContext.contextDestroyed":
                    if (ContextMatches(context)) Post(delegate { RefreshContext(); });
                    break;
                case "browsingContext.userPromptOpened":
                    // A site asked something (alert, confirm, beforeunload). Left
                    // unanswered it blocks the load forever, so we always accept.
                    AnswerPrompt(parameters);
                    break;
            }
        }

        bool ContextMatches(string context)
        {
            if (context.Length == 0 || _context.Length == 0) return true;
            return string.Equals(context, _context, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Answers alert/confirm/beforeunload so the page never hangs.</summary>
        void AnswerPrompt(Dictionary<string, object> parameters)
        {
            if (_client == null || !_client.Ready) return;
            string context = Json.Text(parameters, "context");
            var answer = new Dictionary<string, object>();
            answer["context"] = context.Length > 0 ? context : _context;
            answer["action"] = "accept";
            Run(delegate { _client.Fire("browsingContext.handleUserPrompt", answer); });
        }

        /// <summary>Runs on the UI thread from any thread.</summary>
        void Post(MethodInvoker action)
        {
            try
            {
                Control target = _host ?? (_owner);
                if (target != null && target.IsHandleCreated && !target.IsDisposed)
                    target.BeginInvoke(action);
            }
            catch { }
        }

        Control _owner;

        public void BindTo(Control owner)
        {
            _owner = owner;
        }

        void SetLoading(bool loading)
        {
            if (_loading == loading) return;
            _loading = loading;
            EventHandler<bool> handler = LoadingChanged;
            if (handler != null) handler(this, loading);
        }

        void SetUrl(string url)
        {
            if (url.Length == 0 || string.Equals(url, _url, StringComparison.Ordinal)) return;
            _url = url;
            EventHandler<string> handler = UrlChanged;
            if (handler != null) handler(this, url);
        }

        void SetTitle(string title)
        {
            if (string.Equals(title, _title, StringComparison.Ordinal)) return;
            _title = title;
            EventHandler<string> handler = TitleChanged;
            if (handler != null) handler(this, title);
        }

        /// <summary>
        /// Folds popups into the current tab so the embedded window stays the only
        /// surface the user has to look at.
        /// </summary>
        /// <summary>
        /// Real sites must not be rewritten beyond what embedding forces on us:
        /// a link that asks for a new window would otherwise leave the page and
        /// strand the user in a window we have to throw away. Only the anchor
        /// target is touched, and only for plain clicks without modifiers.
        /// window.open is deliberately left alone so logins and payments keep working.
        /// </summary>
        public void HardenPage()
        {
            Evaluate(
                "(function(){try{" +
                "if(window.__mbrHardened){return 1}window.__mbrHardened=1;" +
                "document.addEventListener('click',function(e){try{" +
                "if(e.defaultPrevented||e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey)return;" +
                "var n=e.target;while(n&&n.nodeType===1&&n.tagName!=='A')n=n.parentNode;" +
                "if(!n||!n.getAttribute)return;" +
                "var t=(n.getAttribute('target')||'').toLowerCase();" +
                "if(t==='_blank'||t==='_new')n.setAttribute('target','_self');" +
                "}catch(e){}},true);" +
                "return 1}catch(e){return 0}})()");
        }

        void RefreshTitle()
        {
            string title = Evaluate("document.title||''");
            SetTitle(title);
        }

        /// <summary>
        /// Compares what the page thinks its viewport is with the real window
        /// rectangle. A difference means Gecko reserves space we cannot see,
        /// a match means the gap belongs to the native window instead.
        /// </summary>
        string ViewportMetrics()
        {
            Native.RECT frame, client;
            Native.TryGetWindowRect(_window, out frame);
            Native.GetClientRect(_window, out client);
            return "page=" + Evaluate("[innerWidth, innerHeight, visualViewport ? Math.round(visualViewport.height) : -1].join('x')") +
                   " windowClient=" + client.Width + "x" + client.Height +
                   " host=" + (_host == null ? "-" : _host.ClientSize.Width + "x" + _host.ClientSize.Height) +
                   " clipped=" + _clipped;
        }

        // ── periodic work ────────────────────────────────────────
        int _tick;
        int _lastLoggedWidth, _lastLoggedHeight;

        void Pump()
        {
            if (!Running)
            {
                StopPump();
                return;
            }
            if (_window == IntPtr.Zero || !Native.IsWindow(_window))
            {
                Debug.Log("gecko: browser window is gone, engine stopped" +
                          " exe=" + _exeName + " running=" + Process.GetProcessesByName(_exeName).Length);
                _running = false;
                _url = "";
                _title = "";
                StopPump();
                CleanupProfile();
                return;
            }

            AdoptStrayWindows();

            if (_host == null && _tick % 20 == 0)
                Debug.Log("gecko pump: no host, window=" + _windowWidth + "x" + _windowHeight);

            if (_host != null && !_host.IsDisposed && _host.Visible && _host.ClientSize.Width > 80)
            {
                Fit();
                KeepKeyboard();
                if (_tick % 8 == 0) Evaluate("window.scrollTo(window.scrollX, window.scrollY)");
            }

            if (!_loading && ++_tick % 4 == 0) RefreshTitle();
        }

        /// <summary>
        /// Restores the keyboard hand-off if it was lost. Switching tabs tears the
        /// shared input state down and rebuilds it from several different event
        /// paths, so instead of trusting that order, the pump checks the real
        /// state and repairs it: without this the page goes silent after a tab is
        /// closed until the window is clicked again.
        /// </summary>
        void KeepKeyboard()
        {
            // Only the shared state itself is repaired here. Focus is left alone:
            // the address bar and other controls of ours legitimately hold it,
            // and taking it back on every tick would fight the user's typing.
            if (_inputShared && _mouseHook != IntPtr.Zero && _keyHook != IntPtr.Zero) return;
            ShareInputQueue();
            InstallMouseHook();
            InstallKeyboardHook();
            GiveKeyboardFocus();
            if (Debug.Enabled) Debug.Log("gecko input: keyboard restored");
        }

        /// <summary>
        /// A page that slips past the hardening script can still open a real
        /// browser window. We read what it wanted to show, close the window and
        /// hand the address back to the app so it can appear as a normal link.
        /// </summary>
        void AdoptStrayWindows()
        {
            foreach (IntPtr h in GeckoWindows())
            {
                if (_knownWindows.Contains(h)) continue;
                _knownWindows.Add(h);
                Debug.Log("gecko: stray window \"" + TitleOf(h) + "\" closed");
                string target = NewestContextUrl();
                Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                if (target.Length > 0)
                {
                    Run(delegate { Navigate(target); });
                    EventHandler<string> handler = NavigationRequested;
                    if (handler != null) handler(this, target);
                }
                return;
            }
        }

        string NewestContextUrl()
        {
            Dictionary<string, object> tree = _client.Call("browsingContext.getTree", null, 6000);
            if (tree == null) return "";
            var list = tree["contexts"] as List<object>;
            if (list == null) return "";
            string url = "";
            foreach (object item in list)
            {
                var context = Json.Object(item);
                if (context == null) continue;
                if (string.Equals(Json.Text(context, "context"), _context, StringComparison.OrdinalIgnoreCase)) continue;
                string candidate = Json.Text(context, "url");
                if (candidate.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = candidate;
            }
            return url;
        }

        static void Sleep(int ms)
        {
            System.Threading.Thread.Sleep(ms);
        }

        public void Dispose()
        {
            StopPump();
            RemoveMouseHook();
            RemoveKeyboardHook();
            ReleaseInputQueue();
            if (_client != null)
            {
                _client.Event -= OnBidiEvent;
                try { _client.Dispose(); } catch { }
                _client = null;
            }
            if (_window != IntPtr.Zero)
            {
                try { Native.PostMessage(_window, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); } catch { }
                _window = IntPtr.Zero;
            }
            if (_launcher != null)
            {
                try { if (!_launcher.HasExited) _launcher.Kill(); } catch { }
                _launcher = null;
            }
            KillProcesses();
            CleanupProfile();
            _running = false;
        }

        void KillProcesses()
        {
            // Only kill browsers started from our own throw-away profile, never the
            // user's own Firefox window.
            if (_exeName.Length == 0 || _profileDir.Length == 0) return;
            try
            {
                var query = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '" + _exeName + ".exe'");
                foreach (System.Management.ManagementObject item in query.Get())
                {
                    string command = Convert.ToString(item["CommandLine"], CultureInfo.InvariantCulture);
                    if (command.Length == 0 || command.IndexOf(_profileDir, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Kill(Convert.ToInt32(item["ProcessId"], CultureInfo.InvariantCulture));
                }
            }
            catch { }
            Kill(_launcher);
        }

        void Kill(int pid)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                using (p) { p.Kill(); }
            }
            catch { }
        }

        void Kill(Process process)
        {
            if (process == null) return;
            int pid;
            try { pid = process.Id; } catch { return; }
            Kill(pid);
        }
    }
}