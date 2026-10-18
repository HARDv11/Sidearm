using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Sidearm
{
    /// <summary>
    /// Keeps the filter lists fresh and the engine built. Lists download on first launch (and weekly after that)
    /// into data\filters\. A seed\ folder next to the exe, if there is one, covers an offline first launch.
    /// </summary>
    internal static class Adblock
    {
        sealed class ListDef
        {
            public string File, Name;
            public string[] Urls;
        }

        static readonly ListDef[] Lists =
        {
            new ListDef { File = "easylist.txt", Name = "EasyList (ads)", Urls = new[] {
                "https://easylist.to/easylist/easylist.txt",
                "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/thirdparties/easylist/easylist.txt" } },
            new ListDef { File = "easyprivacy.txt", Name = "EasyPrivacy (trackers)", Urls = new[] {
                "https://easylist.to/easylist/easyprivacy.txt",
                "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/thirdparties/easylist/easyprivacy.txt" } },
            new ListDef { File = "easylist-cookies.txt", Name = "EasyList Cookie (consent banners)", Urls = new[] {
                "https://secure.fanboy.co.nz/fanboy-cookiemonster.txt",
                "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/thirdparties/easylist/easylist-cookies.txt" } },
        };

        public static volatile FilterEngine Engine;
        public static string Status = "Loading filter lists…";
        public static DateTime ListsDate = DateTime.MinValue;
        public static event Action Changed;
        static bool busy;

        static string FilterDir { get { return Path.Combine(Program.DataDir, "filters"); } }
        static string SeedDir { get { return Path.Combine(Program.AppDir, "seed"); } }

        /// <summary>Build from what's on disk now, then refresh from the internet if the copy is over a week old.</summary>
        public static void Start()
        {
            Task.Run(() =>
            {
                Build();
                if ((DateTime.UtcNow - ListsDate).TotalDays >= 7) UpdateNow(false);
            });
        }

        public static void UpdateNow(bool force)
        {
            if (busy) return;
            busy = true;
            Task.Run(() =>
            {
                try
                {
                    Status = "Updating filter lists…";
                    Raise();
                    Directory.CreateDirectory(FilterDir);
                    int ok = 0;
                    foreach (var l in Lists)
                    {
                        string text = Download(l);
                        if (text == null) continue;
                        File.WriteAllText(Path.Combine(FilterDir, l.File), text, Encoding.UTF8);
                        ok++;
                    }
                    Log.Info("Filter lists updated: " + ok + " of " + Lists.Length);
                    Build();
                    if (ok < Lists.Length) Status += " (" + (Lists.Length - ok) + " list(s) couldn't be downloaded; using the older copy)";
                }
                catch (Exception ex) { Log.Error("Filter update", ex); }
                finally { busy = false; Raise(); }
            });
        }

        static string Download(ListDef l)
        {
            foreach (var url in l.Urls)
            {
                try
                {
                    using (var wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.UserAgent] = "Sidearm/" + Program.Version.Split(' ')[0] + " (filter list update)";
                        string text = wc.DownloadString(url);
                        if (text != null && text.Length > 20000 && (text.StartsWith("[Adblock", StringComparison.Ordinal) || text.Contains("! Title:")))
                            return text;
                        Log.Info("Filter list from " + url + " looked wrong, trying the next source");
                    }
                }
                catch (Exception ex) { Log.Info("Filter list download failed (" + url + "): " + ex.Message); }
            }
            return null;
        }

        static void Build()
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var engine = new FilterEngine();
                DateTime newest = DateTime.MinValue;
                int lists = 0;
                foreach (var l in Lists)
                {
                    string fresh = Path.Combine(FilterDir, l.File), seed = Path.Combine(SeedDir, l.File);
                    string file = File.Exists(fresh) ? fresh : File.Exists(seed) ? seed : null;
                    if (file == null) continue;
                    engine.AddList(File.ReadAllText(file, Encoding.UTF8));
                    var when = File.GetLastWriteTimeUtc(file);
                    if (file == seed) when = File.GetLastWriteTimeUtc(seed);
                    if (when > newest) newest = when;
                    lists++;
                }
                if (lists == 0) { Status = "No filter lists yet"; Raise(); return; }
                Engine = engine;
                ListsDate = newest;
                GC.Collect();
                Status = engine.NetworkCount.ToString("N0") + " blocking rules, " + engine.CosmeticCount.ToString("N0") + " hiding rules · lists from " + newest.ToLocalTime().ToString("d MMM yyyy");
                Log.Info("Filter engine built in " + sw.ElapsedMilliseconds + " ms: " + engine.Stats + " (skipped " + engine.Skipped + ")");
                Raise();
            }
            catch (Exception ex) { Log.Error("Filter build", ex); Status = "Filter lists failed to load (see log)"; }
        }

        static void Raise()
        {
            var h = Changed;
            if (h != null) try { h(); } catch (Exception) { }
        }

        public static string HostOf(string url)
        {
            int s = url.IndexOf("://", StringComparison.Ordinal);
            if (s < 0) return "";
            s += 3;
            int e = s;
            while (e < url.Length && url[e] != '/' && url[e] != '?' && url[e] != '#') e++;
            string host = url.Substring(s, e - s);
            int at = host.LastIndexOf('@');
            if (at >= 0) host = host.Substring(at + 1);
            if (host.StartsWith("[", StringComparison.Ordinal)) return host.ToLowerInvariant();
            int colon = host.IndexOf(':');
            if (colon >= 0) host = host.Substring(0, colon);
            return host.ToLowerInvariant();
        }
    }
}
