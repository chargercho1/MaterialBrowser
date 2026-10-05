using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MaterialBrowser
{
    public class MenuItem
    {
        public string Text = "";
        public string Icon = "";
        public string Shortcut = "";
        public bool Separator;
        public bool Enabled = true;
        public bool Danger;
        public bool Checked;
        public Action Action;
        public List<MenuItem> Children;
    }

    public static class Menus
    {
        public static MenuItem Sep() { return new MenuItem { Separator = true }; }

        public static MenuItem Item(string text, string icon, Action action, string shortcut = "", bool enabled = true)
        {
            return new MenuItem { Text = text, Icon = icon, Action = action, Shortcut = shortcut, Enabled = enabled };
        }

        public static MenuItem Danger(string text, string icon, Action action, string shortcut = "")
        {
            return new MenuItem { Text = text, Icon = icon, Action = action, Shortcut = shortcut, Danger = true };
        }

        public static MenuItem Check(string text, string icon, bool @checked, Action action)
        {
            return new MenuItem { Text = text, Icon = icon, Action = action, Checked = @checked };
        }
    }

    /// <summary>
    /// Borderless context menu drawn in the app's own style. A low-level mouse hook
    /// closes it as soon as the button is released outside, so it never steals input.
    /// </summary>
    public class MenuPopup : Form
    {
        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONUP = 0x0202;

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetModuleHandle(string name);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData, flags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);

        static IntPtr _hook;
        static HookProc _proc;
        static MenuPopup _active;

        readonly List<MenuItem> _items;
        int _hover = -1;
        MenuPopup _sub;
        int[] _y;

        public MenuPopup(List<MenuItem> items)
        {
            _items = items;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Opacity = 0.99;
        }

        int ItemHeight { get { return Ui.PX(38); } }
        int SepHeight { get { return Ui.PX(11); } }
        int Pad { get { return Ui.PX(8); } }

        void Measure()
        {
            using (Graphics g = CreateGraphics())
            {
                int max = Ui.PX(200);
                foreach (MenuItem it in _items)
                {
                    if (it.Separator) continue;
                    int w = (int)g.MeasureString(it.Text, Ui.Label).Width;
                    if (it.Shortcut.Length > 0) w += (int)g.MeasureString(it.Shortcut, Ui.Caption).Width + Ui.PX(30);
                    if (it.Icon.Length > 0) w += Ui.PX(30);
                    if (w > max) max = w;
                }
                int rows = 0;
                foreach (MenuItem it in _items) rows += it.Separator ? SepHeight : ItemHeight;
                ClientSize = new Size(Math.Min(Ui.PX(340), max + Ui.PX(40)), rows + Pad * 2);
            }
        }

        void ComputeRows()
        {
            _y = new int[_items.Count];
            int y = Pad;
            for (int i = 0; i < _items.Count; i++)
            {
                _y[i] = y;
                y += _items[i].Separator ? SepHeight : ItemHeight;
            }
        }

        /// <summary>Shows the menu at the cursor, clamped to the working area of the nearest screen.</summary>
        public void ShowAtCursor(Form owner, Point screenPoint)
        {
            Measure();
            Point p = screenPoint;
            var wa = Screen.FromPoint(p).WorkingArea;
            if (p.X + Width > wa.Right - Ui.PX(4)) p.X = Math.Max(wa.Left + Ui.PX(4), p.X - Width);
            if (p.Y + Height > wa.Bottom - Ui.PX(4))
            {
                int above = p.Y - Height;
                if (above < wa.Top + Ui.PX(4)) p.Y = Math.Max(wa.Top + Ui.PX(4), wa.Bottom - Height - Ui.PX(4));
                else p.Y = above;
            }
            Location = p;
            ComputeRows();
            _active = this;
            _proc = HookProcImpl;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            ShowDialog(owner);
            Cleanup();
        }

        void Cleanup()
        {
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            _proc = null;
            if (_active == this) _active = null;
            if (_sub != null) { _sub.Close(); _sub = null; }
        }

        static IntPtr HookProcImpl(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0 && wParam.ToInt32() == WM_LBUTTONUP && _active != null)
                {
                    var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    var pt = new Point(data.pt.x, data.pt.y);
                    if (!_active.ClientRectangle.Contains(_active.PointToClient(pt)))
                        _active.BeginInvoke(new MethodInvoker(_active.Close));
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            Cursor = Cursors.Hand;
        }

        int Hit(Point p)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Separator) continue;
                if (p.Y >= _y[i] && p.Y < _y[i] + ItemHeight) return i;
            }
            return -1;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int i = Hit(e.Location);
            if (i < 0) return;
            MenuItem it = _items[i];
            if (!it.Enabled) return;
            Close();
            if (it.Action != null && _sub == null) it.Action();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { MoveHover(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { MoveHover(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                if (_hover >= 0 && _items[_hover].Enabled)
                {
                    MenuItem it = _items[_hover];
                    Close();
                    if (it.Action != null) it.Action();
                }
                e.Handled = true;
            }
        }

        void MoveHover(int step)
        {
            if (_items.Count == 0) return;
            int i = _hover;
            for (int n = 0; n < _items.Count; n++)
            {
                i = (i + step + _items.Count) % _items.Count;
                if (!_items[i].Separator) break;
            }
            _hover = i;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Scheme s = Ui.T;
            var rect = new Rectangle(0, 0, Width, Height);

            // Soft drop shadow built from a few stacked translucent rounds.
            for (int i = 4; i >= 1; i--)
            {
                var sh = new Rectangle(-i, -i + Ui.PX(2), Width + i * 2, Height + i * 2);
                Ui.FillRound(g, sh, Ui.PX(16) + i, Scheme.WithAlpha(s.Dark ? Color.Black : Color.FromArgb(40, 40, 50), 12));
            }
            Ui.FillRound(g, rect, Ui.PX(16), s.SurfaceHigh);
            Ui.StrokeRound(g, rect, Ui.PX(16), Scheme.WithAlpha(s.OutlineVariant, 140), 1f);

            for (int i = 0; i < _items.Count; i++)
            {
                MenuItem it = _items[i];
                int y = _y[i];
                if (it.Separator)
                {
                    using (var b = new SolidBrush(Scheme.WithAlpha(s.OutlineVariant, 160)))
                        g.FillRectangle(b, Pad + Ui.PX(6), y + SepHeight / 2, Width - (Pad + Ui.PX(6)) * 2, Ui.PX(1));
                    continue;
                }

                var box = new Rectangle(Pad, y, Width - Pad * 2, ItemHeight);
                if (i == _hover && it.Enabled)
                    Ui.FillRound(g, new Rectangle(box.X, box.Y + Ui.PX(2), box.Width, box.Height - Ui.PX(4)), Ui.PX(10),
                        it.Danger ? Scheme.WithAlpha(s.ErrorContainer, 220) : Ui.StateLayer(s, s.Dark ? 0.14 : 0.07));

                Color fg = !it.Enabled ? Scheme.WithAlpha(s.OnSurfaceVariant, 110)
                         : it.Danger ? s.Error.Tone(s.Dark ? 70 : 40)
                         : s.OnSurface;

                int iconSize = Ui.PX(18);
                int textX = box.X + Ui.PX(12);
                if (it.Checked)
                {
                    Icons.Draw(g, "check", new Rectangle(textX, box.Y + (box.Height - iconSize) / 2, iconSize, iconSize), fg, Ui.PX(1.8f));
                    textX += Ui.PX(26);
                }
                else if (it.Icon.Length > 0)
                {
                    Icons.Draw(g, it.Icon, new Rectangle(textX, box.Y + (box.Height - iconSize) / 2, iconSize, iconSize), fg, Ui.PX(1.6f));
                    textX += Ui.PX(26);
                }
                else textX += Ui.PX(4);

                int shortcutW = 0;
                if (it.Shortcut.Length > 0)
                {
                    shortcutW = (int)g.MeasureString(it.Shortcut, Ui.Caption).Width + Ui.PX(14);
                    using (var b = new SolidBrush(Scheme.WithAlpha(s.OnSurfaceVariant, it.Enabled ? 200 : 90)))
                        g.DrawString(it.Shortcut, Ui.Caption, b,
                            new Rectangle(box.Right - shortcutW, 0, shortcutW, Height),
                            new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far });
                }

                using (var b = new SolidBrush(fg))
                    g.DrawString(Ui.Ellipsis(it.Text, Ui.Label, box.Width - (textX - box.X) - shortcutW - Ui.PX(12), g), Ui.Label, b,
                        new Rectangle(textX, 0, box.Width - (textX - box.X) - shortcutW, Height),
                        new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
            }
        }
    }

    public static class MenuHost
    {
        /// <summary>Builds and shows a context menu at the cursor, owned by the given window.</summary>
        public static void Show(Form owner, List<MenuItem> items, Point screenPoint)
        {
            if (items == null || items.Count == 0) return;
            using (var menu = new MenuPopup(items))
                menu.ShowAtCursor(owner, screenPoint);
        }

        public static void Show(Form owner, List<MenuItem> items)
        {
            Show(owner, items, Cursor.Position);
        }
    }
}
