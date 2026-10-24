using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Sidearm
{
    /// <summary>Milestone 3: Glance (a link floating over the page) and the image viewer's shell side.</summary>
    internal sealed partial class MainForm
    {
        Tab glance;
        Panel glanceHost;
        GlanceRail glanceRail;

        /// <summary>0.9: the round buttons beside the Glance, Zen style.</summary>
        void ShowGlanceRail()
        {
            if (glanceRail == null)
            {
                glanceRail = new GlanceRail { Visible = false };
                glanceRail.Command += op => OnGlanceCommand(op);
                Controls.Add(glanceRail);
            }
            var list = new List<GlanceRail.Button>
            {
                new GlanceRail.Button { Op = "close", Tip = "Close (Esc or click outside)", Icon = GlanceRail.IconClose },
                new GlanceRail.Button { Op = "promote", Tip = "Open here, in this tab (Alt+Enter)", Icon = GlanceRail.IconExpand },
                new GlanceRail.Button { Op = "newtab", Tip = "Open in a new tab (Alt+Shift+Enter)", Icon = GlanceRail.IconNewTab },
                new GlanceRail.Button { Op = "zen", Tip = "Open in " + BrowserName + " (Ctrl+Enter)", Icon = GlanceRail.IconOut },
                new GlanceRail.Button { Op = "copy", Tip = "Copy link", Icon = GlanceRail.IconLink }
            };
            if (RavenReady) list.Add(new GlanceRail.Button { Op = "friend", Tip = "Send to a friend", Icon = GlanceRail.IconWing });
            glanceRail.SetButtons(list);
            glanceRail.Back = IsLight ? Color.FromArgb(0xFA, 0xF9, 0xF5) : Color.FromArgb(0x17, 0x1B, 0x1D);
            glanceRail.Line = FrameLine;
            glanceRail.Ink = IsLight ? Color.FromArgb(0x3F, 0x45, 0x3F) : Color.FromArgb(0xC9, 0xCC, 0xC5);
            glanceRail.Accent = AccentColor;
            glanceRail.Visible = true;
            LayoutChildren();
        }

        // ───────────────────────── Glance ─────────────────────────

        async void OpenGlance(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url) || !IsWebUrl(url)) return;
                if (glance != null && glance.View != null && glance.View.CoreWebView2 != null)
                {
                    glance.Url = url;
                    glance.View.CoreWebView2.Navigate(url); // one Glance at a time: a link inside it replaces it
                    return;
                }
                if (glanceHost == null)
                {
                    glanceHost = new Panel { BackColor = FrameLine, Padding = new Padding(1), Visible = false };
                    Controls.Add(glanceHost);
                }
                glance = new Tab { Id = -1, Url = url, Title = url, IsGlance = true };
                glanceHost.Visible = true;
                ShowGlanceRail();
                if (active != null) PostToContent(active, new { t = "dim", on = true });
                PushTabs();
                await EnsureView(glance, true);
                if (glance != null && glance.View != null)
                {
                    SetViewVisible(glance.View, true);
                    glance.View.Focus();
                }
            }
            catch (Exception ex) { Log.Error("Glance", ex); }
        }

        void CloseGlance()
        {
            if (glance == null) return;
            var g = glance;
            glance = null;
            g.Closed = true;
            DisposeView(g); // the engine is destroyed, not kept around
            if (glanceHost != null) glanceHost.Visible = false;
            if (glanceRail != null) glanceRail.Visible = false;
            if (active != null)
            {
                PostToContent(active, new { t = "dim", on = false });
                if (active.View != null) active.View.Focus();
            }
            PushTabs();
        }

        void OnGlanceCommand(string op)
        {
            if (glance == null) return;
            string url = glance.Url;
            switch (op)
            {
                case "promote": CloseGlance(); Navigate(url, false); break;
                case "newtab": CloseGlance(); Navigate(url, true); break;
                case "zen": SendToBrowser(url, false); break;
                case "zenPrivate": SendToBrowser(url, true); break;
                case "copy": CopyLink(url); break;
                case "friend": SendToFriend(url, glance.Title); break;
                default: CloseGlance(); break;
            }
        }

        object GlanceInfo()
        {
            if (glance == null) return null;
            return new { url = glance.Url ?? "", title = glance.Title ?? "", loading = glance.Loading };
        }

        Rectangle GlanceBounds(Rectangle contentBounds)
        {
            int mx = Math.Max(ToPx(14), contentBounds.Width / 20), my = ToPx(10);
            // 0.9: the right margin holds the button column
            int right = mx;
            if (glanceRail != null && glanceRail.Visible) right = Math.Max(mx, glanceRail.Want(Native.Scale(Handle)).Width + 2 * ToPx(8));
            return new Rectangle(contentBounds.X + mx, contentBounds.Y + my,
                Math.Max(0, contentBounds.Width - mx - right), Math.Max(0, contentBounds.Height - 2 * my));
        }

        // ───────────────────────── image viewer: the parts a page can't do ─────────────────────────

        DateTime lastImageAction = DateTime.MinValue;

        async void OnImageRequest(Tab tab, Dictionary<string, object> m)
        {
            string op = Json.Str(m, "op"), url = Json.Str(m, "url");
            if (string.IsNullOrEmpty(url)) return;
            if (op == "lens" || op == "tineye" || op == "bing")
            {
                if (!IsWebUrl(url)) { Toast("Reverse search needs an image on the web", null, null); return; }
                string e = Uri.EscapeDataString(url);
                string target = op == "lens" ? "https://lens.google.com/uploadbyurl?url=" + e
                    : op == "tineye" ? "https://tineye.com/search?url=" + e
                    : "https://www.bing.com/images/search?view=detailv2&iss=sbi&form=SBIVSP&sbisrc=UrlPaste&q=imgurl:" + e;
                Navigate(target, true);
                return;
            }
            if ((DateTime.UtcNow - lastImageAction).TotalSeconds < 0.8) return;
            lastImageAction = DateTime.UtcNow;
            var bytes = await DownloadBytes(url, tab.Url);
            if (bytes == null) { Toast("Couldn't load that image", null, null); return; }
            var img = Decode(bytes);
            if (img == null)
            {
                // WebP, AVIF and friends: Windows' picture code can't read them, so keep the file as it came
                ImageFallback(op, url, bytes);
                return;
            }
            try
            {
                if (op == "copy")
                {
                    Clipboard.SetImage(img);
                    Toast("Image copied", null, null);
                }
                else if (op == "save")
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    Directory.CreateDirectory(dir);
                    string name = SafeName(url) + ".png";
                    string path = Path.Combine(dir, name);
                    for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, SafeName(url) + " (" + i + ").png");
                    img.Save(path, ImageFormat.Png); // re-encoded: whatever the site sent, you get a plain PNG
                    Toast("Saved to Downloads: " + Path.GetFileName(path), null, null);
                }
                else if (op == "popout")
                {
                    var w = new ImageWindow(img, Json.Str(m, "title") ?? SafeName(url), IsLight);
                    w.Show();
                    img = null; // the window owns it now
                }
            }
            catch (Exception ex) { Log.Error("Image " + op, ex); Toast("That didn't work: " + ex.Message, null, null); }
            finally { if (img != null && op != "popout") img.Dispose(); }
        }

        static string SafeName(string url)
        {
            string name = "image";
            try
            {
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    name = Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
            }
            catch (Exception) { }
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(name)) name = "image";
            if (name.Length > 60) name = name.Substring(0, 60);
            return name;
        }

        /// <summary>A format GDI+ can't read: save the original file, copy it as a file, or show it in a Glance.</summary>
        void ImageFallback(string op, string url, byte[] bytes)
        {
            string ext = SniffExtension(bytes);
            try
            {
                if (op == "popout")
                {
                    if (IsWebUrl(url)) OpenGlance(url); else Toast("This image format can't pop out", null, null);
                    return;
                }
                string dir = op == "save"
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                    : Path.Combine(Path.GetTempPath(), "Sidearm");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, SafeName(url) + ext);
                for (int i = 2; File.Exists(path) && op == "save"; i++) path = Path.Combine(dir, SafeName(url) + " (" + i + ")" + ext);
                File.WriteAllBytes(path, bytes);
                if (op == "save") Toast("Saved to Downloads: " + Path.GetFileName(path) + " (kept as " + ext.TrimStart('.').ToUpperInvariant() + ")", null, null);
                else
                {
                    var files = new System.Collections.Specialized.StringCollection();
                    files.Add(path);
                    Clipboard.SetFileDropList(files);
                    Toast("Copied as a " + ext.TrimStart('.').ToUpperInvariant() + " file: paste it into a chat or folder", null, null);
                }
            }
            catch (Exception ex) { Log.Error("Image fallback " + op, ex); Toast("That didn't work: " + ex.Message, null, null); }
        }

        static string SniffExtension(byte[] b)
        {
            if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
            if (b.Length > 12 && b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p')
            {
                string brand = System.Text.Encoding.ASCII.GetString(b, 8, 4);
                return brand.StartsWith("avi") ? ".avif" : brand.StartsWith("hei") || brand.StartsWith("mif") ? ".heic" : ".img";
            }
            if (b.Length > 4 && b[0] == '<') return ".svg";
            return ".img";
        }

        static Image Decode(byte[] bytes)
        {
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var decoded = Image.FromStream(ms))
                    return new Bitmap(decoded); // a copy that doesn't depend on the stream
            }
            catch (Exception) { return null; }
        }

        static async Task<byte[]> DownloadBytes(string url, string referer)
        {
            try
            {
                byte[] bytes;
                if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = url.IndexOf(',');
                    if (comma < 0 || url.IndexOf(";base64", 0, comma, StringComparison.OrdinalIgnoreCase) < 0) return null;
                    bytes = Convert.FromBase64String(url.Substring(comma + 1));
                }
                else if (IsWebUrl(url))
                {
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0";
                        wc.Headers[HttpRequestHeader.Accept] = "image/png,image/jpeg,image/gif,image/bmp,image/*;q=0.8";
                        if (IsWebUrl(referer)) wc.Headers[HttpRequestHeader.Referer] = referer;
                        var task = wc.DownloadDataTaskAsync(new Uri(url));
                        if (await Task.WhenAny(task, Task.Delay(15000)) != task) { wc.CancelAsync(); return null; }
                        bytes = await task;
                    }
                }
                else return null;
                if (bytes == null || bytes.Length == 0 || bytes.Length > 40 * 1024 * 1024) return null;
                return bytes;
            }
            catch (Exception ex)
            {
                Log.Info("Image download failed (" + ex.GetType().Name + "): " + (url.Length > 120 ? url.Substring(0, 120) : url));
                return null;
            }
        }
    }
}
