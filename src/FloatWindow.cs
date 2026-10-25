using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>
    /// 0.9: what the video pop-out and the page pop-out share. A small always-on-top window that can go ghost
    /// (see-through, clicks pass through) together with Sidearm.
    /// 0.9.5: no Windows title bar any more. Sidearm draws its own strip on top (the title, back to Sidearm, ghost,
    /// close): drag it to move, the wheel over it sets the opacity, right-click it for the menu; the edges resize.
    /// </summary>
    internal class FloatWindow : Form
    {
        static readonly int[] Levels = { 100, 85, 70, 55 };

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);

        readonly List<KeyValuePair<string, Action>> actions = new List<KeyValuePair<string, Action>>();
        readonly FloatBar bar;
        double solid = 1.0;
        bool ghost;
        public double GhostOpacity = 0.6;
        public WebView2 View;
        /// <summary>The ghost button: the shell turns ghost on for every pop-out (and keeps Alt+G to come back).</summary>
        public event Action GhostRequested;
        /// <summary>The ↩ button; only shown when something listens.</summary>
        public event Action BackRequested;

        public FloatWindow(string title, Rectangle bounds, Color back)
        {
            Text = "Sidearm · " + title;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(0x11, 0x14, 0x16);
            KeyPreview = true;
            MinimumSize = new Size(240, 160);
            Bounds = bounds;
            try { Icon = new Icon(System.IO.Path.Combine(Program.AppDir, "sidearm.ico"), SystemInformation.SmallIconSize); } catch (Exception) { }
            bar = new FloatBar(this) { Dock = DockStyle.Top, Title = title };
            Controls.Add(bar);
            Padding = new Padding(3, 3, 3, 3);
        }

        /// <summary>The strip's colors: Sidearm's frame (with your tint), its line, text, dim text and accent.</summary>
        public void Theme(Color frame, Color line, Color ink, Color dim, Color accent)
        {
            BackColor = frame;
            bar.Back = frame; bar.Line = line; bar.Ink = ink; bar.Dim = dim; bar.Accent = accent;
            bar.Invalidate();
        }

        public void SetTitle(string title)
        {
            Text = "Sidearm · " + title;
            bar.Title = title;
            bar.Invalidate();
        }

        /// <summary>The views go below the strip (call after adding the view, so the strip docks first).</summary>
        protected void PlaceView(Control v)
        {
            v.Dock = DockStyle.Fill;
            Controls.Add(v);
            v.BringToFront(); // Fill docks last: the strip keeps the top
        }

        /// <summary>An item for the strip's menu, above the opacity levels.</summary>
        protected void AddAction(string label, Action run) { actions.Add(new KeyValuePair<string, Action>(label, run)); }

        internal bool HasBack { get { return BackRequested != null; } }
        internal void RaiseBack() { var h = BackRequested; if (h != null) h(); }
        internal void RaiseGhost() { var h = GhostRequested; if (h != null) h(); }

        public bool Ghost { get { return ghost; } }
        public double Solid { get { return solid; } }

        /// <summary>See-through and click-through, like Sidearm's ghost mode (opacity first, then click-through).</summary>
        public void SetGhost(bool on)
        {
            if (on == ghost || IsDisposed) return;
            if (on)
            {
                solid = Opacity;
                Opacity = Math.Max(0.2, Math.Min(0.9, GhostOpacity));
                ghost = true;
                UpdateStyles();
            }
            else
            {
                ghost = false;
                Opacity = solid;
                UpdateStyles();
            }
            bar.Invalidate();
        }

        internal void SetSolid(double value)
        {
            solid = Math.Round(Math.Max(0.3, Math.Min(1.0, value)), 2);
            if (!ghost) Opacity = solid;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                if (ghost) cp.ExStyle |= 0x00000020 | 0x00080000; // WS_EX_TRANSPARENT | WS_EX_LAYERED
                return cp;
            }
        }

        /// <summary>Shows it again after a holster without taking the focus from whatever you're in.</summary>
        public void ShowQuietly()
        {
            if (IsDisposed || Visible) return;
            ShowWindow(Handle, 8); // SW_SHOWNA
            Visible = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            double s = Native.Scale(Handle);
            int b = Math.Max(3, (int)Math.Round(4 * s));
            Padding = new Padding(b, 0, b, b);
            bar.Height = (int)Math.Round(30 * s);
        }

        internal void ShowMenu(Point at)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
            foreach (var a in actions)
            {
                var run = a.Value;
                menu.Items.Add(Lang.T(a.Key), null, (s, e) => BeginInvoke(run));
            }
            if (actions.Count > 0) menu.Items.Add(new ToolStripSeparator());
            foreach (int level in Levels)
            {
                int lv = level;
                var item = new ToolStripMenuItem(Lang.T("Opacity " + lv + "%")) { Checked = Math.Abs(solid * 100 - lv) < 1 };
                item.Click += (s, e) => SetSolid(lv / 100.0);
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("Close"), null, (s, e) => BeginInvoke(new Action(Close)));
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(this, PointToClient(at));
        }

        internal void BeginMove()
        {
            Native.ReleaseCapture();
            var c = Cursor.Position;
            Native.PostMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, (IntPtr)((c.Y << 16) | (c.X & 0xFFFF)));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (m.Result.ToInt32() == Native.HTCLIENT && WindowState == FormWindowState.Normal) m.Result = (IntPtr)Edge(m.LParam);
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>The thin frame around the page resizes the window; the strip's top edge too.</summary>
        int Edge(IntPtr lParam)
        {
            long lp = lParam.ToInt64();
            var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            int b = Math.Max(Padding.Left, 4) + 2, w = ClientSize.Width, h = ClientSize.Height;
            bool left = p.X < b, right = p.X >= w - b, top = p.Y < b, bottom = p.Y >= h - b;
            if (top && left) return Native.HTTOPLEFT;
            if (top && right) return Native.HTTOPRIGHT;
            if (bottom && left) return Native.HTBOTTOMLEFT;
            if (bottom && right) return Native.HTBOTTOMRIGHT;
            if (left) return Native.HTLEFT;
            if (right) return Native.HTRIGHT;
            if (top) return Native.HTTOP;
            if (bottom) return Native.HTBOTTOM;
            return Native.HTCLIENT;
        }
    }

    /// <summary>0.9.5: the pop-out's own strip, drawn like Sidearm's: reticle, title, then ↩, ghost and ×.</summary>
    internal sealed class FloatBar : Control
    {
        readonly FloatWindow owner;
        readonly ToolTip tip = new ToolTip { InitialDelay = 400, ShowAlways = true };
        public string Title = "";
        public Color Back = Color.FromArgb(0x11, 0x14, 0x16), Line = Color.FromArgb(0x2B, 0x31, 0x33), Ink = Color.FromArgb(0xC9, 0xCC, 0xC5),
            Dim = Color.FromArgb(0x8A, 0x90, 0x8A), Accent = Color.FromArgb(0xD4, 0xA2, 0x4C);
        int hover = -1, down = -1;
        Point press;
        bool pressed;

        public FloatBar(FloatWindow owner)
        {
            this.owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 30;
        }

        double Dpi { get { return IsHandleCreated ? Native.Scale(Handle) : 1.0; } }

        string[] Ops { get { return owner.HasBack ? new[] { "back", "ghost", "close" } : new[] { "ghost", "close" }; } }

        static string TipFor(string op)
        {
            switch (op)
            {
                case "back": return Lang.T("Back to Sidearm  (Alt+Shift+P)");
                case "ghost": return Lang.T("Ghost: see-through, clicks go to the game (Alt+G brings it back)");
                default: return Lang.T("Close");
            }
        }

        Rectangle ButtonRect(int i)
        {
            double s = Dpi;
            int bw = (int)Math.Round(30 * s), bh = (int)Math.Round(24 * s), gap = (int)Math.Round(2 * s);
            int n = Ops.Length, right = Width - (int)Math.Round(3 * s);
            int x = right - (n - i) * bw - (n - 1 - i) * gap;
            return new Rectangle(x, (Height - bh) / 2, bw, bh);
        }

        int Hit(Point p) { for (int i = 0; i < Ops.Length; i++) if (ButtonRect(i).Contains(p)) return i; return -1; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float s = (float)Dpi;
            // the reticle, Sidearm's mark
            float cx = 15 * s, cy = Height / 2f, r = 5.5f * s;
            using (var pen = new Pen(Accent, 1.5f * s))
            {
                g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
                g.DrawLine(pen, cx, cy - r - 2.5f * s, cx, cy - r + 2.5f * s);
                g.DrawLine(pen, cx, cy + r - 2.5f * s, cx, cy + r + 2.5f * s);
                g.DrawLine(pen, cx - r - 2.5f * s, cy, cx - r + 2.5f * s, cy);
                g.DrawLine(pen, cx + r - 2.5f * s, cy, cx + r + 2.5f * s, cy);
            }
            int textLeft = (int)Math.Round(28 * s), textRight = ButtonRect(0).Left - (int)Math.Round(6 * s);
            if (textRight > textLeft)
                using (var f = new Font("Segoe UI", 9f))
                    TextRenderer.DrawText(g, owner.Ghost ? "◌ " + Title : Title, f, new Rectangle(textLeft, 0, textRight - textLeft, Height), Dim,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            var ops = Ops;
            for (int i = 0; i < ops.Length; i++)
            {
                var b = ButtonRect(i);
                bool hot = i == hover, isClose = ops[i] == "close";
                if (hot)
                    using (var path = Rounded(b, 5 * s))
                    using (var hb = new SolidBrush(isClose ? Color.FromArgb(0xC4, 0x2B, 0x1C) : Color.FromArgb(40, Ink))) g.FillPath(hb, path);
                var ink = hot ? (isClose ? Color.White : Ink) : Dim;
                float icon = 11 * s;
                var box = new RectangleF(b.X + (b.Width - icon) / 2f, b.Y + (b.Height - icon) / 2f, icon, icon);
                using (var pen = new Pen(ink, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    if (isClose) GlanceRail.IconClose(g, box, pen, null);
                    else if (ops[i] == "back") IconBack(g, box, pen);
                    else IconGhost(g, box, pen, owner.Ghost ? Accent : ink);
                }
            }
            using (var lp = new Pen(Line, 1)) g.DrawLine(lp, 0, Height - 1, Width, Height - 1);
        }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        /// <summary>↩: back into Sidearm.</summary>
        static void IconBack(Graphics g, RectangleF r, Pen pen)
        {
            using (var p = new GraphicsPath())
            {
                p.AddLine(r.Right, r.Y + r.Height * .1f, r.Right, r.Y + r.Height * .45f);
                p.AddArc(r.X + r.Width * .35f, r.Y + r.Height * .1f, r.Width * .65f, r.Height * .7f, 0, 90);
                p.AddLine(r.X + r.Width * .68f, r.Y + r.Height * .8f, r.X, r.Y + r.Height * .8f);
                g.DrawPath(pen, p);
            }
            g.DrawLines(pen, new[] { new PointF(r.X + r.Width * .3f, r.Y + r.Height * .5f), new PointF(r.X, r.Y + r.Height * .8f), new PointF(r.X + r.Width * .3f, r.Bottom + r.Height * .1f) });
        }

        /// <summary>A little ghost: rounded head, wavy hem, two eyes.</summary>
        static void IconGhost(Graphics g, RectangleF r, Pen pen, Color eyes)
        {
            using (var p = new GraphicsPath())
            {
                float w = r.Width, h = r.Height, x = r.X, y = r.Y;
                p.AddArc(x, y - h * .05f, w, w, 180, 180);
                p.AddLine(x + w, y + w * .45f, x + w, y + h);
                p.AddLine(x + w, y + h, x + w * .75f, y + h * .82f);
                p.AddLine(x + w * .75f, y + h * .82f, x + w * .5f, y + h);
                p.AddLine(x + w * .5f, y + h, x + w * .25f, y + h * .82f);
                p.AddLine(x + w * .25f, y + h * .82f, x, y + h);
                p.CloseFigure();
                g.DrawPath(pen, p);
            }
            using (var b = new SolidBrush(eyes))
            {
                float d = r.Width * .16f;
                g.FillEllipse(b, r.X + r.Width * .3f - d / 2, r.Y + r.Height * .4f - d / 2, d, d);
                g.FillEllipse(b, r.X + r.Width * .7f - d / 2, r.Y + r.Height * .4f - d / 2, d, d);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != hover) { hover = h; Invalidate(); tip.SetToolTip(this, h >= 0 ? TipFor(Ops[h]) : null); }
            if (pressed && down < 0 && (e.Button & MouseButtons.Left) != 0 && Math.Abs(e.X - press.X) + Math.Abs(e.Y - press.Y) > 3)
            {
                pressed = false;
                owner.BeginMove();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            down = Hit(e.Location);
            press = e.Location;
            pressed = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            pressed = false;
            if (e.Button == MouseButtons.Right) { owner.ShowMenu(PointToScreen(e.Location)); return; }
            if (e.Button != MouseButtons.Left) return;
            int h = Hit(e.Location);
            if (h >= 0 && h == down)
            {
                string op = Ops[h];
                if (op == "close") owner.BeginInvoke(new Action(owner.Close));
                else if (op == "back") owner.BeginInvoke(new Action(owner.RaiseBack));
                else owner.BeginInvoke(new Action(owner.RaiseGhost));
            }
            down = -1;
        }

        protected override void WndProc(ref Message m)
        {
            // the strip's top few pixels belong to the window's frame: resizing from the top works there
            if (m.Msg == Native.WM_NCHITTEST)
            {
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                if (p.Y < Math.Max(3, (int)Math.Round(4 * Dpi)) && owner.WindowState == FormWindowState.Normal) { m.Result = (IntPtr)(-1); return; } // HTTRANSPARENT
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            owner.SetSolid(owner.Solid + (e.Delta > 0 ? 0.05 : -0.05)); // the wheel over the strip: opacity, like Ctrl + wheel on Sidearm's
        }

        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
}
