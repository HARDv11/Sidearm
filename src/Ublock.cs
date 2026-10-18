using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;

namespace Sidearm
{
    /// <summary>
    /// Experimental: uBlock Origin Lite (Raymond Hill, GPLv3) as an engine extension instead of Sidearm's own
    /// filter engine. It blocks inside the engine with declarativeNetRequest, so nothing runs on Sidearm's UI
    /// thread. Sidearm never ships it: it downloads the official Chromium build from GitHub when you turn it on.
    /// </summary>
    internal static class Ublock
    {
        const string ReleaseApi = "https://api.github.com/repos/uBlockOrigin/uBOL-home/releases/latest";
        public static string Dir { get { return Path.Combine(Program.DataDir, "extensions", "ublock-lite"); } }
        public static string Status = "Not installed";
        public static string ExtId;       // the engine's id for it, for its settings page
        public static bool Loaded;        // installed and switched on in the engine
        static bool busy;

        static bool IsUblock(CoreWebView2BrowserExtension e) { return Regex.IsMatch(e.Name ?? "", "uBlock", RegexOptions.IgnoreCase); }

        /// <summary>Make the engine match the setting: on (installing if needed) or off.</summary>
        public static async Task<string> Sync(CoreWebView2Profile profile, bool want)
        {
            if (busy) return Status;
            busy = true;
            try
            {
                var found = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(IsUblock);
                if (!want)
                {
                    if (found != null && found.IsEnabled) await found.EnableAsync(false);
                    Loaded = false;
                    Status = found != null ? "Installed, switched off" : (File.Exists(Path.Combine(Dir, "manifest.json")) ? "Downloaded, switched off" : "Not installed");
                    if (found != null) ExtId = found.Id;
                    return Status;
                }
                if (found == null)
                {
                    if (!File.Exists(Path.Combine(Dir, "manifest.json"))) { Status = "Downloading uBlock Origin Lite…"; await Task.Run(() => Download()); }
                    found = await profile.AddBrowserExtensionAsync(Dir);
                    Log.Info("Loaded extension " + found.Name + " (" + found.Id + ")");
                }
                if (!found.IsEnabled) await found.EnableAsync(true);
                ExtId = found.Id;
                Loaded = true;
                Status = "On: " + found.Name + " " + Version();
                return Status;
            }
            catch (Exception ex)
            {
                Log.Error("uBlock Origin Lite", ex);
                Loaded = false;
                Status = "Couldn't turn it on: " + ex.Message;
                return Status;
            }
            finally { busy = false; }
        }

        /// <summary>Fetch the newest release again (the engine reloads it).</summary>
        public static async Task<string> Update(CoreWebView2Profile profile)
        {
            if (busy) return Status;
            try
            {
                var found = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(IsUblock);
                if (found != null) await found.RemoveAsync(); // the folder can't change while the engine holds it
                Status = "Downloading uBlock Origin Lite…";
                await Task.Run(() => Download());
            }
            catch (Exception ex) { Log.Error("uBlock update", ex); Status = "Update failed: " + ex.Message; return Status; }
            return await Sync(profile, true);
        }

        public static string Version()
        {
            try
            {
                var m = new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(Path.Combine(Dir, "manifest.json")));
                return Json.Str(m, "version") ?? "";
            }
            catch (Exception) { return ""; }
        }

        /// <summary>Downloads the Chromium build from the latest GitHub release and unpacks it into data\extensions\ublock-lite.</summary>
        static void Download()
        {
            string zipUrl = null, tag = "";
            using (var wc = Client())
            {
                var rel = new JavaScriptSerializer().DeserializeObject(wc.DownloadString(ReleaseApi)) as System.Collections.Generic.Dictionary<string, object>;
                tag = Json.Str(rel, "tag_name") ?? "";
                var assets = rel != null && rel.ContainsKey("assets") ? rel["assets"] as object[] : null;
                if (assets != null)
                    foreach (var a in assets)
                    {
                        var d = a as System.Collections.Generic.Dictionary<string, object>;
                        string name = (Json.Str(d, "name") ?? "").ToLowerInvariant();
                        if (name.Contains("chromium") && name.EndsWith(".zip")) { zipUrl = Json.Str(d, "browser_download_url"); break; }
                    }
            }
            if (zipUrl == null) throw new Exception("no Chromium build in the latest uBlock Origin Lite release");
            if (!zipUrl.StartsWith("https://github.com/uBlockOrigin/", StringComparison.OrdinalIgnoreCase)
                && !zipUrl.StartsWith("https://objects.githubusercontent.com/", StringComparison.OrdinalIgnoreCase))
                throw new Exception("unexpected download address " + zipUrl);

            string tmp = Path.Combine(Program.DataDir, "extensions", "ublock-lite.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(tmp));
            using (var wc = Client()) wc.DownloadFile(zipUrl, tmp);
            string unpack = Dir + ".new";
            if (Directory.Exists(unpack)) Directory.Delete(unpack, true);
            ZipFile.ExtractToDirectory(tmp, unpack);
            File.Delete(tmp);

            // the manifest may sit in a subfolder of the zip
            string root = Directory.GetFiles(unpack, "manifest.json", SearchOption.AllDirectories)
                .OrderBy(f => f.Length).Select(Path.GetDirectoryName).FirstOrDefault();
            if (root == null) { Directory.Delete(unpack, true); throw new Exception("the download has no manifest.json"); }
            // the engine refuses folders starting with "_" (reserved), except _locales
            foreach (var d in Directory.GetDirectories(root))
            {
                string n = Path.GetFileName(d);
                if (n.StartsWith("_") && n != "_locales") Directory.Delete(d, true);
            }
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
            Directory.Move(root, Dir);
            if (Directory.Exists(unpack)) Directory.Delete(unpack, true);
            Log.Info("Downloaded uBlock Origin Lite " + tag + " from " + zipUrl);
        }

        static WebClient Client()
        {
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "Sidearm/" + Program.Version.Split(' ')[0] + " (uBlock Origin Lite download)";
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json, */*";
            return wc;
        }
    }
}
