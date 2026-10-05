using System;
using System.Drawing;
using System.Windows.Forms;

namespace MaterialBrowser
{
    /// <summary>
    /// Firefox engine surface. Mirrors SystemView so MainForm can treat both
    /// external engines the same way, but the pixels come from a real Gecko
    /// window that is re-parented into this panel. One browser process is shared
    /// by every tab, so the panel takes the window when it becomes visible and
    /// gives it back when it is hidden.
    /// </summary>
    public class GeckoView : Panel
    {
        GeckoEngine _engine;
        bool _attached;
        string _pendingUrl = "";

        public event EventHandler<string> TitleChanged;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<bool> LoadingChanged;
        public event EventHandler<string> NavigationRequested;

        /// <summary>Raised for the shortcuts the app owns while the browser holds the keyboard.</summary>
        public event EventHandler<GeckoShortcut> ShortcutPressed;

        public GeckoView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.T.Surface;
        }

        /// <summary>Static facts about the installed browser, safe to call before launch.</summary>
        public static bool Installed { get { return GeckoEngine.Installed().Count > 0; } }

        public static string Describe()
        {
            var list = GeckoEngine.Installed();
            if (list.Count == 0) return "Firefox не найден";
            return list[0].ToString();
        }

        GeckoEngine Engine { get { return _engine ?? GeckoEngine.Shared; } }

        /// <summary>
        /// Takes the shared engine if this panel has not used it yet. A tab opened
        /// later runs into an engine that is already running, so it has to adopt
        /// the shared instance rather than start one of its own.
        /// </summary>
        GeckoEngine Adopt()
        {
            GeckoEngine engine = Bind();
            return engine != null && engine.Available ? engine : null;
        }

        /// <summary>Same, for the operations that only need the browser window.</summary>
        GeckoEngine AdoptRunning()
        {
            GeckoEngine engine = Bind();
            return engine != null && engine.Running ? engine : null;
        }

        GeckoEngine Bind()
        {
            if (_engine == null)
            {
                GeckoEngine shared = GeckoEngine.Shared;
                if (!shared.Available && !shared.Running) return null;
                shared.BindTo(this);
                _engine = shared;
                Hook();
            }
            return _engine;
        }

        public bool Available
        {
            get { return Engine.Available; }
        }

        /// <summary>
        /// True when the browser window itself is alive. Taking the window into a
        /// panel is a native operation and must not depend on the control
        /// channel: if that channel drops, the page still has to come back.
        /// </summary>
        public bool EngineRunning
        {
            get { return Engine.Running; }
        }

        public string CurrentUrl
        {
            get { return Available ? Engine.CurrentUrl : _pendingUrl; }
        }

        public string CurrentTitle
        {
            get { return Available ? Engine.CurrentTitle : ""; }
        }

        // The shared window keeps its own history; MainForm's tab history drives
        // Back/Forward, so the engine never claims to own them.
        public bool CanGoBack { get { return false; } }
        public bool CanGoForward { get { return false; } }

        public string ClipNote
        {
            get { return Available ? Engine.ClipNote : ""; }
        }

        /// <summary>Starts the browser if needed and hooks the shared window to this panel.</summary>
        public bool Ensure()
        {
            GeckoEngine engine = Engine;
            if (engine.Available)
            {
                _engine = engine;
                Hook();
                return true;
            }
            if (_engine != null) return false;

            engine.BindTo(this);
            engine.TitleChanged += OnTitle;
            engine.UrlChanged += OnUrl;
            engine.LoadingChanged += OnLoading;
            engine.NavigationRequested += OnNavigation;
            engine.ShortcutPressed += OnShortcut;

            int width = ClientSize.Width > 200 ? ClientSize.Width : 1280;
            int height = ClientSize.Height > 200 ? ClientSize.Height : 800;
            Debug.Log("gecko view: ensure start " + width + "x" + height + " installed=" + Describe());
            if (!engine.Start(width, height))
            {
                engine.TitleChanged -= OnTitle;
                engine.UrlChanged -= OnUrl;
                engine.LoadingChanged -= OnLoading;
                engine.NavigationRequested -= OnNavigation;
                engine.ShortcutPressed -= OnShortcut;
                return false;
            }
            _engine = engine;
            Hook();
            return true;
        }

        void Hook()
        {
            if (_engine == null) return;
            _engine.TitleChanged -= OnTitle;
            _engine.UrlChanged -= OnUrl;
            _engine.LoadingChanged -= OnLoading;
            _engine.NavigationRequested -= OnNavigation;
            _engine.TitleChanged += OnTitle;
            _engine.UrlChanged += OnUrl;
            _engine.LoadingChanged += OnLoading;
            _engine.NavigationRequested += OnNavigation;
        }

        void OnTitle(object sender, string title)
        {
            EventHandler<string> handler = TitleChanged;
            if (handler != null) handler(this, title);
            EventHandler<string> status = StatusChanged;
            if (status != null) status(this, title);
        }

        void OnUrl(object sender, string url)
        {
            EventHandler<string> handler = StatusChanged;
            if (handler != null) handler(this, url);
        }

        void OnLoading(object sender, bool loading)
        {
            EventHandler<bool> handler = LoadingChanged;
            if (handler != null) handler(this, loading);
        }

        void OnNavigation(object sender, string target)
        {
            EventHandler<string> handler = NavigationRequested;
            if (handler != null) handler(this, target);
        }

        void OnShortcut(object sender, GeckoShortcut shortcut)
        {
            EventHandler<GeckoShortcut> handler = ShortcutPressed;
            if (handler != null) handler(this, shortcut);
        }

        /// <summary>Takes the shared browser window right now, whatever WinForms thinks.</summary>
        public void AttachNow()
        {
            if (!Visible || IsDisposed) return;
            GeckoEngine engine = AdoptRunning();
            if (engine == null) return;
            engine.Attach(this);
            _attached = true;
            // The page is what the user is looking at, so it gets the keyboard.
            engine.GiveKeyboardFocus();
        }

        public void Navigate(string url)
        {
            if (url.Length == 0) return;
            _pendingUrl = url;
            GeckoEngine engine = Available ? Adopt() : null;
            if (engine == null) return;
            engine.Attach(this);
            _attached = true;
            engine.Navigate(url);
        }

        public void GoBack() { }
        public void GoForward() { }

        public void Reload()
        {
            GeckoEngine engine = Available ? Adopt() : null;
            if (engine != null) engine.Reload();
        }

        public void Stop()
        {
            GeckoEngine engine = Available ? Adopt() : null;
            if (engine != null) engine.Stop();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
            {
                if (_attached && _engine != null)
                {
                    _engine.Detach(this);
                    _attached = false;
                }
                return;
            }
            GeckoEngine engine = AdoptRunning();
            if (engine == null) return;
            engine.Attach(this);
            _attached = true;
            engine.GiveKeyboardFocus();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_attached && Available) _engine.Fit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _engine != null)
            {
                _engine.TitleChanged -= OnTitle;
                _engine.UrlChanged -= OnUrl;
                _engine.LoadingChanged -= OnLoading;
                _engine.NavigationRequested -= OnNavigation;
                _engine.ShortcutPressed -= OnShortcut;
                if (_attached)
                {
                    _engine.Detach(this);
                    _attached = false;
                }
            }
            base.Dispose(disposing);
        }
    }
}