using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using MaterialBrowser;

static class DumpBoxes
{
    static int y0, y1;
    static Graphics g;

    static void PrintAll(Box box, int depth, ref int count, int maxDepth)
    {
        string tag = box.Node != null ? box.Node.Tag : "-";
        string cls = box.Node != null && box.Node.ClassList.Length > 0 ? "." + box.Node.ClassList : "";
        if (cls.Length > 30) cls = cls.Substring(0, 30);
        Console.WriteLine(new string(' ', depth * 2) + string.Format("{0,-9} <{1}{2}> x={3,7} y={4,9} w={5,8} h={6,8}",
            box.Kind, tag, cls, box.Rect.X, box.Rect.Y, box.Rect.Width, box.Rect.Height));
        if (depth >= maxDepth || count++ > 300) return;
        foreach (Box child in box.Children) PrintAll(child, depth + 1, ref count, maxDepth);
    }

    static void Print(Box box, int depth, ref int count, int maxDepth)
    {
        if (box.Rect.Height <= 0) return;
        if (box.Rect.Bottom < y0 || box.Rect.Top > y1) return;
        if (count++ > 400) return;
        string tag = box.Node != null ? box.Node.Tag : "-";
        string cls = box.Node != null && box.Node.ClassList.Length > 0 ? "." + box.Node.ClassList : "";
        if (cls.Length > 26) cls = cls.Substring(0, 26);
        string txt = box.Text.Length > 20 ? box.Text.Substring(0, 20) : box.Text;
        Console.WriteLine(new string(' ', depth * 2) + string.Format("{0,-9} <{1}{2}> x={3,7} y={4,9} w={5,8} h={6,8} fs={7} '{8}'",
            box.Kind, tag, cls, box.Rect.X, box.Rect.Y, box.Rect.Width, box.Rect.Height,
            box.Font != null ? box.Font.SizeInPoints : 0, txt));
        if (depth >= maxDepth) return;
        foreach (Box child in box.Children) Print(child, depth + 1, ref count, maxDepth);
    }

