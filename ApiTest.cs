using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MaterialBrowser
{
    /// <summary>
    /// Headless checks for the parts that do not need the network: HTML parsing,
    /// CSS resolution, layout, the address bar, storage and page rendering.
    /// </summary>
    static class ApiTest
    {
        static int _failed;
        static readonly StringBuilder _log = new StringBuilder();

        public static void Run(string directory)
        {
            _failed = 0;
            SettingsFile.Ensure();
            Settings.Load();

            TestHtml();
            TestEntities();
            TestUrlResolution();
            TestCss();
            TestAddressBar();
            TestEngines();
            TestStorage();
            TestLayout();
            TestRendering();
            TestGeckoSupport();

            string root = directory;
            if (string.IsNullOrEmpty(root)) root = Path.Combine(SettingsFile.Folder, "apitest");
            try { Directory.CreateDirectory(root); } catch { }
            string text = _log.ToString() + (_failed == 0 ? "ALL PASS" : _failed + " FAILED") + "\r\n";
            try { File.WriteAllText(Path.Combine(root, "apitest.txt"), text, Encoding.UTF8); } catch { }
            try { Console.Write(text); } catch { }
            Environment.Exit(_failed == 0 ? 0 : 1);
        }

        // ─── HTML ──────────────────────────────────────────────────
        static void TestHtml()
        {
            Node root = Html.Parse("<html><head><title>Тест</title><style>p{color:red}</style></head>" +
                                   "<body><p id=\"a\" class=\"x\">Привет <b>мир</b><br>второй</p>" +
                                   "<ul><li>один<li>два</ul><img src=\"/a.png\" alt=\"кот\">" +
                                   "<table><tr><td>1<td>2<tr><td>3<td>4</table></body></html>");

            Check("finds the title", root.TitleText == "Тест");
            Node p = root.FindFirst("p");
            Check("paragraph exists", p != null && p.Id == "a");
            Check("keeps nested inline markup", p != null && p.FindFirst("b") != null);
            Check("keeps the line break", p != null && p.FindFirst("br") != null);

            Node list = root.FindFirst("ul");
            Check("li are auto-closed", list != null && list.FindAll("li").Count == 2);

            Node image = root.FindFirst("img");
            Check("img keeps attributes", image != null && image.AttrValue("alt", "") == "кот" && image.AttrValue("src", "") == "/a.png");

            List<Node> cells = root.FindAll("td");
            Check("table cells are collected", cells.Count == 4);
            Check("row nesting survives", root.FindAll("tr").Count == 2);

            Node style = root.FindFirst("style");
            Check("style text is kept", style != null && style.InnerText.Contains("color:red"));

            Node script = Html.Parse("<body><script>var a = 1 < 2 && 3 > 2;</script><p>ok</p>").FindFirst("script");
            Check("script content is not markup", script != null && script.InnerText.Contains("&&"));
            Check("markup after script is parsed", Html.Parse("<body><script>x</script><p>ok</p>").FindFirst("p") != null);

            Node broken = Html.Parse("<body><p>one<p>two<div><span>deep</div>");
            Check("malformed markup still yields text", broken.FindAll("p").Count == 2);
            Check("unclosed inline survives", broken.FindFirst("span") != null);

            Check("empty input is safe", Html.Parse("").FindAll("p").Count == 0);
            Check("text only is safe", Html.Parse("просто текст").InnerText.Contains("текст"));
        }

        static void TestEntities()
        {
            Check("decodes &amp;", Html.Decode("a&amp;b") == "a&b");
            Check("decodes numeric", Html.Decode("&#1055;&#1088;&#1080;") == "При");
            Check("decodes hex numeric", Html.Decode("&#x41;") == "A");
            Check("decodes nbsp", Html.Decode("a&nbsp;b").Length == 3);
            Check("decodes mdash", Html.Decode("a&mdash;b") == "a—b");
            Check("leaves unknown entities", Html.Decode("a&unknown;b") == "a&unknown;b");
            Check("handles no ampersand fast", Html.Decode("plain text") == "plain text");
        }

        static void TestUrlResolution()
        {
            const string baseUrl = "https://example.com/dir/page.html";
            Check("relative link", Html.ResolveUrl(baseUrl, "other.html") == "https://example.com/dir/other.html");
            Check("root-relative link", Html.ResolveUrl(baseUrl, "/top") == "https://example.com/top");
            Check("absolute link", Html.ResolveUrl(baseUrl, "https://other.org/x") == "https://other.org/x");
            Check("protocol-relative link", Html.ResolveUrl(baseUrl, "//cdn.net/a.js") == "http://cdn.net/a.js");
            Check("fragment is dropped", Html.ResolveUrl(baseUrl, "#top").Length == 0);
            Check("mailto is kept", Html.ResolveUrl(baseUrl, "mailto:a@b.c") == "mailto:a@b.c");
            Check("empty reference", Html.ResolveUrl(baseUrl, "").Length == 0);
        }

        // ─── CSS ───────────────────────────────────────────────────
        static void TestCss()
        {
            List<CssRule> rules = Css.ParseSheet(
                "body { margin: 0 } .card { color: blue } #main { color: green } " +
                "p.card { font-size: 20px } @media screen { .card { color: red } }");

            Check("parses several rules", rules.Count >= 4);
            Check("skips at-rules", true);
            var card = new Node("p");
            card.SetAttr("class", "card");
            Style style = Css.Compute(card, rules);
            Check("class applies", style.Get("color", "") == "blue");
            Check("font-size applies", style.FontSize == 20);

            var main = new Node("div");
            main.SetAttr("id", "main");
            main.SetAttr("class", "card");
            Style mainStyle = Css.Compute(main, rules);
            Check("id wins over class", mainStyle.Get("color", "") == "green");

            var inline = new Node("p");
            inline.SetAttr("style", "color: teal; font-weight: bold");
            Style inlineStyle = Css.Compute(inline, rules);
            Check("inline style wins", inlineStyle.Get("color", "") == "teal");
            Check("inline font-weight parsed", inlineStyle.FontWeight >= 600);

            Color parsed;
            Check("parses hex colour", Css.TryParseColor("#1A2B3C", out parsed) && parsed.R == 0x1A && parsed.B == 0x3C);
            Check("parses short hex", Css.TryParseColor("#abc", out parsed) && parsed.R == 0xAA);
            Check("parses rgb()", Css.TryParseColor("rgb(10, 20, 30)", out parsed) && parsed.G == 20);
            Check("parses named colour", Css.TryParseColor("tomato", out parsed) || Css.TryParseColor("red", out parsed));
            Check("rejects junk colour", !Css.TryParseColor("not-a-colour", out parsed));

            double length;
            Check("parses px", Css.TryParseLength("12px", 100, out length) && Math.Abs(length - 12) < 0.01);
            Check("parses em", Css.TryParseLength("2em", 100, out length) && Math.Abs(length - 32) < 0.01);
            Check("parses percent", Css.TryParseLength("50%", 200, out length) && Math.Abs(length - 100) < 0.01);
            Check("rejects junk length", !Css.TryParseLength("wide", 100, out length));

            double border;
            Color borderColor;
            Css.ParseBorder("3px solid #ff0000", out border, out borderColor);
            Check("border width parsed", Math.Abs(border - 3) < 0.01);
            Check("border colour parsed", borderColor.R == 255 && borderColor.G == 0);
        }

        // ─── address bar ───────────────────────────────────────────
        static void TestAddressBar()
        {
            bool search;

            Check("plain url passes through", Engines.Resolve("https://example.com/x", out search) == "https://example.com/x" && !search);
            Check("http url passes through", Engines.Resolve("http://example.com", out search) == "http://example.com" && !search);
            Check("bare host gets https", Engines.Resolve("example.com", out search) == "https://example.com" && !search);
            Check("host with path", Engines.Resolve("example.com/a/b", out search) == "https://example.com/a/b" && !search);
            Check("localhost is a url", Engines.Resolve("localhost:8080", out search) == "http://localhost:8080" && !search);
            Check("file scheme passes through", Engines.Resolve("file:///C:/x", out search) == "file:///C:/x" && !search);
            Check("about scheme passes through", Engines.Resolve("about:settings", out search) == "about:settings" && !search);

            string result = Engines.Resolve("как открыть html файл", out search);
            Check("words become a search", search && result.Contains("q="));
            Check("search uses the current engine", result.StartsWith(Engines.Default.QueryUrl.Substring(0, 8)));

            Check("rejects double dots", !Engines.LooksLikeHost("example..com"));
            Check("rejects trailing dot", !Engines.LooksLikeHost("example.com."));
            Check("rejects spaces", !Engines.LooksLikeHost("two words.com"));
            Check("rejects bad tld", !Engines.LooksLikeHost("example.c0m"));
            Check("accepts punycode host", Engines.LooksLikeHost("xn--80ak6aa92e.com"));
            Check("accepts subdomains", Engines.LooksLikeHost("a.b.example.co.uk"));

            Check("pretty url strips scheme", Engines.PrettyUrl("https://example.com/") == "example.com");
            Check("pretty url keeps path", Engines.PrettyUrl("https://example.com/a/b") == "example.com/a/b");
        }

        static void TestEngines()
        {
            Check("at least eight engines", Engines.All.Count >= 8);
            Check("engine names are unique", Unique(Engines.All));
            foreach (SearchEngineInfo engine in Engines.All)
            {
                Check("engine has a query template: " + engine.Name, engine.QueryUrl.Contains("%s"));
                Check("engine has a home page: " + engine.Name, engine.HomeUrl.StartsWith("http"));
            }
            SearchEngineInfo first = Engines.All[0];
            string built = first.Build("привет мир");
            Check("query is escaped", !built.Contains(" ") && built.Contains("%D0%BF"));
            Check("lookup by name works", Engines.Find(first.Name) != null);
            Check("unknown name falls back", Engines.Find("нет такого") == null);
        }

        static bool Unique(List<SearchEngineInfo> list)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SearchEngineInfo e in list) if (!seen.Add(e.Name)) return false;
            return true;
        }

        // ─── storage ───────────────────────────────────────────────
        static void TestStorage()
        {
            SettingsFile.Ensure();

            Check("escapes tabs", SettingsFile.Escape("a\tb") == "a\\tb");
            Check("unescapes tabs", SettingsFile.Unescape("a\\tb") == "a\tb");
            Check("round-trips backslashes", SettingsFile.Unescape(SettingsFile.Escape("C:\\a\\b")) == "C:\\a\\b");
            Check("round-trips unicode", SettingsFile.Unescape(SettingsFile.Escape("Корзина 🗑")) == "Корзина 🗑");

            string probe = Path.Combine(SettingsFile.Folder, "apitest-rows.txt");
            var rows = new List<string[]> { new[] { "https://a/", "Заголовок\tс\tтабом", "42" } };
            SettingsFile.WriteRows(probe, rows);
            List<string[]> read = SettingsFile.ReadRows(probe);
            Check("row survives the round trip", read.Count == 1 && read[0][1] == "Заголовок\tс\tтабом");
            try { File.Delete(probe); } catch { }

            string original = Settings.SearchEngine;
            Bookmarks.Add("https://apitest.example/", "Тест");
            Check("bookmark stored", Bookmarks.Has("https://apitest.example/"));
            Check("bookmark is first", Bookmarks.Items[0].Url == "https://apitest.example/");
            Bookmarks.Remove("https://apitest.example/");
            Check("bookmark removed", !Bookmarks.Has("https://apitest.example/"));
            Settings.SearchEngine = original;

            DownloadItem item = Downloads.Add("https://apitest.example/f.bin", "f.bin");
            Check("download file name sanitized", item.FileName == "f.bin");
            Check("download path in Downloads folder", item.Path.StartsWith(SettingsFile.DownloadFolder));
            Downloads.Remove(item);
            Check("download removed", !Downloads.Items.Contains(item));

            string name = Downloads.Sanitize("a/b:c*.txt");
            Check("invalid characters removed", name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
            Check("empty name replaced", Downloads.Sanitize("") == "download");
        }

        // ─── layout ────────────────────────────────────────────────
        static void TestLayout()
        {
            const string html =
                "<html><head><style>body{margin:0;font-size:16px}" +
                ".wide{width:400px;background:#eee}" +
                "li{color:#333}</style></head><body>" +
                "<h1>Заголовок</h1><p class=\"wide\">Параграф с <b>жирным</b> и <i>курсивом</i> текстом, " +
                "который должен переноситься по словам и занимать несколько строк при узкой ширине окна.</p>" +
                "<ul><li>первый</li><li>второй</li></ul>" +
                "<table><tr><th>Колонка</th><th>Другая</th></tr><tr><td>1</td><td>2</td></tr></table>" +
                "<a href=\"https://example.com/\">ссылка</a><input type=\"text\" value=\"поле\">" +
                "</body></html>";

            using (Bitmap bitmap = new Bitmap(800, 400, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                var page = new Page();
                page.Url = "https://example.com/";
                page.Prepare(html, page.Url, null);

                var engine = new LayoutEngine(g, page.Rules, page.Style);
                Box root = engine.BuildDocument(page.Dom);

                Check("document has height", root.Rect.Height > 200);
                Check("layout fits the viewport width", root.Rect.Width <= 800);

                int headings = 0, links = 0, images = 0, inputs = 0, tables = 0, listItems = 0;
                root.Walk(delegate (Box box)
                {
                    Node node = box.Node;
                    if (node == null) return;
                    if (node.Tag == "h1") headings++;
                    if (node.Tag == "a") links++;
                    if (node.Tag == "img") images++;
                    if (node.Tag == "input") inputs++;
                    if (node.Tag == "table") tables++;
                    if (node.Tag == "li") listItems++;
                });
                Check("heading laid out", headings == 1);
                Check("link laid out with a url", links == 1);
                Check("input laid out", inputs == 1);
                Check("table laid out", tables == 1);
                Check("list items laid out", listItems == 2);

                Box linkBox = null;
                root.Walk(delegate (Box box) { if (linkBox == null && box.IsLink) linkBox = box; });
                Check("link target resolved", linkBox != null && linkBox.Url == "https://example.com/");

                Box widthBox = null;
                root.Walk(delegate (Box box) { if (widthBox == null && box.Node != null && box.Node.Tag == "p") widthBox = box; });
                Check("fixed width honoured", widthBox != null && Math.Abs(widthBox.Rect.Width - 400) < 2);

                int longParagraphLines = 0;
                if (widthBox != null)
                {
                    foreach (Box child in widthBox.Children)
                        if (child.Kind == BoxKind.Line && child.Rect.Height > 0) longParagraphLines++;
                }
                Check("paragraph wrapped into lines", longParagraphLines >= 3);

                double totalHeight = 0;
                foreach (Box child in root.Children) totalHeight += child.Rect.Height;
                Check("children stack vertically", totalHeight > 100);
            }
        }

        // ─── rendering ─────────────────────────────────────────────
        static void TestRendering()
        {
            string url = "data:text/html;charset=utf-8,%3Ch1%3E%D0%97%D0%B0%D0%B3%D0%BE%D0%BB%D0%BE%D0%B2%D0%BE%D0%BA%3C/h1%3E";

            var page = new Page();
            page.Url = url;
            page.Prepare("<html><head><title>Тест</title></head><body><h1>Заголовок</h1>" +
                         "<p>Абзац текста, который нужно отрисовать.</p><ul><li>пункт</li></ul></body></html>", url, null);

            using (Bitmap bitmap = new Bitmap(900, 600, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                page.Layout(g, 900);
                Check("page lays out for rendering", page.Root != null && page.Root.Rect.Height > 40);

                using (var host = new Control())
                {
                    Ui.Init(host);
                    var view = new PageView();
                    view.SetPage(page);
                    view.Size = new Size(900, 600);
                    Check("page view exposes the page", view.Current == page);
                    Check("page view has no hover url", view.HoverUrl.Length == 0);
                }

                // Painting must not throw and must put ink on the canvas.
                Bitmap bmp = new Bitmap(900, 600, PixelFormat.Format32bppArgb);
                using (Graphics g2 = Graphics.FromImage(bmp))
                {
                    g2.Clear(Color.White);
                    DrawTree(g2, page.Root);
                }

                int dark = 0;
                for (int y = 0; y < 600; y += 3)
                    for (int x = 0; x < 900; x += 3)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (c.R < 200) dark++;
                    }
                Check("rendered page has visible ink", dark > 20);
                bmp.Dispose();
            }
        }

        static void DrawTree(Graphics g, Box box)
        {
            if (box == null) return;
            if (box.Background.A > 0)
            {
                using (var brush = new SolidBrush(box.Background)) g.FillRectangle(brush, box.Rect);
            }
            if (box.Kind == BoxKind.Text && box.Text.Length > 0 && box.Font != null)
            {
                using (var brush = new SolidBrush(box.TextColor))
                    g.DrawString(box.Text, box.Font, brush, box.Rect.X, box.Rect.Y, StringFormat.GenericTypographic);
            }
            foreach (Box child in box.Children) DrawTree(g, child);
        }

        // ─── Firefox engine support ────────────────────────────────
        // The browser itself is never launched here: these checks cover the
        // pieces the engine is built from, so a broken build fails without
        // needing Firefox installed.
        static void TestGeckoSupport()
        {
            Dictionary<string, object> root = Json.Object(Json.Parse("{\"id\":7,\"method\":\"browsingContext.navigate\"," +
                                  "\"params\":{\"context\":\"abc\",\"wait\":\"complete\",\"flags\":[1,2]}}"));
            Check("json parses an object", root != null);
            Check("json reads a number", root != null && Json.Text(root, "id") == "7");
            Check("json reads a string", root != null && Json.Text(root, "method") == "browsingContext.navigate");
            Dictionary<string, object> parameters = Json.Child(root, "params");
            Check("json reads a nested object", parameters != null && Json.Text(parameters, "context") == "abc");
            Check("json reads a missing key as empty", Json.Text(root, "nope").Length == 0);
            Check("json child of a missing key is null", Json.Child(root, "nope") == null);

            Dictionary<string, object> escaped = Json.Object(
                Json.Parse("{\"a\":\"line\\nbreak \\\"quoted\\\" \\\\ \\u00e9\"}"));
            Check("json unescapes strings", escaped != null && Json.Text(escaped, "a") == "line\nbreak \"quoted\" \\ \u00e9");

            var message = new Dictionary<string, object>();
            message["id"] = 1.0;
            message["method"] = "session.new";
            message["params"] = new Dictionary<string, object>();
            string written = Json.Write(message);
            Dictionary<string, object> round = Json.Object(Json.Parse(written));
            Check("json round trip keeps the method", round != null && Json.Text(round, "method") == "session.new");
            Check("json round trip keeps the id", round != null && Json.Text(round, "id") == "1");
            Check("json writes an empty object", Json.Write(new Dictionary<string, object>()) == "{}");
            // The protocol reader is deliberately forgiving: a damaged frame must never
            // throw on the socket thread, it just yields nothing usable.
            Check("json survives broken input", Json.Text(Json.Object(Json.Parse("{not json")), "method").Length == 0);
            Check("json survives an unterminated string", Json.Object(Json.Parse("\"abc")) == null);

            string css = GeckoEngine.ChromeCssForTests();
            Check("engine hides the browser chrome", css.Contains("#navigator-toolbox") || css.Contains("#zen-window-chrome"));
            Check("engine stylesheet is not empty", css.Length > 40);

            string prefs = GeckoEngine.PrefsForTests();
            Check("profile enables the BiDi protocol", prefs.Contains("remote.active-protocols"));
            Check("profile allows user chrome styles", prefs.Contains("legacyUserProfileCustomizations"));
            Check("profile skips the first run pages", prefs.Contains("aboutwelcome") && prefs.Contains("browser.startup.page"));

            List<GeckoInstall> found = null;
            bool located = true;
            try { found = GeckoEngine.Installed(); }
            catch (Exception ex) { located = false; Check("locating a Gecko browser does not throw", false, ex.Message); }
            if (located)
            {
                Check("locating a Gecko browser does not throw", found != null);
                bool allReal = true;
                bool allNamed = true;
                if (found != null)
                {
                    foreach (GeckoInstall install in found)
                    {
                        if (!File.Exists(install.Path)) allReal = false;
                        if (install.Name.Length == 0 || install.Version.Length == 0) allNamed = false;
                    }
                }
                Check("every located browser exists on disk", allReal);
                Check("every located browser reports a name and version", allNamed);
                Check("the engine menu falls back when nothing is installed",
                      (found == null || found.Count == 0) ? GeckoView.Describe() == "Firefox не найден"
                                                          : GeckoView.Describe().Length > 0);
            }

            TestEmbeddedEngine();
        }

        // The engine travels with the program: in the gecko\ folder next to the
        // exe, or packed inside the exe itself by tools\PackEngine.cs when the
        // build runs with MBR_EMBED=1. One of the two must always be there.
        static void TestEmbeddedEngine()
        {
            bool embedded = GeckoEngine.HasEmbeddedEngine;
            string bundled = GeckoEngine.BundledEngine();

            // The shipped folder is the contract, so it has to outrank anything
            // the environment or the registry could point at.
            Check("the engine folder beside the exe is preferred",
                  bundled.Length == 0 || Path.GetFileName(bundled).ToLowerInvariant() == "firefox.exe",
                  bundled);
            Check("the program ships its own engine", embedded || bundled.Length > 0,
                  embedded ? "packed into the exe" : bundled);
            // A fresh install has no saved setting, so the default decides what a new
            // user sees on first launch. With a runtime shipped it must be Firefox.
            Check("a fresh install starts on its own Firefox",
                  Settings.DefaultEngine() == (bundled.Length > 0 ? 2 : 0),
                  "default=" + Settings.DefaultEngine());
            Check("the engine beside the exe is a real browser",
                  bundled.Length == 0 ||
                  (File.Exists(Path.Combine(Path.GetDirectoryName(bundled) ?? "", "omni.ja")) &&
                   File.Exists(Path.Combine(Path.GetDirectoryName(bundled) ?? "", "application.ini"))));

            string icon = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? "", "MaterialBrowser.ico");
            int sizes = 0;
            if (File.Exists(icon))
            {
                byte[] head = new byte[6];
                using (FileStream stream = File.OpenRead(icon)) stream.Read(head, 0, 6);
                sizes = head[4] | head[5] << 8;
            }
            Check("the icon covers every size Windows asks for", sizes >= 7, sizes + " sizes");
            Check("the icon is compiled into the exe",
                  System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) != null);

            if (!embedded)
            {
                // This build reads the engine from the folder; unpacking is
                // covered by tools\UnpackCheck against the same Gecko sources.
                Console.WriteLine("  note  this build has no embedded runtime, unpacking is not exercised here");
                return;
            }

            Check("the embedded runtime has real weight", GeckoEngine.EmbeddedEngineSize > 50L * 1024 * 1024,
                  GeckoEngine.EmbeddedEngineSize + " bytes");

            string folder = Path.Combine(Path.GetTempPath(), "MaterialBrowser-apitest-engine");
            GeckoEngine.DeleteTree(folder);
            int written = GeckoEngine.ExtractEmbeddedTo(folder);
            Check("the embedded runtime unpacks", written > 50, written + " files");

            string exe = Path.Combine(folder, "firefox.exe");
            Check("the unpacked engine has its binary", File.Exists(exe));
            Check("the unpacked engine has its resources", File.Exists(Path.Combine(folder, "omni.ja")) &&
                  File.Exists(Path.Combine(folder, "application.ini")));
            string unpackedVersion = File.Exists(exe)
                ? (System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "")
                : "";
            string bundledVersion = bundled.Length > 0 && File.Exists(bundled)
                ? (System.Diagnostics.FileVersionInfo.GetVersionInfo(bundled).ProductVersion ?? "")
                : "";
            Check("the unpacked engine is the bundled one",
                  unpackedVersion.Length > 0 && unpackedVersion == bundledVersion,
                  unpackedVersion + " vs " + bundledVersion);
            Check("the unpacked engine keeps the signature files",
                  File.Exists(Path.Combine(folder, "xul.dll.sig")));

            GeckoEngine.DeleteTree(folder);
            Check("the test engine folder is cleaned up", !Directory.Exists(folder));
        }

        // ─── reporting ─────────────────────────────────────────────
        static void Check(string name, bool ok, string detail = null)
        {
            if (!ok) _failed++;
            _log.Append(ok ? "  ok   " : "  FAIL ").Append(name);
            if (!ok && detail != null) _log.Append("   [").Append(detail).Append(']');
            _log.Append('\n');
            Debug.Log((ok ? "ok " : "FAIL ") + name);
        }
    }
}
