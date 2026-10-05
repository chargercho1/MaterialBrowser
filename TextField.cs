using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MaterialBrowser
{
    /// <summary>
    /// Minimal single-line text field drawn by hand: WinForms' TextBox cannot be
    /// transparent, which breaks the Material You pill styling.
    /// </summary>
    public class MaterialTextField : Control
    {
        private string _text = "";
        private int _caret;
        private int _offset;
        private bool _focused;
        private readonly Timer _caretTimer;
        private bool _caretOn = true;

        public event EventHandler TextEdited;
        public event EventHandler EnterPressed;

        public string Text2
        {
            get { return _text; }
        }

        public void SetText(string value)
        {
            _text = value ?? "";
            _caret = _text.Length;
            _offset = 0;
            Invalidate();
        }

        public MaterialTextField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.IBeam;
            TabStop = true;
            Height = Ui.PX(24);
            _caretTimer = new Timer();
            _caretTimer.Interval = 500;
            _caretTimer.Tick += delegate
            {
                _caretOn = !_caretOn;
                if (_focused) Invalidate();
            };
            _caretTimer.Start();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _focused = true;
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _focused = false;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            bool ctrl = (e.Modifiers & Keys.Control) == Keys.Control;

            if (e.KeyCode == Keys.Enter)
            {
                EventHandler h = EnterPressed;
                if (h != null) h(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                return;
            }
            if (ctrl && e.KeyCode == Keys.A) { _caret = _text.Length; e.Handled = true; Invalidate(); return; }
            if (ctrl && e.KeyCode == Keys.V) { Paste(); e.Handled = true; return; }
            if (ctrl && e.KeyCode == Keys.C) { Copy(); e.Handled = true; return; }
            if (ctrl && e.KeyCode == Keys.X) { Copy(); DeleteSelection(); e.Handled = true; return; }

            if (e.KeyCode == Keys.Back)
            {
                if (_caret > 0) { _text = _text.Remove(_caret - 1, 1); _caret--; Changed(); }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                if (_caret < _text.Length) { _text = _text.Remove(_caret, 1); Changed(); }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Left)
            {
                if (_caret > 0) _caret--;
                e.Handled = true;
                Invalidate();
            }
            else if (e.KeyCode == Keys.Right)
            {
                if (_caret < _text.Length) _caret++;
                e.Handled = true;
                Invalidate();
            }
            else if (e.KeyCode == Keys.Home) { _caret = 0; e.Handled = true; Invalidate(); }
            else if (e.KeyCode == Keys.End) { _caret = _text.Length; e.Handled = true; Invalidate(); }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            char c = e.KeyChar;
            if (c == (char)13 || c == (char)27 || c == (char)8 || c < ' ') return;
            if ((ModifierKeys & Keys.Control) == Keys.Control) return;
            if (_caret < _text.Length) _text = _text.Insert(_caret, c.ToString());
            else _text += c;
            _caret++;
            Changed();
            e.Handled = true;
        }

        private void Copy()
        {
            try { if (_text.Length > 0) Clipboard.SetText(_text); } catch { }
        }

        private void Paste()
        {
            try
            {
                string t = Clipboard.GetText();
                if (string.IsNullOrEmpty(t)) return;
                t = t.Replace("\r", " ").Replace("\n", " ");
                _text = _text.Insert(_caret, t);
                _caret += t.Length;
                Changed();
            }
            catch { }
        }

        private void Changed()
        {
            _caretOn = true;
            _caretTimer.Stop();
            _caretTimer.Start();
            EventHandler h = TextEdited;
            if (h != null) h(this, EventArgs.Empty);
            Invalidate();
        }

        private void DeleteSelection()
        {
            _text = "";
            _caret = 0;
            Changed();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _caret = CaretFromX(e.X);
            Invalidate();
        }

        private int CaretFromX(int x)
        {
            int best = 0;
            double bestDistance = double.MaxValue;
            for (int i = 0; i <= _text.Length; i++)
            {
                float w = MeasurePrefix(i);
                double d = Math.Abs(w - x);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        private float MeasurePrefix(int count)
        {
            using (Graphics g = CreateGraphics())
                return g.MeasureString(count == 0 ? "" : _text.Substring(0, count), Font).Width;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Scheme s = Ui.T;

            float caretX = MeasurePrefix(_caret);
            if (caretX < _offset) _offset = (int)caretX - 4;
            float right = MeasurePrefix(_text.Length);
            if (caretX - _offset > Width) _offset = (int)(caretX - Width + 6);

            var clip = new Rectangle(0, 0, Width, Height);
            g.SetClip(clip);

            using (var b = new SolidBrush(s.OnSurface))
                g.DrawString(_text, Font, b, new PointF(-_offset, 0), StringFormat.GenericTypographic);

            if (_focused && _caretOn)
            {
                using (var b = new SolidBrush(s.OnSurface))
                    g.FillRectangle(b, caretX - _offset, 2, Math.Max(1f, Ui.PX(1.4f)), Height - 4);
            }
            g.ResetClip();
        }
    }
}