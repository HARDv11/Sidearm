using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Sidearm
{
    /// <summary>
    /// Word look-up for the hover pop-up: a dictionary definition (English words), a Wikipedia
    /// summary (names, terms, phrases) and a translation to Ukrainian (or English for Cyrillic text).
    /// Done here rather than in the page so websites' own security rules can't block it.
    /// </summary>
    internal static class Lookup
    {
        static readonly Dictionary<string, Dictionary<string, object>> cache = new Dictionary<string, Dictionary<string, object>>();

        public static void ClearCache() { lock (cache) cache.Clear(); }
        static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        static readonly Regex Cyrillic = new Regex(@"[Ѐ-ӿ]");
        static readonly Queue<DateTime> recent = new Queue<DateTime>();

        public static bool Allow()
        {
            var now = DateTime.UtcNow;
            while (recent.Count > 0 && (now - recent.Peek()).TotalSeconds > 60) recent.Dequeue();
            if (recent.Count >= 40) return false;
            recent.Enqueue(now);
            return true;
        }

        public static async Task<Dictionary<string, object>> Run(string text)
        {
            text = Regex.Replace((text ?? "").Trim(), @"\s+", " ").Trim('"', '“', '”', '«', '»', '.', ',', ';', ':', '!', '?', '(', ')');
            if (text.Length > 80) text = text.Substring(0, 80);
            var r = new Dictionary<string, object>();
            r["word"] = text;
            if (text.Length == 0) return r;
            string key = text.ToLowerInvariant();
            Dictionary<string, object> hit;
            if (cache.TryGetValue(key, out hit)) return hit;

            bool cyr = Cyrillic.IsMatch(text);
            bool latinWord = Regex.IsMatch(text, @"^[A-Za-z][A-Za-z'’\-]*$");
            bool proper = !cyr && char.IsUpper(text[0]) && !(text.Length > 1 && char.IsUpper(text[1]) && latinWord && text.ToUpperInvariant() == text && text.Length > 5);
            string tl = cyr ? "en" : "uk";

            bool oneWord = text.IndexOf(' ') < 0;
            var dictTask = latinWord ? Dict(text.ToLowerInvariant()) : Task.FromResult<Dictionary<string, object>>(null);
            var wiktTask = oneWord ? Wiktionary(text, cyr) : Task.FromResult<Dictionary<string, object>>(null);
            var wikiTask = Wiki(cyr ? "uk" : "en", text);
            var trTask = Translate(text, tl);
            await Task.WhenAll(wiktTask, wikiTask, trTask);
            // dictionaryapi.dev is sometimes slow for minutes: once the rest has answered, give it a moment, not six seconds
            if (!dictTask.IsCompleted) await Task.WhenAny(dictTask, Task.Delay(wiktTask.Result != null ? 400 : 1500));
            var dictResult = dictTask.IsCompleted ? dictTask.Result : null;

            var d = dictResult ?? wiktTask.Result;
            var w = wikiTask.Result;
            r["translation"] = trTask.Result;
            r["tlang"] = tl == "uk" ? "UA" : "EN";
            string explain = null;

            if (d != null)
            {
                r["phonetic"] = d["phonetic"];
                r["defs"] = d["defs"];
                r["source"] = d.ContainsKey("src") ? "Wiktionary" : "Dictionary";
                var defs = d["defs"] as List<Dictionary<string, object>>;
                if (defs != null && defs.Count > 0) explain = defs[0]["text"] as string;
            }
            if (w != null && (d == null || proper))
            {
                r["summary"] = w["extract"];
                r["summaryTitle"] = w["title"];
                r["summaryUrl"] = w["url"];
                if (d == null) { r["source"] = "Wikipedia"; explain = FirstSentence(w["extract"] as string); }
            }
            if (!cyr && !string.IsNullOrEmpty(explain)) r["explainTr"] = await Translate(explain, tl);
            if (d != null || w != null || r["translation"] != null) cache[key] = r;
            return r;
        }

        static async Task<Dictionary<string, object>> Dict(string word)
        {
            string body = await Fetch("https://api.dictionaryapi.dev/api/v2/entries/en/" + Uri.EscapeDataString(word));
            if (body == null) return null;
            try
            {
                var entries = Arr(json.DeserializeObject(body));
                if (entries == null || entries.Length == 0) return null;
                string phonetic = null;
                var defs = new List<Dictionary<string, object>>();
                foreach (var eo in entries)
                {
                    var e = eo as Dictionary<string, object>;
                    if (e == null) continue;
                    if (phonetic == null) phonetic = Str(e, "phonetic");
                    if (phonetic == null)
                    {
                        var ph = Get<object[]>(e, "phonetics");
                        if (ph != null) foreach (var p in ph) { var t = Str(p as Dictionary<string, object>, "text"); if (!string.IsNullOrEmpty(t)) { phonetic = t; break; } }
                    }
                    var meanings = Get<object[]>(e, "meanings");
                    if (meanings == null) continue;
                    foreach (var mo in meanings)
                    {
                        var m = mo as Dictionary<string, object>;
                        var list = Get<object[]>(m, "definitions");
                        if (list == null || list.Length == 0) continue;
                        var first = list[0] as Dictionary<string, object>;
                        string text = Str(first, "definition");
                        if (string.IsNullOrEmpty(text)) continue;
                        if (defs.Exists(x => (string)x["pos"] == Str(m, "partOfSpeech"))) continue;
                        defs.Add(new Dictionary<string, object> { { "pos", Str(m, "partOfSpeech") ?? "" }, { "text", text }, { "example", Str(first, "example") ?? "" } });
                        if (defs.Count >= 3) break;
                    }
                    if (defs.Count >= 3) break;
                }
                if (defs.Count == 0) return null;
                return new Dictionary<string, object> { { "phonetic", phonetic ?? "" }, { "defs", defs } };
            }
            catch (Exception) { return null; }
        }

        static async Task<Dictionary<string, object>> Wiki(string lang, string text)
        {
            string title = text.Replace(' ', '_');
            if (title.Length > 0 && char.IsLower(title[0]) && lang == "en") title = char.ToUpperInvariant(title[0]) + title.Substring(1);
            string body = await Fetch("https://" + lang + ".wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(title) + "?redirect=true");
            if (body == null) return null;
            try
            {
                var o = json.DeserializeObject(body) as Dictionary<string, object>;
                if (o == null || Str(o, "type") == "disambiguation") return null;
                string extract = Str(o, "extract");
                if (string.IsNullOrWhiteSpace(extract)) return null;
                if (extract.Length > 420)
                {
                    int cut = extract.LastIndexOf(". ", 420, StringComparison.Ordinal);
                    extract = cut > 120 ? extract.Substring(0, cut + 1) : extract.Substring(0, 420) + "…";
                }
                string url = null;
                var cu = Get<Dictionary<string, object>>(o, "content_urls");
                var desk = Get<Dictionary<string, object>>(cu, "desktop");
                url = Str(desk, "page");
                return new Dictionary<string, object> { { "title", Str(o, "title") ?? text }, { "extract", extract }, { "url", url ?? "" } };
            }
            catch (Exception) { return null; }
        }

        /// <summary>Google's free endpoint first; if it refuses (429), its Chrome-extension twin, then MyMemory.</summary>
        public static async Task<string> Translate(string text, string target)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (text.Length > 600) text = text.Substring(0, 600);
            string q = Uri.EscapeDataString(text);
            string r = FirstText(await Fetch("https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=" + target + "&dt=t&q=" + q), true);
            if (r == null) r = FirstText(await Fetch("https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=auto&tl=" + target + "&q=" + q), false);
            if (r == null)
            {
                string source = target == "uk" ? "en" : "uk";
                string body = await Fetch("https://api.mymemory.translated.net/get?q=" + q + "&langpair=" + source + "|" + target);
                try
                {
                    var o = json.DeserializeObject(body ?? "") as Dictionary<string, object>;
                    var rd = Get<Dictionary<string, object>>(o, "responseData");
                    r = Str(rd, "translatedText");
                    if (r != null && r.IndexOf("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) >= 0) r = null;
                }
                catch (Exception) { r = null; }
            }
            return string.IsNullOrWhiteSpace(r) ? null : r.Trim();
        }

        /// <summary>
        /// In-place page translation: many short texts at once. They travel as one request, one text per line,
        /// and come back the same way. If Google merges or splits lines, each text is translated on its own.
        /// Null when Google won't answer (rate limit or offline).
        /// </summary>
        public static async Task<List<string>> TranslateBatch(List<string> texts, string target)
        {
            if (texts == null || texts.Count == 0) return new List<string>();
            var clean = texts.Select(t => Regex.Replace(t ?? "", @"\s+", " ").Trim()).ToList();
            string joined = string.Join("\n", clean);
            string body = await Fetch("https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=" + Uri.EscapeDataString(target) + "&dt=t&q=" + Uri.EscapeDataString(joined));
            string all = FirstText(body, true);
            if (all != null)
            {
                var lines = all.Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();
                while (lines.Count > clean.Count && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
                if (lines.Count == clean.Count) return lines;
                Log.Info("Batch translation came back with " + lines.Count + " lines for " + clean.Count + "; translating one by one");
            }
            else if (body == null) return null;
            // one by one, a few at a time
            var result = new string[clean.Count];
            var gate = new System.Threading.SemaphoreSlim(4);
            var jobs = clean.Select(async (t, i) =>
            {
                await gate.WaitAsync();
                try { result[i] = await Translate(t, target) ?? t; }
                finally { gate.Release(); }
            });
            await Task.WhenAll(jobs);
            return result.ToList();
        }

        /// <summary>gtx: [[["переклад","source",…],…],null,"en"] (segments joined). clients5: ["переклад"] or [["переклад","en"]].</summary>
        static string FirstText(string body, bool segments)
        {
            if (body == null) return null;
            try
            {
                var o = Arr(json.DeserializeObject(body));
                if (o == null || o.Length == 0) return null;
                if (segments)
                {
                    var segs = Arr(o[0]);
                    if (segs == null) return null;
                    var sb = new StringBuilder();
                    foreach (var seg in segs) { var a = Arr(seg); if (a != null && a.Length > 0 && a[0] is string) sb.Append((string)a[0]); }
                    return sb.Length == 0 ? null : sb.ToString();
                }
                if (o[0] is string) return (string)o[0];
                var inner = Arr(o[0]);
                return inner != null && inner.Length > 0 ? inner[0] as string : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>English Wiktionary definitions: a fallback for English words and the source for Ukrainian/Russian ones.</summary>
        static async Task<Dictionary<string, object>> Wiktionary(string word, bool cyr)
        {
            string body = await Fetch("https://en.wiktionary.org/api/rest_v1/page/definition/" + Uri.EscapeDataString(cyr ? word.ToLowerInvariant() : word.ToLowerInvariant()));
            if (body == null) return null;
            try
            {
                var o = json.DeserializeObject(body) as Dictionary<string, object>;
                if (o == null) return null;
                var langs = cyr ? new[] { "uk", "ru", "be" } : new[] { "en" };
                var defs = new List<Dictionary<string, object>>();
                foreach (var lang in langs)
                {
                    var entries = Get<object[]>(o, lang);
                    if (entries == null) continue;
                    foreach (var eo in entries)
                    {
                        var e = eo as Dictionary<string, object>;
                        var list = Get<object[]>(e, "definitions");
                        if (list == null) continue;
                        foreach (var dob in list)
                        {
                            string text = StripHtml(Str(dob as Dictionary<string, object>, "definition"));
                            if (string.IsNullOrWhiteSpace(text)) continue;
                            defs.Add(new Dictionary<string, object> { { "pos", (Str(e, "partOfSpeech") ?? "").ToLowerInvariant() }, { "text", text }, { "example", "" } });
                            break; // first sense per part of speech
                        }
                        if (defs.Count >= 3) break;
                    }
                    if (defs.Count > 0) break;
                }
                if (defs.Count == 0) return null;
                return new Dictionary<string, object> { { "phonetic", "" }, { "defs", defs }, { "src", "Wiktionary" } };
            }
            catch (Exception) { return null; }
        }

        static string StripHtml(string html)
        {
            if (html == null) return null;
            string t = Regex.Replace(html, "<[^>]+>", "");
            return WebUtility.HtmlDecode(t).Trim();
        }

        static string FirstSentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int i = s.IndexOf(". ", StringComparison.Ordinal);
            return i > 20 ? s.Substring(0, i + 1) : s;
        }

        static async Task<string> Get(string url)
        {
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    // Google rate-limits unknown clients, so look like the Edge engine we are; Wikimedia asks for an honest UA.
                    bool wikimedia = url.Contains("wikipedia.org") || url.Contains("wiktionary.org");
                    wc.Headers[HttpRequestHeader.UserAgent] = wikimedia
                        ? "Sidearm/" + Program.Version.Split(' ')[0] + " (personal word look-up tool for Windows)"
                        : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0";
                    wc.Headers[HttpRequestHeader.Accept] = "application/json";
                    var task = wc.DownloadStringTaskAsync(new Uri(url));
                    if (await Task.WhenAny(task, Task.Delay(6000)) != task)
                    {
                        wc.CancelAsync();
                        Log.Info("Look-up timed out: " + new Uri(url).Host);
                        return null;
                    }
                    return await task;
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp == null || resp.StatusCode != HttpStatusCode.NotFound) Log.Info("Look-up request failed (" + new Uri(url).Host + "): " + ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Look-up request failed", ex);
                return null;
            }
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) ? v as string : null;
        }

        static T Get<T>(Dictionary<string, object> d, string k) where T : class
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v)) return null;
            if (typeof(T) == typeof(object[])) return Arr(v) as T;
            return v as T;
        }

        /// <summary>JSON arrays may arrive as object[] or ArrayList depending on the call; take either.</summary>
        static object[] Arr(object v)
        {
            if (v is object[]) return (object[])v;
            var list = v as System.Collections.IEnumerable;
            if (list == null || v is string) return null;
            var r = new List<object>();
            foreach (var o in list) r.Add(o);
            return r.ToArray();
        }

        public static Func<string, Task<string>> Fetch = Get; // swapped out by tests
    }
}
