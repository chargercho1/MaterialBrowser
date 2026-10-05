using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace MaterialBrowser
{
    public class MainForm : Form
    {
        public static bool DisableBackdrop;

        class Tab
        {
            public string Url = "";
            public string Title = "";
            public bool Loading;
            public Page Page;
            public Panel Surface;
            public PageView View;
            public SystemView System;
            public GeckoView Gecko;
            public readonly List<string> Back = new List<string>();
            public readonly List<string> Forward = new List<string>();
            public bool Private;
            public readonly CookieContainer Cookies = new CookieContainer();
        }

        const int TopBarHeight = 56;
        const int ToolbarHeight = 56;
        const int StatusHeight = 28;

        readonly List<Tab> _tabs = new List<Tab>();
        int _active;

        TabStrip _tabsStrip;
        Sidebar _sidebar;
        AddressBar _address;
        BookmarksBar _bookmarksBar;
        LoadingBar _loading;
        StatusStrip _status;
        Snackbar _snackbar;

        IconButton _back;
        IconButton _forward;
        IconButton _reload;
        IconButton _home;
        IconButton _menu;
        IconButton _sidebarToggle;
        WindowButton _minimize;
        WindowButton _maximize;
        WindowButton _close;

        ListPage _listPage;
        SettingsPage _settingsPage;

        string _mode = "web";         // web | bookmarks | history | downloads | settings
        bool _backdrop;
        bool _maximized;
        Rectangle _restoreBounds;
        int _sidebarWidth = Ui.PX(248);
        int _pendingRender;
        readonly System.Windows.Forms.Timer _pendingTimer = new System.Windows.Forms.Timer();

        public MainForm()
        {
            Ui.Init(this);
            AppTheme.Load();
            Settings.Load();
            SettingsFile.Ensure();
            Bookmarks.Load();
            History.Load();
            Downloads.Load();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.PX(1280), Ui.PX(840));
            MinimumSize = new Size(Ui.PX(940), Ui.PX(580));
            Text = "Material Browser";
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            BuildUi();
            AppTheme.Changed += delegate { ApplyScheme(); };
            ApplyScheme();

            Images.Changed += delegate { RepaintPages(); };
            Bookmarks.Changed += delegate { _bookmarksBar.Rebuild(); _sidebar.Rebuild(); };
            Downloads.Changed += delegate { if (_mode == "downloads") FillDownloads(); };
            History.Changed += delegate { if (_mode == "history") FillHistory(); };

            _pendingTimer.Interval = 200;
            _pendingTimer.Tick += delegate
            {
                if (_pendingRender <= 0) return;
                _pendingRender--;
                RepaintPages();
                if (_pendingRender <= 0) _pendingTimer.Stop();
            };

            RestoreSession();
        }

        private Tab Current { get { return _tabs.Count > 0 ? _tabs[_active] : null; } }

        // ─── construction ──────────────────────────────────────────
        private void BuildUi()
        {
            _tabsStrip = new TabStrip();
            _tabsStrip.ActiveChanged += delegate { OnTabActivated(); };
            _tabsStrip.TabClosed += delegate { OnTabClosed(); };
            _tabsStrip.NewTabRequested += delegate { OpenTab(HomeUrl(), false); };
            Controls.Add(_tabsStrip);

            _back = MakeButton("back", delegate { GoBack(); });
            _forward = MakeButton("forward", delegate { GoForward(); });
            _reload = MakeButton("refresh", delegate { ReloadCurrent(); });
            _home = MakeButton("home", delegate { ShowHome(); });
            Controls.Add(_back);
            Controls.Add(_forward);
            Controls.Add(_reload);
            Controls.Add(_home);

            _address = new AddressBar();
            _address.Submitted += delegate { SubmitAddress(); };
            _address.StarClicked += delegate { ToggleBookmark(); };
            _address.PrivateClicked += delegate { Toast("Эта вкладка приватная: история не сохраняется"); };
            Controls.Add(_address);

            _menu = MakeButton("menu", delegate { ShowMainMenu(new Point(_menu.Width / 2, _menu.Height + Ui.PX(6))); });
            Controls.Add(_menu);

            _sidebarToggle = MakeButton("grid", delegate
            {
                _sidebar.Visible = !_sidebar.Visible;
                LayoutChrome();
            });
            _sidebarToggle.ActiveState = true;
            Controls.Add(_sidebarToggle);

            _sidebar = new Sidebar();
            _sidebar.Rebuild();
            _sidebar.PageRequested += delegate (object s, string page) { OnSidebarPage(page); };
            Controls.Add(_sidebar);

            _bookmarksBar = new BookmarksBar();
            _bookmarksBar.Rebuild();
            _bookmarksBar.ChipActivated += delegate (object s, string url) { Navigate(url); };
            Controls.Add(_bookmarksBar);

            _loading = new LoadingBar();
            Controls.Add(_loading);

            _listPage = new ListPage();
            _listPage.ItemActivated += delegate (object s, string payload) { OnListActivated(payload); };
            _listPage.ContextRequested += delegate (object s, ListPage.ContextEventArgs e) { ShowListMenu(e); };
            _listPage.Visible = false;
            Controls.Add(_listPage);

            _settingsPage = new SettingsPage();
            _settingsPage.Visible = false;
            Controls.Add(_settingsPage);

            _minimize = MakeWindowButton("minimize", delegate { WindowState = FormWindowState.Minimized; });
            _maximize = MakeWindowButton("maximize", delegate { ToggleMaximize(); });
            _close = MakeWindowButton("close", delegate { Close(); });
            _close.IsCloseButton = true;
            Controls.Add(_minimize);
            Controls.Add(_maximize);
            Controls.Add(_close);

            _status = new StatusStrip();
            Controls.Add(_status);

            _snackbar = new Snackbar();
            Controls.Add(_snackbar);
        }

        private IconButton MakeButton(string icon, Action action)
        {
            var b = new IconButton();
            b.IconName = icon;
            b.Size = new Size(Ui.PX(38), Ui.PX(38));
            b.Clicked += delegate { action(); };
            return b;
        }

        private WindowButton MakeWindowButton(string icon, Action action)
        {
            var b = new WindowButton();
            b.IconName = icon;
            b.Size = new Size(Ui.PX(46), Ui.PX(36));
            b.Clicked += delegate { action(); };
            return b;
        }

        // ─── tabs ──────────────────────────────────────────────────
        private Tab CreateTab(string url, bool isPrivate)
        {
            var tab = new Tab();
            tab.Private = isPrivate;
            tab.Surface = new Panel();
            tab.Surface.BackColor = Color.Transparent;
            // NB: never Dock.Fill this panel. It is a direct child of the form and sits on
            // top of the chrome, so docking would stretch it over the toolbar and sidebar.
            tab.Surface.Visible = false;

            tab.View = new PageView();
            tab.View.Dock = DockStyle.Fill;
            tab.View.LinkActivated += delegate (object s, string target) { OnLink(target); };
            tab.View.StatusChanged += delegate { UpdateStatus(); };
            tab.View.RelayoutNeeded += delegate { if (ReferenceEquals(Current, tab)) Relayout(tab); };
            tab.Surface.Controls.Add(tab.View);

            tab.System = new SystemView();
            tab.System.Dock = DockStyle.Fill;
            tab.System.Visible = false;
            tab.System.TitleChanged += delegate (object s, string title) { SetTabTitle(title); };
            tab.System.LoadingChanged += delegate (object s, bool loading) { SetLoading(loading); };
            tab.System.StatusChanged += delegate { UpdateStatus(); };
            tab.System.NavigationRequested += delegate (object s, string target) { OnLink(target); };
            tab.Surface.Controls.Add(tab.System);

            tab.Gecko = new GeckoView();
            tab.Gecko.Dock = DockStyle.Fill;
            tab.Gecko.Visible = false;
            tab.Gecko.TitleChanged += delegate (object s, string title) { SetTabTitle(title); };
            tab.Gecko.LoadingChanged += delegate (object s, bool loading) { SetLoading(loading); };
            tab.Gecko.StatusChanged += delegate { UpdateChrome(); };
            tab.Gecko.NavigationRequested += delegate (object s, string target) { OnLink(target); };
            tab.Gecko.ShortcutPressed += OnEngineShortcut;

            tab.Surface.Controls.Add(tab.Gecko);

            Controls.Add(tab.Surface);
            tab.Surface.BringToFront();
            _tabs.Add(tab);
            return tab;
        }

        public void OpenTab(string url, bool isPrivate)
        {
            Tab tab = CreateTab(url, isPrivate);
            _active = _tabs.Count - 1;
            ShowActiveSurface();
            SyncTabs();
            Navigate(url);
        }

        private void CloseTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            Tab tab = _tabs[index];
            Controls.Remove(tab.Surface);
            tab.Surface.Dispose();
            _tabs.RemoveAt(index);
            if (_active >= _tabs.Count) _active = _tabs.Count - 1;
            if (_active < 0) _active = 0;
            SyncTabs();
            if (_tabs.Count == 0) { Close(); return; }
            ShowActiveSurface();
            OnTabActivated();
        }

        private void SyncTabs()
        {
            var paths = new List<string>();
            foreach (Tab t in _tabs) paths.Add(string.IsNullOrEmpty(t.Title) ? t.Url : t.Title);
            _tabsStrip.SetTabs(paths, _active);
        }

        private void OnTabActivated()
        {
            _active = _tabsStrip.ActiveIndex;
            ShowActiveSurface();
            AttachGecko();
            UpdateChrome();
        }

        private void OnTabClosed()
        {
            // TabStrip already dropped the row; mirror that in our own list.
            if (_tabs.Count > _tabsStrip.Count)
            {
                int stale = -1;
                for (int i = 0; i < _tabs.Count; i++)
                {
                    string path = string.IsNullOrEmpty(_tabs[i].Title) ? _tabs[i].Url : _tabs[i].Title;
                    bool found = false;
                    for (int k = 0; k < _tabsStrip.Count; k++)
                        if (_tabsStrip.TabPath(k) == path) { found = true; break; }
                    if (!found) { stale = i; break; }
                }
                if (stale >= 0)
                {
                    Controls.Remove(_tabs[stale].Surface);
                    _tabs[stale].Surface.Dispose();
                    _tabs.RemoveAt(stale);
                }
            }
            if (_active >= _tabs.Count) _active = Math.Max(0, _tabs.Count - 1);
            SyncTabs();
            if (_tabs.Count == 0) { Close(); return; }
            ShowActiveSurface();
            OnTabActivated();
        }

        private void ShowActiveSurface()
        {
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].Surface.Visible = i == _active;
            if (Current != null)
            {
                Current.Surface.BringToFront();
                AttachGecko();
            }
        }

        /// <summary>Hands the single browser window to the tab that is now on screen.</summary>
        private void AttachGecko()
        {
            Tab tab = Current;
            if (tab == null || !tab.Gecko.EngineRunning) return;
            if (!tab.Gecko.Visible) return;
            tab.Gecko.AttachNow();
            string url = tab.Url;
            if (url.Length > 0 && !string.Equals(url, tab.Gecko.CurrentUrl, StringComparison.OrdinalIgnoreCase))
                tab.Gecko.Navigate(url);
        }

        private void SetTabTitle(string title)
        {
            if (Current == null) return;
            if (!string.IsNullOrEmpty(title)) Current.Title = title;
            SyncTabs();
        }

        // ─── navigation ────────────────────────────────────────────
        private string HomeUrl()
        {
            string home = Settings.HomePage;
            if (string.IsNullOrEmpty(home)) home = Engines.Default.HomeUrl;
            return home;
        }

        public void Navigate(string url)
        {
            if (Current == null) return;
            Navigate(url, true);
        }

        private void Navigate(string url, bool recordHistory)
        {
            Tab tab = Current;
            if (tab == null) return;
            url = (url ?? "").Trim();
            if (url.Length == 0) return;

            if (url.StartsWith("tab\t", StringComparison.Ordinal))
            {
                string target = url.Substring(4);
                OpenTab(target, tab.Private);
                return;
            }

            if (recordHistory && tab.Url.Length > 0 && !string.Equals(tab.Url, url, StringComparison.OrdinalIgnoreCase))
            {
                tab.Back.Add(tab.Url);
                tab.Forward.Clear();
                if (tab.Back.Count > 60) tab.Back.RemoveAt(0);
            }

            tab.Url = url;
            tab.Title = WebText.TabTitle(url);
            SyncTabs();
            ShowMode("web");
            UpdateChrome();

            if (HandleSpecial(url, tab)) return;

            Debug.Log("navigate: engine=" + Settings.EngineMode + " url=" + url);
            if (Settings.EngineMode == 1 && SystemEngineReady())
            {
                SetLoading(true);
                tab.Page = null;
                tab.View.SetPage(null);
                tab.View.Visible = false;
                tab.System.Visible = true;
                tab.Gecko.Visible = false;
                tab.System.Navigate(url);
                if (!tab.Private) History.Visit(url, tab.Title);
                return;
            }

            if (Settings.EngineMode == 2 && GeckoEngineReady())
            {
                SetLoading(true);
                tab.Page = null;
                tab.View.SetPage(null);
                tab.View.Visible = false;
                tab.System.Visible = false;
                tab.Gecko.Visible = true;
                tab.Gecko.BringToFront();
                tab.Gecko.Navigate(url);
                if (!tab.Private) History.Visit(url, tab.Title);
                return;
            }

            LoadWithBuiltinEngine(tab, url);
        }

        bool GeckoEngineReady()
        {
            Tab tab = Current;
            if (tab == null) return false;
            Debug.Log("gecko: engine ready check, mode=" + Settings.EngineMode);
            if (tab.Gecko.Available) return true;
            Toast("Запускаю " + GeckoView.Describe() + "…");
            if (!tab.Gecko.Ensure())
            {
                string why = GeckoEngine.Shared.LastError;
                Toast("Движок Firefox недоступен: " + (why.Length > 0 ? why : "запуск не удался") +
                      ". Использую встроенный");
                Settings.EngineMode = 0;
                Settings.Save();
                return false;
            }
            // Firefox saves files by itself, so the downloads page learns about
            // them by watching the folder it writes to.
            GeckoDownloads.Start();
            return true;
        }

        bool SystemEngineReady()
        {
            Tab tab = Current;
            if (tab == null) return false;
            if (tab.System.Available) return true;
            if (!tab.System.Ensure())
            {
                Toast("Системный движок недоступен, используется встроенный");
                Settings.EngineMode = 0;
                Settings.Save();
                return false;
            }
            return true;
        }

        private void LoadWithBuiltinEngine(Tab tab, string url)
        {
            SetLoading(true);
            tab.System.Visible = false;
            tab.Gecko.Visible = false;
            tab.View.Visible = true;
            if (Settings.EngineMode == 1) tab.View.Visible = true;

            string referer = tab.Url != url && tab.Back.Count > 0 ? tab.Back[tab.Back.Count - 1] : "";
            var cookieCopy = tab.Cookies;

            var worker = new Thread(delegate ()
            {
                string error;
                HttpResult result = Http.Fetch(url, referer, cookieCopy, out error);
                Page page = null;

                if (result.Ok)
                {
                    page = new Page();
                    page.ApplyTheme(Ui.T);
                    try
                    {
                        page.Prepare(result.Body ?? "", result.Url, cookieCopy);
                    }
                    catch (Exception ex)
                    {
                        page = Page.ErrorPage(result.Url, "Не удалось разобрать страницу: " + ex.Message, 0);
                    }
                    page.Ok = true;
                    page.Status = result.Status;
                    page.ElapsedMs = result.ElapsedMs;
                    page.Length = result.Length;
                }
                else
                {
                    page = Page.ErrorPage(result.Url.Length > 0 ? result.Url : url, error.Length > 0 ? error : "Страница недоступна", result.StatusCode);
                    page.ApplyTheme(Ui.T);
                    page.Status = result.Status;
                    page.ElapsedMs = result.ElapsedMs;
                }

                try
                {
                    if (IsHandleCreated)
                        BeginInvoke(new MethodInvoker(delegate { FinishLoad(tab, page); }));
                }
                catch { }
            });
            worker.IsBackground = true;
            worker.Start();
        }


        private void FinishLoad(Tab tab, Page page)
        {
            if (ReferenceEquals(tab, Current))
            {
                tab.Page = page;
                tab.View.SetPage(page);
                Relayout(tab);
                SetLoading(false);
                UpdateChrome();
                if (!string.IsNullOrEmpty(page.Title)) { tab.Title = page.Title; SyncTabs(); }
                if (page.Ok && !tab.Private) History.Visit(tab.Url, page.Title);
                ShowMode("web");
            }
            ScheduleRepaint();
        }

        private void Relayout(Tab tab)
        {
            if (tab == null || tab.Page == null) return;
            if (tab.View.Width < 50) return;
            try
            {
                // NB: never reuse the Graphics handed to OnPaint. It is only valid for the
                // duration of that call, and MeasureString on a released one throws
                // ArgumentException. The view measures on its own offscreen surface.
                tab.View.RefreshLayout();
            }
            catch (Exception ex)
            {
                Debug.Log("layout failed: " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        

        private void ScheduleRepaint()
        {
            _pendingRender = 8;
            if (!_pendingTimer.Enabled) _pendingTimer.Start();
        }

        private void RepaintPages()
        {
            foreach (Tab tab in _tabs)
            {
                if (tab.Page == null || tab.View.Width < 50) continue;
                try
                {
                    tab.View.RefreshLayout();
                }
                catch { }
            }
        }

        private void SetLoading(bool loading)
        {
            Tab tab = Current;
            if (tab != null) tab.Loading = loading;
            if (loading) _loading.Start(); else _loading.Stop();
            if (_address != null) _address.SetLoading(loading);
            UpdateStatus();
        }

        private void OnLink(string target)
        {
            if (Current == null) return;
            if (target.StartsWith("about:")) { HandleSpecial(target, Current); return; }
            Navigate(target);
        }

        private void GoBack()
        {
            Tab tab = Current;
            if (tab == null || tab.Back.Count == 0) return;
            tab.Forward.Add(tab.Url);
            string target = tab.Back[tab.Back.Count - 1];
            tab.Back.RemoveAt(tab.Back.Count - 1);
            Navigate(target, false);
        }

        private void GoForward()
        {
            Tab tab = Current;
            if (tab == null || tab.Forward.Count == 0) return;
            tab.Back.Add(tab.Url);
            string target = tab.Forward[tab.Forward.Count - 1];
            tab.Forward.RemoveAt(tab.Forward.Count - 1);
            Navigate(target, false);
        }

        private void ReloadCurrent()
        {
            Tab tab = Current;
            if (tab == null) return;
            if (Settings.EngineMode == 1 && tab.System.Available) { tab.System.Reload(); SetLoading(true); return; }
            if (Settings.EngineMode == 2 && tab.Gecko.Available) { tab.Gecko.Reload(); SetLoading(true); return; }
            Navigate(tab.Url, false);
        }

        private void ShowHome()
        {
            Navigate(HomeUrl());
        }

        private void SubmitAddress()
        {
            string text = _address.Text2.Trim();
            if (text.Length == 0) return;
            bool wasSearch;
            string url = Engines.Resolve(text, out wasSearch);
            if (wasSearch)
            {
                Toast("Поиск: " + Engines.Default.Name);
                _address.SetText(url);
            }
            _address.SetText(url);
            Navigate(url);
        }

        private void ToggleBookmark()
        {
            Tab tab = Current;
            if (tab == null || tab.Url.Length == 0) return;
            if (tab.Url.StartsWith("about:")) { Toast("Эту страницу нельзя добавить в избранное"); return; }
            if (Bookmarks.Has(tab.Url))
            {
                Bookmarks.Remove(tab.Url);
                Toast("Удалено из избранного");
            }
            else
            {
                Bookmarks.Add(tab.Url, tab.Page != null ? tab.Page.Title : tab.Title);
                Toast("Добавлено в избранное");
            }
        }

        // ─── special pages ─────────────────────────────────────────
        private bool HandleSpecial(string url, Tab tab)
        {
            if (!url.StartsWith("about:")) return false;

            string name = url.Substring(6).ToLowerInvariant();

            // The Firefox engine draws the page itself, so it has no use for our
            // new-tab screen: a fresh tab goes to the home page, and that also
            // makes sure the browser is actually started on a cold launch.
            if (Settings.EngineMode == 2 && (name == "blank" || name == "newtab"))
            {
                Navigate(HomeUrl());
                return true;
            }

            switch (name)
            {
                case "blank":
                    tab.Page = null;
                    tab.View.SetPage(null);
                    tab.Url = "";
                    tab.Title = "Новая вкладка";
                    SyncTabs();
                    ShowMode("web");
                    return true;
                case "newtab":
                    tab.Page = null;
                    tab.View.SetPage(null);
                    tab.Url = "";
                    tab.Title = "Новая вкладка";
                    SyncTabs();
                    ShowMode("web");
                    UpdateChrome();
                    return true;
                case "home":
                    ShowMode("web");
                    Navigate(HomeUrl());
                    return true;
                case "bookmarks": ShowBookmarks(); return true;
                case "history": ShowHistory(); return true;
                case "downloads": ShowDownloads(); return true;
                case "settings": ShowSettings(); return true;
                case "retry": Navigate(tab.Url, false); return true;
                default:
                    Toast("Неизвестная служебная страница: " + name);
                    return true;
            }
        }

        private void ShowMode(string mode)
        {
            _mode = mode;
            Tab tab = Current;

            foreach (Tab t in _tabs) t.Surface.Visible = false;
            _listPage.Visible = mode == "bookmarks" || mode == "history" || mode == "downloads";
            _settingsPage.Visible = mode == "settings";
            if (tab != null && (mode == "web")) tab.Surface.Visible = true;

            if (mode == "bookmarks") FillBookmarks();
            else if (mode == "history") FillHistory();
            else if (mode == "downloads") FillDownloads();
            else if (mode == "settings") BuildSettings();

            _sidebar.SetActive(mode == "web" ? "" : mode);
            LayoutChrome();
        }

        private void OnSidebarPage(string page)
        {
            if (page.Length == 0) return;
            if (page == "bookmarks") { ShowBookmarks(); return; }
            if (page == "history") { ShowHistory(); return; }
            if (page == "downloads") { ShowDownloads(); return; }
            if (page == "settings") { ShowSettings(); return; }
            Navigate(page);
        }

        public void DebugNavigate(string url) { Navigate(url); }

        public void DebugShowSettings() { ShowSettings(); }

        public void DebugShowBookmarks() { ShowBookmarks(); }

        public void DebugShowHistory() { ShowHistory(); }

        public void DebugShowDownloads() { ShowDownloads(); }

    private void ShowBookmarks()
        {
            ShowMode("bookmarks");
            _status.TextLeft = "Избранное";
            _status.TextMiddle = Bookmarks.Items.Count + " записей";
            _status.Invalidate();
        }

        private void ShowHistory()
        {
            ShowMode("history");
            _status.TextLeft = "История";
            _status.TextMiddle = History.Items.Count + " записей";
            _status.Invalidate();
        }

        private void ShowDownloads()
        {
            ShowMode("downloads");
            _status.TextLeft = "Загрузки";
            _status.TextMiddle = Downloads.Items.Count + " файлов";
            _status.Invalidate();
        }

        private void ShowSettings()
        {
            ShowMode("settings");
            _status.TextLeft = "Настройки";
            _status.TextMiddle = "";
            _status.Invalidate();
        }

        private void FillBookmarks()
        {
            var items = new List<ListItemData>();
            foreach (Bookmark b in Bookmarks.Items)
            {
                var item = new ListItemData();
                item.Title = b.Display;
                item.Subtitle = Engines.PrettyUrl(b.Url);
                item.Trailing = Format.Time(b.Added);
                item.Icon = "star";
                item.Payload = b.Url;
                items.Add(item);
            }
            _listPage.SetItems(items);
        }

        private void FillHistory()
        {
            var items = new List<ListItemData>();
            int limit = Math.Min(History.Items.Count, 300);
            for (int i = 0; i < limit; i++)
            {
                HistoryEntry e = History.Items[i];
                var item = new ListItemData();
                item.Title = e.Display;
                item.Subtitle = Engines.PrettyUrl(e.Url);
                item.Trailing = Format.Time(e.Visited);
                item.Icon = "clock";
                item.Payload = e.Url;
                items.Add(item);
            }
            _listPage.SetItems(items);
        }

        private void FillDownloads()
        {
            var items = new List<ListItemData>();
            foreach (DownloadItem d in Downloads.Items)
            {
                var item = new ListItemData();
                item.Title = d.FileName;
                item.Subtitle = Engines.PrettyUrl(d.Url);
                item.Trailing = d.Done ? Format.Bytes(d.Bytes) : d.Status;
                item.TrailingColor = d.Failed ? Ui.T.Error.Tone(Ui.T.Dark ? 70 : 40) : Color.Empty;
                item.Icon = d.Failed ? "warning" : (d.Done ? "check" : "download");
                item.Payload = d.Url;
                if (!d.Done && !d.Failed) item.Progress = d.Percent / 100.0;
                items.Add(item);
            }
            _listPage.SetItems(items);
        }

        private void OnListActivated(string payload)
        {
            if (_mode == "downloads") { StartDownload(payload); return; }
            Navigate(payload);
        }

        private void ShowListMenu(ListPage.ContextEventArgs e)
        {
            var items = new List<MenuItem>();
            if (_mode == "bookmarks")
            {
                items.Add(Menus.Item("Открыть", "open-in", delegate { Navigate(e.Item.Payload); }));
                items.Add(Menus.Item("Открыть в новой вкладке", "plus-box", delegate { OpenTab(e.Item.Payload, Current != null && Current.Private); }));
                items.Add(Menus.Sep());
                items.Add(Menus.Danger("Удалить", "trash", delegate { Bookmarks.Remove(e.Item.Payload); FillBookmarks(); }));
            }
            else if (_mode == "history")
            {
                items.Add(Menus.Item("Открыть", "open-in", delegate { Navigate(e.Item.Payload); }));
                items.Add(Menus.Item("В избранное", "star", delegate { Bookmarks.Add(e.Item.Payload, e.Item.Title); }));
                items.Add(Menus.Sep());
                items.Add(Menus.Danger("Удалить из истории", "trash", delegate { History.Remove(e.Item.Payload); FillHistory(); }));
            }
            else
            {
                items.Add(Menus.Item("Открыть папку", "folder", delegate
                {
                    try { Native.ShellExecute(Handle, "explore", Path.GetDirectoryName(LocalOf(e.Item.Payload)), null, null, 3); } catch { }
                }));
                items.Add(Menus.Danger("Убрать из списка", "close", delegate { RemoveDownloadByUrl(e.Item.Payload); }));
            }
            MenuHost.Show(this, items, PointToScreen(e.Location));
        }

        static string LocalOf(string url)
        {
            try
            {
                string name = Engines.PrettyUrl(url);
                string candidate = Path.Combine(SettingsFile.DownloadFolder, name);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
            return SettingsFile.DownloadFolder;
        }

        void RemoveDownloadByUrl(string url)
        {
            foreach (DownloadItem d in new List<DownloadItem>(Downloads.Items))
                if (d.Url == url) Downloads.Remove(d);
            FillDownloads();
        }

        // ─── downloads ─────────────────────────────────────────────
        public void StartDownload(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            string name = GuessFileName(url);
            DownloadItem item = Downloads.Add(url, name);
            Tab tab = Current;
            CookieContainer cookies = tab != null ? tab.Cookies : null;

            var worker = new Thread(delegate ()
            {
                try { Http.Download(url, item, cookies); }
                catch (Exception ex)
                {
                    item.Failed = true;
                    item.Status = "ошибка: " + ex.Message;
                }
                try
                {
                    if (IsHandleCreated)
                        BeginInvoke(new MethodInvoker(delegate { Toast(item.Done ? "Загрузка завершена: " + item.FileName : item.Status); }));
                }
                catch { }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        static string GuessFileName(string url)
        {
            try
            {
                string path = new Uri(url).AbsolutePath;
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) return "download";
                return Downloads.Sanitize(Uri.UnescapeDataString(name));
            }
            catch { return "download"; }
        }

        // ─── settings page ─────────────────────────────────────────
        private void BuildSettings()
        {
            _settingsPage.Clear();

            _settingsPage.AddHeader("Настройки", "Поисковая система, оформление, движок и данные");

            // ── Поиск ──────────────────────────────────────────────
            _settingsPage.AddCard("Поисковая система");
            _settingsPage.AddRow(new ChoiceRow("Поисковая система по умолчанию",
                "Используется, когда в адресной строке введён запрос, а не адрес",
                "search", Engines.Default.Name, delegate { ShowSearchEngineMenu(); }));

            var names = new List<string>();
            foreach (SearchEngineInfo e in Engines.All) names.Add(e.Name);
            _settingsPage.AddRow(new ChoiceRow("Домашняя страница", Engines.PrettyUrl(Settings.HomePage), "home",
                delegate { EditHomePage(); }));
            _settingsPage.AddRow(new TextRowProxy("Строка поиска", "Подсказка в адресной строке", "text",
                Engines.Default.Build("пример запроса"), delegate { }, true));

            // ── Оформление ─────────────────────────────────────────
            _settingsPage.AddCard("Оформление");
            _settingsPage.AddRow(new ChoiceRow("Тема", "Светлая, тёмная или как в Windows", "paint",
                ThemeName(Settings.ThemeMode), delegate { ShowThemeMenu(); }));
            _settingsPage.AddRow(new SwitchRow("Следовать за системным акцентом",
                "Цвет берётся из настроек Windows", "palette", AppTheme.FollowSystem,
                delegate (bool value)
                {
                    AppTheme.SetSource(AppTheme.CustomSource, value);
                    Settings.Save();
                    RepaintPages();
                }));
            _settingsPage.AddRow(new ChoiceRow("Свой цвет", "Используется, если следовать за системой выключено", "sparkles",
                "#" + AppTheme.CustomSource.R.ToString("X2") + AppTheme.CustomSource.G.ToString("X2") + AppTexture(), delegate { ShowAccentPicker(); }));
            _settingsPage.AddRow(new SliderRow("Размер шрифта страниц", "Масштаб встроенного движка", "text", 60, 200, 10, Settings.FontScale,
                delegate (int value)
                {
                    Settings.FontScale = value;
                    Settings.Save();
                    RepaintPages();
                }));

            // ── Движок ─────────────────────────────────────────────
            _settingsPage.AddCard("Движок страниц");
            _settingsPage.AddRow(new ChoiceRow("Движок по умолчанию", EngineLabel(),
                "globe", EngineShortName(), delegate { ShowEngineMenu(); }));
            _settingsPage.AddRow(new SwitchRow("Загружать изображения", "Не загружать при медленном интернете", "image", Settings.LoadImages,
                delegate (bool value) { Settings.LoadImages = value; Settings.Save(); ReloadCurrent(); }));
            _settingsPage.AddRow(new SwitchRow("Показывать рамки вместо изображений",
                "Если картинка не загрузилась, показывать подпись alt", "image", Settings.ShowImagesBroken,
                delegate (bool value) { Settings.ShowImagesBroken = value; Settings.Save(); RepaintPages(); }));
            _settingsPage.AddRow(new SliderRow("Лимит изображений на страницу", "Ограничение нагрузки", "download", 0, 200, 10, Settings.MaxImages,
                delegate (int value) { Settings.MaxImages = value; Settings.Save(); }));
            _settingsPage.AddRow(new ChoiceRow("User-Agent", "Заголовок при запросах", "cast",
                Settings.UserAgent.Length > 0 ? Settings.UserAgent : "Material Browser", delegate { ShowUserAgentMenu(); }));

            // ── Интерфейс ──────────────────────────────────────────
            _settingsPage.AddCard("Интерфейс");
            _settingsPage.AddRow(new SwitchRow("Панель закладок", "Показывать под адресной строкой", "star", Settings.ShowBookmarksBar,
                delegate (bool value) { Settings.ShowBookmarksBar = value; Settings.Save(); LayoutChrome(); }));
            _settingsPage.AddRow(new SwitchRow("Строка состояния", "Ссылка под курсором и статус загрузки", "info", Settings.ShowStatusBar,
                delegate (bool value) { Settings.ShowStatusBar = value; Settings.Save(); LayoutChrome(); }));
            _settingsPage.AddRow(new SwitchRow("Боковая панель", "Избранное, история, загрузки и настройки", "grid", _sidebar.Visible,
                delegate (bool value) { _sidebar.Visible = value; LayoutChrome(); }));
            _settingsPage.AddRow(new SwitchRow("Восстанавливать вкладки при запуске", "Открывать прошлую сессию", "refresh", Settings.RestoreSession,
                delegate (bool value) { Settings.RestoreSession = value; Settings.Save(); }));

            // ── Данные ─────────────────────────────────────────────
            _settingsPage.AddCard("Данные");
            _settingsPage.AddRow(new ActionRow("Очистить историю", "Удалить все посещённые страницы", "clock",
                delegate { History.Clear(); Toast("История очищена"); }, true));
            _settingsPage.AddRow(new ActionRow("Очистить закладки", "Удалить все закладки", "trash",
                delegate { Bookmarks.Clear(); Toast("Закладки очищены"); }, true));
            _settingsPage.AddRow(new ActionRow("Очистить кеш изображений", "Освободить память", "memory",
                delegate { Images.Clear(); Toast("Кеш очищен"); }));
            _settingsPage.AddRow(new ActionRow("Сбросить настройки", "Вернуть значения по умолчанию", "refresh",
                delegate
                {
                    Settings.SearchEngine = "Google";
                    Settings.EngineMode = 0;
                    Settings.FontScale = 100;
                    Settings.ThemeMode = -1;
                    Settings.ShowBookmarksBar = true;
                    Settings.ShowStatusBar = true;
                    Settings.LoadImages = true;
                    Settings.ShowImagesBroken = true;
                    Settings.MaxImages = 60;
                    Settings.UserAgent = "";
                    Settings.Save();
                    BuildSettings();
                    Relayout(Current);
                    Toast("Настройки сброшены");
                }, true));

            _settingsPage.LayoutCards();
        }

        static string EngineLabel()
        {
            if (Settings.EngineMode == 2) return "Firefox · " + GeckoView.Describe();
            if (Settings.EngineMode == 1) return "Системный WebBrowser (Windows)";
            return "Встроенный (без JavaScript)";
        }

        static string EngineShortName()
        {
            if (Settings.EngineMode == 2) return "Firefox";
            if (Settings.EngineMode == 1) return "Системный";
            return "Встроенный";
        }

        static string AppTexture()
        {
            return AppTheme.CustomSource.B.ToString("X2");
        }

        static string ThemeName(int mode)
        {
            if (mode == 0) return "Светлая";
            if (mode == 1) return "Тёмная";
            return "Как в Windows";
        }

        private void ShowSearchEngineMenu()
        {
            var items = new List<MenuItem>();
            foreach (SearchEngineInfo engine in Engines.All)
            {
                SearchEngineInfo captured = engine;
                items.Add(Menus.Check(engine.Name, engine.Host, Settings.SearchEngine == engine.Name, delegate
                {
                    Settings.SearchEngine = captured.Name;
                    Settings.Save();
                    BuildSettings();
                    Toast("Поисковая система: " + captured.Name);
                }));
            }
            MenuHost.Show(this, items, PointToScreen(new Point(_settingsPage.Left + Ui.PX(60), _settingsPage.Top + Ui.PX(60))));
        }

        private void ShowThemeMenu()
        {
            var items = new List<MenuItem>();
            items.Add(Menus.Check("Как в Windows", "monitor", Settings.ThemeMode == -1, delegate { SetTheme(-1); }));
            items.Add(Menus.Check("Светлая", "sun", Settings.ThemeMode == 0, delegate { SetTheme(0); }));
            items.Add(Menus.Check("Тёмная", "moon", Settings.ThemeMode == 1, delegate { SetTheme(1); }));
            MenuHost.Show(this, items, PointToScreen(new Point(_settingsPage.Left + Ui.PX(60), _settingsPage.Top + Ui.PX(60))));
        }

        private void SetTheme(int mode)
        {
            Settings.ThemeMode = mode;
            Settings.Save();
            if (mode == -1) { AppTheme.DarkOverride = null; AppTheme.Build(); AppTheme.Raise(); }
            else AppTheme.SetDark(mode == 1);
            BuildSettings();
            RepaintPages();
        }

        private void ShowEngineMenu()
        {
            var items = new List<MenuItem>();
            items.Add(Menus.Check("Встроенный", "sparkles", Settings.EngineMode == 0, delegate
            {
                Settings.EngineMode = 0; Settings.Save(); BuildSettings(); ReloadCurrent();
            }));
            items.Add(Menus.Check("Системный WebBrowser", "monitor", Settings.EngineMode == 1, delegate
            {
                Settings.EngineMode = 1; Settings.Save(); BuildSettings(); ReloadCurrent();
            }));
            items.Add(Menus.Check("Firefox (Gecko)", "globe", Settings.EngineMode == 2, delegate
            {
                Settings.EngineMode = 2; Settings.Save(); BuildSettings(); ReloadCurrent();
            }));
            MenuHost.Show(this, items, PointToScreen(new Point(_settingsPage.Left + Ui.PX(60), _settingsPage.Top + Ui.PX(60))));
        }

        private void ShowUserAgentMenu()
        {
            var items = new List<MenuItem>();
            for (int i = 0; i < Settings.UserAgents.Length; i++)
            {
                int index = i;
                items.Add(Menus.Check(Settings.UserAgentNames[i], "cast", Settings.UserAgent == Settings.UserAgents[i], delegate
                {
                    Settings.UserAgent = Settings.UserAgents[index];
                    Settings.Save();
                    BuildSettings();
                }));
            }
            MenuHost.Show(this, items, PointToScreen(new Point(_settingsPage.Left + Ui.PX(60), _settingsPage.Top + Ui.PX(60))));
        }

        private void EditHomePage()
        {
            using (var dialog = new InputDialog("Домашняя страница", "Открывается при запуске и по кнопке «Домой»",
                Settings.HomePage, delegate (string text) { return null; }))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Settings.HomePage = dialog.Result1;
                Settings.Save();
                BuildSettings();
            }
        }

        private void ShowAccentPicker()
        {
            var menu = new List<MenuItem>();
            Color[] colors =
            {
                Color.FromArgb(103, 80, 164), Color.FromArgb(0, 105, 92), Color.FromArgb(210, 78, 78),
                Color.FromArgb(200, 120, 30), Color.FromArgb(60, 110, 200), Color.FromArgb(150, 60, 160),
                Color.FromArgb(30, 140, 160), Color.FromArgb(90, 110, 40)
            };
            foreach (Color color in colors)
            {
                Color captured = color;
                string label = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
                menu.Add(Menus.Item(label, "sparkles", delegate
                {
                    AppTheme.SetSource(captured, false);
                    Settings.Save();
                    BuildSettings();
                    RepaintPages();
                }));
            }
            MenuHost.Show(this, menu, PointToScreen(new Point(_settingsPage.Left + Ui.PX(60), _settingsPage.Top + Ui.PX(60))));
        }

        /// <summary>Read-only row used to show a sample of the current search URL.</summary>
        public class TextRowProxy : SettingsRow
        {
            public TextRowProxy(string title, string subtitle, string icon, string value, Action onClick, bool readOnly)
                : base()
            {
                Title = title; Subtitle = subtitle; Icon = icon;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Scheme s = Ui.T;
                using (var brush = new SolidBrush(s.OnSurfaceVariant))
                    e.Graphics.DrawString(Subtitle, Ui.Caption, brush,
                        new Rectangle(Ui.PX(52), Height - Ui.PX(26), Width - Ui.PX(80), Ui.PX(20)));
            }
        }

        // ─── main menu ─────────────────────────────────────────────
        private void ShowMainMenu(Point below)
        {
            var items = new List<MenuItem>();
            items.Add(Menus.Item("Новая вкладка", "plus-box", delegate { OpenTab(HomeUrl(), false); }, "Ctrl+T"));
            items.Add(Menus.Item("Новая приватная вкладка", "shield", delegate { OpenTab(HomeUrl(), true); }));
            items.Add(Menus.Sep());
            items.Add(Menus.Item("Избранное", "star", delegate { ShowBookmarks(); }));
            items.Add(Menus.Item("История", "clock", delegate { ShowHistory(); }));
            items.Add(Menus.Item("Загрузки", "download", delegate { ShowDownloads(); }));
            items.Add(Menus.Item("Загрузить файл по ссылке", "save", delegate
            {
                using (var dialog = new InputDialog("Загрузка", "Ссылка на файл", "https://", null))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) StartDownload(dialog.Result1);
                }
            }));
            items.Add(Menus.Sep());
            items.Add(Menus.Item("Настройки", "tune", delegate { ShowSettings(); }));
            items.Add(Menus.Check("Загружать изображения", "image", Settings.LoadImages, delegate
            {
                Settings.LoadImages = !Settings.LoadImages;
                Settings.Save();
                ReloadCurrent();
            }));
            items.Add(Menus.Sep());
            items.Add(Menus.Danger("Закрыть вкладку", "close", delegate { if (_tabs.Count > 1) CloseTab(_active); }, "Ctrl+W"));

            Point screen = PointToScreen(new Point(_menu.Right, _menu.Bottom));
            MenuHost.Show(this, items, screen);
        }

        // ─── session ───────────────────────────────────────────────
        private void RestoreSession()
        {
            List<string> urls = new List<string>();
            if (Settings.RestoreSession)
                foreach (string[] row in SettingsFile.ReadRows(SettingsFile.SessionFile))
                    if (row.Length > 0 && row[0].Length > 0) urls.Add(row[0]);

            if (urls.Count == 0) urls.Add("about:newtab");

            int count = Math.Min(urls.Count, SettingsFile.MaxPages);
            for (int i = 0; i < count; i++)
            {
                CreateTab(urls[i], false);
            }
            _active = 0;
            ShowActiveSurface();
            SyncTabs();
            OnTabActivated();

            if (urls[0].StartsWith("about:")) HandleSpecial(urls[0], Current);
            else Navigate(urls[0], false);
        }

        private void SaveSession()
        {
            var rows = new List<string[]>();
            foreach (Tab t in _tabs)
            {
                if (t.Url.Length > 0) rows.Add(new[] { t.Url });
            }
            SettingsFile.WriteRows(SettingsFile.SessionFile, rows);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSession();
            Settings.Save();
            History.Save();
            Downloads.Save();
            try { GeckoDownloads.Stop(); } catch { }
            try { GeckoEngine.ReleaseShared(); } catch { }
            base.OnFormClosing(e);
        }

        // ─── chrome ────────────────────────────────────────────────
        private void UpdateChrome()
        {
            Tab tab = Current;
            if (tab == null) return;

            bool builtin = !(Settings.EngineMode == 1 && tab.System.Available) &&
                           !(Settings.EngineMode == 2 && tab.Gecko.Available);
            if (_address != null)
            {
                string liveUrl = tab.Url;
            if (!builtin)
            {
                string engineUrl = Settings.EngineMode == 2 ? tab.Gecko.CurrentUrl : tab.System.CurrentUrl;
                if (engineUrl.Length > 0) liveUrl = engineUrl;
            }
            _address.SetText(liveUrl);
                _address.SetStarred(Bookmarks.Has(tab.Url));
                _address.SetPrivate(tab.Private);
                _address.SetPlaceholder(Engines.Default.Name.Length > 0 ? "Поиск в " + Engines.Default.Name : "Поиск или адрес");
                if (_mode != "web") _address.SetText("");
            }

            _back.Enabled = builtin ? tab.Back.Count > 0 : tab.System.CanGoBack;
            _forward.Enabled = builtin ? tab.Forward.Count > 0 : tab.System.CanGoForward;
            _reload.Enabled = tab.Url.Length > 0 || tab.System.Available;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_status == null) return;
            Tab tab = Current;
            string text = "";
            if (_mode == "web" && tab != null)
            {
                PageView view = tab.View;
                if (view.Visible && view.HoverUrl.Length > 0) text = Engines.PrettyUrl(view.HoverUrl);
                else if (view.Visible && tab.Page != null)
                {
                    string meta = tab.Page.Status;
                    if (tab.Page.ElapsedMs > 0) meta += "   ·   " + tab.Page.ElapsedMs + " мс";
                    if (tab.Page.LoadedImages > 0) meta += "   ·   изображений: " + tab.Page.LoadedImages;
                    text = meta;
                }
                else if (tab.System != null && tab.System.Visible) text = tab.System.CurrentUrl;
            }
            _status.TextLeft = _mode == "web" && tab != null && tab.Url.Length > 0 ? Engines.PrettyUrl(tab.Url) : "";
            _status.TextMiddle = text;
            _status.TextRight = tab != null && tab.Private ? "приватная вкладка"
                : Settings.EngineMode == 2 ? "движок Firefox"
                : Settings.EngineMode == 1 ? "системный движок" : "встроенный движок";
            _status.Invalidate();
        }

        private void Toast(string message)
        {
            if (_snackbar != null) _snackbar.Show(message);
        }

        // ─── layout ────────────────────────────────────────────────
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState != FormWindowState.Maximized && !_maximized && Width > 200) _restoreBounds = Bounds;
            LayoutChrome();
        }

        private void LayoutChrome()
        {
            if (_tabsStrip == null || _address == null) return;

            int w = ClientSize.Width, h = ClientSize.Height;
            bool narrow = w < Ui.PX(1000);

            int btn = Ui.PX(38);
            int navY = (TopBarHeight - btn) / 2;
            int bx = Ui.PX(10);
            _sidebarToggle.SetBounds(bx, navY, btn, btn); bx += btn + Ui.PX(2);
            _back.SetBounds(bx, navY, btn, btn); bx += btn + Ui.PX(2);
            _forward.SetBounds(bx, navY, btn, btn); bx += btn + Ui.PX(2);
            _reload.SetBounds(bx, navY, btn, btn); bx += btn + Ui.PX(2);
            _home.SetBounds(bx, navY, btn, btn); bx += btn + Ui.PX(12);

            int winY = (TopBarHeight - Ui.PX(36)) / 2;
            int wx = w - Ui.PX(8);
            wx -= _close.Width; _close.Location = new Point(wx, winY);
            wx -= _maximize.Width; _maximize.Location = new Point(wx, winY);
            wx -= _minimize.Width; _minimize.Location = new Point(wx, winY);

            int tabLeft = bx;
            _tabsStrip.SetBounds(tabLeft, Ui.PX(8), Math.Max(Ui.PX(80), wx - Ui.PX(10) - tabLeft), TopBarHeight - Ui.PX(16));

            int contentLeft = 0;
            int toolbarY = TopBarHeight + Ui.PX(10);
            int tb = Ui.PX(36);
            _menu.SetBounds(w - tb - Ui.PX(14), toolbarY, tb, tb);

            int addressRight = w - tb - Ui.PX(24);
            int addressLeft = Ui.PX(14);
            if (!narrow) addressLeft = _railSafeLeft();
            _address.SetBounds(addressLeft, toolbarY - Ui.PX(5), Math.Max(Ui.PX(200), addressRight - addressLeft), Ui.PX(46));

            int y = TopBarHeight + ToolbarHeight;
            int bookmarkBarHeight = Settings.ShowBookmarksBar ? Ui.PX(36) : 0;
            _bookmarksBar.Visible = Settings.ShowBookmarksBar;
            _bookmarksBar.SetBounds(0, y, w, bookmarkBarHeight);
            if (Settings.ShowBookmarksBar) y += bookmarkBarHeight;

            _loading.SetBounds(0, y - Ui.PX(3), w, Ui.PX(3));

            int statusHeight = Settings.ShowStatusBar ? StatusHeight : 0;
            _status.Visible = Settings.ShowStatusBar;
            _status.SetBounds(0, h - statusHeight, w, statusHeight);

            _sidebar.SetBounds(0, TopBarHeight, _sidebar.Visible ? _sidebarWidth : 0, h - TopBarHeight);
            if (_sidebar.Visible) _sidebar.LayoutEntries();
            contentLeft = _sidebar.Visible ? _sidebarWidth : 0;

            int contentTop = y;
            int contentHeight = Math.Max(Ui.PX(60), h - contentTop - statusHeight);
            int contentWidth = w - contentLeft;

            _listPage.SetBounds(contentLeft + Ui.PX(8), contentTop + Ui.PX(8), contentWidth - Ui.PX(16), contentHeight - Ui.PX(16));
            _settingsPage.SetBounds(contentLeft, contentTop, contentWidth, contentHeight);

            Tab tab = Current;
            if (tab != null && tab.Surface != null)
            {
                tab.Surface.SetBounds(contentLeft, contentTop, contentWidth, contentHeight);
                if (tab.Page != null) Relayout(tab);
            }

            int snackW = Math.Min(Ui.PX(520), contentWidth - Ui.PX(60));
            _snackbar.SetBounds(contentLeft + (contentWidth - snackW) / 2, h - statusHeight - Ui.PX(56), snackW, Ui.PX(46));

            if (_settingsPage.Visible) { _settingsPage.LayoutCards(); _settingsPage.Invalidate(); }
        }

        private int _railSafeLeft()
        {
            return Ui.PX(14);
        }

        // ─── theming ───────────────────────────────────────────────
        private void ApplyScheme()
        {
            Scheme s = Ui.T;
            BackColor = s.Surface;
            if (_address == null) return;

            if (!IsHandleCreated) return;
            Native.SetImmersiveDarkMode(Handle, s.Dark);
            _backdrop = !DisableBackdrop && Native.ApplyBackdrop(Handle, s.Dark,
                Scheme.WithAlpha(s.Dark ? Color.FromArgb(28, 28, 32) : Color.FromArgb(244, 246, 250), 235));
            // NB: never touch Form.Opacity. Assigning it - even 1.0 - adds WS_EX_LAYERED,
            // which makes the whole client area composite as blank on some Windows builds.
            Native.SetCornerPreference(Handle, true);
            if (!SysInfo.Cached.IsWindows11) Native.ApplyRegionRounding(Handle, Width, Height, Ui.PX(12));

            foreach (Tab tab in _tabs)
            {
                if (tab.Page != null)
                {
                    tab.Page.ApplyTheme(s);
                    if (ReferenceEquals(tab, Current)) Relayout(tab);
                }
                if (tab.System != null) tab.System.BackColor = s.Surface;
            }
            Invalidate();
            LayoutChrome();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (g != null) g.SmoothingMode = SmoothingMode.AntiAlias;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Scheme s = Ui.T;
            var rect = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);

            if (_backdrop)
            {
                using (var brush = new LinearGradientBrush(rect,
                    Scheme.WithAlpha(Ui.Hover(s.Surface, s.Dark, 0.03), 210),
                    Scheme.WithAlpha(Ui.Hover(s.Surface, s.Dark, -0.03), 205),
                    LinearGradientMode.Vertical))
                    g.FillRectangle(brush, rect);
            }
            else
            {
                using (var brush = new SolidBrush(s.Surface)) g.FillRectangle(brush, rect);
            }

            using (var brush = new SolidBrush(Scheme.WithAlpha(s.OutlineVariant, 120)))
                g.FillRectangle(brush, 0, TopBarHeight - Ui.PX(1), ClientSize.Width, Ui.PX(1));
            if (_sidebar.Visible)
                using (var brush = new SolidBrush(Scheme.WithAlpha(s.OutlineVariant, 90)))
                    g.FillRectangle(brush, _sidebarWidth - Ui.PX(1), TopBarHeight, Ui.PX(1), ClientSize.Height - TopBarHeight);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyScheme();
            LayoutChrome();
        }

        // ─── window behaviour ──────────────────────────────────────
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < TopBarHeight && IsDragArea(e.Location))
            {
                if (e.Clicks >= 2) { ToggleMaximize(); return; }
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
            }
        }

        /// <summary>
        /// The empty parts of the title bar move the window. Only the controls
        /// that do something of their own keep the click: buttons, the address
        /// bar and the tabs. GetChildAtPoint is not usable here, the tab strip
        /// covers nearly the whole bar.
        /// </summary>
        private bool IsDragArea(Point p)
        {
            if (p.Y < 0 || p.Y >= TopBarHeight) return false;

            foreach (Control c in new Control[]
            {
                _back, _forward, _reload, _home, _menu, _sidebarToggle, _maximize,
                _minimize, _close, _address,
            })
            {
                if (c == null || !c.Visible) continue;
                if (p.X >= c.Left && p.X < c.Right && p.Y >= c.Top && p.Y < c.Bottom) return false;
            }

            // The strip itself is chrome, but a tab on it still needs its click.
            if (_tabsStrip != null && _tabsStrip.Visible)
            {
                Point inside = new Point(p.X - _tabsStrip.Left, p.Y - _tabsStrip.Top);
                if (inside.X >= 0 && inside.X < _tabsStrip.Width &&
                    inside.Y >= 0 && inside.Y < _tabsStrip.Height && !_tabsStrip.IsOnTab(inside))
                {
                    return true;
                }
            }
            return true;
        }

        private void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                Bounds = _restoreBounds;
                _maximized = false;
                _maximize.IconName = "maximize";
            }
            else
            {
                _restoreBounds = Bounds;
                WindowState = FormWindowState.Maximized;
                _maximized = true;
                _maximize.IconName = "restore";
            }
            LayoutChrome();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x031E)
            {
                base.WndProc(ref m);
                try { AppTheme.RefreshFromSystem(); } catch { }
                return;
            }
            if (m.Msg == 0x0084)
            {
                base.WndProc(ref m);
                Point p = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, (m.LParam.ToInt32() >> 16) & 0xFFFF));
                const int border = 6;
                int w = ClientSize.Width, h = ClientSize.Height;
                bool maximized = WindowState == FormWindowState.Maximized;
                int result = 0;
                if (!maximized)
                {
                    if (p.Y < border) result = 12;
                    else if (p.Y >= h - border) result = 6;
                    else if (p.X < border) result = 10;
                    else if (p.X >= w - border) result = 11;
                }
                if (result != 0) m.Result = new IntPtr(result);
                return;
            }
            base.WndProc(ref m);
        }

        // ─── keyboard ──────────────────────────────────────────────
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            bool alt = (ModifierKeys & Keys.Alt) == Keys.Alt;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            if (HandleShortcut(e.KeyCode, ctrl, alt, shift)) e.Handled = true;
        }

        /// <summary>
        /// The app's own keyboard shortcuts. They are also routed here from the
        /// Firefox engine: while a page has the keyboard, the browser window is
        /// the focus window and our form would never see the key at all. The
        /// modifiers come along with it, because the keys are then held in the
        /// browser's input queue rather than ours.
        /// </summary>
        private bool HandleShortcut(Keys key, bool ctrl, bool alt, bool shift)
        {
            if (alt && key == Keys.Left) { GoBack(); return true; }
            if (alt && key == Keys.Right) { GoForward(); return true; }
            if (alt && key == Keys.Home) { ShowHome(); return true; }

            if (!ctrl) return false;
            switch (key)
            {
                case Keys.T: OpenTab(Current != null && Current.Url.Length > 0 ? Current.Url : HomeUrl(), shift); return true;
                case Keys.N: OpenTab(HomeUrl(), shift); return true;
                case Keys.W: if (_tabs.Count > 1) CloseTab(_active); return true;
                case Keys.L: _address.FocusInput(); return true;
                case Keys.D: ToggleBookmark(); return true;
                case Keys.J: ShowDownloads(); return true;
                case Keys.B: ShowBookmarks(); return true;
                case Keys.H: ShowHistory(); return true;
                case Keys.I: ShowSettings(); return true;
                case Keys.R: ReloadCurrent(); return true;
                case Keys.F: _address.FocusInput(); return true;
            }
            return false;
        }

        private void OnEngineShortcut(object sender, GeckoShortcut shortcut)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                HandleShortcut(shortcut.Key, shortcut.Ctrl, shortcut.Alt, shortcut.Shift);
            });
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _mode != "web")
            {
                ShowMode("web");
                if (Current != null && Current.Url.Length > 0) Navigate(Current.Url, false);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pendingTimer.Stop();
                foreach (Tab tab in _tabs)
                {
                    if (tab.System != null) tab.System.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
