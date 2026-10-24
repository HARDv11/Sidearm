using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sidearm
{
    /// <summary>
    /// 0.9: the last things you copied inside Sidearm (text and pictures), so a copy doesn't wipe the one before it.
    /// Memory only: nothing is ever written to disk, and quitting forgets it all. Sidearm doesn't listen to the
    /// clipboard: while it's on screen and in front, it compares Windows' clipboard counter now and then, and only
    /// reads the clipboard when that number moved while Sidearm was the window you were using.
    /// </summary>
    internal sealed class ClipHistory
    {
        public sealed class Item
        {
            public int Id;
            public string Text;        // text items
            public byte[] Png;         // picture items, kept compressed
            public string Thumb;       // a small JPEG data URL for the list
            public int W, H;
            public DateTime At;
        }

        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();

        public const int MaxItems = 10, MaxPictures = 3;
        const int MaxText = 100000, MaxPng = 6 * 1024 * 1024;

        readonly List<Item> items = new List<Item>();
        uint lastSeq;
        int nextId;

        public IList<Item> Items { get { return items; } }
        public event Action Changed;

        /// <summary>What's on the clipboard right now came from somewhere else: don't take it.</summary>
        public void Baseline() { lastSeq = GetClipboardSequenceNumber(); }

        /// <summary>Called on a short timer while Sidearm is on screen. <paramref name="ours"/>: is Sidearm in front?
        /// (asked only when the counter moved)</summary>
        public void Check(Func<bool> ours)
        {
            uint seq = GetClipboardSequenceNumber();
            if (seq == lastSeq) return;
            lastSeq = seq;
            if (!ours()) return;
            try { Take(); }
            catch (Exception ex) { Log.Error("Clipboard history", ex); }
        }

        void Take()
        {
            if (Clipboard.ContainsText())
            {
                string t = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t)) return;
                if (t.Length > MaxText) t = t.Substring(0, MaxText);
                AddText(t);
            }
            else if (Clipboard.ContainsImage())
            {
                using (var img = Clipboard.GetImage())
                    if (img != null) AddPicture(img);
            }
        }

        public void AddText(string t)
        {
            items.RemoveAll(i => i.Text != null && i.Text == t);
            items.Insert(0, new Item { Id = ++nextId, Text = t, At = DateTime.Now });
            Trim();
        }

        public void AddPicture(Image img)
        {
            byte[] png;
            using (var ms = new MemoryStream())
            {
                img.Save(ms, ImageFormat.Png);
                png = ms.ToArray();
            }
            if (png.Length > MaxPng) { Log.Info("Clipboard history: a picture of " + (png.Length / 1048576) + " MB is too big to keep"); return; }
            items.RemoveAll(i => i.Png != null && i.Png.Length == png.Length && i.Png.SequenceEqual(png));
            items.Insert(0, new Item { Id = ++nextId, Png = png, Thumb = Thumbnail(img), W = img.Width, H = img.Height, At = DateTime.Now });
            Trim();
        }

        void Trim()
        {
            // pictures are the heavy ones: only the newest few stay
            int pics = 0;
            for (int i = 0; i < items.Count; i++)
                if (items[i].Png != null && ++pics > MaxPictures) { items.RemoveAt(i); i--; }
            while (items.Count > MaxItems) items.RemoveAt(items.Count - 1);
            var h = Changed; if (h != null) h();
        }

        /// <summary>Puts an item back on the clipboard and at the top of the list.</summary>
        public bool Restore(int id)
        {
            var it = items.FirstOrDefault(i => i.Id == id);
            if (it == null) return false;
            try
            {
                if (it.Text != null) Clipboard.SetText(it.Text);
                else using (var ms = new MemoryStream(it.Png)) using (var img = Image.FromStream(ms)) Clipboard.SetImage(img);
            }
            catch (Exception ex) { Log.Error("Clipboard history: restore", ex); return false; }
            lastSeq = GetClipboardSequenceNumber(); // our own copy: don't take it in again
            items.Remove(it);
            items.Insert(0, it);
            var h = Changed; if (h != null) h();
            return true;
        }

        public void Clear()
        {
            if (items.Count == 0) return;
            items.Clear();
            var h = Changed; if (h != null) h();
        }

        /// <summary>For the bar's list: short text and thumbnails, never the full pictures.</summary>
        public List<object> Snapshot()
        {
            return items.Select(i => (object)new
            {
                id = i.Id,
                kind = i.Text != null ? "text" : "image",
                text = i.Text == null ? "" : (i.Text.Length > 300 ? i.Text.Substring(0, 300) : i.Text),
                chars = i.Text == null ? 0 : i.Text.Length,
                thumb = i.Thumb ?? "",
                w = i.W, h = i.H,
                at = i.At.ToString("HH:mm")
            }).ToList();
        }

        static string Thumbnail(Image img)
        {
            try
            {
                double k = Math.Min(1.0, 96.0 / Math.Max(img.Width, img.Height));
                int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
                using (var bmp = new Bitmap(w, h))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, 0, 0, w, h);
                    }
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Jpeg);
                        return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch (Exception) { return ""; }
        }
    }
}
