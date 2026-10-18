using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sidearm
{
    /// <summary>
    /// A popped-out image: its own small always-on-top window, handy for keeping a map or a diagram
    /// up while you play. Scroll to zoom, drag to pan, double-click for fit / 1:1, R rotates, Esc closes.
    /// </summary>
    internal sealed class ImageWindow : Form
    {
        Image image;
        float zoom = 1f, fitZoom = 1f;
        PointF offset;
        Point dragFrom;
        PointF dragOffset;
        bool dragging, fitted = true;
        readonly Color back;

        public ImageWindow(Image img, string title, bool light)
        {
            image = img;
            back = light ? Color.FromArgb(0xEB, 0xE8, 0xE0) : Color.FromArgb(0x0D, 0x0F, 0x10);
            Text = "Sidearm · " + title;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = back;
            KeyPreview = true;
            MinimumSize = new Size(200, 150);

            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            double scale = Math.Min(1.0, Math.Min(wa.Width * 0.5 / img.Width, wa.Height * 0.6 / img.Height));
            var size = new Size(Math.Max(260, (int)(img.Width * scale)) + 16, Math.Max(200, (int)(img.Height * scale)) + 40);
            Bounds = new Rectangle(Math.Max(wa.Left, Cursor.Position.X - size.Width / 2), Math.Max(wa.Top, Cursor.Position.Y - size.Height / 2), size.Width, size.Height);
            try { Icon = new Icon(System.IO.Path.Combine(Program.AppDir, "sidearm.ico"), SystemInformation.SmallIconSize); } catch (Exception) { }
            Fit();
        }

        void Fit()
        {
            if (image == null || ClientSize.Width < 1) return;
            fitZoom = Math.Min(1f, Math.Min((float)ClientSize.Width / image.Width, (float)ClientSize.Height / image.Height));
            zoom = fitZoom;
            offset = new PointF((ClientSize.Width - image.Width * zoom) / 2f, (ClientSize.Height - image.Height * zoom) / 2f);
            fitted = true;
            Invalidate();
        }

        void ZoomAt(float factor, Point at)
        {
            float next = Math.Max(0.05f, Math.Min(16f, zoom * factor));
            offset = new PointF(at.X - (at.X - offset.X) * next / zoom, at.Y - (at.Y - offset.Y) * next / zoom);
            zoom = next;
            fitted = false;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (fitted) Fit(); else Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(back);
            if (image == null) return;
            e.Graphics.InterpolationMode = zoom < 1 ? InterpolationMode.HighQualityBicubic : (zoom >= 3 ? InterpolationMode.NearestNeighbor : InterpolationMode.Bicubic);
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(image, offset.X, offset.Y, image.Width * zoom, image.Height * zoom);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ZoomAt(e.Delta > 0 ? 1.2f : 1 / 1.2f, e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragFrom = e.Location;
            dragOffset = offset;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            offset = new PointF(dragOffset.X + e.X - dragFrom.X, dragOffset.Y + e.Y - dragFrom.Y);
            fitted = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (fitted) ZoomAt(1f / zoom, e.Location); else Fit();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.R:
                    image.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    Fit();
                    return true;
                case Keys.D0: case Keys.NumPad0: Fit(); return true;
                case Keys.Oemplus: case Keys.Add: ZoomAt(1.2f, new Point(ClientSize.Width / 2, ClientSize.Height / 2)); return true;
                case Keys.OemMinus: case Keys.Subtract: ZoomAt(1 / 1.2f, new Point(ClientSize.Width / 2, ClientSize.Height / 2)); return true;
                case Keys.Control | Keys.C:
                    try { Clipboard.SetImage(image); } catch (Exception) { }
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (image != null) { image.Dispose(); image = null; }
        }
    }
}
