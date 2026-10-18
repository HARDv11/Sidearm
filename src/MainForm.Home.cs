using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sidearm
{
    /// <summary>
    /// 0.8: the start page's notes and pinned sites. Both live in data\ (notes.txt, pins.json), so they survive
    /// updates and amnesia mode, which wipes site storage. Only the start page may read or change them.
    /// </summary>
    internal sealed partial class MainForm
    {
        const int MaxPins = 12;
        const int MaxNotesChars = 200000;

        static string NotesPath { get { return Path.Combine(Program.DataDir, "notes.txt"); } }
        static string PinsPath { get { return Path.Combine(Program.DataDir, "pins.json"); } }

        static bool IsStartPage(string source)
        {
            return source != null && source.StartsWith(NewTabUrl, StringComparison.OrdinalIgnoreCase);
        }

        void OnHomeMessage(Tab tab, Dictionary<string, object> m)
        {
            string t = Json.Str(m, "t");
            try
            {
                switch (t)
                {
                    case "home-get":
                        PostToContent(tab, new { t = "notes", text = ReadNotes() });
                        PostToContent(tab, new { t = "pins", list = ReadPins() });
                        break;
                    case "notes-set":
                        WriteNotes(Json.Str(m, "text") ?? "");
                        break;
                    case "pins-set":
                        object raw;
                        m.TryGetValue("list", out raw);
                        var list = new List<Pin>();
                        var arr = raw as System.Collections.IEnumerable;
                        if (arr != null)
                            foreach (var o in arr)
                            {
                                var d = o as Dictionary<string, object>;
                                if (d == null) continue;
                                var p = CleanPin(Json.Str(d, "title"), Json.Str(d, "url"), Json.Str(d, "icon"));
                                if (p != null && list.Count < MaxPins) list.Add(p);
                            }
                        WritePins(list);
                        PushHome(tab);
                        break;
                }
            }
            catch (Exception ex) { Log.Error("Start page: " + t, ex); }
        }

        // ── notes ──

        static string ReadNotes()
        {
            try { return File.Exists(NotesPath) ? File.ReadAllText(NotesPath, Encoding.UTF8) : ""; }
            catch (Exception ex) { Log.Error("notes.txt", ex); return ""; }
        }

        static void WriteNotes(string text)
        {
            if (text.Length > MaxNotesChars) text = text.Substring(0, MaxNotesChars);
            try { File.WriteAllText(NotesPath, text, new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Error("Save notes", ex); }
        }

        /// <summary>"n buy potions" in either bar: one line, stamped with the time, added to the notes.</summary>
        void AppendNote(string text, bool toast)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length == 0) return;
            if (text.Length > 2000) text = text.Substring(0, 2000);
            string notes = ReadNotes();
            string line = "[" + DateTime.Now.ToString("dd.MM HH:mm") + "] " + text;
            notes = notes.Length == 0 ? line : notes.TrimEnd('\r', '\n') + "\n" + line;
            WriteNotes(notes);
            Log.Info("Note added (" + text.Length + " characters)");
            if (toast) Toast("Added to your notes on the start page", null, null);
            PushHome(null);
        }

        /// <summary>Open start pages show the change right away.</summary>
        void PushHome(Tab except)
        {
            string notes = null;
            List<Pin> pins = null;
            foreach (var t in tabs)
            {
                if (t == except || t.View == null || !t.IsNewTab) continue;
                if (notes == null) { notes = ReadNotes(); pins = ReadPins(); }
                PostToContent(t, new { t = "notes", text = notes });
                PostToContent(t, new { t = "pins", list = pins });
            }
        }

        // ── pinned sites ──

        internal sealed class Pin
        {
            public string title { get; set; }
            public string url { get; set; }
            public string icon { get; set; }
        }

        static List<Pin> DefaultPins()
        {
            // two examples so the row isn't empty on day one; remove them with the ×
            return new List<Pin>
            {
                new Pin { title = "HoYoLAB", url = "https://www.hoyolab.com/", icon = "https://www.hoyolab.com/favicon.ico" },
                new Pin { title = "LIGA:ZAKON", url = "https://ligazakon.net/", icon = "https://ligazakon.net/favicon.ico" }
            };
        }

        static List<Pin> ReadPins()
        {
            try
            {
                if (!File.Exists(PinsPath)) return DefaultPins();
                var list = new List<Pin>();
                var arr = Json.ParseAny(File.ReadAllText(PinsPath, Encoding.UTF8)) as System.Collections.IEnumerable;
                if (arr != null)
                    foreach (var o in arr)
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        var p = CleanPin(Json.Str(d, "title"), Json.Str(d, "url"), Json.Str(d, "icon"));
                        if (p != null && list.Count < MaxPins) list.Add(p);
                    }
                return list;
            }
            catch (Exception ex) { Log.Error("pins.json", ex); return new List<Pin>(); }
        }

        static void WritePins(List<Pin> list)
        {
            try { File.WriteAllText(PinsPath, Json.Pretty(Json.Serialize(list)), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Error("Save pins", ex); }
        }

        /// <summary>Only plain web addresses; titles and icons are trimmed. Anything else is dropped.</summary>
        static Pin CleanPin(string title, string url, string icon)
        {
            Uri u;
            url = (url ?? "").Trim();
            if (url.Length == 0 || url.Length > 2000) return null;
            if (!url.Contains("://")) url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "https" && u.Scheme != "http") || u.Host.Length == 0) return null;
            Uri iu;
            icon = (icon ?? "").Trim();
            if (icon.Length > 2000 || !Uri.TryCreate(icon, UriKind.Absolute, out iu) || (iu.Scheme != "https" && iu.Scheme != "http")) icon = "";
            title = (title ?? "").Trim();
            if (title.Length == 0) title = u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
            if (title.Length > 60) title = title.Substring(0, 60).TrimEnd() + "…";
            return new Pin { title = title, url = u.AbsoluteUri, icon = icon };
        }

        /// <summary>Right-click on a page → "Pin to start page".</summary>
        void PinPage(Tab tab)
        {
            if (tab == null || !IsWebUrl(tab.Url) || tab.IsNewTab) return;
            var pins = ReadPins();
            string url = IsTranslated(tab.Url) ? OriginalOf(tab.Url) : tab.Url;
            var p = CleanPin(tab.Title, url, tab.Favicon);
            if (p == null) return;
            if (pins.Any(x => string.Equals(x.url, p.url, StringComparison.OrdinalIgnoreCase))) { Toast("Already on the start page", null, null); return; }
            if (pins.Count >= MaxPins) { Toast("The start page holds 12 sites. Remove one there first", null, null); return; }
            pins.Add(p);
            WritePins(pins);
            Log.Info("Pinned to the start page: " + HostOf(p.url));
            Toast("Pinned to the start page", null, null);
            PushHome(null);
        }
    }
}
