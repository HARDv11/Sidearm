using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Sidearm
{
    /// <summary>
    /// 0.9: Glance's buttons, Zen style: a column of round buttons in the dimmed strip beside the Glance (close, open
    /// here, new tab, your browser, copy link, send to a friend). Drawn by Sidearm itself, not by the page under it.
    /// </summary>
    internal sealed class GlanceRail : Control
    {
        public sealed class Button { public string Op, Tip; public Action<Graphics, RectangleF, Pen, Brush> Icon; }

        readonly List<Button> buttons = new List<Button>();
        readonly ToolTip tip = new ToolTip { InitialDelay = 350, ReshowDelay = 100, ShowAlways = true };
        int hover = -1, down = -1;
        public event Action<string> Command;

        public Color Back = Color.FromArgb(0x17, 0x1B, 0x1D), Line = Color.FromArgb(0x34, 0x3C, 0x3F),
            Ink = Color.FromArgb(0xC9, 0xCC, 0xC5), Accent = Color.FromArgb(0xD4, 0xA2, 0x4C);

        public GlanceRail()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        // A child window can't be see-through over the page's window, so the control is cut to its pill shape:
        // outside the pill, the dimmed page shows.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < 2 || Height < 2) return;
            using (var path = Rounded(new RectangleF(0, 0, Width, Height), Width / 2f))
            {
                var old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        public void SetButtons(IEnumerable<Button> list)
        {
            buttons.Clear();
            buttons.AddRange(list);
            hover = down = -1;
            Invalidate();
        }

        /// <summary>Size of one button, its gap and the padding around the column, for this DPI.</summary>
        public static int Cell(double scale) { return (int)Math.Round(40 * scale); }
        public static int Gap(double scale) { return (int)Math.Round(6 * scale); }
        public static int Pad(double scale) { return (int)Math.Round(5 * scale); }
        public Size Want(double scale)
        {
            int c = Cell(scale), g = Gap(scale), p = Pad(scale);
            return new Size(c + 2 * p, buttons.Count * c + Math.Max(0, buttons.Count - 1) * g + 2 * p);
        }

        RectangleF CellRect(int i)
        {
            double scale = Native.Scale(Handle);
            int c = Cell(scale), g = Gap(scale), p = Pad(scale);
            return new RectangleF(p, p + i * (c + g), c, c);
        }

        int HitTest(Point pt)
        {
            for (int i = 0; i < buttons.Count; i++) if (CellRect(i).Contains(pt)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = (float)Native.Scale(Handle);
            // the column itself: a rounded pill
            var all = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Rounded(all, (Width - 1) / 2f))
            using (var b = new SolidBrush(Back))
            using (var p = new Pen(Line, Math.Max(1, scale)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            for (int i = 0; i < buttons.Count; i++)
            {
                var r = CellRect(i);
                if (i == hover)
                    using (var hb = new SolidBrush(Color.FromArgb(i == down ? 70 : 42, Accent)))
                        g.FillEllipse(hb, r);
                var ink = i == hover ? Accent : Ink;
                float inset = r.Width * 0.29f;
                var icon = new RectangleF(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset);
                using (var pen = new Pen(ink, 1.7f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                using (var brush = new SolidBrush(ink))
                    buttons[i].Icon(g, icon, pen, brush);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                Invalidate();
                if (h >= 0) tip.SetToolTip(this, Lang.T(buttons[h].Tip)); else tip.SetToolTip(this, null);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = down = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = HitTest(e.Location); Invalidate(); } base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            bool fire = e.Button == MouseButtons.Left && h >= 0 && h == down;
            down = -1;
            Invalidate();
            base.OnMouseUp(e);
            if (fire) { var c = Command; if (c != null) c(buttons[h].Op); }
        }

        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ───────────── the icons, drawn in a unit box ─────────────

        static PointF P(RectangleF r, float x, float y) { return new PointF(r.X + r.Width * x, r.Y + r.Height * y); }

        public static void IconClose(Graphics g, RectangleF r, Pen pen, Brush b)
        {
            g.DrawLine(pen, P(r, .12f, .12f), P(r, .88f, .88f));
            g.DrawLine(pen, P(r, .88f, .12f), P(r, .12f, .88f));
        }

        /// <summary>Open here: the Glance grows into the tab (a box with an arrow out to its corner).</summary>
        public static void IconExpand(Graphics g, RectangleF r, Pen pen, Brush b)
        {
            g.DrawLines(pen, new[] { P(r, .55f, .05f), P(r, .95f, .05f), P(r, .95f, .45f) });
            g.DrawLine(pen, P(r, .95f, .05f), P(r, .55f, .45f));
            g.DrawLines(pen, new[] { P(r, .45f, .95f), P(r, .05f, .95f), P(r, .05f, .55f) });
            g.DrawLine(pen, P(r, .05f, .95f), P(r, .45f, .55f));
        }

        public static void IconNewTab(Graphics g, RectangleF r, Pen pen, Brush b)
        {
            using (var path = Rounded(new RectangleF(P(r, .04f, .04f), new SizeF(r.Width * .92f, r.Height * .92f)), r.Width * .22f)) g.DrawPath(pen, path);
            g.DrawLine(pen, P(r, .5f, .3f), P(r, .5f, .7f));
            g.DrawLine(pen, P(r, .3f, .5f), P(r, .7f, .5f));
        }

        /// <summary>Your browser: an arrow leaving a box.</summary>
        public static void IconOut(Graphics g, RectangleF r, Pen pen, Brush b)
        {
            g.DrawLines(pen, new[] { P(r, .42f, .12f), P(r, .1f, .12f), P(r, .1f, .9f), P(r, .88f, .9f), P(r, .88f, .58f) });
            g.DrawLine(pen, P(r, .5f, .5f), P(r, .95f, .05f));
            g.DrawLines(pen, new[] { P(r, .62f, .05f), P(r, .95f, .05f), P(r, .95f, .38f) });
        }

        public static void IconLink(Graphics g, RectangleF r, Pen pen, Brush b)
        {
            var st = g.Save();
            g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
            g.RotateTransform(-45);
            float w = r.Width * .58f, h = r.Height * .38f;
            using (var a = Rounded(new RectangleF(-w + r.Width * .06f, -h / 2, w, h), h / 2)) g.DrawPath(pen, a);
            using (var c = Rounded(new RectangleF(-r.Width * .06f, -h / 2, w, h), h / 2)) g.DrawPath(pen, c);
            g.Restore(st);
        }

        // Knight's wing (Wing.svg, 49 × 52), the send button everywhere in Raven
        const string WingPath = "M19.9767 50.9765C30.1062 51.7595 43.0724 46.3271 48.0343 26.689C37.8531 23.9082 12.6427 12.0489 0.270828 0C-0.0203096 2.40424 -0.0492685 4.48545 0.0552561 6.22643C0.315037 10.5534 3.00547 14.1405 6.54663 16.6405C11.2753 19.9788 18.945 24.757 28.4015 28.3571C11.6717 24.1886 9.99749 23.6146 2.27084 18.9995C2.27084 19.4432 2.28943 19.88 2.32342 20.3077C2.76514 25.8675 7.18006 30.0705 12.4631 31.8582C17.657 33.6158 23.5875 34.8544 27.6436 35.4756C19.7708 37.4995 10.8353 34.644 6.83533 34.144C6.93729 34.6847 7.061 35.1997 7.20135 35.6891C8.67099 40.8134 14.173 43.1603 19.5037 43.1984C22.7664 43.2218 26.217 42.8508 30.2707 41.4996C23.7708 46.9994 17.7305 46.3334 13.2708 45.9994C13.8242 49.0819 16.8543 50.7352 19.9767 50.9765Z";
        static GraphicsPath wing;

        public static void IconWing(Graphics g, RectangleF r, Pen pen, Brush b)
        {
            if (wing == null) wing = ParsePath(WingPath);
            var st = g.Save();
            float k = Math.Min(r.Width / 49f, r.Height / 52f) * 1.12f;
            g.TranslateTransform(r.X + (r.Width - 49 * k) / 2, r.Y + (r.Height - 52 * k) / 2);
            g.ScaleTransform(k, k);
            g.FillPath(b, wing);
            g.Restore(st);
        }

        /// <summary>Just enough of SVG path syntax for the wing: absolute M, C, L and Z.</summary>
        internal static GraphicsPath ParsePath(string d)
        {
            var path = new GraphicsPath();
            var nums = new List<float>();
            char cmd = ' ';
            PointF cur = PointF.Empty, start = PointF.Empty;
            Action flush = () =>
            {
                int i = 0;
                if (cmd == 'M') { while (i + 1 < nums.Count) { cur = new PointF(nums[i], nums[i + 1]); if (i == 0) { path.StartFigure(); start = cur; } else path.AddLine(cur, cur); i += 2; } }
                else if (cmd == 'L') { while (i + 1 < nums.Count) { var p = new PointF(nums[i], nums[i + 1]); path.AddLine(cur, p); cur = p; i += 2; } }
                else if (cmd == 'C') { while (i + 5 < nums.Count) { var p = new PointF(nums[i + 4], nums[i + 5]); path.AddBezier(cur, new PointF(nums[i], nums[i + 1]), new PointF(nums[i + 2], nums[i + 3]), p); cur = p; i += 6; } }
                else if (cmd == 'Z') { path.CloseFigure(); cur = start; }
                nums.Clear();
            };
            foreach (Match m in Regex.Matches(d, @"[MCLZmclz]|-?\d*\.?\d+(?:e-?\d+)?"))
            {
                string t = m.Value;
                if (char.IsLetter(t[0]) && t.Length == 1) { flush(); cmd = char.ToUpperInvariant(t[0]); if (cmd == 'Z') flush(); }
                else nums.Add(float.Parse(t, CultureInfo.InvariantCulture));
            }
            flush();
            return path;
        }
    }
}
