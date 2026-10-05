using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MaterialBrowser
{
    /// <summary>
    /// Hand-drawn vector icon set (24×24 grid, scaled on demand) so the app needs no image assets.
    /// </summary>
    public static class Icons
    {
        static readonly Dictionary<string, List<string>> _alias = new Dictionary<string, List<string>>
        {
            { "light",   new List<string> { "sun" } },
            { "sun",     new List<string> { "brightness" } },
            { "night",   new List<string> { "moon" } },
            { "dark",    new List<string> { "moon" } },
            { "paint",   new List<string> { "palette" } },
            { "apps",    new List<string> { "grid" } },
            { "device",  new List<string> { "monitor" } },
            { "security",new List<string> { "shield" } },
            { "privacy", new List<string> { "shield" } },
            { "update",  new List<string> { "refresh" } },
            { "time",    new List<string> { "clock" } },
            { "lang",    new List<string> { "globe" } },
            { "game",    new List<string> { "gamepad" } },
            { "a11y",    new List<string> { "accessibility" } },
            { "sound",   new List<string> { "speaker" } },
            { "disk",    new List<string> { "storage" } },
            { "keyboard",new List<string> { "keyboard" } },
            { "wall",    new List<string> { "palette" } },
            { "recycle", new List<string> { "trash" } },
            { "bin",     new List<string> { "trash" } },
            { "delete",  new List<string> { "trash" } },
            { "rename",  new List<string> { "pencil" } },
            { "edit",    new List<string> { "pencil" } },
            { "newfolder", new List<string> { "folder-plus" } },
            { "create",  new List<string> { "folder-plus" } },
            { "pc",      new List<string> { "desktop" } },
            { "thispc",  new List<string> { "desktop" } },
            { "documents", new List<string> { "doc" } },
            { "pictures", new List<string> { "image" } },
            { "music",   new List<string> { "note" } },
            { "videos",  new List<string> { "film" } },
            { "downloads", new List<string> { "download" } },
            { "drive",   new List<string> { "hdd" } },
            { "hdd",     new List<string> { "hdd" } },
            { "vol",     new List<string> { "hdd" } },
            { "more",    new List<string> { "dots-v" } },
            { "menu",    new List<string> { "dots-v" } },
            { "sort",    new List<string> { "sort-az" } },
            { "eye",     new List<string> { "eye" } },
            { "tile",    new List<string> { "grid" } },
            { "list",    new List<string> { "rows" } },
            { "open",    new List<string> { "open-in" } },
            { "newtab",  new List<string> { "plus-box" } },
            { "up",      new List<string> { "arrow-up" } },
            { "properties", new List<string> { "info" } },
            { "check",   new List<string> { "check" } },
            { "undo",    new List<string> { "arrow-up" } },
        };

        public static void Draw(Graphics g, string name, RectangleF box, Color color, float weight)
        {
            string key = (name ?? "info").ToLowerInvariant();
            List<string> alt;
            if (_alias.TryGetValue(key, out alt)) key = alt[0];

            float w = Math.Max(1f, weight);
            var pen = new Pen(color, w);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            var brush = new SolidBrush(color);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Action<Pen, RectangleF> DR = delegate (Pen stroke, RectangleF r)
            {
                g.DrawRectangle(stroke, r.X, r.Y, r.Width, r.Height);
            };

            float x0 = box.Left, y0 = box.Top, s = Math.Min(box.Width, box.Height);
            Func<float, float, PointF> P = delegate (float x, float y)
            {
                return new PointF(x0 + x * s, y0 + y * s);
            };
            Func<float, float, float, float, RectangleF> R = delegate (float x, float y, float ww, float hh)
            {
                return new RectangleF(x0 + x * s, y0 + y * s, ww * s, hh * s);
            };

            switch (key)
            {
                case "home":
                    {
                        PointF a = P(0.10f, 0.48f), b = P(0.50f, 0.12f), c = P(0.90f, 0.48f);
                        g.DrawLines(pen, new PointF[] { a, b, c });
                        g.DrawLines(pen, new PointF[] { P(0.22f, 0.44f), P(0.22f, 0.88f), P(0.78f, 0.88f), P(0.78f, 0.44f) });
                        break;
                    }
                case "wifi":
                    {
                        g.DrawArc(pen, R(0.08f, 0.22f, 0.84f, 0.70f), 200, 140);
                        g.DrawArc(pen, R(0.26f, 0.42f, 0.48f, 0.50f), 200, 140);
                        g.FillEllipse(brush, R(0.44f, 0.72f, 0.12f, 0.12f));
                        break;
                    }
                case "ethernet":
                    {
                        DR(pen, R(0.12f, 0.34f, 0.76f, 0.42f));
                        g.DrawLine(pen, P(0.32f, 0.76f), P(0.32f, 0.92f));
                        g.DrawLine(pen, P(0.50f, 0.76f), P(0.50f, 0.92f));
                        g.DrawLine(pen, P(0.68f, 0.76f), P(0.68f, 0.92f));
                        break;
                    }
                case "bluetooth":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.50f, 0.10f), P(0.78f, 0.35f), P(0.50f, 0.60f), P(0.50f, 0.90f) });
                        g.DrawLines(pen, new PointF[] { P(0.50f, 0.10f), P(0.22f, 0.35f), P(0.50f, 0.60f) });
                        g.DrawLine(pen, P(0.28f, 0.72f), P(0.72f, 0.28f));
                        break;
                    }
                case "airplane":
                    {
                        g.FillPolygon(brush, new PointF[] { P(0.50f, 0.08f), P(0.60f, 0.40f), P(0.92f, 0.62f), P(0.92f, 0.72f),
                                                           P(0.60f, 0.62f), P(0.58f, 0.86f), P(0.70f, 0.94f), P(0.50f, 0.90f),
                                                           P(0.30f, 0.94f), P(0.42f, 0.86f), P(0.40f, 0.62f), P(0.08f, 0.72f),
                                                           P(0.08f, 0.62f), P(0.40f, 0.40f) });
                        break;
                    }
                case "mouse":
                    {
                        g.DrawArc(pen, R(0.26f, 0.06f, 0.48f, 0.88f), 180, 180);
                        g.DrawLine(pen, P(0.26f, 0.50f), P(0.74f, 0.50f));
                        g.DrawLine(pen, P(0.50f, 0.06f), P(0.50f, 0.28f));
                        break;
                    }
                case "monitor":
                    {
                        DR(pen, R(0.08f, 0.14f, 0.84f, 0.56f));
                        g.DrawLine(pen, P(0.50f, 0.70f), P(0.50f, 0.86f));
                        g.DrawLine(pen, P(0.30f, 0.90f), P(0.70f, 0.90f));
                        break;
                    }
                case "palette":
                    {
                        g.DrawEllipse(pen, R(0.08f, 0.08f, 0.84f, 0.84f));
                        g.FillEllipse(brush, R(0.28f, 0.28f, 0.13f, 0.13f));
                        g.FillEllipse(brush, R(0.55f, 0.30f, 0.13f, 0.13f));
                        g.FillEllipse(brush, R(0.30f, 0.55f, 0.13f, 0.13f));
                        break;
                    }
                case "grid":
                    {
                        g.FillRectangle(brush, R(0.14f, 0.14f, 0.30f, 0.30f));
                        g.FillRectangle(brush, R(0.56f, 0.14f, 0.30f, 0.30f));
                        g.FillRectangle(brush, R(0.14f, 0.56f, 0.30f, 0.30f));
                        g.FillRectangle(brush, R(0.56f, 0.56f, 0.30f, 0.30f));
                        break;
                    }
                case "account":
                    {
                        g.DrawEllipse(pen, R(0.33f, 0.12f, 0.34f, 0.34f));
                        g.DrawArc(pen, R(0.16f, 0.52f, 0.68f, 0.60f), 190, 160);
                        break;
                    }
                case "people":
                    {
                        g.DrawEllipse(pen, R(0.36f, 0.14f, 0.28f, 0.28f));
                        g.DrawArc(pen, R(0.22f, 0.48f, 0.56f, 0.50f), 190, 160);
                        g.DrawArc(pen, R(0.02f, 0.24f, 0.26f, 0.26f), 110, 250);
                        break;
                    }
                case "clock":
                    {
                        g.DrawEllipse(pen, R(0.08f, 0.08f, 0.84f, 0.84f));
                        g.DrawLine(pen, P(0.50f, 0.28f), P(0.50f, 0.52f));
                        g.DrawLine(pen, P(0.50f, 0.52f), P(0.70f, 0.62f));
                        break;
                    }
                case "globe":
                    {
                        g.DrawEllipse(pen, R(0.08f, 0.08f, 0.84f, 0.84f));
                        g.DrawEllipse(pen, R(0.33f, 0.08f, 0.34f, 0.84f));
                        g.DrawLine(pen, P(0.08f, 0.50f), P(0.92f, 0.50f));
                        break;
                    }
                case "gamepad":
                    {
                        g.DrawArc(pen, R(0.06f, 0.24f, 0.88f, 0.60f), 180, 180);
                        g.DrawLine(pen, P(0.06f, 0.54f), P(0.06f, 0.62f));
                        g.DrawLine(pen, P(0.94f, 0.54f), P(0.94f, 0.62f));
                        g.DrawLine(pen, P(0.24f, 0.40f), P(0.24f, 0.56f));
                        g.DrawLine(pen, P(0.16f, 0.48f), P(0.32f, 0.48f));
                        g.FillEllipse(brush, R(0.66f, 0.40f, 0.09f, 0.09f));
                        g.FillEllipse(brush, R(0.78f, 0.50f, 0.09f, 0.09f));
                        break;
                    }
                case "accessibility":
                    {
                        g.FillEllipse(brush, R(0.42f, 0.08f, 0.16f, 0.16f));
                        g.DrawLine(pen, P(0.50f, 0.26f), P(0.50f, 0.54f));
                        g.DrawLine(pen, P(0.24f, 0.36f), P(0.76f, 0.36f));
                        g.DrawLine(pen, P(0.50f, 0.54f), P(0.30f, 0.90f));
                        g.DrawLine(pen, P(0.50f, 0.54f), P(0.70f, 0.90f));
                        break;
                    }
                case "shield":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.50f, 0.08f), P(0.88f, 0.24f), P(0.86f, 0.58f),
                                                       P(0.50f, 0.92f), P(0.14f, 0.58f), P(0.12f, 0.24f), P(0.50f, 0.08f) });
                        g.DrawLine(pen, P(0.34f, 0.48f), P(0.46f, 0.60f));
                        g.DrawLine(pen, P(0.46f, 0.60f), P(0.68f, 0.38f));
                        break;
                    }
                case "refresh":
                    {
                        g.DrawArc(pen, R(0.12f, 0.12f, 0.76f, 0.76f), 40, 280);
                        PointF tip = P(0.88f, 0.30f);
                        g.FillPolygon(brush, new PointF[] { tip, P(0.62f, 0.20f), P(0.72f, 0.42f) });
                        break;
                    }
                case "download":
                    {
                        g.DrawLine(pen, P(0.50f, 0.10f), P(0.50f, 0.62f));
                        g.DrawLines(pen, new PointF[] { P(0.28f, 0.42f), P(0.50f, 0.64f), P(0.72f, 0.42f) });
                        g.DrawLine(pen, P(0.16f, 0.84f), P(0.84f, 0.84f));
                        break;
                    }
                case "search":
                    {
                        g.DrawEllipse(pen, R(0.10f, 0.10f, 0.62f, 0.62f));
                        g.DrawLine(pen, P(0.66f, 0.66f), P(0.90f, 0.90f));
                        break;
                    }
                case "close":
                    {
                        g.DrawLine(pen, P(0.20f, 0.20f), P(0.80f, 0.80f));
                        g.DrawLine(pen, P(0.80f, 0.20f), P(0.20f, 0.80f));
                        break;
                    }
                case "minimize":
                    {
                        g.DrawLine(pen, P(0.20f, 0.70f), P(0.80f, 0.70f));
                        break;
                    }
                case "maximize":
                    {
                        DR(pen, R(0.16f, 0.16f, 0.68f, 0.68f));
                        break;
                    }
                case "restore":
                    {
                        DR(pen, R(0.12f, 0.28f, 0.60f, 0.60f));
                        g.DrawLines(pen, new PointF[] { P(0.28f, 0.26f), P(0.28f, 0.12f), P(0.88f, 0.12f), P(0.88f, 0.72f), P(0.74f, 0.72f) });
                        break;
                    }
                case "chevron_right":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.36f, 0.20f), P(0.66f, 0.50f), P(0.36f, 0.80f) });
                        break;
                    }
                case "chevron_down":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.22f, 0.38f), P(0.50f, 0.66f), P(0.78f, 0.38f) });
                        break;
                    }
                case "back":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.74f, 0.18f), P(0.34f, 0.50f), P(0.74f, 0.82f) });
                        g.DrawLine(pen, P(0.36f, 0.50f), P(0.88f, 0.50f));
                        break;
                    }
                case "brightness":
                    {
                        g.DrawEllipse(pen, R(0.32f, 0.32f, 0.36f, 0.36f));
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4.0;
                            float cx = 0.5f + (float)Math.Cos(a) * 0.34f;
                            float cy = 0.5f + (float)Math.Sin(a) * 0.34f;
                            float ex = 0.5f + (float)Math.Cos(a) * 0.45f;
                            float ey = 0.5f + (float)Math.Sin(a) * 0.45f;
                            g.DrawLine(pen, P(cx, cy), P(ex, ey));
                        }
                        break;
                    }
                case "moon":
                    {
                        GraphicsPath path = new GraphicsPath();
                        path.AddArc(R(0.10f, 0.08f, 0.86f, 0.86f), 130, 280);
                        path.AddArc(R(0.28f, 0.02f, 0.78f, 0.78f), 40, 200);
                        path.CloseFigure();
                        g.FillPath(brush, path);
                        break;
                    }
                case "battery":
                    {
                        DR(pen, R(0.08f, 0.28f, 0.74f, 0.44f));
                        g.FillRectangle(brush, R(0.84f, 0.42f, 0.08f, 0.16f));
                        g.FillRectangle(brush, R(0.14f, 0.36f, 0.36f, 0.28f));
                        break;
                    }
                case "star":
                    {
                        var pts = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double a = -Math.PI / 2 + i * Math.PI / 5;
                            double r = (i % 2 == 0) ? 0.44 : 0.18;
                            pts[i] = P(0.5f + (float)(Math.Cos(a) * r), 0.5f + (float)(Math.Sin(a) * r));
                        }
                        g.FillPolygon(brush, pts);
                        break;
                    }
                case "bell":
                    {
                        GraphicsPath path = new GraphicsPath();
                        path.AddArc(R(0.20f, 0.10f, 0.60f, 0.60f), 180, 180);
                        path.AddLine(P(0.20f, 0.50f), P(0.20f, 0.70f));
                        path.AddLine(P(0.20f, 0.70f), P(0.80f, 0.70f));
                        path.AddLine(P(0.80f, 0.70f), P(0.80f, 0.50f));
                        path.CloseFigure();
                        g.DrawPath(pen, path);
                        g.DrawArc(pen, R(0.38f, 0.70f, 0.24f, 0.20f), 0, 180);
                        break;
                    }
                case "lock":
                    {
                        g.DrawArc(pen, R(0.28f, 0.12f, 0.44f, 0.44f), 180, 180);
                        g.DrawLine(pen, P(0.28f, 0.34f), P(0.28f, 0.54f));
                        g.DrawLine(pen, P(0.72f, 0.34f), P(0.72f, 0.54f));
                        DR(pen, R(0.20f, 0.52f, 0.60f, 0.38f));
                        break;
                    }
                case "storage":
                    {
                        DR(pen, R(0.10f, 0.24f, 0.80f, 0.22f));
                        DR(pen, R(0.10f, 0.56f, 0.80f, 0.22f));
                        g.FillEllipse(brush, R(0.66f, 0.30f, 0.10f, 0.10f));
                        g.FillEllipse(brush, R(0.66f, 0.62f, 0.10f, 0.10f));
                        break;
                    }
                case "speaker":
                    {
                        g.FillPolygon(brush, new PointF[] { P(0.10f, 0.38f), P(0.26f, 0.38f), P(0.44f, 0.20f),
                                                           P(0.44f, 0.80f), P(0.26f, 0.62f), P(0.10f, 0.62f) });
                        g.DrawArc(pen, R(0.46f, 0.28f, 0.28f, 0.44f), 300, 120);
                        g.DrawArc(pen, R(0.54f, 0.16f, 0.42f, 0.68f), 300, 120);
                        break;
                    }
                case "keyboard":
                    {
                        DR(pen, R(0.06f, 0.28f, 0.88f, 0.44f));
                        for (int i = 0; i < 4; i++)
                            g.FillRectangle(brush, R(0.16f + i * 0.18f, 0.38f, 0.10f, 0.09f));
                        g.FillRectangle(brush, R(0.30f, 0.55f, 0.40f, 0.09f));
                        break;
                    }
                case "printer":
                    {
                        DR(pen, R(0.22f, 0.08f, 0.56f, 0.26f));
                        DR(pen, R(0.10f, 0.34f, 0.80f, 0.34f));
                        DR(pen, R(0.22f, 0.62f, 0.56f, 0.30f));
                        break;
                    }
                case "taskbar":
                    {
                        DR(pen, R(0.06f, 0.62f, 0.88f, 0.28f));
                        g.FillRectangle(brush, R(0.14f, 0.68f, 0.14f, 0.16f));
                        g.FillRectangle(brush, R(0.36f, 0.68f, 0.14f, 0.16f));
                        g.FillRectangle(brush, R(0.58f, 0.68f, 0.14f, 0.16f));
                        break;
                    }
                case "power":
                    {
                        g.DrawArc(pen, R(0.18f, 0.18f, 0.64f, 0.64f), 290, 160);
                        g.DrawLine(pen, P(0.50f, 0.10f), P(0.50f, 0.46f));
                        break;
                    }
                case "lightbulb":
                    {
                        g.DrawArc(pen, R(0.24f, 0.06f, 0.52f, 0.52f), 200, 320);
                        g.DrawLines(pen, new PointF[] { P(0.36f, 0.58f), P(0.36f, 0.76f), P(0.64f, 0.76f), P(0.64f, 0.58f) });
                        g.DrawLine(pen, P(0.40f, 0.86f), P(0.60f, 0.86f));
                        break;
                    }
                case "tune":
                    {
                        g.DrawLine(pen, P(0.10f, 0.26f), P(0.90f, 0.26f));
                        g.DrawLine(pen, P(0.10f, 0.52f), P(0.90f, 0.52f));
                        g.DrawLine(pen, P(0.10f, 0.78f), P(0.90f, 0.78f));
                        g.FillEllipse(brush, R(0.30f, 0.16f, 0.14f, 0.14f));
                        g.FillEllipse(brush, R(0.62f, 0.42f, 0.14f, 0.14f));
                        g.FillEllipse(brush, R(0.40f, 0.68f, 0.14f, 0.14f));
                        break;
                    }
                case "folder":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.08f, 0.78f), P(0.08f, 0.20f), P(0.40f, 0.20f),
                                                       P(0.48f, 0.32f), P(0.92f, 0.32f), P(0.92f, 0.78f), P(0.08f, 0.78f) });
                        break;
                    }
                case "text":
                    {
                        var f = new Font("Segoe UI", s * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel);
                        using (var sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("Aa", f, brush, R(0f, 0f, 1f, 1f), sf);
                        }
                        f.Dispose();
                        break;
                    }
                case "language":
                    {
                        var f = new Font("Segoe UI", s * 0.40f, FontStyle.Bold, GraphicsUnit.Pixel);
                        using (var sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("文", f, brush, R(0f, 0f, 1f, 1f), sf);
                        }
                        f.Dispose();
                        break;
                    }
                case "sparkles":
                    {
                        g.FillPolygon(brush, new PointF[] { P(0.34f, 0.08f), P(0.40f, 0.28f), P(0.60f, 0.34f), P(0.40f, 0.40f),
                                                           P(0.34f, 0.60f), P(0.28f, 0.40f), P(0.08f, 0.34f), P(0.28f, 0.28f) });
                        g.FillPolygon(brush, new PointF[] { P(0.74f, 0.52f), P(0.78f, 0.64f), P(0.90f, 0.68f), P(0.78f, 0.72f),
                                                           P(0.74f, 0.84f), P(0.70f, 0.72f), P(0.58f, 0.68f), P(0.70f, 0.64f) });
                        break;
                    }
                case "cast":
                    {
                        g.DrawArc(pen, R(0.10f, 0.10f, 0.80f, 0.80f), 180, 180);
                        g.DrawArc(pen, R(0.10f, 0.10f, 0.50f, 0.50f), 180, 180);
                        g.FillEllipse(brush, R(0.10f, 0.78f, 0.14f, 0.14f));
                        break;
                    }
                case "memory":
                    {
                        DR(pen, R(0.10f, 0.30f, 0.80f, 0.40f));
                        for (int i = 0; i < 4; i++)
                            g.DrawLine(pen, P(0.24f + i * 0.18f, 0.70f), P(0.24f + i * 0.18f, 0.84f));
                        break;
                    }
                case "add":
                    {
                        g.DrawLine(pen, P(0.50f, 0.18f), P(0.50f, 0.82f));
                        g.DrawLine(pen, P(0.18f, 0.50f), P(0.82f, 0.50f));
                        break;
                    }
                case "trash":
                    {
                        g.DrawLine(pen, P(0.34f, 0.24f), P(0.38f, 0.88f));
                        g.DrawLine(pen, P(0.50f, 0.24f), P(0.50f, 0.88f));
                        g.DrawLine(pen, P(0.66f, 0.24f), P(0.62f, 0.88f));
                        g.DrawLine(pen, P(0.24f, 0.24f), P(0.76f, 0.24f));
                        g.DrawLines(pen, new PointF[] { P(0.30f, 0.24f), P(0.34f, 0.12f), P(0.66f, 0.12f), P(0.70f, 0.24f) });
                        g.DrawLines(pen, new PointF[] { P(0.30f, 0.24f), P(0.36f, 0.90f), P(0.64f, 0.90f), P(0.70f, 0.24f) });
                        break;
                    }
                case "copy":
                    {
                        DR(pen, R(0.10f, 0.10f, 0.58f, 0.58f));
                        DR(pen, R(0.32f, 0.32f, 0.58f, 0.58f));
                        break;
                    }
                case "cut":
                    {
                        g.DrawEllipse(pen, R(0.21f, 0.63f, 0.26f, 0.26f));
                        g.DrawEllipse(pen, R(0.53f, 0.63f, 0.26f, 0.26f));
                        g.DrawLine(pen, P(0.30f, 0.16f), P(0.56f, 0.56f));
                        g.DrawLine(pen, P(0.70f, 0.16f), P(0.44f, 0.56f));
                        break;
                    }
                case "paste":
                    {
                        DR(pen, R(0.12f, 0.20f, 0.76f, 0.68f));
                        g.DrawLine(pen, P(0.36f, 0.20f), P(0.36f, 0.08f));
                        g.DrawLine(pen, P(0.64f, 0.20f), P(0.64f, 0.08f));
                        g.DrawLine(pen, P(0.36f, 0.08f), P(0.64f, 0.08f));
                        break;
                    }
                case "pencil":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.16f, 0.84f), P(0.20f, 0.66f), P(0.70f, 0.16f), P(0.86f, 0.32f), P(0.36f, 0.82f), P(0.16f, 0.84f) });
                        g.DrawLine(pen, P(0.62f, 0.24f), P(0.78f, 0.40f));
                        break;
                    }
                case "folder-plus":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.08f, 0.80f), P(0.08f, 0.20f), P(0.38f, 0.20f), P(0.46f, 0.32f), P(0.92f, 0.32f), P(0.92f, 0.80f), P(0.08f, 0.80f) });
                        g.DrawLine(pen, P(0.66f, 0.46f), P(0.66f, 0.70f));
                        g.DrawLine(pen, P(0.54f, 0.58f), P(0.78f, 0.58f));
                        break;
                    }
                case "hdd":
                    {
                        DR(pen, R(0.06f, 0.20f, 0.88f, 0.60f));
                        g.DrawLine(pen, P(0.06f, 0.62f), P(0.94f, 0.62f));
                        g.FillEllipse(brush, R(0.60f, 0.68f, 0.10f, 0.10f));
                        g.DrawLine(pen, P(0.14f, 0.40f), P(0.46f, 0.40f));
                        break;
                    }
                case "desktop":
                    {
                        DR(pen, R(0.08f, 0.14f, 0.84f, 0.54f));
                        g.DrawLine(pen, P(0.34f, 0.86f), P(0.66f, 0.86f));
                        g.DrawLine(pen, P(0.50f, 0.68f), P(0.50f, 0.86f));
                        break;
                    }
                case "doc":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.20f, 0.08f), P(0.60f, 0.08f), P(0.80f, 0.28f), P(0.80f, 0.92f), P(0.20f, 0.92f), P(0.20f, 0.08f) });
                        g.DrawLines(pen, new PointF[] { P(0.60f, 0.08f), P(0.60f, 0.28f), P(0.80f, 0.28f) });
                        g.DrawLine(pen, P(0.32f, 0.50f), P(0.68f, 0.50f));
                        g.DrawLine(pen, P(0.32f, 0.66f), P(0.68f, 0.66f));
                        break;
                    }
                case "image":
                    {
                        DR(pen, R(0.10f, 0.16f, 0.80f, 0.68f));
                        g.DrawEllipse(pen, R(0.30f, 0.30f, 0.14f, 0.14f));
                        g.DrawLines(pen, new PointF[] { P(0.12f, 0.78f), P(0.40f, 0.48f), P(0.58f, 0.66f), P(0.72f, 0.54f), P(0.90f, 0.78f) });
                        break;
                    }
                case "note":
                    {
                        g.FillEllipse(brush, R(0.40f, 0.36f, 0.20f, 0.20f));
                        g.DrawLine(pen, P(0.50f, 0.56f), P(0.50f, 0.92f));
                        g.DrawLine(pen, P(0.36f, 0.36f), P(0.64f, 0.36f));
                        g.DrawLine(pen, P(0.36f, 0.36f), P(0.36f, 0.56f));
                        g.DrawLine(pen, P(0.64f, 0.36f), P(0.64f, 0.56f));
                        break;
                    }
                case "film":
                    {
                        DR(pen, R(0.10f, 0.18f, 0.80f, 0.64f));
                        for (int i = 1; i <= 3; i++)
                        {
                            float y = 0.18f + 0.64f * i / 4f;
                            g.DrawLine(pen, P(0.10f, y), P(0.26f, y));
                            g.DrawLine(pen, P(0.74f, y), P(0.90f, y));
                        }
                        g.DrawLines(pen, new PointF[] { P(0.40f, 0.40f), P(0.56f, 0.50f), P(0.40f, 0.60f), P(0.40f, 0.40f) });
                        break;
                    }
                case "dots-v":
                    {
                        g.FillEllipse(brush, R(0.42f, 0.16f, 0.16f, 0.16f));
                        g.FillEllipse(brush, R(0.42f, 0.42f, 0.16f, 0.16f));
                        g.FillEllipse(brush, R(0.42f, 0.68f, 0.16f, 0.16f));
                        break;
                    }
                case "sort-az":
                    {
                        g.DrawLine(pen, P(0.14f, 0.20f), P(0.54f, 0.20f));
                        g.DrawLine(pen, P(0.14f, 0.44f), P(0.44f, 0.44f));
                        g.DrawLine(pen, P(0.14f, 0.68f), P(0.34f, 0.68f));
                        g.DrawLines(pen, new PointF[] { P(0.72f, 0.22f), P(0.88f, 0.22f), P(0.88f, 0.76f) });
                        g.DrawLines(pen, new PointF[] { P(0.66f, 0.62f), P(0.88f, 0.80f), P(0.88f, 0.62f) });
                        break;
                    }
                case "eye":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.06f, 0.50f), P(0.30f, 0.24f), P(0.70f, 0.24f), P(0.94f, 0.50f), P(0.70f, 0.76f), P(0.30f, 0.76f), P(0.06f, 0.50f) });
                        g.DrawEllipse(pen, R(0.38f, 0.38f, 0.24f, 0.24f));
                        break;
                    }
                case "rows":
                    {
                        g.DrawLine(pen, P(0.10f, 0.24f), P(0.90f, 0.24f));
                        g.DrawLine(pen, P(0.10f, 0.50f), P(0.90f, 0.50f));
                        g.DrawLine(pen, P(0.10f, 0.76f), P(0.90f, 0.76f));
                        break;
                    }
                case "open-in":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.44f, 0.14f), P(0.86f, 0.56f) });
                        g.DrawLines(pen, new PointF[] { P(0.62f, 0.56f), P(0.86f, 0.56f), P(0.86f, 0.80f) });
                        g.DrawArc(pen, R(0.08f, 0.08f, 0.84f, 0.84f), 110, 250);
                        break;
                    }
                case "plus-box":
                    {
                        DR(pen, R(0.10f, 0.16f, 0.80f, 0.68f));
                        g.DrawLine(pen, P(0.50f, 0.34f), P(0.50f, 0.66f));
                        g.DrawLine(pen, P(0.34f, 0.50f), P(0.66f, 0.50f));
                        break;
                    }
                case "arrow-up":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.14f, 0.60f), P(0.50f, 0.22f), P(0.86f, 0.60f) });
                        g.DrawLine(pen, P(0.50f, 0.24f), P(0.50f, 0.88f));
                        break;
                    }
                case "info":
                    {
                        g.DrawEllipse(pen, R(0.10f, 0.10f, 0.80f, 0.80f));
                        g.DrawLine(pen, P(0.50f, 0.46f), P(0.50f, 0.74f));
                        g.FillEllipse(brush, R(0.44f, 0.24f, 0.12f, 0.12f));
                        break;
                    }
                case "check":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.14f, 0.52f), P(0.40f, 0.78f), P(0.86f, 0.22f) });
                        break;
                    }
                case "chevron-right":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.38f, 0.16f), P(0.66f, 0.50f), P(0.38f, 0.84f) });
                        break;
                    }
                case "chevron-down":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.16f, 0.38f), P(0.50f, 0.66f), P(0.84f, 0.38f) });
                        break;
                    }
                case "refresh2":
                    {
                        g.DrawArc(pen, R(0.14f, 0.14f, 0.72f, 0.72f), 40, 280);
                        g.DrawLines(pen, new PointF[] { P(0.62f, 0.06f), P(0.86f, 0.20f), P(0.70f, 0.42f) });
                        break;
                    }
                case "warning":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.50f, 0.12f), P(0.92f, 0.84f), P(0.08f, 0.84f), P(0.50f, 0.12f) });
                        g.DrawLine(pen, P(0.50f, 0.42f), P(0.50f, 0.62f));
                        g.FillEllipse(brush, R(0.45f, 0.68f, 0.10f, 0.10f));
                        break;
                    }
                case "file-generic":
                    {
                        g.DrawLines(pen, new PointF[] { P(0.20f, 0.08f), P(0.60f, 0.08f), P(0.80f, 0.28f), P(0.80f, 0.92f), P(0.20f, 0.92f), P(0.20f, 0.08f) });
                        g.DrawLines(pen, new PointF[] { P(0.60f, 0.08f), P(0.60f, 0.28f), P(0.80f, 0.28f) });
                        break;
                    }
                default:
                    {
                        g.DrawEllipse(pen, R(0.10f, 0.10f, 0.80f, 0.80f));
                        g.FillEllipse(brush, R(0.44f, 0.42f, 0.12f, 0.12f));
                        break;
                    }
            }

            pen.Dispose();
            brush.Dispose();
        }
    }
}