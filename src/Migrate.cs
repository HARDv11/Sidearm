using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Sidearm
{
    /// <summary>
    /// A fresh copy of Sidearm (a new zip unpacked into a new folder) looks for an earlier copy nearby and offers
    /// to bring over its data folder: sign-ins, open tabs, settings, per-site choices. Caches are left behind.
    /// The old copy is only read, never changed.
    /// </summary>
    internal static class Migrate
    {
        static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "GrShaderCache",
            "ShaderCache", "GraphiteDawnCache", "Crashpad", "CacheStorage", "ScriptCache", "component_crx_cache"
        };

        /// <summary>Call before settings load, only when this copy has no settings yet.</summary>
        public static void OfferIfFirstRun()
        {
            try
            {
                if (File.Exists(Path.Combine(Program.DataDir, "settings.json"))) return;
                string found = FindEarlier();
                if (found == null) return;
                Lang.Load(FirstRunLanguage());
                string folder = Path.GetDirectoryName(found);
                var answer = MessageBox.Show(
                    Lang.T("Found an earlier Sidearm in " + folder + ". Bring over your sign-ins, tabs and settings? The old copy stays as it is."),
                    "Sidearm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) { Log.Info("Earlier copy found in " + folder + "; not brought over"); return; }
                int files = 0;
                long bytes = 0;
                Copy(found, Program.DataDir, ref files, ref bytes);
                try { File.Delete(Path.Combine(Program.DataDir, "sidearm.log")); } catch (Exception) { }
                Log.Info("Brought over " + files + " files (" + (bytes / 1048576) + " MB) from " + found);
            }
            catch (Exception ex) { Log.Error("Bringing over an earlier copy", ex); }
        }

        /// <summary>The newest data folder near this copy: its siblings, its parent's siblings, and %LOCALAPPDATA%\Sidearm.</summary>
        static string FindEarlier()
        {
            var candidates = new List<string>();
            string self = Path.GetFullPath(Program.DataDir).TrimEnd('\\', '/');
            var app = new DirectoryInfo(Program.AppDir);
            foreach (var root in new[] { app.Parent, app.Parent != null ? app.Parent.Parent : null })
            {
                if (root == null) continue;
                DirectoryInfo[] dirs;
                try { dirs = root.GetDirectories(); } catch (Exception) { continue; }
                foreach (var d in dirs.Take(300))
                {
                    candidates.Add(Path.Combine(d.FullName, "data"));
                    candidates.Add(Path.Combine(d.FullName, "Sidearm", "data"));
                }
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sidearm"));
            string best = null;
            DateTime newest = DateTime.MinValue;
            foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(c).TrimEnd('\\', '/'), self, StringComparison.OrdinalIgnoreCase)) continue;
                    string settings = Path.Combine(c, "settings.json");
                    if (!File.Exists(settings) || !Directory.Exists(Path.Combine(c, "webview"))) continue;
                    var when = File.GetLastWriteTimeUtc(settings);
                    if (when > newest) { newest = when; best = settings; }
                }
                catch (Exception) { }
            }
            return best;
        }

        static string FirstRunLanguage()
        {
            try
            {
                string defaults = Path.Combine(Program.AppDir, "defaults.json");
                if (File.Exists(defaults))
                {
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(defaults, Encoding.UTF8));
                    object v;
                    if (d != null && d.TryGetValue("language", out v) && (v as string) == "uk") return "uk";
                }
            }
            catch (Exception) { }
            return Lang.FromWindows();
        }

        static void Copy(string fromSettings, string to, ref int files, ref long bytes)
        {
            CopyDir(new DirectoryInfo(Path.GetDirectoryName(fromSettings)), to, ref files, ref bytes);
        }

        static void CopyDir(DirectoryInfo from, string to, ref int files, ref long bytes)
        {
            Directory.CreateDirectory(to);
            foreach (var f in from.GetFiles())
            {
                if (f.Name.StartsWith("sidearm.log", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                try { f.CopyTo(Path.Combine(to, f.Name), true); files++; bytes += f.Length; }
                catch (Exception) { } // a file in use is skipped; Chromium rebuilds what it needs
            }
            foreach (var d in from.GetDirectories())
            {
                if (SkipDirs.Contains(d.Name)) continue;
                CopyDir(d, Path.Combine(to, d.Name), ref files, ref bytes);
            }
        }
    }
}
