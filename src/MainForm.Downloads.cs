using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sidearm
{
    /// <summary>
    /// 0.9.5: Alt + drag a box over a page and download what's in it (pictures at their largest, file links, videos)
    /// into Downloads\&lt;site&gt; or a folder you pick. The page finds the links and shows the list; Sidearm downloads,
    /// with the site's own cookies, so files that need you signed in still come.
    /// A page can't start this by itself: the box has to end with Alt really held down, and the token it gets back is
    /// good for one batch of up to 300 files.
    /// </summary>
    internal sealed partial class MainForm
    {
        sealed class DlGrant { public Tab Tab; public DateTime At; public bool Used; }
        readonly Dictionary<string, DlGrant> dlGrants = new Dictionary<string, DlGrant>();
        const int DlMaxFiles = 300;

        string DownloadDir(string pageUrl)
        {
            string d = (S.downloadFolder ?? "").Trim();
            if (d.Length > 0 && Path.IsPathRooted(d)) return d;
            string site = "site";
            try { site = Sites.Key(new Uri(pageUrl).Host); } catch (Exception) { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", SafeFileName(site, "site"));
        }

        string ShortPath(string p)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return p.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? p.Substring(home.Length).TrimStart('\\') : p;
        }

        async void OnDownloadMessage(Tab tab, Dictionary<string, object> m)
        {
            string t = Json.Str(m, "t");
            try
            {
                if (t == "dl-select")
                {
                    if (tab != active && tab != glance && tab.Pop == null) return;
                    if ((Control.ModifierKeys & Keys.Alt) == 0) { Log.Info("Download box: Alt wasn't held, ignored"); return; }
                    foreach (var k in dlGrants.Where(x => (DateTime.UtcNow - x.Value.At).TotalMinutes > 15).Select(x => x.Key).ToList()) dlGrants.Remove(k);
                    string token = NewMsgId();
                    dlGrants[token] = new DlGrant { Tab = tab, At = DateTime.UtcNow };
                    PostToContent(tab, new { t = "dl-token", token = token, folder = ShortPath(DownloadDir(tab.Url)) });
                    return;
                }
                DlGrant g;
                string tok = Json.Str(m, "token");
                if (tok == null || !dlGrants.TryGetValue(tok, out g) || g.Tab != tab || (DateTime.UtcNow - g.At).TotalMinutes > 15) return;
                if (t == "dl-folder")
                {
                    using (var dlg = new FolderBrowserDialog { Description = Lang.T("Where should the downloads go?"), ShowNewFolderButton = true, SelectedPath = DownloadDir(tab.Url) })
                    {
                        if (dlg.ShowDialog(this) == DialogResult.OK && Directory.Exists(dlg.SelectedPath))
                        {
                            S.downloadFolder = dlg.SelectedPath;
                            SaveSettingsSoon();
                        }
                    }
                    PostToContent(tab, new { t = "dl-folder", folder = ShortPath(DownloadDir(tab.Url)) });
                    return;
                }
                if (t != "dl" || g.Used) return;
                g.Used = true;
                object raw; m.TryGetValue("items", out raw);
                var items = new List<KeyValuePair<string, string>>();
                var arr = raw as System.Collections.IEnumerable;
                if (arr != null)
                    foreach (var o in arr)
                    {
                        var d = o as Dictionary<string, object>;
                        string url = Json.Str(d, "url");
                        if (url == null || items.Count >= DlMaxFiles) continue;
                        if (!IsWebUrl(url) && !url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) continue;
                        items.Add(new KeyValuePair<string, string>(url, Json.Str(d, "name") ?? ""));
                    }
                if (items.Count == 0) return;
                string page = IsWebUrl(Json.Str(m, "page")) ? Json.Str(m, "page") : tab.Url;
                await DownloadBatch(tab, items, page);
            }
            catch (Exception ex) { Log.Error("Download box: " + t, ex); }
        }

        async Task DownloadBatch(Tab tab, List<KeyValuePair<string, string>> items, string page)
        {
            string dir = DownloadDir(page);
            Directory.CreateDirectory(dir);
            // the site's cookies for each address (read here, on the UI thread, where the engine lives)
            var cookies = new Dictionary<string, string>();
            var cm = tab.View != null && tab.View.CoreWebView2 != null ? tab.View.CoreWebView2.CookieManager : null;
            foreach (var it in items)
            {
                if (!IsWebUrl(it.Key) || cm == null) continue;
                string origin;
                try { origin = new Uri(it.Key).GetLeftPart(UriPartial.Path); } catch (Exception) { continue; }
                if (cookies.ContainsKey(origin)) continue;
                try
                {
                    var list = await cm.GetCookiesAsync(it.Key);
                    cookies[origin] = string.Join("; ", list.Select(c => c.Name + "=" + c.Value));
                }
                catch (Exception) { cookies[origin] = ""; }
                if (cookies.Count > 60) break;
            }
            int done = 0, failed = 0, total = items.Count;
            DateTime lastPush = DateTime.MinValue;
            Action push = () =>
            {
                if ((DateTime.UtcNow - lastPush).TotalMilliseconds < 200 && done + failed < total) return;
                lastPush = DateTime.UtcNow;
                if (tab.View != null) PostToContent(tab, new { t = "dl-progress", done = done, failed = failed, total = total, finished = done + failed >= total });
            };
            Log.Info("Download box: " + total + " files from " + HostOf(page));
            var gate = new SemaphoreSlim(3);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tasks = items.Select(async it =>
            {
                await gate.WaitAsync();
                try
                {
                    string origin = "";
                    try { origin = new Uri(it.Key).GetLeftPart(UriPartial.Path); } catch (Exception) { }
                    string cookie; cookies.TryGetValue(origin, out cookie);
                    bool ok = await Task.Run(() => DownloadOne(it.Key, it.Value, dir, page, cookie, names));
                    if (ok) done++; else failed++;
                }
                catch (Exception) { failed++; }
                finally { gate.Release(); }
                push();
            }).ToList();
            await Task.WhenAll(tasks);
            push();
            lastSavedFile = dir;
            Log.Info("Download box: " + done + " saved, " + failed + " failed");
            Toast(failed == 0 ? "Downloaded " + done + " to " + ShortPath(dir) : "Downloaded " + done + ", " + failed + " didn't come", "Open folder", "showSaved");
        }

        static readonly Dictionary<string, string> MimeExt = new Dictionary<string, string>
        {
            { "image/jpeg", ".jpg" }, { "image/png", ".png" }, { "image/gif", ".gif" }, { "image/webp", ".webp" }, { "image/avif", ".avif" },
            { "image/svg+xml", ".svg" }, { "image/bmp", ".bmp" }, { "video/mp4", ".mp4" }, { "video/webm", ".webm" }, { "audio/mpeg", ".mp3" },
            { "application/zip", ".zip" }, { "application/pdf", ".pdf" }, { "application/x-7z-compressed", ".7z" }, { "application/vnd.rar", ".rar" },
            { "application/x-rar-compressed", ".rar" }, { "application/java-archive", ".jar" }, { "text/plain", ".txt" }, { "application/json", ".json" }
        };

        /// <summary>One file, streamed to disk (".part" until it's whole). Never replaces a file that's already there.</summary>
        static bool DownloadOne(string url, string name, string dir, string referer, string cookie, HashSet<string> taken)
        {
            string tmp = null;
            try
            {
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = DrawStore.FromDataUrl(url);
                    if (bytes == null) return false;
                    string mime = url.Substring(5, url.IndexOf(';') - 5).ToLowerInvariant();
                    string ext0; MimeExt.TryGetValue(mime, out ext0);
                    string p0 = UniquePath(dir, CleanName(name, "image"), ext0 ?? ".img", taken);
                    File.WriteAllBytes(p0, bytes);
                    return true;
                }
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0";
                req.Accept = "*/*";
                if (IsWebUrlStatic(referer)) req.Referer = referer;
                if (!string.IsNullOrEmpty(cookie)) req.Headers[HttpRequestHeader.Cookie] = cookie;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                req.Timeout = 30000; req.ReadWriteTimeout = 60000;
                req.AllowAutoRedirect = true; req.MaximumAutomaticRedirections = 8;
                using (var res = (HttpWebResponse)req.GetResponse())
                {
                    if ((int)res.StatusCode >= 400) return false;
                    string type = (res.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
                    if (type == "text/html") return false; // a page, not the file (signed out, or a "download" button that goes to another page)
                    string fromHeader = FileNameFromDisposition(res.Headers["Content-Disposition"]);
                    string fname = !string.IsNullOrEmpty(fromHeader) ? fromHeader : !string.IsNullOrEmpty(name) ? name : Path.GetFileName(res.ResponseUri.AbsolutePath);
                    string ext = Path.GetExtension(fname);
                    string baseName = Path.GetFileNameWithoutExtension(fname);
                    if (string.IsNullOrEmpty(ext) || ext.Length > 12) { string e2; ext = MimeExt.TryGetValue(type, out e2) ? e2 : ""; baseName = fname; }
                    string path = UniquePath(dir, CleanName(baseName, "file"), ext, taken);
                    tmp = path + ".part";
                    using (var src = res.GetResponseStream())
                    using (var dst = File.Create(tmp)) src.CopyTo(dst, 81920);
                    File.Move(tmp, path);
                    tmp = null;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Info("Download box: one failed (" + ex.GetType().Name + "): " + (url.Length > 100 ? url.Substring(0, 100) : url));
                return false;
            }
            finally { if (tmp != null) try { File.Delete(tmp); } catch (Exception) { } }
        }

        static bool IsWebUrlStatic(string u) { return u != null && (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)); }

        static string FileNameFromDisposition(string cd)
        {
            if (string.IsNullOrEmpty(cd)) return null;
            var m = Regex.Match(cd, @"filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)");
            if (m.Success) try { return Uri.UnescapeDataString(m.Groups[1].Value.Trim('"', ' ')); } catch (Exception) { }
            m = Regex.Match(cd, "filename\\s*=\\s*\"?([^\";]+)\"?");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        static string CleanName(string n, string fallback)
        {
            n = n ?? "";
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            n = n.Trim().TrimEnd('.');
            if (n.Length > 100) n = n.Substring(0, 100);
            return n.Length == 0 ? fallback : n;
        }

        static string UniquePath(string dir, string baseName, string ext, HashSet<string> taken)
        {
            lock (taken)
            {
                string p = Path.Combine(dir, baseName + ext);
                for (int i = 2; File.Exists(p) || taken.Contains(p); i++) p = Path.Combine(dir, baseName + " (" + i + ")" + ext);
                taken.Add(p);
                return p;
            }
        }
    }
}
