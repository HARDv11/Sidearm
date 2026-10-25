using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Sidearm
{
    /// <summary>
    /// 0.9.5: drawings and marked-up screenshots kept with the notes, in data\notes\drawings\:
    ///   &lt;id&gt;.json      what the drawing page needs to keep editing (its strokes, stamps and text, as it wrote them)
    ///   &lt;id&gt;.bg.png    the screenshot under it (none for a blank sheet)
    ///   &lt;id&gt;.png       the finished picture, for copying and sending
    ///   &lt;id&gt;.jpg       a small thumbnail for the start page
    ///   index.json     id, title, size and time of each, newest first (so the start page doesn't read every drawing)
    /// Only Sidearm's own drawing page and start page reach these, through the shell.
    /// </summary>
    internal static class DrawStore
    {
        public const int MaxDrawings = 400;
        const int MaxDoc = 12 * 1024 * 1024, MaxPng = 60 * 1024 * 1024, MaxThumb = 400 * 1024;

        public sealed class Entry
        {
            public string id { get; set; }
            public string title { get; set; }
            public long updated { get; set; }
            public int w { get; set; }
            public int h { get; set; }
            public bool shot { get; set; }
        }

        static string root;
        public static string Dir
        {
            get { return root ?? Path.Combine(Program.DataDir, "notes", "drawings"); }
            set { root = value; } // tests
        }
        static string IndexPath { get { return Path.Combine(Dir, "index.json"); } }

        public static bool IsId(string id) { return id != null && Regex.IsMatch(id, "^[a-z0-9-]{8,40}$"); }

        public static string NewId()
        {
            var r = new byte[3];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(r);
            return DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + BitConverter.ToString(r).Replace("-", "").ToLowerInvariant();
        }

        public static List<Entry> List()
        {
            try
            {
                if (!File.Exists(IndexPath)) return new List<Entry>();
                var list = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<List<Entry>>(File.ReadAllText(IndexPath, Encoding.UTF8));
                return (list ?? new List<Entry>()).Where(e => e != null && IsId(e.id)).OrderByDescending(e => e.updated).ToList();
            }
            catch (Exception ex) { Log.Error("Drawings index", ex); return new List<Entry>(); }
        }

        static void WriteIndex(List<Entry> list)
        {
            Directory.CreateDirectory(Dir);
            string tmp = IndexPath + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(list.OrderByDescending(e => e.updated).ToList()), new UTF8Encoding(false));
            if (File.Exists(IndexPath)) File.Replace(tmp, IndexPath, null); else File.Move(tmp, IndexPath);
        }

        public static string Thumb(string id)
        {
            string p = Path.Combine(Dir, id + ".jpg");
            try { return IsId(id) && File.Exists(p) ? "data:image/jpeg;base64," + Convert.ToBase64String(File.ReadAllBytes(p)) : ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>What the drawing page needs to reopen a drawing: its document and the screenshot under it.</summary>
        public static Dictionary<string, object> Load(string id)
        {
            if (!IsId(id)) return null;
            string doc = Path.Combine(Dir, id + ".json"), bg = Path.Combine(Dir, id + ".bg.png");
            if (!File.Exists(doc)) return null;
            var entry = List().FirstOrDefault(e => e.id == id);
            return new Dictionary<string, object>
            {
                { "id", id },
                { "title", entry != null ? entry.title : "" },
                { "doc", File.ReadAllText(doc, Encoding.UTF8) },
                { "bg", File.Exists(bg) ? "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(bg)) : "" }
            };
        }

        /// <summary>Saves (or updates) a drawing. bg is only written once: a screenshot never changes under the ink.</summary>
        public static Entry Save(string id, string title, string doc, byte[] flat, byte[] bg, byte[] thumb, int w, int h)
        {
            if (!IsId(id)) throw new ArgumentException("bad id");
            if (doc == null || doc.Length > MaxDoc) throw new ArgumentException("the drawing is too big to keep");
            if (flat == null || flat.Length > MaxPng || !IsPng(flat)) throw new ArgumentException("not a PNG");
            if (bg != null && (bg.Length > MaxPng || !IsPng(bg))) throw new ArgumentException("not a PNG");
            if (thumb != null && (thumb.Length > MaxThumb || !IsJpeg(thumb))) thumb = null;
            var list = List();
            var e = list.FirstOrDefault(x => x.id == id);
            if (e == null && list.Count >= MaxDrawings) throw new InvalidOperationException("You have " + MaxDrawings + " drawings already. Delete a few first");
            Directory.CreateDirectory(Dir);
            WriteAtomic(Path.Combine(Dir, id + ".json"), Encoding.UTF8.GetBytes(doc));
            WriteAtomic(Path.Combine(Dir, id + ".png"), flat);
            string bgPath = Path.Combine(Dir, id + ".bg.png");
            if (bg != null && !File.Exists(bgPath)) WriteAtomic(bgPath, bg);
            if (thumb != null) WriteAtomic(Path.Combine(Dir, id + ".jpg"), thumb);
            if (e == null) { e = new Entry { id = id }; list.Add(e); }
            e.title = CleanTitle(title);
            e.updated = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            e.w = w; e.h = h;
            e.shot = File.Exists(bgPath);
            WriteIndex(list);
            return e;
        }

        public static bool Delete(string id)
        {
            if (!IsId(id)) return false;
            foreach (var ext in new[] { ".json", ".png", ".bg.png", ".jpg" })
                try { File.Delete(Path.Combine(Dir, id + ext)); } catch (Exception) { }
            var list = List();
            int n = list.RemoveAll(x => x.id == id);
            try { WriteIndex(list); } catch (Exception ex) { Log.Error("Drawings index", ex); }
            return n > 0;
        }

        public static byte[] Flat(string id)
        {
            string p = Path.Combine(Dir, id + ".png");
            return IsId(id) && File.Exists(p) ? File.ReadAllBytes(p) : null;
        }

        public static string CleanTitle(string t)
        {
            t = Regex.Replace(t ?? "", @"[\p{C}\s]+", " ").Trim();
            return t.Length > 80 ? t.Substring(0, 80).Trim() : t;
        }

        static void WriteAtomic(string path, byte[] data)
        {
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }

        public static bool IsPng(byte[] b) { return b != null && b.Length > 8 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G'; }
        public static bool IsJpeg(byte[] b) { return b != null && b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8; }

        /// <summary>"data:image/png;base64,…" → bytes, or null if it isn't one of the formats allowed.</summary>
        public static byte[] FromDataUrl(string url, params string[] types)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("data:", StringComparison.Ordinal)) return null;
            int comma = url.IndexOf(',');
            if (comma < 0) return null;
            string head = url.Substring(5, comma - 5);
            if (!head.EndsWith(";base64", StringComparison.Ordinal)) return null;
            string type = head.Substring(0, head.Length - 7);
            if (types.Length > 0 && Array.IndexOf(types, type) < 0) return null;
            try { return Convert.FromBase64String(url.Substring(comma + 1)); }
            catch (Exception) { return null; }
        }
    }
}
