using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Sidearm
{
    /// <summary>Per-site choices from the page panel, kept in data\sites.json and keyed by site (bbc.co.uk, not www.bbc.co.uk).</summary>
    public class SiteSettings
    {
        public bool allowAds { get; set; }    // true = don't block on this site
        public bool autoFog { get; set; }     // clear overlays on every visit
        public bool forceDark { get; set; }
        public double readerSize { get; set; } // 0 = default
        public bool noLookup { get; set; }
        public bool autoTranslate { get; set; } // always translate this site

        [ScriptIgnore] public bool IsDefault { get { return !allowAds && !autoFog && !forceDark && readerSize == 0 && !noLookup && !autoTranslate; } }
    }

    internal static class Sites
    {
        static Dictionary<string, SiteSettings> map;
        static string path;

        public static void Load(string file)
        {
            path = file;
            map = new Dictionary<string, SiteSettings>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(file))
                {
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, SiteSettings>>(File.ReadAllText(file));
                    if (d != null) foreach (var kv in d) if (kv.Value != null) map[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex) { Log.Error("sites.json", ex); }
        }

        public static string Key(string host)
        {
            if (string.IsNullOrEmpty(host)) return "";
            return FilterEngine.Site(host.ToLowerInvariant());
        }

        public static SiteSettings Get(string host)
        {
            SiteSettings s;
            return map != null && map.TryGetValue(Key(host), out s) ? s : new SiteSettings();
        }

        public static void Update(string host, Action<SiteSettings> change)
        {
            string key = Key(host);
            if (key.Length == 0) return;
            SiteSettings s;
            if (!map.TryGetValue(key, out s)) s = new SiteSettings();
            change(s);
            if (s.IsDefault) map.Remove(key); else map[key] = s;
            Save();
        }

        public static void Reset(string key)
        {
            if (map.Remove(key ?? "")) Save();
        }

        public static IEnumerable<KeyValuePair<string, SiteSettings>> All { get { return map; } }

        public static List<string> Where(Func<SiteSettings, bool> pick)
        {
            var list = new List<string>();
            foreach (var kv in map) if (pick(kv.Value)) list.Add(kv.Key);
            return list;
        }

        static void Save()
        {
            try { File.WriteAllText(path, Json.Pretty(new JavaScriptSerializer().Serialize(map))); }
            catch (Exception ex) { Log.Error("Save sites.json", ex); }
        }
    }
}
