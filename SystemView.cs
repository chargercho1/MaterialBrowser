using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace MaterialBrowser
{
    /// <summary>
    /// Optional second engine: the WebBrowser control that Windows still ships.
    /// It is only created on demand and falls back to a message when the machine
    /// has no usable HTML engine registered.
    /// </summary>
    public class SystemView : Panel
    {
        WebBrowser _browser;
        bool _ready;
        string _pendingUrl = "";

        public event EventHandler<string> TitleChanged;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<bool> LoadingChanged;
        public event EventHandler<string> NavigationRequested;

        public SystemView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.T.Surface;
        }

        public bool Available
        {
            get { return _ready && _browser != null && !_browser.IsDisposed; }
        }

        public string CurrentUrl
        {
            get { return Available ? Convert.ToString(_browser.Url) : _pendingUrl; }
        }

        public string CurrentTitle
        {
            get { return Available ? _browser.DocumentTitle ?? "" : ""; }
        }

        public bool CanGoBack
        {
            get { return Available && _browser.CanGoBack; }
        }

        public bool CanGoForward
        {
            get { return Available && _browser.CanGoForward; }
        }

        /// <summary>Creates the ActiveX host. Returns false when the machine cannot provide one.</summary>
        public bool Ensure()
        {
            if (Available) return true;
            if (_browser != null && !_browser.IsDisposed) return false;

            try
            {
                var browser = new WebBrowser();
                browser.Dock = DockStyle.Fill;
                browser.ScriptErrorsSuppressed = true;
                browser.IsWebBrowserContextMenuEnabled = false;
                browser.AllowWebBrowserDrop = false;
                browser.WebBrowserShortcutsEnabled = false;
                browser.ScrollBarsEnabled = true;

                browser.Navigating += OnNavigating;
                browser.Navigated += OnNavigated;
                browser.DocumentCompleted += OnCompleted;
                browser.NewWindow += OnNewWindow;
                browser.StatusTextChanged += OnStatusChanged;

                Controls.Add(browser);
                _browser = browser;
                _ready = true;
                if (_pendingUrl.Length > 0) browser.Navigate(_pendingUrl);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Log("WebBrowser unavailable: " + ex.Message);
                _ready = false;
                _browser = null;
                return false;
            }
        }

        public void Navigate(string url)
        {
            if (url.Length == 0) return;
            _pendingUrl = url;
            if (!Ensure()) return;
            try { _browser.Navigate(url); }
            catch (Exception ex) { Debug.Log("navigate failed: " + ex.Message); }
        }

        public void GoBack() { if (Available) try { _browser.GoBack(); } catch { } }
        public void GoForward() { if (Available) try { _browser.GoForward(); } catch { } }
        public void Reload() { if (Available) try { _browser.Refresh(); } catch { } }
        public void Stop() { if (Available) try { _browser.Stop(); } catch { } }

        void OnNavigating(object sender, WebBrowserNavigatingEventArgs e)
        {
            EventHandler<bool> h = LoadingChanged;
            if (h != null) h(this, true);
            string target = e.Url != null ? e.Url.ToString() : "";
            // The control cannot run page scripts, so let the browser handle the request.
            if (e.Url != null && e.Url.Scheme == "about" && target != "about:blank")
            {
                e.Cancel = true;
                EventHandler<string> request = NavigationRequested;
                if (request != null) request(this, target);
            }
        }

        void OnNavigated(object sender, WebBrowserNavigatedEventArgs e)
        {
            if (e.Url != null) _pendingUrl = e.Url.ToString();
        }

        void OnCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (e.Url != null) _pendingUrl = e.Url.ToString();
            EventHandler<string> h = TitleChanged;
            if (h != null) h(this, CurrentTitle);
            EventHandler<bool> loading = LoadingChanged;
            if (loading != null) loading(this, false);
        }

        void OnNewWindow(object sender, CancelEventArgs e)
        {
            EventHandler<string> h = NavigationRequested;
            if (h != null && _browser != null) h(this, _pendingUrl);
            e.Cancel = true;
        }

        void OnStatusChanged(object sender, EventArgs e)
        {
            EventHandler<string> h = StatusChanged;
            if (h != null) h(this, Available ? _browser.StatusText : "");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _browser != null)
            {
                try
                {
                    _browser.Navigating -= OnNavigating;
                    _browser.Navigated -= OnNavigated;
                    _browser.DocumentCompleted -= OnCompleted;
                    _browser.NewWindow -= OnNewWindow;
                    _browser.StatusTextChanged -= OnStatusChanged;
                }
                catch { }
                _browser.Dispose();
                _browser = null;
            }
            base.Dispose(disposing);
        }
    }
}
