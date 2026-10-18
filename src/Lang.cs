using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Sidearm
{
    /// <summary>
    /// The interface language. English lives in the code; another language is a dictionary in ui\lang\
    /// (the same file the pages use), looked up by the English text. Unknown strings stay English.
    /// </summary>
    internal static class Lang
    {
        public static string Code = "en";
        static Dictionary<string, string> exact = new Dictionary<string, string>();
        static List<KeyValuePair<Regex, string>> templates = new List<KeyValuePair<Regex, string>>();
        public static Dictionary<string, string> Page = new Dictionary<string, string>();

        public static void Load(string code)
        {
            Code = code == "uk" ? "uk" : "en";
            exact = new Dictionary<string, string>();
            templates = new List<KeyValuePair<Regex, string>>();
            Page = new Dictionary<string, string>();
            if (Code == "en") return;
            try
            {
                string js = File.ReadAllText(Path.Combine(Program.UiDir, "lang", Code + ".js"));
                int a = js.IndexOf('{', js.IndexOf('=')), b = js.LastIndexOf('}'); // the object after the '=' (the comment has braces too)
                var d = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(js.Substring(a, b - a + 1));
                foreach (var kv in (Dictionary<string, object>)d["exact"]) exact[kv.Key] = Convert.ToString(kv.Value);
                foreach (var kv in (Dictionary<string, object>)d["page"]) Page[kv.Key] = Convert.ToString(kv.Value);
                foreach (var o in (System.Collections.IEnumerable)d["templates"])
                {
                    var pair = ((System.Collections.IEnumerable)o).Cast<object>().Select(Convert.ToString).ToArray();
                    string src = Regex.Replace(Regex.Escape(pair[0]), @"\\\{(\d)}", "(?<p$1>.+?)");
                    templates.Add(new KeyValuePair<Regex, string>(new Regex("^" + src + "$", RegexOptions.Singleline), pair[1]));
                }
                Log.Info("Interface language: " + Code + " (" + exact.Count + " strings)");
            }
            catch (Exception ex) { Log.Error("Loading language " + Code, ex); Code = "en"; }
        }

        /// <summary>Translate one interface string (whole-string match first, then the {1} templates).</summary>
        public static string T(string s)
        {
            if (Code == "en" || string.IsNullOrEmpty(s)) return s;
            string hit;
            if (exact.TryGetValue(s, out hit)) return hit;
            foreach (var t in templates)
            {
                var m = t.Key.Match(s);
                if (!m.Success) continue;
                string outp = t.Value;
                for (int i = 1; i <= 9; i++)
                {
                    var g = m.Groups["p" + i];
                    if (g.Success) outp = outp.Replace("{" + i + "}", T(g.Value));
                }
                return outp;
            }
            return s;
        }

        /// <summary>The system's language, for a first run with no defaults.json.</summary>
        public static string FromWindows()
        {
            try { return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "uk" ? "uk" : "en"; }
            catch (Exception) { return "en"; }
        }
    }
}
