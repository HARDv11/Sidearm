using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Sidearm
{
    /// <summary>
    /// 0.9.5: screenshots (Alt + S) and the drawing page (ui\draw.html). The page picks the part (an area, what you
    /// see, or the whole page with its scroll), Sidearm takes the picture through the engine (DevTools' screenshot,
    /// so it works on any page), then copies it, saves it, or opens it for drawing on. Drawings live with the notes.
    /// </summary>
    internal sealed partial class MainForm
    {
        string shotToken;
        Tab shotTab;
        DateTime shotAt;
        bool shotOpened;
        readonly Dictionary<string, Dictionary<string, object>> pendingDraws = new Dictionary<string, Dictionary<string, object>>();
        string lastSavedFile;

        static string DrawUrl { get { return UiOrigin + "draw.html"; } }
        static bool IsDrawPage(string source) { return source != null && source.StartsWith(DrawUrl, StringComparison.OrdinalIgnoreCase); }

        string ShotDir
        {
            get
            {
                string d = (S.shotFolder ?? "").Trim();
                if (d.Length > 0 && Path.IsPathRooted(d)) return d;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Sidearm");
            }
        }

        /// <summary>Alt + S: the page shows its picker. A page that can't (a PDF, a page still loading) gets "what you see".</summary>
        async void StartShot(Tab tab)
        {
            try
            {
                if (tab == null || tab.View == null || tab.View.CoreWebView2 == null) { Toast("Nothing to take a picture of", null, null); return; }
                if (!Visible) await DrawAsync();
                CancelLookup();
                shotToken = NewMsgId();
                shotTab = tab;
                shotAt = DateTime.UtcNow;
                shotOpened = false;
                PostToContent(tab, new { t = "shot-pick", token = shotToken, raven = RavenReady && raven.Friends.Any(f => f.status == "friend") });
                try { tab.View.Focus(); } catch (Exception) { }
                await Task.Delay(700);
                if (!shotOpened && shotTab == tab && shotToken != null)
                {
                    Log.Info("Screenshot: the page didn't show its picker; taking what you see");
                    shotToken = null;
                    var png = await Capture(tab, "visible", 0, 0, 0, 0, 0, 0);
                    if (png != null) OpenDraw(png, tab);
                }
            }
            catch (Exception ex) { Log.Error("Screenshot", ex); }
        }

        /// <summary>The picker's answer. Only the tab that was asked, with the token it was given, a few minutes at most.</summary>
        async void OnShotMessage(Tab tab, Dictionary<string, object> m)
        {
            string t = Json.Str(m, "t");
            if (t == "shot-open") { if (tab == shotTab && Json.Str(m, "token") == shotToken) shotOpened = true; return; }
            if (tab != shotTab || shotToken == null || Json.Str(m, "token") != shotToken || (DateTime.UtcNow - shotAt).TotalMinutes > 5) return;
            shotToken = null;
            string kind = Json.Str(m, "kind"), op = Json.Str(m, "op") ?? "draw";
            try
            {
                double dpr = Math.Max(0.5, Math.Min(4, Json.Num(m, "dpr", 1)));
                var png = await Capture(tab, kind, Json.Num(m, "x", 0), Json.Num(m, "y", 0), Json.Num(m, "w", 0), Json.Num(m, "h", 0), Json.Num(m, "sx", 0), Json.Num(m, "sy", 0));
                if (png == null) { Toast("Couldn't take the screenshot", null, null); return; }
                Log.Info("Screenshot: " + kind + " → " + op + " (" + (png.Length / 1024) + " KB)");
                switch (op)
                {
                    case "copy": CopyPng(png); Toast("Screenshot copied", null, null); break;
                    case "save": SavePng(png, tab.Title); break;
                    case "send": OpenDraw(png, tab, "send"); break;
                    default: OpenDraw(png, tab); break;
                }
            }
            catch (Exception ex) { Log.Error("Screenshot " + kind, ex); Toast("Couldn't take the screenshot: " + ex.Message, null, null); }
        }

        /// <summary>
        /// The picture itself, through DevTools (works whatever the page is). Coordinates are the page's (CSS pixels);
        /// the result has the screen's sharpness. The whole page is captured with its scroll, kept under 16k pixels tall.
        /// </summary>
        static async Task<byte[]> Capture(Tab tab, string kind, double x, double y, double w, double h, double sx, double sy)
        {
            var core = tab.View != null ? tab.View.CoreWebView2 : null;
            if (core == null) return null;
            string args;
            if (kind == "area" && w >= 2 && h >= 2)
                args = Json.Serialize(new { format = "png", fromSurface = true, captureBeyondViewport = false, clip = new { x = Math.Round(sx + x, 1), y = Math.Round(sy + y, 1), width = Math.Round(w, 1), height = Math.Round(h, 1), scale = 1 } });
            else if (kind == "full")
            {
                var metrics = Json.Parse(await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
                object co; metrics.TryGetValue("cssContentSize", out co);
                var cs = co as Dictionary<string, object>;
                if (cs == null) { metrics.TryGetValue("contentSize", out co); cs = co as Dictionary<string, object>; }
                double cw = Math.Max(1, Json.Num(cs, "width", 1200)), ch = Math.Max(1, Json.Num(cs, "height", 800));
                object vo; metrics.TryGetValue("cssVisualViewport", out vo);
                double dpr = 1;
                try { dpr = Json.Num(Json.Parse(await core.ExecuteScriptAsync("JSON.stringify({d:window.devicePixelRatio})")), "d", 1); } catch (Exception) { }
                // a very long page: smaller, not cut off (and never more than ~40 million pixels)
                double scale = Math.Min(1.0, Math.Min(16000 / (ch * dpr), Math.Sqrt(40e6 / (cw * ch * dpr * dpr))));
                if (ch > 60000) ch = 60000;
                args = Json.Serialize(new { format = "png", fromSurface = true, captureBeyondViewport = true, clip = new { x = 0, y = 0, width = Math.Round(cw), height = Math.Round(ch), scale = Math.Round(scale, 4) } });
            }
            else args = Json.Serialize(new { format = "png", fromSurface = true });
            var r = Json.Parse(await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", args));
            string data = Json.Str(r, "data");
            if (string.IsNullOrEmpty(data)) return null;
            var png = Convert.FromBase64String(data);
            return DrawStore.IsPng(png) ? png : null;
        }

        // ── what happens to a picture ──

        static void CopyPng(byte[] png)
        {
            // as a picture for most apps, and as PNG for the ones that keep transparency and full quality
            using (var ms = new MemoryStream(png))
            using (var img = Image.FromStream(ms))
            {
                var data = new DataObject();
                data.SetImage(new Bitmap(img));
                data.SetData("PNG", false, new MemoryStream(png));
                Clipboard.SetDataObject(data, true);
            }
        }

        void SavePng(byte[] png, string title)
        {
            try
            {
                string dir = ShotDir;
                Directory.CreateDirectory(dir);
                string name = SafeFileName(title, "Sidearm") + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
                string path = Path.Combine(dir, name + ".png");
                for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, name + " (" + i + ").png");
                File.WriteAllBytes(path, png);
                lastSavedFile = path;
                Toast("Saved: " + Path.GetFileName(path), "Show", "showSaved");
                Log.Info("Screenshot saved to " + (dir == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Sidearm") ? "Pictures\\Sidearm" : "your folder"));
            }
            catch (Exception ex) { Log.Error("Save picture", ex); Toast("Couldn't save: " + ex.Message, null, null); }
        }

        static string SafeFileName(string s, string fallback)
        {
            s = (s ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, ' ');
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim().TrimEnd('.');
            if (s.Length > 60) s = s.Substring(0, 60).Trim();
            return s.Length == 0 ? fallback : s;
        }

        void ShowSavedFile()
        {
            if (string.IsNullOrEmpty(lastSavedFile)) return;
            try
            {
                if (File.Exists(lastSavedFile)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + lastSavedFile + "\"");
                else if (Directory.Exists(lastSavedFile)) System.Diagnostics.Process.Start("explorer.exe", "\"" + lastSavedFile + "\"");
            }
            catch (Exception ex) { Log.Error("Show file", ex); }
        }

        /// <summary>Opens the drawing page: on a fresh screenshot (png), a saved drawing (id), or a blank sheet.</summary>
        void OpenDraw(byte[] png, Tab from, string then = null)
        {
            OpenDrawData(png != null ? "data:image/png;base64," + Convert.ToBase64String(png) : "", from != null ? from.Title : "", from != null && IsWebUrl(from.Url) ? from.Url : "", then);
        }

        /// <summary>The drawing page on any picture (a data URL): a screenshot, or a picture a friend sent.</summary>
        void OpenDrawData(string dataUrl, string title, string url, string then)
        {
            string token = NewMsgId().Substring(0, 12).Replace('_', 'x').Replace('-', 'y');
            pendingDraws[token] = new Dictionary<string, object>
            {
                { "bg", dataUrl ?? "" },
                { "title", Trim(title ?? "", 80) },
                { "url", url ?? "" },
                { "then", then ?? "" }
            };
            while (pendingDraws.Count > 4) pendingDraws.Remove(pendingDraws.Keys.First());
            Navigate(DrawUrl + "?shot=" + token, true);
        }

        void OpenDrawing(string id)
        {
            if (id != null && DrawStore.IsId(id)) Navigate(DrawUrl + "?id=" + id, true);
            else Navigate(DrawUrl + "?new=1", true);
        }

        object DrawingsView(int max)
        {
            return DrawStore.List().Take(max).Select(e => (object)new { id = e.id, title = e.title, updated = e.updated, shot = e.shot, thumb = DrawStore.Thumb(e.id) }).ToList();
        }

        void PushDrawingsToStartPages()
        {
            var list = DrawingsView(12);
            int total = DrawStore.List().Count;
            foreach (var t in tabs) if (t.View != null && t.IsNewTab) PostToContent(t, new { t = "drawings", list = list, total = total });
        }

        object DrawConfig()
        {
            string onAcc, acc = ReadableAccent(out onAcc);
            return new
            {
                accent = acc, onAccent = onAcc, theme = ThemeName,
                raven = RavenReady ? raven.Friends.Where(f => f.status == "friend").OrderByDescending(f => f.pinned).ThenByDescending(f => f.lastAt)
                    .Select(f => (object)new { key = f.key, name = f.Shown, av = f.av }).ToList() : new List<object>(),
                ravenTitle = RavenTitle, folder = ShotDir
            };
        }

        /// <summary>Only Sidearm's own drawing page gets here (checked by its address).</summary>
        async void OnDrawMessage(Tab tab, Dictionary<string, object> m)
        {
            string t = Json.Str(m, "t");
            try
            {
                switch (t)
                {
                    case "draw-get":
                    {
                        string shot = Json.Str(m, "shot"), id = Json.Str(m, "id");
                        Dictionary<string, object> pending;
                        if (shot != null && pendingDraws.TryGetValue(shot, out pending))
                        {
                            pendingDraws.Remove(shot);
                            PostToContent(tab, new { t = "draw-load", id = DrawStore.NewId(), fresh = true, title = Json.Str(pending, "title"), bg = Json.Str(pending, "bg"), url = Json.Str(pending, "url"), then = Json.Str(pending, "then"), doc = "", cfg = DrawConfig() });
                        }
                        else if (id != null && DrawStore.IsId(id))
                        {
                            var d = DrawStore.Load(id);
                            if (d == null) PostToContent(tab, new { t = "draw-load", id = DrawStore.NewId(), fresh = true, missing = true, title = "", bg = "", doc = "", cfg = DrawConfig() });
                            else PostToContent(tab, new { t = "draw-load", id = id, fresh = false, title = d["title"], bg = d["bg"], doc = d["doc"], cfg = DrawConfig() });
                        }
                        else PostToContent(tab, new { t = "draw-load", id = DrawStore.NewId(), fresh = true, title = "", bg = "", doc = "", cfg = DrawConfig() });
                        break;
                    }
                    case "draw-save":
                    {
                        string id = Json.Str(m, "id");
                        var flat = DrawStore.FromDataUrl(Json.Str(m, "png"), "image/png");
                        var bg = DrawStore.FromDataUrl(Json.Str(m, "bg"), "image/png");
                        var thumb = DrawStore.FromDataUrl(Json.Str(m, "thumb"), "image/jpeg");
                        var e = await Task.Run(() => DrawStore.Save(id, Json.Str(m, "title"), Json.Str(m, "doc"), flat, bg, thumb, (int)Json.Num(m, "w", 0), (int)Json.Num(m, "h", 0)));
                        PostToContent(tab, new { t = "draw-saved", id = e.id, req = Json.Num(m, "req", 0) });
                        PushDrawingsToStartPages();
                        Log.Info("Drawing saved (" + DrawStore.List().Count + " kept)");
                        break;
                    }
                    case "draw-copy":
                    {
                        var png = DrawStore.FromDataUrl(Json.Str(m, "png"), "image/png");
                        if (png != null) { CopyPng(png); PostToContent(tab, new { t = "draw-done", op = "copy" }); }
                        break;
                    }
                    case "draw-export":
                    {
                        var png = DrawStore.FromDataUrl(Json.Str(m, "png"), "image/png");
                        if (png != null) { SavePng(png, Json.Str(m, "title")); PostToContent(tab, new { t = "draw-done", op = "export", path = lastSavedFile ?? "" }); }
                        break;
                    }
                    case "draw-send":
                    {
                        string key = Json.Str(m, "key");
                        var pic = DrawStore.FromDataUrl(Json.Str(m, "data"), "image/webp", "image/jpeg", "image/png");
                        if (pic == null) break;
                        var msg = await RavenSendPic(key, pic, (int)Json.Num(m, "w", 0), (int)Json.Num(m, "h", 0), Json.Str(m, "text"));
                        PostToContent(tab, new { t = "draw-sent", ok = msg != null && msg.state != "failed", key = key });
                        break;
                    }
                    case "draw-delete":
                        if (DrawStore.Delete(Json.Str(m, "id"))) { PushDrawingsToStartPages(); Log.Info("Drawing deleted"); }
                        break;
                    case "draw-folder":
                        ShowSavedFile();
                        break;
                    case "draw-close":
                        CloseTab(tab, true);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Drawing page: " + t, ex);
                PostToContent(tab, new { t = "draw-error", op = t, text = ex.Message });
            }
        }
    }
}
