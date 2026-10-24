using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Sidearm
{
    /// <summary>
    /// 0.9: Raven's side tabs, stuck to the outside of Sidearm's window: one per friend who wrote (avatar and unread
    /// count), and the wing for all chats. Drawn by the shell, no web view. On the left of the window, or on the right
    /// when Sidearm sits against the left edge of the screen. Clicking doesn't take the focus.
    /// </summary>
    internal sealed class RavenTabs : Form
    {
        public sealed class Item { public string Key, Tip; public int Avatar = -1, Unread; public bool Open; }

        readonly List<Item> items = new List<Item>();
        readonly ToolTip tip = new ToolTip { InitialDelay = 300, ShowAlways = true };
        static readonly Dictionary<int, Image> avatars = new Dictionary<int, Image>();
        int hover = -1;
        public bool RightSide;
        public event Action<string> Picked;
        public Color Back = Color.FromArgb(0x17, 0x1B, 0x1D), Line = Color.FromArgb(0x34, 0x3C, 0x3F), Ink = Color.FromArgb(0xC9, 0xCC, 0xC5),
            Accent = Color.FromArgb(0xD4, 0xA2, 0x4C), Badge = Color.FromArgb(0x4D, 0x67, 0x27);

        public RavenTabs()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | 0x08000000; // WS_EX_NOACTIVATE: a click doesn't take the focus
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public int Count { get { return items.Count; } }

        public static int TabW(double s) { return (int)Math.Round(46 * s); }
        public static int TabH(double s) { return (int)Math.Round(50 * s); }
        public static int Gap(double s) { return (int)Math.Round(4 * s); }

        public Size Want(double s) { return new Size(TabW(s), items.Count * TabH(s) + Math.Max(0, items.Count - 1) * Gap(s)); }

        public void SetItems(IList<Item> list)
        {
            items.Clear();
            items.AddRange(list);
            hover = -1;
            Invalidate();
        }

        double Dpi { get { return Native.Scale(Handle); } }

        Rectangle TabRect(int i)
        {
            double s = Dpi;
            return new Rectangle(0, i * (TabH(s) + Gap(s)), TabW(s), TabH(s));
        }

        GraphicsPath TabPath(RectangleF r, float rad)
        {
            // rounded on the outside, square against the window
            var p = new GraphicsPath();
            float d = rad * 2;
            if (!RightSide)
            {
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddLine(r.X + rad, r.Y, r.Right, r.Y);
                p.AddLine(r.Right, r.Y, r.Right, r.Bottom);
                p.AddLine(r.Right, r.Bottom, r.X + rad, r.Bottom);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            }
            else
            {
                p.AddLine(r.X, r.Y, r.Right - rad, r.Y);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddLine(r.Right - rad, r.Bottom, r.X, r.Bottom);
            }
            p.CloseFigure();
            return p;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ShapeIt(); }

        /// <summary>Only the tabs are window: between and around them the game shows and clicks go through.</summary>
        public void ShapeIt()
        {
            if (Width < 2 || Height < 2) return;
            float rad = (float)(10 * Dpi);
            var region = new Region(Rectangle.Empty);
            for (int i = 0; i < items.Count; i++)
                using (var p = TabPath(TabRect(i), rad)) region.Union(p);
            var old = Region;
            Region = region;
            if (old != null) old.Dispose();
        }

        static Image AvatarImage(int n)
        {
            Image img;
            if (avatars.TryGetValue(n, out img)) return img;
            try
            {
                string path = Path.Combine(Program.UiDir, "avatars", n + ".png");
                // read into memory: a file held open in ui\ would stop the updater from swapping it
                if (File.Exists(path))
                    using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                    using (var tmp = Image.FromStream(ms)) img = new Bitmap(tmp);
                else img = null;
            }
            catch (Exception) { img = null; }
            avatars[n] = img;
            return img;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = (float)Dpi, rad = 10 * s, lw = Math.Max(1.5f, 2 * s);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var r = TabRect(i);
                var inner = new RectangleF(r.X + lw / 2, r.Y + lw / 2, r.Width - lw, r.Height - lw);
                using (var path = TabPath(inner, rad))
                {
                    if (i == hover) using (var hb = new SolidBrush(Color.FromArgb(36, Accent))) g.FillPath(hb, path);
                    using (var pen = new Pen(it.Open ? Accent : Line, lw)) g.DrawPath(pen, path);
                }
                float cx = r.X + r.Width / 2f + (RightSide ? -2 * s : 2 * s), cy = r.Y + r.Height / 2f;
                if (it.Avatar >= 0)
                {
                    var img = AvatarImage(it.Avatar);
                    float circle = 32 * s, size = circle / 0.68f; // the pictures have room around the circle for what sticks out
                    if (img != null) g.DrawImage(img, cx - size / 2, cy - size / 2, size, size);
                    else using (var b = new SolidBrush(Line)) g.FillEllipse(b, cx - circle / 2, cy - circle / 2, circle, circle);
                }
                else
                {
                    var box = new RectangleF(cx - 12 * s, cy - 12 * s, 24 * s, 24 * s);
                    using (var b = new SolidBrush(i == hover ? Accent : Ink)) GlanceRail.IconWing(g, box, null, b);
                }
                if (it.Unread > 0)
                {
                    string n = it.Unread > 9 ? "9+" : it.Unread.ToString();
                    float d = 17 * s, bx = RightSide ? r.Right - d - 2 * s : r.X + 2 * s, by = r.Y + 2 * s;
                    using (var b = new SolidBrush(Badge)) g.FillEllipse(b, bx, by, d, d);
                    using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(n, f, Brushes.White, new RectangleF(bx, by + 0.5f * s, d, d), sf);
                }
            }
        }

        int HitTest(Point p) { for (int i = 0; i < items.Count; i++) if (TabRect(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                Invalidate();
                tip.SetToolTip(this, h >= 0 ? items[h].Tip : null);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int h = HitTest(e.Location);
            if (h >= 0) { var p = Picked; if (p != null) p(items[h].Key); }
        }

        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
}