    public static void Main(string[] args)
    {
        string file = args.Length > 0 ? args[0] : @"G:\MaterialBrowser\samples\sample.html";
        string url = args.Length > 1 ? args[1] : "file:///G:/MaterialBrowser/samples/sample.html";
        int viewport = args.Length > 2 ? int.Parse(args[2]) : 1032;
        int startY = args.Length > 3 ? int.Parse(args[3]) : 0;
        int endY = args.Length > 4 ? int.Parse(args[4]) : 100000;
        y0 = startY;
        y1 = endY;

        string html = File.ReadAllText(file);
        using (Bitmap bmp = new Bitmap(viewport, 4000, PixelFormat.Format32bppArgb))
        using (Graphics surface = Graphics.FromImage(bmp))
        {
            g = surface;
            g.Clear(Color.White);
            var page = new Page();
            page.Url = url;
            page.Prepare(html, url, null);
            page.Style.ViewportWidth = viewport;
            Console.WriteLine("rules=" + page.Rules.Count);
            if (args.Length > 5 && args[5] == "--vars")
            {
                int withCustom = 0;
                foreach (CssRule rule in page.Rules)
                {
                    if (rule.Custom.Count == 0) continue;
                    withCustom++;
                    Console.WriteLine(string.Join(",", rule.Selectors) + "  ->  " + rule.Custom.Count + " vars");
                }
                Console.WriteLine("rules with custom props: " + withCustom);
                int idx = 0;
                foreach (CssRule rule in page.Rules)
                {
                    if (idx++ > 400) break;
                    string sel = string.Join(",", rule.Selectors);
                    if (sel.IndexOf("body", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine("RULE[" + idx + "] " + sel);
                    foreach (KeyValuePair<string, string> kv in rule.Declarations)
                        Console.WriteLine("      " + kv.Key + " = " + kv.Value);
                }
                return;
            }
            var engine = new LayoutEngine(g, page.Rules, page.Style);
            Box root = engine.BuildDocument(page.Dom);
            Console.WriteLine("root h=" + root.Rect.Height);
            if (args.Length > 5 && args[5].StartsWith("--"))
            {
                if (args[5] == "--children")
                {
                    string wanted = args[6];
                    root.Walk(delegate (Box box)
                    {
                        if (box.Node == null || box.Node.ClassList.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0) return;
                        Console.WriteLine("NODE <" + box.Node.Tag + "> domChildren=" + box.Node.Children.Count + " boxes=" + box.Children.Count + " rect=" + box.Rect); foreach (Box bx in box.Children) { Console.WriteLine("    BOX " + bx.Kind + " kids=" + bx.Children.Count + " first=" + (bx.Children.Count > 0 ? bx.Children[0].Text : "") + " rect=" + bx.Rect); }
                        foreach (Node c in box.Node.Children)
                        {
                            string t = c.IsElement ? c.Tag + " class=\"" + c.ClassList + "\" domChildren=" + c.Children.Count + " inner=" + c.InnerText.Length : "#text len=" + c.Text.Length;
                            if (t.Length > 120) t = t.Substring(0, 120);
                            Console.WriteLine("    " + t);
                            foreach (Node sub in c.Children)
                            {
                                string gt = sub.IsElement ? sub.Tag + " class=\"" + sub.ClassList + "\" domChildren=" + sub.Children.Count + " inner=" + sub.InnerText.Length : "#text len=" + sub.Text.Length + " '"  + sub.Text + "'";
                                if (gt.Length > 120) gt = gt.Substring(0, 120);
                                Console.WriteLine("        " + gt);
                            }
                        }
                    });
                    return;
                }
                if (args[5] == "--sub")
                {
                    string wanted = args[6];
                    int subCount = 0;
                    root.Walk(delegate (Box box)
                    {
                        if (box.Node == null || box.Node.ClassList.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0) return;
                        if (subCount++ > 0) return;
                        PrintAll(box, 0, ref subCount, 30);
                    });
                    return;
                }
                string target = args[5] == "--tall" ? "" : args[6];
                float limit = args[5] == "--tall" ? float.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture) : 0;
                root.Walk(delegate (Box box)
                {
                    if (args[5] != "--tall" && box.Node == null) return;
                    if (args[5] == "--tall")
                    {
                        if (box.Rect.Height < limit) return;
                        Node n = box.Node;
                        var path = new List<string>();
                        while (n != null && path.Count < 20)
                        {
                            string cls = n.IsElement ? n.Tag + "." + n.ClassList : "#text";
                            if (cls.Length > 40) cls = cls.Substring(0, 40);
                            path.Add(cls);
                            n = n.Parent;
                        }
                        path.Reverse();
                        Console.WriteLine("PATH: " + string.Join(" > ", path.ToArray()));
                    }
                    else if (box.Node.ClassList.IndexOf(target, StringComparison.OrdinalIgnoreCase) < 0) return;
                    Style s = box.Node != null ? Css.Compute(box.Node, page.Rules) : new Style();
                    string label = box.Node != null ? "<" + box.Node.Tag + " class=\"" + box.Node.ClassList + "\">" : "<anonymous>";
                    Console.WriteLine("--- " + label + " rect=" + box.Rect);
                    foreach (KeyValuePair<string, string> kv in s.Values)
                        Console.WriteLine("      " + kv.Key + " = " + kv.Value);
                });
                return;
            }
            if (false)
            {
                string wanted = args[6];
                root.Walk(delegate (Box box)
                {
                    if (box.Node == null || box.Node.ClassList.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0) return;
                    Style s = Css.Compute(box.Node, page.Rules);
                    Console.WriteLine("--- <" + box.Node.Tag + " class=\"" + box.Node.ClassList + "\"> rect=" + box.Rect);
                    foreach (KeyValuePair<string, string> kv in s.Values)
                        Console.WriteLine("      " + kv.Key + " = " + kv.Value);
                });
                return;
            }
            int count = 0;
            int maxDepth = args.Length > 6 ? int.Parse(args[6]) : 6;
            Console.WriteLine("-- tree --");
            Print(root, 0, ref count, maxDepth);
        }
    }
}