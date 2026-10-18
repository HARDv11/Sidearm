using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>0.6: page translation, your accent on the window frame, and the speed work.</summary>
    internal sealed partial class MainForm
    {
        // ───────────────────────── translate ─────────────────────────
        // Google's translation proxy: example.com becomes example-com.translate.goog, translated, links included.

        const string TrSuffix = ".translate.goog";
        readonly HashSet<string> offeredTranslate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static bool IsTranslated(string url)
        {
            Uri u;
            return Uri.TryCreate(url ?? "", UriKind.Absolute, out u) && u.Host.EndsWith(TrSuffix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>example.com/page → example-com.translate.goog/page?_x_tr_sl=auto&amp;_x_tr_tl=uk</summary>
        internal static string TranslateUrl(string url, string to)
        {
            Uri u;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out u) || u.Scheme != "https" && u.Scheme != "http") return null;
            string host = u.Host.Replace("-", "--").Replace(".", "-") + TrSuffix;
            string q = u.Query.TrimStart('?');
            string tr = "_x_tr_sl=auto&_x_tr_tl=" + Uri.EscapeDataString(to) + "&_x_tr_hl=" + Uri.EscapeDataString(to) + "&_x_tr_pto=wapp";
            return "https://" + host + u.AbsolutePath + "?" + (q.Length > 0 ? q + "&" : "") + tr + u.Fragment;
        }

        /// <summary>The page a translated address came from.</summary>
        internal static string OriginalOf(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out u) || !u.Host.EndsWith(TrSuffix, StringComparison.OrdinalIgnoreCase)) return url;
            string sub = u.Host.Substring(0, u.Host.Length - TrSuffix.Length);
            string host = sub.Replace("--", "\u0001").Replace("-", ".").Replace("\u0001", "-");
            var keep = u.Query.TrimStart('?').Split('&').Where(p => p.Length > 0 && !p.StartsWith("_x_tr_", StringComparison.Ordinal)).ToArray();
            return "https://" + host + u.AbsolutePath + (keep.Length > 0 ? "?" + string.Join("&", keep) : "") + u.Fragment;
        }

        void ToggleTranslate(Tab tab)
        {
            if (!IsWebPage(tab)) { Toast("Translate works on web pages", null, null); return; }
            try
            {
                if (tab.InPlace || (S.translateMode == "inplace" && !IsTranslated(tab.Url))) { ToggleInPlace(tab); return; }
                if (IsTranslated(tab.Url))
                {
                    string orig = OriginalOf(tab.Url);
                    tab.NoAutoTranslate = orig;
                    tab.View.CoreWebView2.Navigate(orig);
                    return;
                }
                string tr = TranslateUrl(tab.Url, S.translateTo);
                if (tr == null) { Toast("This page can't be translated", null, null); return; }
                tab.View.CoreWebView2.Navigate(tr);
            }
            catch (Exception ex) { Log.Error("Translate", ex); }
        }

        /// <summary>Languages you read, so they're never offered for translation.</summary>
        bool Reads(string lang)
        {
            if (string.Equals(lang, S.translateTo, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var l in (S.neverTranslate ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (string.Equals(l.Trim(), lang, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void OnPageLang(Tab tab, string lang)
        {
            lang = (lang ?? "").Trim().ToLowerInvariant();
            if (lang.Length > 8) lang = lang.Substring(0, 8);
            tab.Lang = lang;
            if (tab == active) PushTabs();
            if (tab != active || lang.Length < 2 || Reads(lang) || IsTranslated(tab.Url) || tab.InPlace) return;
            if (tab.NoAutoTranslate != null && tab.NoAutoTranslate == tab.Url) return; // you asked for the original
            var site = Sites.Get(tab.PageHost);
            if (site.autoTranslate || S.autoTranslate == "always") { ToggleTranslate(tab); return; }
            if (S.autoTranslate == "offer" && offeredTranslate.Add(Sites.Key(tab.PageHost)))
                Toast("This page is in " + LangName(lang), "Translate", "translate");
        }

        static readonly Dictionary<string, string> LangNames = new Dictionary<string, string>
        {
            { "en", "English" }, { "uk", "Ukrainian" }, { "de", "German" }, { "fr", "French" }, { "es", "Spanish" }, { "it", "Italian" },
            { "pl", "Polish" }, { "ru", "Russian" }, { "be", "Belarusian" }, { "cs", "Czech" }, { "sk", "Slovak" }, { "pt", "Portuguese" },
            { "nl", "Dutch" }, { "sv", "Swedish" }, { "no", "Norwegian" }, { "nb", "Norwegian" }, { "da", "Danish" }, { "fi", "Finnish" },
            { "et", "Estonian" }, { "lv", "Latvian" }, { "lt", "Lithuanian" }, { "hu", "Hungarian" }, { "ro", "Romanian" }, { "bg", "Bulgarian" },
            { "sr", "Serbian" }, { "hr", "Croatian" }, { "sl", "Slovenian" }, { "el", "Greek" }, { "tr", "Turkish" }, { "ka", "Georgian" },
            { "he", "Hebrew" }, { "ar", "Arabic" }, { "fa", "Persian" }, { "hi", "Hindi" }, { "ja", "Japanese" }, { "ko", "Korean" },
            { "zh", "Chinese" }, { "vi", "Vietnamese" }, { "th", "Thai" }, { "id", "Indonesian" }
        };
        static string LangName(string code) { string n; return LangNames.TryGetValue(code, out n) ? n : code.ToUpperInvariant(); }

        // ───────────────────────── your color on the window frame ─────────────────────────

        /// <summary>Your accent as the pages draw it: nudged light on dark, deep on light (same rule as ui\look.js).</summary>
        static Color? ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return null;
            try { int v = Convert.ToInt32(hex.Substring(1), 16); return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255); }
            catch (Exception) { return null; }
        }

        string ReadableAccent(out string onAccent)
        {
            onAccent = "";
            var c = ParseHex(S.accent);
            if (c == null) return "";
            float h = c.Value.GetHue() / 360f, s = Math.Min(c.Value.GetSaturation(), 0.85f), l = c.Value.GetBrightness();
            l = IsLight ? Math.Max(0.24f, Math.Min(0.42f, l)) : Math.Max(0.55f, Math.Min(0.74f, l));
            var rgb = FromHsl(h, s, l);
            double lum = 0.2126 * Lin(rgb.R) + 0.7152 * Lin(rgb.G) + 0.0722 * Lin(rgb.B);
            onAccent = lum > 0.42 ? "#17120a" : "#fff8ea";
            return string.Format("#{0:x2}{1:x2}{2:x2}", rgb.R, rgb.G, rgb.B);
        }

        static double Lin(int v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }

        static Color FromHsl(float h, float s, float l)
        {
            Func<float, float, float, float> f = (p, q, t) =>
            {
                if (t < 0) t += 1; if (t > 1) t -= 1;
                if (t < 1f / 6) return p + (q - p) * 6 * t;
                if (t < 0.5f) return q;
                if (t < 2f / 3) return p + (q - p) * (2f / 3 - t) * 6;
                return p;
            };
            if (s <= 0) { int g = (int)Math.Round(l * 255); return Color.FromArgb(g, g, g); }
            float q2 = l < 0.5f ? l * (1 + s) : l + s - l * s, p2 = 2 * l - q2;
            return Color.FromArgb((int)Math.Round(f(p2, q2, h + 1f / 3) * 255), (int)Math.Round(f(p2, q2, h) * 255), (int)Math.Round(f(p2, q2, h - 1f / 3) * 255));
        }

        /// <summary>The window's own edge takes the strip's tint, so the frame and the strip match.</summary>
        Color Tinted(Color baseColor)
        {
            var c = ParseHex(S.accent) ?? ParseHex(S.accent2);
            if (c == null || S.tint <= 0) return baseColor;
            double k = S.tint / 100.0 * (IsLight ? 0.32 : 0.38);
            return Color.FromArgb((int)(baseColor.R + (c.Value.R - baseColor.R) * k), (int)(baseColor.G + (c.Value.G - baseColor.G) * k), (int)(baseColor.B + (c.Value.B - baseColor.B) * k));
        }

        void ApplyAccentFrame()
        {
            if (!IsHandleCreated) return;
            int bgr = IsLight ? 0x00AEBDC4 : 0x00333B3D;
            if (!string.IsNullOrEmpty(S.accent) && S.accent.Length == 7)
            {
                int rgb = Convert.ToInt32(S.accent.Substring(1), 16);
                bgr = ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
            }
            Native.StyleFrame(Handle, bgr, !IsLight);
        }

        // ───────────────────────── load timing ─────────────────────────
        // So a slow page can be blamed fairly: the site, or Sidearm's own filtering.

        static double TicksToMs(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }

        void OnPageLoaded(Tab tab, bool ok)
        {
            if (tab.NavStart == 0) return;
            tab.LoadMs = (int)TicksToMs(Stopwatch.GetTimestamp() - tab.NavStart);
            tab.NavStart = 0;
            if (!tab.IsNewTab && IsWebUrl(tab.Url))
                Log.Info(string.Format("Loaded {0} in {1} ms{2}; filtering {3:0.0} ms over {4} requests", tab.PageHost, tab.LoadMs,
                    ok ? "" : " (failed)", TicksToMs(tab.FilterTicks), tab.Requests));
        }

        // ───────────────────────── a spare tab engine ─────────────────────────
        // Starting a page engine is the slow part of a new tab. While Sidearm is drawn, one blank engine
        // waits ready; it's thrown away on holster, so it costs nothing while you play.

        WebView2 spare;
        bool spareBusy;
        readonly Timer spareTimer = new Timer { Interval = 1500 };
        bool spareTimerHooked;

        void PrepareSpareSoon()
        {
            if (!S.spareTab || spare != null || spareBusy) return;
            if (!spareTimerHooked)
            {
                spareTimerHooked = true;
                spareTimer.Tick += (s, e) => { spareTimer.Stop(); CreateSpare(); };
            }
            spareTimer.Stop();
            spareTimer.Start();
        }

        async void CreateSpare()
        {
            if (!S.spareTab || spare != null || spareBusy || env == null || !Visible) return;
            spareBusy = true;
            try
            {
                var v = new WebView2 { DefaultBackgroundColor = PageColor, Dock = DockStyle.Fill, Visible = false };
                content.Controls.Add(v);
                v.KeyDown += OnViewKeyDown;
                var forceHandle = v.Handle;
                await v.EnsureCoreWebView2Async(env);
                if (!Visible || !S.spareTab) { content.Controls.Remove(v); v.Dispose(); return; }
                spare = v;
            }
            catch (Exception ex) { Log.Error("Spare tab", ex); }
            finally { spareBusy = false; }
        }

        /// <summary>Hands over the waiting engine, if there is one.</summary>
        WebView2 TakeSpare()
        {
            var v = spare;
            spare = null;
            if (v != null && (v.IsDisposed || v.CoreWebView2 == null)) return null;
            if (v != null) PrepareSpareSoon(); // and start the next one
            return v;
        }

        void DropSpare()
        {
            spareTimer.Stop();
            var v = spare;
            spare = null;
            if (v == null) return;
            try { content.Controls.Remove(v); v.Dispose(); } catch (Exception) { }
        }

        // ───────────────────────── warm start ─────────────────────────
        // Started with Windows: get the engine going in the background, then let it sleep,
        // so even the first draw is quick.

        void WarmStartSoon()
        {
            var t = new Timer { Interval = 20000 }; // let Windows finish starting up first
            t.Tick += async (s, e) =>
            {
                t.Stop(); t.Dispose();
                if (Visible || initTask != null) return;
                try
                {
                    Log.Info("Warm start: starting the engine in the background");
                    await EnsureInitialized();
                    if (Visible) return;
                    sleepTimer.Interval = 4000;
                    sleepTimer.Start();
                    if (S.unloadAfterHideMinutes > 0)
                    {
                        unloadTimer.Interval = Math.Min(int.MaxValue / 2, S.unloadAfterHideMinutes * 60000);
                        unloadTimer.Start();
                    }
                }
                catch (Exception ex) { Log.Error("Warm start", ex); }
            };
            t.Start();
        }

        // ───────────────────────── background tabs use less memory ─────────────────────────

        static void SetMemoryLow(Tab tab, bool low)
        {
            try
            {
                if (tab.View != null && tab.View.CoreWebView2 != null)
                    tab.View.CoreWebView2.MemoryUsageTargetLevel = low ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch (Exception) { } // older WebView2 runtimes don't have this
        }

        object BrowserInfo()
        {
            string exe;
            var k = TargetBrowser(out exe);
            return new { name = k.Name, path = exe ?? "", choice = S.sendTo ?? "auto", zen = FindExe(KindById("zen")) != null };
        }
    }
}
