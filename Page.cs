using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;

namespace MaterialBrowser
{
    /// <summary>One loaded document: DOM, style sheet, laid-out boxes and metadata.</summary>
    public class Page
    {
        public string Url = "";
        public string Title = "";
        public string Status = "";
        public bool Ok;
        public string Error = "";
        public long ElapsedMs;
        public long Length;

        public Node Dom;
        public readonly List<CssRule> Rules = new List<CssRule>();
        public Box Root;

        public PageStyle Style = new PageStyle();
        public CookieContainer Cookies;

        public int LoadedImages;

        public void ApplyTheme(Scheme scheme)
        {
            Style.Text = scheme.Dark ? Color.FromArgb(226, 226, 230) : Color.FromArgb(32, 33, 36);
            Style.Background = scheme.Dark ? Color.FromArgb(24, 24, 27) : Color.White;
            Style.Link = scheme.Dark ? Color.FromArgb(140, 190, 255) : Color.FromArgb(26, 115, 232);
            Style.Visited = scheme.Dark ? Color.FromArgb(198, 160, 255) : Color.FromArgb(130, 84, 200);
            Style.Border = scheme.Dark ? Color.FromArgb(64, 64, 70) : Color.FromArgb(218, 220, 224);
            Style.Muted = scheme.Dark ? Color.FromArgb(160, 160, 168) : Color.FromArgb(95, 99, 104);
            Style.FontScale = Settings.FontFactor;
        }

        /// <summary>Builds the DOM, the rule set and queues the images. Must run off the UI thread.</summary>
        public void Prepare(string html, string url, CookieContainer cookies)
        {
            Cookies = cookies;
            Style.BaseUrl = url;
            Url = url;
            Dom = Html.Parse(html);
            Title = Dom.TitleText;
            if (string.IsNullOrEmpty(Title)) Title = WebText.TabTitle(url);
            Rules.Clear();
            foreach (Node style in Dom.FindAll("style"))
            {
                string css = style.InnerText;
                if (css.Length > 0) Rules.AddRange(Css.ParseSheet(css));
            }
            Css.ExpandRules(Rules);
            Node baseNode = Dom.FindFirst("base");
            if (baseNode != null)
            {
                string href = baseNode.AttrValue("href", "");
                string resolved = Html.ResolveUrl(url, href);
                if (resolved.Length > 0) Style.BaseUrl = resolved;
            }
            QueueImages();
        }

        void QueueImages()
        {
            if (!Settings.LoadImages || Dom == null) return;
            Images.Referer = Style.BaseUrl;
            int budget = Settings.MaxImages;
            foreach (Node img in Dom.FindAll("img"))
            {
                if (budget-- <= 0) break;
                string src = img.AttrValue("src", img.AttrValue("data-src", ""));
                if (src.Length == 0) continue;
                string url = Html.ResolveUrl(Style.BaseUrl, src);
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Images.IsKnown(url)) Images.Prefetch(url, Cookies);
                }
                else if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Images.IsKnown(url)) Images.Prefetch(url, Cookies);
                }
                else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Images.IsKnown(url)) Images.Prefetch(url, Cookies);
                }
            }
        }

        /// <summary>Runs the layout pass. Needs a Graphics for text measurement.</summary>
        public void Layout(Graphics g, int viewportWidth)
        {
            if (Dom == null) return;
            Style.ViewportWidth = Math.Max(200, viewportWidth);
            Css.ExpandRules(Rules);
            var engine = new LayoutEngine(g, Rules, Style);
            Root = engine.BuildDocument(Dom);
        }

        /// <summary>Error page shown when navigation fails.</summary>
        public static Page ErrorPage(string url, string error, int statusCode)
        {
            var page = new Page();
            page.Url = url;
            page.Ok = false;
            page.Error = error;
            page.Title = "Ошибка загрузки";
            page.Status = statusCode > 0 ? "HTTP " + statusCode : "";

            string body = "<h1>" + Http.Escape(error) + "</h1><p>Не удалось загрузить страницу.</p>" +
                          "<p style=\"color:#888\">" + Http.Escape(url) + "</p>" +
                          "<p><a href=\"about:home\">Домашняя страница</a> &nbsp; <a href=\"about:retry\">Повторить</a></p>";
            page.Dom = Html.Parse("<!doctype html><html><head><meta charset=\"utf-8\"><title>Ошибка</title></head><body>" + body + "</body></html>");
            return page;
        }

        public static Page TextPage(string title, string html)
        {
            var page = new Page();
            page.Url = "about:" + title;
            page.Title = title;
            page.Ok = true;
            page.Status = "";
            page.Dom = Html.Parse("<!doctype html><html><head><meta charset=\"utf-8\"><title>" + Http.Escape(title) + "</title></head><body>" + html + "</body></html>");
            return page;
        }
    }
}
