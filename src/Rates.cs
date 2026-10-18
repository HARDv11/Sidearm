using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Sidearm
{
    /// <summary>
    /// Exchange rates for "100 usd" in the bar: the National Bank of Ukraine's official rates
    /// (hryvnias for one unit of each currency), refreshed every few hours and kept in data\rates.json.
    /// If the NBU can't be reached, open.er-api.com is the fallback. Neither needs a key.
    /// </summary>
    internal static class Rates
    {
        public static Dictionary<string, double> Uah;   // "USD" → 41.5 (hryvnias)
        public static string Date = "", Source = "";
        public static event Action Changed;
        static DateTime fetchedUtc = DateTime.MinValue;
        static bool busy;

        static string FilePath { get { return Path.Combine(Program.DataDir, "rates.json"); } }

        /// <summary>What the bar needs, or null before the first download.</summary>
        public static object Snapshot()
        {
            var u = Uah;
            return u == null ? null : new { date = Date, source = Source, uah = u };
        }

        /// <summary>Load the saved copy, then refresh in the background if it's older than six hours.</summary>
        public static void Start()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(FilePath, Encoding.UTF8));
                    var raw = d != null && d.ContainsKey("uah") ? d["uah"] as Dictionary<string, object> : null;
                    if (raw != null)
                    {
                        var map = new Dictionary<string, double>();
                        foreach (var kv in raw) map[kv.Key] = Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture);
                        Uah = map;
                        Date = Json.Str(d, "date") ?? "";
                        Source = Json.Str(d, "source") ?? "";
                        fetchedUtc = File.GetLastWriteTimeUtc(FilePath);
                    }
                }
            }
            catch (Exception ex) { Log.Error("rates.json", ex); }
            RefreshIfOld();
        }

        public static void RefreshIfOld()
        {
            if (busy || (DateTime.UtcNow - fetchedUtc).TotalHours < 6) return;
            busy = true;
            Task.Run(() =>
            {
                try
                {
                    if (FromNbu() || FromErApi())
                    {
                        fetchedUtc = DateTime.UtcNow;
                        Save();
                        var h = Changed;
                        if (h != null) h();
                    }
                    else fetchedUtc = DateTime.UtcNow.AddHours(-5); // try again in an hour
                }
                catch (Exception ex) { Log.Error("Exchange rates", ex); }
                finally { busy = false; }
            });
        }

        static bool FromNbu()
        {
            string body = Fetch("https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?json");
            if (body == null) return false;
            try
            {
                var list = new JavaScriptSerializer().DeserializeObject(body) as object[];
                if (list == null || list.Length < 10) return false;
                var map = new Dictionary<string, double>();
                string date = "";
                foreach (var o in list)
                {
                    var e = o as Dictionary<string, object>;
                    string cc = Json.Str(e, "cc");
                    double rate = Json.Num(e, "rate", 0);
                    if (string.IsNullOrEmpty(cc) || cc.Length != 3 || rate <= 0) continue;
                    map[cc.ToUpperInvariant()] = rate;
                    if (date.Length == 0) date = Json.Str(e, "exchangedate") ?? "";
                }
                if (!map.ContainsKey("USD")) return false;
                Uah = map; Date = date; Source = "NBU";
                Log.Info("Exchange rates from the NBU for " + date + ": " + map.Count + " currencies");
                return true;
            }
            catch (Exception ex) { Log.Error("NBU rates", ex); return false; }
        }

        static bool FromErApi()
        {
            string body = Fetch("https://open.er-api.com/v6/latest/UAH");
            if (body == null) return false;
            try
            {
                var d = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>;
                var rates = d != null && d.ContainsKey("rates") ? d["rates"] as Dictionary<string, object> : null;
                if (rates == null) return false;
                var map = new Dictionary<string, double>();
                foreach (var kv in rates)
                {
                    double perUah = Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture);
                    if (perUah > 0 && kv.Key.Length == 3 && kv.Key != "UAH") map[kv.Key] = 1 / perUah;
                }
                if (!map.ContainsKey("USD")) return false;
                Uah = map; Date = DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture); Source = "ER-API";
                Log.Info("Exchange rates from open.er-api.com: " + map.Count + " currencies");
                return true;
            }
            catch (Exception ex) { Log.Error("ER-API rates", ex); return false; }
        }

        internal static Func<string, string> Fetch = Get; // swapped out by tests

        static void Save()
        {
            try { File.WriteAllText(FilePath, Json.Serialize(new { date = Date, source = Source, uah = Uah }), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Error("Save rates", ex); }
        }

        static string Get(string url)
        {
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.UserAgent] = "Sidearm/" + Program.Version.Split(' ')[0] + " (currency conversion)";
                    var t = wc.DownloadStringTaskAsync(new Uri(url));
                    if (!t.Wait(8000)) { wc.CancelAsync(); Log.Info("Rates timed out: " + new Uri(url).Host); return null; }
                    return t.Result;
                }
            }
            catch (Exception ex) { Log.Info("Rates request failed (" + new Uri(url).Host + "): " + (ex.InnerException ?? ex).Message); return null; }
        }
    }
}
