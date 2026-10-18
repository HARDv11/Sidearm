using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    /// <summary>0.7: the quick bar, timers, money, video pop-out, in-place translation and uBlock Origin Lite.</summary>
    internal sealed partial class MainForm
    {
        // ───────────────────────── quick bar ─────────────────────────

        const int QuickHotkeyId = 0x5A1F;
        QuickBar quick;
        Task quickInit;
        bool quickKeyOk, quickKeyWarned;
        readonly Timer quickSleep = new Timer { Interval = 30000 };

        void RegisterQuickHotkey()
        {
            if (!IsHandleCreated) return;
            Native.UnregisterHotKey(Handle, QuickHotkeyId);
            quickKeyOk = false;
            if (!S.quickBar) return;
            uint mods, vk;
            if (!Native.ParseHotkey(S.quickHotkey, out mods, out vk)) { Log.Info("Could not parse quick bar key " + S.quickHotkey); return; }
            quickKeyOk = Native.RegisterHotKey(Handle, QuickHotkeyId, mods | Native.MOD_NOREPEAT, vk);
            Log.Info(quickKeyOk ? "Quick bar key registered: " + S.quickHotkey : "Quick bar key " + S.quickHotkey + " is taken by another app");
        }

        async void SummonQuick()
        {
            try
            {
                if (quick != null && quick.Visible) { quick.Dismiss(false); return; } // the key again closes it
                if (Visible && !ghost && IsForegroundOurs()) { FocusBar(true); return; } // Sidearm is up: use its own bar
                quickSleep.Stop();
                await EnsureQuick();
                if (quick == null) return;
                quick.Place(S.quickPosition); // show the window first, then wake its view (see NudgeAfterShow)
                SetViewVisible(quick.View, true);
                Resume(quick.View);
                quick.Present();
                Rates.RefreshIfOld();
            }
            catch (Exception ex) { Log.Error("Quick bar", ex); }
        }

        async Task EnsureQuick()
        {
            if (quick != null && !quick.IsDisposed) { if (quickInit != null) await quickInit; return; }
            await EnsureInitialized();
            if (env == null) return;
            quick = new QuickBar(FrameColor);
            quick.ForegroundIsOurs = IsForegroundOurs; // Sidearm's own windows count too, not only the engine's
            quick.Message += OnQuickMessage;
            quick.VisibleChanged += (s, e) => { if (quick != null && !quick.Visible) { quickSleep.Stop(); quickSleep.Start(); } };
            var h = quick.Handle;
            Native.StyleFrame(quick.Handle, ToBgr(FrameLine), !IsLight);
            quickInit = quick.Init(env, S.uiScale);
            await quickInit;
            for (int i = 0; i < 40 && !quick.Ready; i++) await Task.Delay(50); // the page says hello first
        }

        void InitQuickSleep()
        {
            quickSleep.Tick += async (s, e) =>
            {
                quickSleep.Stop();
                if (quick != null && !quick.Visible) await TrySuspend(quick.View);
            };
        }

        void DropQuick()
        {
            if (quick == null) return;
            try { quick.Message -= OnQuickMessage; quick.Dispose(); } catch (Exception) { }
            quick = null; quickInit = null;
        }

        void PostToQuick(object message)
        {
            if (quick != null && quick.Ready) quick.Post(message);
        }

        async void OnQuickMessage(Dictionary<string, object> m)
        {
            string op = Json.Str(m, "op");
            try
            {
                switch (op)
                {
                    case "ready":
                        quick.Post(InitMessage());
                        PushTimers();
                        break;
                    case "go":
                        string url = Json.Str(m, "url");
                        if (string.IsNullOrEmpty(url)) return;
                        quick.Dismiss(true);
                        await DrawAsync();
                        Navigate(url, Json.Bool(m, "newTab"));
                        break;
                    case "zen":
                        quick.Dismiss(false);
                        SendToBrowser(Json.Str(m, "url"), Json.Bool(m, "private"));
                        break;
                    case "copy":
                        try { Clipboard.SetText(Json.Str(m, "text") ?? ""); } catch (Exception) { }
                        break;
                    case "timer":
                        OnTimerMessage(m); // the bar says "Timer set" and closes itself
                        break;
                    case "note":
                        AppendNote(Json.Str(m, "text"), false); // the bar says "Noted" and closes itself
                        break;
                    case "esc":
                        quick.Dismiss(false);
                        break;
                    case "settings":
                        quick.Dismiss(true);
                        OpenSettingsPage();
                        break;
                }
            }
            catch (Exception ex) { Log.Error("Quick bar: " + op, ex); }
        }

        static int ToBgr(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // ───────────────────────── timers ─────────────────────────
        // "timer 5m boss" in either bar: a chime and a notification when it's up, even while holstered.

        sealed class GameTimer { public int Id; public string Label; public DateTime Ends; public int Seconds; }
        readonly List<GameTimer> timers = new List<GameTimer>();
        readonly Timer timerTick = new Timer { Interval = 250 };
        int nextTimerId = 1;

        void InitTimers()
        {
            timerTick.Tick += (s, e) => CheckTimers();
        }

        void OnTimerMessage(Dictionary<string, object> m)
        {
            if (Json.Str(m, "op") == "cancel" || Json.Bool(m, "cancel"))
            {
                int id = (int)Json.Num(m, "id", 0);
                int n = id > 0 ? timers.RemoveAll(t => t.Id == id) : timers.Count;
                if (id <= 0) timers.Clear();
                Toast(n > 0 ? (n == 1 ? "Timer stopped" : "Timers stopped") : "No timers running", null, null);
                PushTimers();
                return;
            }
            int secs = (int)Json.Num(m, "seconds", 0);
            if (secs < 1 || secs > 86400) return;
            if (timers.Count >= 10) { Toast("Ten timers is the limit", null, null); return; }
            string label = Trim((Json.Str(m, "label") ?? "").Trim(), 60);
            timers.Add(new GameTimer { Id = nextTimerId++, Label = label, Seconds = secs, Ends = DateTime.UtcNow.AddSeconds(secs) });
            timerTick.Start();
            Log.Info("Timer set: " + Clock(secs) + (label.Length > 0 ? " (" + label + ")" : ""));
            Toast(label.Length > 0 ? "Timer set: " + Clock(secs) + " " + label : "Timer set: " + Clock(secs), null, null);
            PushTimers();
        }

        void CheckTimers()
        {
            if (timers.Count == 0) { timerTick.Stop(); return; }
            var now = DateTime.UtcNow;
            var due = timers.Where(t => t.Ends <= now).ToList();
            if (due.Count == 0) return;
            foreach (var t in due)
            {
                timers.Remove(t);
                string what = t.Label.Length > 0 ? t.Label : "Your " + Clock(t.Seconds) + " timer";
                Chime.Play();
                ShowBalloon("Time's up", what);
                if (Visible) Toast("Time's up: " + what, null, null);
                Log.Info("Timer done: " + what);
            }
            PushTimers();
        }

        void PushTimers()
        {
            long now = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
            var list = timers.Select(t => new { id = t.Id, label = t.Label, ends = (long)(t.Ends - Epoch).TotalMilliseconds }).ToList();
            var msg = new { type = "timers", now = now, list = list };
            PostToUi(msg);
            PostToQuick(msg);
        }

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static string Clock(int secs)
        {
            int h = secs / 3600, m = secs % 3600 / 60, s = secs % 60;
            return (h > 0 ? h + ":" + m.ToString("00") : m.ToString()) + ":" + s.ToString("00");
        }

        // ───────────────────────── money ─────────────────────────

        void InitRates()
        {
            Rates.Changed += () =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        var msg = new { type = "rates", rates = Rates.Snapshot() };
                        PostToUi(msg);
                        PostToQuick(msg);
                    }));
                }
                catch (Exception) { }
            };
            Rates.Start();
        }

        // ───────────────────────── video pop-out ─────────────────────────
        // Alt+P, the shield panel or the right-click menu: the page's video in its own small always-on-top
        // window that stays up while Sidearm is holstered.

        VideoWindow video;
        Tab videoTab; // a tab-like record so the pop-out gets ad blocking too

        const string VideoProbeJs = @"(function(){
  var vs=[].slice.call(document.querySelectorAll('video')).filter(function(v){return v.offsetWidth*v.offsetHeight>0||v.currentTime>0;});
  vs.sort(function(a,b){return b.offsetWidth*b.offsetHeight-a.offsetWidth*a.offsetHeight;});
  var v=vs[0]; if(!v) return {found:false,title:document.title};
  var r={found:true,time:v.currentTime||0,paused:v.paused,title:document.title};
  try{v.pause();}catch(e){}
  return r;})()";

        async void PopOutVideo(Tab tab)
        {
            if (!IsWebPage(tab)) { Toast("Pop out works on pages with a video", null, null); return; }
            try
            {
                var info = Json.Parse(await tab.View.CoreWebView2.ExecuteScriptAsync(VideoProbeJs));
                double time = Json.Num(info, "time", 0);
                string title = Json.Str(info, "title") ?? tab.Title ?? "";
                string page = IsTranslated(tab.Url) ? OriginalOf(tab.Url) : tab.Url;
                string wrapped = VideoEmbedUrl(page, time);
                if (wrapped == null && !Json.Bool(info, "found")) { Toast("No video on this page", null, null); return; }
                await OpenVideoWindow(wrapped ?? page, wrapped == null ? time : -1, title);
                if (tab == glance) CloseGlance();
            }
            catch (Exception ex) { Log.Error("Pop out video", ex); Toast("Couldn't pop out this video", null, null); }
        }

        static readonly Regex YouTubeId = new Regex(@"^https?://(?:(?:www|m)\.)?(?:youtube\.com/(?:watch\?(?:.*&)?v=|shorts/|live/|embed/)|youtu\.be/)([A-Za-z0-9_-]{11})", RegexOptions.IgnoreCase);
        static readonly Regex TwitchVideo = new Regex(@"^https?://(?:www\.|m\.)?twitch\.tv/videos/(\d+)", RegexOptions.IgnoreCase);
        static readonly Regex TwitchChannel = new Regex(@"^https?://(?:www\.|m\.)?twitch\.tv/([A-Za-z0-9_]{3,25})/?(?:[?#].*)?$", RegexOptions.IgnoreCase);
        static readonly HashSet<string> TwitchReserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "directory", "videos", "settings", "subscriptions", "inventory", "wallet", "drops", "search", "downloads", "jobs", "p", "turbo", "store" };

        /// <summary>YouTube and Twitch play best in their own embedded players (Sidearm's pop.html hosts them).</summary>
        internal static string VideoEmbedUrl(string url, double time)
        {
            int t = (int)Math.Max(0, time);
            var m = YouTubeId.Match(url ?? "");
            if (m.Success)
            {
                if (t == 0) { var tm = Regex.Match(url, @"[?&#]t=(\d+)"); if (tm.Success) t = int.Parse(tm.Groups[1].Value); }
                return UiOrigin + "pop.html?yt=" + m.Groups[1].Value + "&t=" + t;
            }
            m = TwitchVideo.Match(url ?? "");
            if (m.Success) return UiOrigin + "pop.html?twv=" + m.Groups[1].Value + "&t=" + t;
            m = TwitchChannel.Match(url ?? "");
            if (m.Success && !TwitchReserved.Contains(m.Groups[1].Value)) return UiOrigin + "pop.html?twc=" + m.Groups[1].Value.ToLowerInvariant();
            return null;
        }

        static bool IsVideoSite(string url) { return VideoEmbedUrl(url, 0) != null; }

        /// <summary>For any other site: open the page and let its biggest video fill the window.</summary>
        static string VideoFocusJs(double time)
        {
            return @"(function(T){
  if(window.top!==window||window.__sidearmPop)return; window.__sidearmPop=1;
  var tries=0;
  function esc(e){if(e.key==='Escape'){try{chrome.webview.postMessage({t:'esc'})}catch(x){}}}
  window.addEventListener('keydown',esc,true);
  function go(){
    var vs=[].slice.call(document.querySelectorAll('video'));
    vs.sort(function(a,b){return b.offsetWidth*b.offsetHeight-a.offsetWidth*a.offsetHeight;});
    var v=vs[0];
    if(!v||(!v.offsetWidth&&!v.currentSrc)){if(++tries<60)setTimeout(go,500);return;}
    var st=document.createElement('style');
    st.textContent='html,body{overflow:hidden!important;background:#000!important;margin:0!important}body>*:not(.__sa_v):not(.__sa_play){display:none!important}'+
      '.__sa_v{position:fixed!important;left:0!important;top:0!important;width:100vw!important;height:100vh!important;max-width:none!important;max-height:none!important;z-index:2147483646!important;object-fit:contain!important;background:#000!important;transform:none!important;margin:0!important;display:block!important;visibility:visible!important;opacity:1!important}'+
      '.__sa_play{position:fixed;inset:0;z-index:2147483647;display:flex;align-items:center;justify-content:center;background:rgba(0,0,0,.35);color:#fff;font:600 15px system-ui;cursor:pointer}';
    (document.head||document.documentElement).appendChild(st);
    v.classList.add('__sa_v'); v.controls=true; v.muted=false;
    document.body.appendChild(v);
    if(T>0){try{v.currentTime=T}catch(e){}}
    var p=v.play();
    if(p&&p.catch)p.catch(function(){
      var b=document.createElement('div');b.className='__sa_play';b.textContent='▶  Click to play';
      b.onclick=function(){b.remove();v.play();};document.body.appendChild(b);
    });
  }
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',go);else go();
})(" + time.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        async Task OpenVideoWindow(string url, double time, string title)
        {
            await EnsureEnvironment();
            if (video != null && !video.IsDisposed) video.Close(); // one at a time
            var bounds = VideoBounds();
            var w = new VideoWindow(Trim(title, 60), bounds, Color.Black);
            video = w;
            videoTab = new Tab { Id = -2, Url = url, Title = title };
            w.Closing2 += () =>
            {
                if (w.WindowState == FormWindowState.Normal) { S.videoBounds = w.Left + "," + w.Top + "," + w.Width + "," + w.Height; SaveSettingsSoon(); }
                if (video == w) { video = null; videoTab = null; }
            };
            w.Show();
            await w.View.EnsureCoreWebView2Async(env);
            var core = w.View.CoreWebView2;
            core.Settings.AreDevToolsEnabled = S.devTools;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping(Host, Program.UiDir, CoreWebView2HostResourceAccessKind.Allow);
            var vt = videoTab;
            HookPage(core, vt);
            core.NavigationStarting += (s, e) =>
            {
                if (!e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) { e.Cancel = true; return; }
                OnMainNavigation(vt, e.Uri);
            };
            core.NewWindowRequested += (s, e) => { e.Handled = true; if (e.IsUserInitiated) Navigate(e.Uri, true); };
            core.DocumentTitleChanged += (s, e) => { if (!core.Source.StartsWith(UiOrigin, StringComparison.OrdinalIgnoreCase)) w.Text = "Sidearm · " + Trim(core.DocumentTitle, 60); };
            core.WebMessageReceived += (s, e) =>
            {
                var m = Json.Parse(e.WebMessageAsJson);
                string t = Json.Str(m, "t");
                if (t == "esc") w.Close();
                else if (t == "open" && e.Source.StartsWith(UiOrigin, StringComparison.OrdinalIgnoreCase))
                {
                    string page = Json.Str(m, "url");
                    if (IsWebUrl(page)) { w.Close(); Draw(); Navigate(page, true); }
                }
            };
            if (time >= 0) await core.AddScriptToExecuteOnDocumentCreatedAsync(VideoFocusJs(time));
            core.Navigate(url);
            Log.Info("Popped out a video: " + url);
        }

        Rectangle VideoBounds()
        {
            var parts = (S.videoBounds ?? "").Split(',');
            int x, y, wd, ht;
            if (parts.Length == 4 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y) && int.TryParse(parts[2], out wd) && int.TryParse(parts[3], out ht)
                && wd >= 240 && ht >= 160 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(x + 20, y + 5, Math.Max(1, wd - 40), 20))))
                return new Rectangle(x, y, wd, ht);
            var wa = Screen.FromHandle(Handle).WorkingArea;
            double scale = Native.Scale(Handle);
            int w = (int)(480 * scale), h = (int)(300 * scale), margin = (int)(16 * scale);
            return new Rectangle(wa.Left + margin, wa.Bottom - h - margin, w, h);
        }

        // ───────────────────────── in-place translation ─────────────────────────
        // Translates the words where they are, so the page (and your login) stays the real site.
        // The page sends its text in batches; Sidearm asks Google Translate and hands the words back.

        void ToggleInPlace(Tab tab)
        {
            tab.InPlace = !tab.InPlace;
            tab.TrBatches = 0;
            if (!tab.InPlace) tab.NoAutoTranslate = tab.Url;
            PostToContent(tab, new { t = "tr-inplace", on = tab.InPlace, to = S.translateTo });
            PushTabs();
        }

        async void OnTranslateBatch(Tab tab, Dictionary<string, object> m)
        {
            if (!tab.InPlace || ++tab.TrBatches > 400) return; // only while you asked, and never endlessly
            int id = (int)Json.Num(m, "id", 0);
            var texts = new List<string>();
            object raw;
            int chars = 0;
            if (m.TryGetValue("texts", out raw) && raw is System.Collections.IEnumerable && !(raw is string))
                foreach (var o in (System.Collections.IEnumerable)raw)
                {
                    string s = (o as string ?? "");
                    if (s.Length > 1200) s = s.Substring(0, 1200);
                    texts.Add(s);
                    chars += s.Length;
                    if (texts.Count >= 60 || chars > 6000) break;
                }
            if (texts.Count == 0) return;
            var result = await Lookup.TranslateBatch(texts, S.translateTo);
            if (!tab.InPlace) return;
            PostToContent(tab, new { t = "tr-result", id = id, texts = result });
        }

        // ───────────────────────── uBlock Origin Lite (experimental) ─────────────────────────

        bool UblockOn { get { return S.adblock && S.blocker == "ublock" && Ublock.Loaded; } }

        async void SyncUblock()
        {
            if (ui == null || ui.CoreWebView2 == null) return;
            bool want = S.adblock && S.blocker == "ublock";
            if (want && !Ublock.Loaded) { Ublock.Status = "Starting uBlock Origin Lite…"; PushSettingsSnapshot(); }
            await Ublock.Sync(ui.CoreWebView2.Profile, want);
            // Sidearm's own lists cost ~30 MB: build them only while they're the blocker
            if (Ublock.Loaded && want) { if (Adblock.Engine != null) { Adblock.Engine = null; GC.Collect(); Log.Info("Sidearm's filter engine off: uBlock Origin Lite is blocking"); } }
            else if (Adblock.Engine == null) Adblock.Start();
            RefreshInjection(); // the page script's YouTube ad skipper is only for Sidearm's own blocker
            PushTabs();
            PushSettingsSnapshot();
            if (want && !Ublock.Loaded) Toast("uBlock Origin Lite didn't start, so Sidearm's own blocker is on. Details in Settings", null, null);
        }

        string UblockDashboard { get { return Ublock.ExtId == null ? null : "chrome-extension://" + Ublock.ExtId + "/dashboard.html"; } }

        bool IsUblockPage(string url)
        {
            return Ublock.ExtId != null && (url ?? "").StartsWith("chrome-extension://" + Ublock.ExtId + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
