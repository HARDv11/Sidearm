using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sidearm
{
    /// <summary>
    /// 0.8: updates from GitHub Releases. Once a day Sidearm asks GitHub for the latest release; if it's newer,
    /// it says so. Nothing is installed until you click Update. Then the shared zip is downloaded, its SHA-256 is
    /// checked against the one GitHub publishes, and the files are swapped in: a running exe can't be overwritten
    /// but it can be renamed, so each old file becomes "name.old" and the new one takes its place. data\ is never
    /// touched. The new copy deletes the .old files once it has run for half a minute; if it fails to start three
    /// times in a row, the .old files are put back.
    /// </summary>
    internal static class Updater
    {
        public const string Repo = "HARDv11/Sidearm";
        static readonly string ApiUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
        static readonly string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";

        public static string Current { get { return Program.Version.Split(' ')[0]; } }
        /// <summary>The newer version on GitHub ("0.8.1"), or null.</summary>
        public static string Latest;
        public static string LatestTitle = "", Notes = "", PageUrl = "";
        public static string Status = "Not checked yet";
        public static bool Busy;
        static string zipUrl, zipName, zipSha;

        static string UpdateDir { get { return Path.Combine(Program.DataDir, "update"); } }
        static string PendingPath { get { return Path.Combine(UpdateDir, "pending.json"); } }

        static WebClient Client(string why)
        {
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "Sidearm/" + Current + " (" + why + ")";
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json, */*";
            return wc;
        }

        /// <summary>"0.8.1" vs "0.8.0" → 1. Anything after the numbers is ignored.</summary>
        public static int Compare(string a, string b)
        {
            Func<string, int[]> parts = s =>
            {
                var m = Regex.Match(s ?? "", @"(\d+)(?:\.(\d+))?(?:\.(\d+))?");
                return new[] { 1, 2, 3 }.Select(i => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0).ToArray();
            };
            var x = parts(a); var y = parts(b);
            for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            return 0;
        }

        /// <summary>Asks GitHub for the latest release (blocking: run it off the UI thread). True if it's newer.</summary>
        public static bool Check()
        {
            try
            {
                string json;
                using (var wc = Client("update check")) json = wc.DownloadString(ApiUrl);
                return Read(Json.Parse(json));
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                Status = resp != null && resp.StatusCode == HttpStatusCode.NotFound ? "No releases on GitHub yet" : "Couldn't reach GitHub: " + ex.Message;
                Log.Info("Update check: " + Status);
                return false;
            }
            catch (Exception ex)
            {
                Status = "Couldn't check: " + ex.Message;
                Log.Error("Update check", ex);
                return false;
            }
        }

        /// <summary>Reads a release from GitHub's JSON. Public so tests can feed it canned answers.</summary>
        public static bool Read(Dictionary<string, object> rel)
        {
            Latest = null; zipUrl = zipName = zipSha = null;
            if (rel == null) { Status = "GitHub's answer didn't make sense"; return false; }
            string tag = Json.Str(rel, "tag_name") ?? "";
            LatestTitle = Json.Str(rel, "name") ?? tag;
            Notes = (Json.Str(rel, "body") ?? "").Trim();
            if (Notes.Length > 4000) Notes = Notes.Substring(0, 4000) + "…";
            PageUrl = Json.Str(rel, "html_url") ?? "";
            object raw;
            var assets = new List<Dictionary<string, object>>();
            if (rel.TryGetValue("assets", out raw) && raw is IEnumerable)
                foreach (var o in (IEnumerable)raw) { var d = o as Dictionary<string, object>; if (d != null) assets.Add(d); }
            if (Compare(tag, Current) <= 0)
            {
                Status = "You have the latest version (" + Current + ")";
                Log.Info("Update check: up to date (" + Current + ", GitHub has " + tag + ")");
                return false;
            }
            // the shared zip: the build without source, which every copy can use (data\ keeps your language)
            // Sidearm-0.8.2-ua.zip / -en.zip since 0.8.2 (-share-uk.zip and plain Sidearm-0.8.1.zip before). Either language
            // does: the updater skips defaults.json, and your language lives in data\settings.json.
            Func<string, Dictionary<string, object>> pick = re => assets.FirstOrDefault(a => Regex.IsMatch(Json.Str(a, "name") ?? "", re, RegexOptions.IgnoreCase));
            var zip = pick(@"^Sidearm-[\d.]+-ua\.zip$") ?? pick(@"^Sidearm-[\d.]+-en\.zip$")
                   ?? pick(@"-share-uk\.zip$") ?? pick(@"^Sidearm-[\d.]+\.zip$");
            Latest = tag.TrimStart('v', 'V');
            if (zip == null)
            {
                Status = "Sidearm " + Latest + " is on GitHub, but without a zip Sidearm can install. Download it from the Releases page";
                Log.Info("Update check: " + tag + " has no installable zip");
                return true;
            }
            zipName = Json.Str(zip, "name");
            zipUrl = Json.Str(zip, "browser_download_url") ?? "";
            if (!zipUrl.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase)) { zipUrl = null; Status = "The download link isn't on GitHub, so Sidearm won't use it"; return true; }
            // GitHub publishes a SHA-256 for every uploaded file ("digest"); a "<zip>.sha256" file next to it also works
            var digest = Json.Str(zip, "digest") ?? "";
            if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) zipSha = digest.Substring(7).Trim().ToLowerInvariant();
            if (zipSha == null)
            {
                var sumAsset = assets.FirstOrDefault(a => string.Equals(Json.Str(a, "name"), zipName + ".sha256", StringComparison.OrdinalIgnoreCase));
                if (sumAsset != null) shaUrl = Json.Str(sumAsset, "browser_download_url");
            }
            Status = "Sidearm " + Latest + " is out";
            Log.Info("Update check: " + tag + " is out (" + zipName + ")");
            return true;
        }
        static string shaUrl;

        public static bool CanInstall { get { return Latest != null && zipUrl != null && !Busy; } }

        /// <summary>Downloads, checks and unpacks the new version into data\update. Returns the folder holding Sidearm.exe.</summary>
        public static string Prepare()
        {
            // not CanInstall: that also says "not busy", and UpdateNow marks itself busy before it calls this
            // (0.8.0 checked CanInstall here, so its Update button could never work)
            if (Latest == null || zipUrl == null) throw new InvalidOperationException("No update to install");
            Directory.CreateDirectory(UpdateDir);
            string zipPath = Path.Combine(UpdateDir, zipName);
            string unpack = Path.Combine(UpdateDir, "new");
            using (var wc = Client("update download"))
            {
                if (zipSha == null && shaUrl != null && shaUrl.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(wc.DownloadString(shaUrl), @"\b[0-9a-fA-F]{64}\b");
                    if (m.Success) zipSha = m.Value.ToLowerInvariant();
                }
                if (zipSha == null) throw new Exception("this release has no checksum, so Sidearm won't install it. Download it from the Releases page instead");
                wc.DownloadFile(zipUrl, zipPath);
            }
            string app = Unpack(zipPath, zipSha, unpack);
            Log.Info("Update " + Latest + ": downloaded and checked (SHA-256 " + zipSha.Substring(0, 12) + "…)");
            return app;
        }

        /// <summary>Checks the zip against its SHA-256 and unpacks it into <paramref name="unpack"/>. Returns the Sidearm folder in it.</summary>
        public static string Unpack(string zipPath, string expectSha, string unpack)
        {
            if (Directory.Exists(unpack)) Directory.Delete(unpack, true);
            string got;
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(zipPath))
                got = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            if (got != expectSha)
            {
                try { File.Delete(zipPath); } catch (Exception) { }
                throw new Exception("the download doesn't match GitHub's checksum, so it was thrown away");
            }
            Directory.CreateDirectory(unpack);
            string root = Path.GetFullPath(unpack) + Path.DirectorySeparatorChar;
            using (var z = ZipFile.OpenRead(zipPath))
                foreach (var e in z.Entries)
                {
                    string dest = Path.GetFullPath(Path.Combine(unpack, e.FullName));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new Exception("the zip has a strange path in it: " + e.FullName);
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            try { File.Delete(zipPath); } catch (Exception) { }
            string app = Path.Combine(unpack, "Sidearm");
            if (!File.Exists(Path.Combine(app, "Sidearm.exe")) || !File.Exists(Path.Combine(app, "ui", "chrome.html")))
                throw new Exception("the zip doesn't look like Sidearm (no Sidearm\\Sidearm.exe and ui)");
            return app;
        }

        sealed class Swap { public string rel, old; }

        /// <summary>Puts the new files in place, keeping each replaced one as ".old". Rolls back if anything fails.</summary>
        public static void Apply(string newApp)
        {
            var done = new List<Swap>();
            string appDir = Program.AppDir;
            try
            {
                foreach (var src in Directory.GetFiles(newApp, "*", SearchOption.AllDirectories))
                {
                    string rel = src.Substring(newApp.Length).TrimStart('\\', '/');
                    string top = rel.Split('\\', '/')[0];
                    // your data stays yours; defaults.json only matters for a brand-new copy
                    if (top.Equals("data", StringComparison.OrdinalIgnoreCase) || rel.Equals("defaults.json", StringComparison.OrdinalIgnoreCase)) continue;
                    string target = Path.Combine(appDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    var s = new Swap { rel = rel };
                    if (File.Exists(target))
                    {
                        s.old = target + ".old";
                        for (int n = 2; File.Exists(s.old); n++)
                        {
                            try { File.Delete(s.old); } catch (Exception) { s.old = target + ".old" + n; }
                        }
                        File.Move(target, s.old);
                    }
                    try { File.Move(src, target); }
                    catch (Exception) { if (s.old != null) File.Move(s.old, target); throw; }
                    done.Add(s);
                }
                WritePending(new Dictionary<string, object>
                {
                    { "version", Latest }, { "from", Current }, { "attempts", 0 },
                    { "files", done.Select(d => new Dictionary<string, object> { { "rel", d.rel }, { "old", d.old } }).ToArray() }
                });
                Log.Info("Update " + Latest + ": " + done.Count + " files swapped in; restarting");
            }
            catch (Exception)
            {
                Restore(done.Select(d => new Dictionary<string, object> { { "rel", d.rel }, { "old", d.old } }));
                throw;
            }
            finally
            {
                try { Directory.Delete(Path.Combine(UpdateDir, "new"), true); } catch (Exception) { }
            }
        }

        static void Restore(IEnumerable<Dictionary<string, object>> files)
        {
            foreach (var f in files.Reverse())
            {
                string target = Path.Combine(Program.AppDir, Json.Str(f, "rel"));
                string old = Json.Str(f, "old");
                try
                {
                    if (File.Exists(target))
                    {
                        string bad = target + ".bad";
                        try { if (File.Exists(bad)) File.Delete(bad); } catch (Exception) { }
                        File.Move(target, bad);
                    }
                    if (!string.IsNullOrEmpty(old) && File.Exists(old)) File.Move(old, target);
                }
                catch (Exception ex) { Log.Error("Restoring " + target, ex); }
            }
        }

        static string FailedPath { get { return Path.Combine(UpdateDir, "failed.txt"); } }

        public static void RememberFailure(string message)
        {
            try { Directory.CreateDirectory(UpdateDir); File.WriteAllText(FailedPath, message ?? ""); } catch (Exception) { }
        }

        /// <summary>The message an update left when it failed to install, once; null if there's none.</summary>
        public static string TakeFailure()
        {
            try
            {
                if (!File.Exists(FailedPath)) return null;
                string m = File.ReadAllText(FailedPath);
                File.Delete(FailedPath);
                return m;
            }
            catch (Exception) { return null; }
        }

        static void WritePending(Dictionary<string, object> p)
        {
            Directory.CreateDirectory(UpdateDir);
            File.WriteAllText(PendingPath, Json.Serialize(p));
        }

        static Dictionary<string, object> ReadPending()
        {
            try { return File.Exists(PendingPath) ? Json.Parse(File.ReadAllText(PendingPath)) : null; }
            catch (Exception) { return null; }
        }

        static IEnumerable<Dictionary<string, object>> PendingFiles(Dictionary<string, object> p)
        {
            object raw;
            if (p != null && p.TryGetValue("files", out raw) && raw is IEnumerable)
                foreach (var o in (IEnumerable)raw) { var d = o as Dictionary<string, object>; if (d != null) yield return d; }
        }

        /// <summary>
        /// At startup, before anything else: a fresh update counts its starts. Three starts that never got to
        /// <see cref="Confirm"/> mean the new version doesn't work here, so the old files go back. True = restart now.
        /// </summary>
        public static bool OnStartup()
        {
            var p = ReadPending();
            if (p == null) { CleanLeftovers(); return false; }
            int attempts = (int)Json.Num(p, "attempts", 0) + 1;
            if (attempts >= 3)
            {
                Log.Info("Update " + Json.Str(p, "version") + " didn't start properly twice; putting " + Json.Str(p, "from") + " back");
                Restore(PendingFiles(p).ToList());
                try { File.Delete(PendingPath); } catch (Exception) { }
                return true;
            }
            p["attempts"] = attempts;
            try { WritePending(p); } catch (Exception) { }
            return false;
        }

        /// <summary>".bad" files from a restore: Sidearm's own folder and ui only (never data\).</summary>
        static void CleanLeftovers()
        {
            try
            {
                var files = Directory.GetFiles(Program.AppDir, "*.bad").Concat(Directory.GetFiles(Program.UiDir ?? Path.Combine(Program.AppDir, "ui"), "*.bad", SearchOption.AllDirectories));
                foreach (var f in files) try { File.Delete(f); } catch (Exception) { }
            }
            catch (Exception) { }
        }

        /// <summary>The new version has been running for a while: the old files can go.</summary>
        public static void Confirm()
        {
            var p = ReadPending();
            if (p == null) return;
            int removed = 0;
            foreach (var f in PendingFiles(p))
            {
                string old = Json.Str(f, "old");
                try { if (!string.IsNullOrEmpty(old) && File.Exists(old)) { File.Delete(old); removed++; } } catch (Exception) { }
            }
            try { File.Delete(PendingPath); } catch (Exception) { }
            Log.Info("Updated from " + Json.Str(p, "from") + " to " + Current + "; removed " + removed + " old files");
        }

        /// <summary>Starts the new copy, which waits for this one to exit before it takes over.</summary>
        public static void Restart(bool tray)
        {
            string exe = Path.Combine(Program.AppDir, "Sidearm.exe");
            Process.Start(new ProcessStartInfo(exe, "--after-update " + Process.GetCurrentProcess().Id + (tray ? " --tray" : "")) { UseShellExecute = false, WorkingDirectory = Program.AppDir });
        }
    }
}
