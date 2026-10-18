using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Sidearm
{
    /// <summary>
    /// Brings in Modern for Wikipedia from a browser where you already installed it (Edge, Chrome, Brave,
    /// Zen or Firefox). The extension is closed-source, so Sidearm never ships it: it only loads your copy.
    /// </summary>
    internal static class Extensions
    {
        public const string ChromeId = "emdkdnnopdnajipoapepbeeiemahbjcn";
        const string Folder = "modern-wikipedia";

        public static string TargetDir { get { return Path.Combine(Program.DataDir, "extensions", Folder); } }

        /// <summary>Finds the newest copy of the extension on this PC. Returns null (and why) if none.</summary>
        public static string Find(out string from, out bool firefox)
        {
            from = null; firefox = false;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var chromium = new[]
            {
                new[] { "Microsoft Edge", Path.Combine(local, @"Microsoft\Edge\User Data") },
                new[] { "Google Chrome", Path.Combine(local, @"Google\Chrome\User Data") },
                new[] { "Brave", Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data") },
                new[] { "Vivaldi", Path.Combine(local, @"Vivaldi\User Data") },
                new[] { "Opera", Path.Combine(roaming, @"Opera Software\Opera Stable") },
            };
            foreach (var b in chromium)
            {
                if (!Directory.Exists(b[1])) continue;
                foreach (var ext in SafeDirs(b[1], "Extensions", 2))
                {
                    string dir = Path.Combine(ext, ChromeId);
                    if (!Directory.Exists(dir)) continue;
                    string best = null;
                    foreach (var v in Directory.GetDirectories(dir))
                        if (File.Exists(Path.Combine(v, "manifest.json")) && (best == null || string.CompareOrdinal(v, best) > 0)) best = v;
                    if (best != null) { from = b[0]; return best; }
                }
            }
            var gecko = new[]
            {
                new[] { "Zen", Path.Combine(roaming, @"zen\Profiles") },
                new[] { "Firefox", Path.Combine(roaming, @"Mozilla\Firefox\Profiles") },
                new[] { "Floorp", Path.Combine(roaming, @"Floorp\Profiles") },
                new[] { "LibreWolf", Path.Combine(roaming, @"librewolf\Profiles") },
            };
            foreach (var b in gecko)
            {
                if (!Directory.Exists(b[1])) continue;
                foreach (var profile in Directory.GetDirectories(b[1]))
                {
                    string exts = Path.Combine(profile, "extensions");
                    if (!Directory.Exists(exts)) continue;
                    foreach (var xpi in Directory.GetFiles(exts, "*.xpi"))
                        if (IsModernWiki(xpi)) { from = b[0]; firefox = true; return xpi; }
                }
            }
            return null;
        }

        static IEnumerable<string> SafeDirs(string root, string name, int depth)
        {
            var found = new List<string>();
            try
            {
                foreach (var d in Directory.GetDirectories(root))
                {
                    if (string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)) found.Add(d);
                    else if (depth > 1) found.AddRange(SafeDirs(d, name, depth - 1));
                }
            }
            catch (Exception) { }
            return found;
        }

        static bool IsModernWiki(string xpi)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(xpi))
                {
                    var m = zip.GetEntry("manifest.json");
                    if (m == null) return false;
                    string manifest;
                    using (var r = new StreamReader(m.Open())) manifest = r.ReadToEnd();
                    if (Regex.IsMatch(manifest, "modernwiki|Modern for Wikipedia", RegexOptions.IgnoreCase)) return true;
                    // localized names live in _locales/en/messages.json
                    var msg = zip.GetEntry("_locales/en/messages.json") ?? zip.GetEntry("_locales/en_US/messages.json");
                    if (msg == null) return false;
                    using (var r = new StreamReader(msg.Open()))
                        return Regex.IsMatch(r.ReadToEnd(), "Modern (for|design for) Wikipedia", RegexOptions.IgnoreCase);
                }
            }
            catch (Exception) { return false; }
        }

        /// <summary>Copies your installed copy into data\extensions and loads it. Returns a message for the UI.</summary>
        public static async Task<string> Import(CoreWebView2Profile profile)
        {
            string from; bool firefox;
            string source = Find(out from, out firefox);
            if (source == null)
                return "Couldn't find Modern for Wikipedia in Edge, Chrome, Brave, Vivaldi, Opera, Zen or Firefox. Install it in one of them first, then import again.";
            string target = TargetDir;
            try
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(target);
                if (firefox) ZipFile.ExtractToDirectory(source, target);
                else CopyDir(source, target);
                // WebView2 refuses folders starting with "_" except _locales
                foreach (var d in Directory.GetDirectories(target))
                {
                    string n = Path.GetFileName(d);
                    if (n.StartsWith("_") && n != "_locales") Directory.Delete(d, true);
                }
                foreach (var f in Directory.GetFiles(target))
                    if (Path.GetFileName(f).StartsWith("_")) File.Delete(f);
            }
            catch (Exception ex)
            {
                Log.Error("Copying the extension", ex);
                return "Found it in " + from + " but couldn't copy it: " + ex.Message;
            }
            try
            {
                await RemoveExisting(profile);
                var ext = await profile.AddBrowserExtensionAsync(target);
                Log.Info("Loaded extension " + ext.Name + " (" + ext.Id + ") from " + from);
                return "Imported " + ext.Name + " from " + from + ". Open any Wikipedia article to see it.";
            }
            catch (Exception ex)
            {
                Log.Error("Loading the extension", ex);
                string hint = firefox ? " The Zen/Firefox build may not run in Sidearm's engine; installing it in Edge or Chrome and importing from there usually works." : "";
                return "Found it in " + from + " but the engine refused it: " + ex.Message + hint;
            }
        }

        public static async Task<string> Status(CoreWebView2Profile profile)
        {
            try
            {
                foreach (var e in await profile.GetBrowserExtensionsAsync())
                    if (Regex.IsMatch(e.Name ?? "", "Wikipedia", RegexOptions.IgnoreCase)) return (e.IsEnabled ? "Installed: " : "Installed (off): ") + e.Name;
            }
            catch (Exception) { }
            return "Not installed";
        }

        public static async Task RemoveExisting(CoreWebView2Profile profile)
        {
            try
            {
                foreach (var e in await profile.GetBrowserExtensionsAsync())
                    if (Regex.IsMatch(e.Name ?? "", "Wikipedia", RegexOptions.IgnoreCase)) await e.RemoveAsync();
            }
            catch (Exception ex) { Log.Error("Remove extension", ex); }
        }

        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}
