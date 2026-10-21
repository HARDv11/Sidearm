using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Sidearm
{
    internal sealed class Tab
    {
        public int Id;
        public string Url = MainForm.NewTabUrl;
        public string Title = "New tab";
        public string Favicon = "";
        public bool Loading, Closed;
        public WebView2 View;
        public Task Creating;
        public DateTime LastActive = DateTime.UtcNow;
        public Timer SuspendTimer;
        public string InjectId;           // id of the page script, so it can be replaced when settings change
        public string MainNavUri = "", PageHost = "";
        public int Blocked;
        public bool AdsAllowed;
        public readonly HashSet<string> CosmeticSent = new HashSet<string>(StringComparer.Ordinal);
        public double ExpectZoom = 1.0;   // the last zoom Sidearm itself applied
        public bool IsGlance;
        public bool ForNewWindow;        // made for a page's new-window request: gets a fresh engine, never the spare
        public string ZoomHost;           // site the current zoom belongs to
        public bool Muted, MuteKnown, MuteHinted, Audible;
        public string Lang = "";          // the page's language, as it reported it
        public string NoAutoTranslate;    // you asked for the original of this address: don't translate it again
        public long NavStart, FilterTicks; // load timing (Stopwatch ticks)
        public int Requests, LoadMs;
        public bool InPlace;              // translated where it stands (Settings → Translation → In place)
        public int TrBatches;
        public bool HasVideo;             // the page played a video (offers Pop out)

        public bool IsNewTab { get { return Url == null || Url.StartsWith(MainForm.NewTabUrl, StringComparison.OrdinalIgnoreCase); } }
    }

    internal sealed class ClosedTab
    {
        public string Url, Title;
    }

    /// <summary>Up to five light tabs; background tabs fall asleep, the oldest is recycled at the cap.</summary>
    internal sealed partial class MainForm
    {
        readonly List<Tab> tabs = new List<Tab>();
        readonly List<ClosedTab> closed = new List<ClosedTab>();
        Tab active;
        int nextTabId;

        // ───────────────────────── session ─────────────────────────

        string SessionPath { get { return Path.Combine(Program.DataDir, "session.json"); } }

        void RestoreSession()
        {
            try
            {
                if (File.Exists(SessionPath))
                {
                    var d = Json.Parse(File.ReadAllText(SessionPath));
                    var list = d != null && d.ContainsKey("tabs") ? d["tabs"] as System.Collections.IEnumerable : null;
                    if (list != null)
                        foreach (var o in list)
                        {
                            var td = o as Dictionary<string, object>;
                            string url = Json.Str(td, "url");
                            if (string.IsNullOrEmpty(url) || !IsWebUrl(url) && !url.StartsWith(UiOrigin)) continue;
                            if (tabs.Count >= S.maxTabs) break;
                            tabs.Add(new Tab { Id = ++nextTabId, Url = url, Title = Json.Str(td, "title") ?? url });
                        }
                    int idx = (int)Json.Num(d, "active", 0);
                    if (tabs.Count > 0) active = tabs[Math.Max(0, Math.Min(idx, tabs.Count - 1))];
                }
            }
            catch (Exception ex) { Log.Error("Could not restore session", ex); }
            if (tabs.Count == 0) tabs.Add(new Tab { Id = ++nextTabId });
            if (active == null) active = tabs[0];
        }

        void SaveSessionSoon() { saveTimer.Stop(); saveTimer.Start(); }

        void SaveSession()
        {
            if (initTask == null) return; // never drawn this run: the tabs were never loaded, so don't overwrite the saved ones
            try
            {
                // amnesia keeps nothing between sessions
                var list = S.amnesia ? new List<object>() : tabs.Select(t => (object)new { url = t.Url, title = t.Title }).ToList();
                string json = Json.Pretty(Json.Serialize(new { active = Math.Max(0, tabs.IndexOf(active)), tabs = list }));
                File.WriteAllText(SessionPath, json);
            }
            catch (Exception ex) { Log.Error("Could not save session", ex); }
        }

        // ───────────────────────── tab operations ─────────────────────────

        Task<Tab> OpenTab(string url, bool activate, bool navigate) { return OpenTab(url, activate, navigate, false); }

        async Task<Tab> OpenTab(string url, bool activate, bool navigate, bool forNewWindow)
        {
            if (tabs.Count >= S.maxTabs)
            {
                var victim = tabs.Where(t => t != active).OrderBy(t => t.LastActive).FirstOrDefault();
                if (victim != null)
                {
                    Log.Info("Recycled a tab at the " + S.maxTabs + "-tab cap: " + HostOf(victim.Url));
                    RemoveTab(victim, true);
                    Toast("Recycled “" + Trim(victim.Title, 32) + "” (" + S.maxTabs + "-tab cap)", "Bring back", "reopen");
                }
            }
            var tab = new Tab { Id = ++nextTabId, Url = url ?? NewTabUrl, Title = url == null || url == NewTabUrl ? "New tab" : url, ForNewWindow = forNewWindow };
            int at = active != null ? tabs.IndexOf(active) + 1 : tabs.Count;
            tabs.Insert(Math.Max(0, Math.Min(at, tabs.Count)), tab);
            PushTabs();
            if (activate) await ActivateTab(tab, navigate);
            else
            {
                tab.LastActive = DateTime.UtcNow.AddSeconds(-1);
                await EnsureView(tab, navigate);
                ScheduleSuspend(tab);
            }
            PushTabs();
            SaveSessionSoon();
            return tab;
        }

        async void NewTabAndFocus()
        {
            try
            {
                if (active != null && active.IsNewTab && active.View != null) { FocusBar(true); return; }
                await OpenTab(NewTabUrl, true, true);
                FocusBar(true);
            }
            catch (Exception ex) { Log.Error("New tab", ex); }
        }

        static async void Forget(Task t)
        {
            try { await t; } catch (Exception ex) { Log.Error("Background task", ex); }
        }

        async Task ActivateTab(Tab tab, bool navigate)
        {
            if (tab == null || tab.Closed) return;
            if (tab != active) CloseGlance();
            active = tab;
            tab.LastActive = DateTime.UtcNow;
            StopSuspendTimer(tab);
            PushTabs();
            await EnsureView(tab, navigate);
            if (active != tab || tab.View == null) return; // the user moved on while it loaded
            SetViewVisible(tab.View, true);
            tab.View.BringToFront();
            Resume(tab.View);
            SetMemoryLow(tab, false);
            foreach (var other in tabs)
            {
                if (other == tab || other.View == null) continue;
                SetViewVisible(other.View, false);
                SetMemoryLow(other, true); // background tabs give memory back straight away
                bool asleep = other.View.CoreWebView2 != null && other.View.CoreWebView2.IsSuspended;
                if (!asleep && other.SuspendTimer == null) ScheduleSuspend(other);
            }
            if (fullscreen) SetFullscreen(false);
            PushTabs();
            SaveSessionSoon();
        }

        void CycleTab(int dir)
        {
            if (tabs.Count < 2 || active == null) return;
            int i = (tabs.IndexOf(active) + dir + tabs.Count) % tabs.Count;
            Forget(ActivateTab(tabs[i], true));
        }

        async void CloseTab(Tab tab, bool remember)
        {
            try
            {
                if (tabs.Count == 1)
                {
                    // Never zero tabs: the last one just goes back to the start page.
                    Remember(tab);
                    tab.Url = NewTabUrl; tab.Title = "New tab"; tab.Favicon = "";
                    if (tab.View != null && tab.View.CoreWebView2 != null) tab.View.CoreWebView2.Navigate(NewTabUrl);
                    PushTabs();
                    FocusBar(true);
                    return;
                }
                int idx = tabs.IndexOf(tab);
                RemoveTab(tab, remember);
                if (active == tab)
                {
                    active = null;
                    await ActivateTab(tabs[Math.Min(idx, tabs.Count - 1)], true);
                }
                PushTabs();
                SaveSessionSoon();
            }
            catch (Exception ex) { Log.Error("Close tab", ex); }
        }

        void RemoveTab(Tab tab, bool remember)
        {
            if (remember) Remember(tab);
            tabs.Remove(tab);
            tab.Closed = true;
            DisposeView(tab);
        }

        void Remember(Tab tab)
        {
            if (tab.IsNewTab) return;
            closed.Add(new ClosedTab { Url = tab.Url, Title = tab.Title });
            if (closed.Count > 20) closed.RemoveAt(0);
        }

        async void ReopenClosed()
        {
            try
            {
                if (closed.Count == 0) { Toast("No closed tabs to bring back", null, null); return; }
                var c = closed[closed.Count - 1];
                closed.RemoveAt(closed.Count - 1);
                var tab = await OpenTab(c.Url, true, true);
                tab.Title = c.Title;
                PushTabs();
            }
            catch (Exception ex) { Log.Error("Reopen", ex); }
        }

        void DisposeView(Tab tab)
        {
            StopSuspendTimer(tab);
            var v = tab.View;
            tab.View = null;
            tab.Loading = false;
            // the next view starts at 100%: forget which site's zoom was applied, or it won't be applied again
            tab.ZoomHost = null;
            tab.ExpectZoom = 1.0;
            if (v == null) return;
            RetireView(v, Trim(tab.Title, 30));
        }

        void ScheduleSuspend(Tab tab)
        {
            StopSuspendTimer(tab);
            var timer = new Timer { Interval = Math.Max(5, S.backgroundTabSuspendSeconds) * 1000 };
            timer.Tick += async (s, e) =>
            {
                StopSuspendTimer(tab);
                if (tab == active || tab.View == null || tab.Closed) return;
                bool ok = await TrySuspend(tab.View);
                if (ok) Log.Info("Background tab asleep: " + Trim(tab.Title, 40));
            };
            tab.SuspendTimer = timer;
            timer.Start();
        }

        static void StopSuspendTimer(Tab tab)
        {
            if (tab.SuspendTimer == null) return;
            tab.SuspendTimer.Stop();
            tab.SuspendTimer.Dispose();
            tab.SuspendTimer = null;
        }

        void OnTabMessage(Dictionary<string, object> m)
        {
            string op = Json.Str(m, "op");
            int id = (int)Json.Num(m, "id", -1);
            var tab = tabs.FirstOrDefault(t => t.Id == id);
            switch (op)
            {
                case "activate": if (tab != null) Forget(ActivateTab(tab, true)); break;
                case "close": if (tab != null) CloseTab(tab, true); break;
                case "new": NewTabAndFocus(); break;
                case "home": GoHome(); break;
                case "reopen": ReopenClosed(); break;
                case "cycle": CycleTab((int)Json.Num(m, "dir", 1)); break;
                case "move":
                    int to = (int)Json.Num(m, "to", -1);
                    if (tab != null && to >= 0 && to < tabs.Count)
                    {
                        tabs.Remove(tab);
                        tabs.Insert(to, tab);
                        PushTabs();
                        SaveSessionSoon();
                    }
                    break;
            }
        }

        void OnNavMessage(string op)
        {
            var core = active != null && active.View != null ? active.View.CoreWebView2 : null;
            if (core == null) return;
            try
            {
                if (op == "back" && core.CanGoBack) core.GoBack();
                else if (op == "forward" && core.CanGoForward) core.GoForward();
                else if (op == "reload") core.Reload();
                else if (op == "stop") core.Stop();
            }
            catch (Exception ex) { Log.Error("Nav " + op, ex); }
        }

        async void Navigate(string url, bool newTab)
        {
            try
            {
                if (string.IsNullOrEmpty(url) || !(IsWebUrl(url) || url.StartsWith(UiOrigin, StringComparison.OrdinalIgnoreCase) || IsUblockPage(url)))
                {
                    Log.Info("Refused to navigate to " + url);
                    return;
                }
                if (url.StartsWith(UiOrigin + "ai?q=", StringComparison.OrdinalIgnoreCase))
                {
                    // the "a" prefix: whichever AI you picked in Settings
                    OpenAI(Uri.UnescapeDataString(url.Substring((UiOrigin + "ai?q=").Length).Replace('+', ' ')));
                    return;
                }
                if (url.StartsWith("https://claude.ai/", StringComparison.OrdinalIgnoreCase) && S.claudeOpenIn != "tab")
                {
                    // claude://claude.ai/new?q=… opens a new chat in the Claude desktop app with the text prefilled (not sent)
                    string target = S.claudeOpenIn == "app" ? "claude://" + url.Substring("https://".Length) : url;
                    try { Process.Start(target); Holster(); return; }
                    catch (Exception ex) { Log.Error("Open Claude (" + S.claudeOpenIn + "), falling back to a tab", ex); }
                }
                if (newTab || active == null)
                {
                    await OpenTab(url, true, true);
                }
                else
                {
                    var tab = active;
                    tab.Url = url;
                    if (tab.View == null) await EnsureView(tab, true);
                    else if (tab.View.CoreWebView2 != null) tab.View.CoreWebView2.Navigate(url);
                }
                if (active != null && active.View != null) active.View.Focus();
            }
            catch (Exception ex) { Log.Error("Navigate", ex); }
        }

        static bool IsWebUrl(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
        }

        // ───────────────────────── web views for tabs ─────────────────────────

        async Task EnsureView(Tab tab, bool navigate)
        {
            if (tab.View != null || tab.Closed) return;
            if (tab.Creating == null) tab.Creating = CreateView(tab, navigate);
            try { await tab.Creating; }
            finally { tab.Creating = null; }
        }

        async Task CreateView(Tab tab, bool navigate)
        {
            tab.ZoomHost = null;
            tab.ExpectZoom = 1.0;
            await EnsureEnvironment();
            var v = tab.IsGlance || tab.ForNewWindow ? null : TakeSpare(); // a ready engine skips the slow part
            if (v == null)
            {
                v = new WebView2 { DefaultBackgroundColor = PageColor, Dock = DockStyle.Fill, Visible = false };
                (tab.IsGlance ? (Control)glanceHost : content).Controls.Add(v);
                v.KeyDown += OnViewKeyDown;
                var forceHandle = v.Handle; // a hidden control would otherwise never initialise
                await v.EnsureCoreWebView2Async(env);
            }
            if (tab.Closed) { RetireView(v, "closed while opening"); return; }
            var core = v.CoreWebView2;
            ConfigureContent(core, tab);
            if (!string.IsNullOrEmpty(injectJs)) tab.InjectId = await core.AddScriptToExecuteOnDocumentCreatedAsync(InjectScript());
            tab.View = v;
            if (navigate) core.Navigate(tab.Url);
        }

        async Task EnsureEnvironment()
        {
            if (env != null) return;
            await EnsureInitialized();
        }

        static readonly string[] HandOffSchemes = { "mailto", "tel", "steam", "discord", "tg", "spotify", "magnet", "zoommtg", "msteams", "slack" };

        void ConfigureContent(CoreWebView2 core, Tab tab)
        {
            var s = core.Settings;
            s.AreDevToolsEnabled = S.devTools;
            s.IsStatusBarEnabled = false; // 0.8.1: the page script shows where links go (the engine's bubble could get stuck)
            s.IsZoomControlEnabled = true;
            s.IsPasswordAutosaveEnabled = false;
            // Allow (with CORS) so any page can load Sidearm's fonts for the look-up card
            core.SetVirtualHostNameToFolderMapping(Host, Program.UiDir, CoreWebView2HostResourceAccessKind.Allow);
            HookPage(core, tab);

            core.NavigationStarting += (sender, e) =>
            {
                Uri u;
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out u)) return;
                string scheme = u.Scheme.ToLowerInvariant();
                if (scheme == "http" || scheme == "https" || scheme == "about" || scheme == "data" || scheme == "blob" || IsUblockPage(e.Uri))
                {
                    OnMainNavigation(tab, e.Uri);
                    tab.Loading = true;
                    PushTabs();
                    return;
                }
                e.Cancel = true;
                // mailto:, steam://, discord:// and friends go to the app that owns them; anything else is dropped
                if (Array.IndexOf(HandOffSchemes, scheme) >= 0)
                    try { Process.Start(e.Uri); } catch (Exception) { }
                else Log.Info("Blocked navigation to " + scheme + ": link");
            };
            core.NavigationCompleted += (sender, e) =>
            {
                tab.Loading = false;
                OnPageLoaded(tab, e.IsSuccess);
                if (pendingGeminiPrompt != null) FillGemini(tab);
                PushTabs();
            };
            HookZoom(tab, core);
            HookAudio(tab, core);
            core.ContextMenuRequested += (sender, e) => OnContextMenu(tab, core, e);
            core.SourceChanged += (sender, e) =>
            {
                tab.Url = core.Source;
                ApplySiteZoom(tab);
                if (tab.IsNewTab) tab.Title = "New tab";
                PushTabs();
                SaveSessionSoon();
            };
            core.DocumentTitleChanged += (sender, e) =>
            {
                tab.Title = tab.IsNewTab ? "New tab" : (string.IsNullOrWhiteSpace(core.DocumentTitle) ? tab.Url : core.DocumentTitle);
                PushTabs();
                SaveSessionSoon();
            };
            core.FaviconChanged += (sender, e) =>
            {
                tab.Favicon = core.FaviconUri ?? "";
                PushTabs();
            };
            core.HistoryChanged += (sender, e) => PushTabs();
            core.ContainsFullScreenElementChanged += (sender, e) =>
            {
                if (tab == active) SetFullscreen(core.ContainsFullScreenElement);
            };
            core.WindowCloseRequested += (sender, e) => { if (tab.IsGlance) CloseGlance(); else CloseTab(tab, false); };
            core.NewWindowRequested += (sender, e) => OnNewWindow(e);
            core.WebMessageReceived += (sender, e) => OnContentMessage(tab, e);
            core.ProcessFailed += (sender, e) =>
            {
                Log.Info("Tab process failed (" + e.ProcessFailedKind + ") on " + tab.Url);
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited
                    || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                {
                    if (tab == active) Toast("The page crashed, reloading", null, null);
                    try { core.Reload(); } catch (Exception) { }
                }
            };
        }

        /// <summary>
        /// Links that want a new window become tabs. Ctrl/middle-click opens in the background.
        /// Pop-ups the page opens on its own (no click) are blocked.
        /// </summary>
        async void OnNewWindow(CoreWebView2NewWindowRequestedEventArgs e)
        {
            var deferral = e.GetDeferral();
            try
            {
                e.Handled = true;
                if (!e.IsUserInitiated)
                {
                    Log.Info("Blocked pop-up: " + e.Uri);
                    Toast("Blocked a pop-up", null, null);
                    return;
                }
                bool background = (Control.ModifierKeys & Keys.Control) != 0 || (Control.MouseButtons & MouseButtons.Middle) != 0;
                Log.Info("New window: " + HostOf(e.Uri) + (background ? " (background tab)" : ""));
                var tab = await OpenTab(e.Uri, !background, false, true);
                if (tab.View != null && tab.View.CoreWebView2 != null && !tab.Closed)
                    e.NewWindow = tab.View.CoreWebView2; // keeps window.opener, so sign-in pop-ups work
            }
            catch (Exception ex) { Log.Error("New window", ex); }
            finally { deferral.Complete(); }
        }

        DateTime lastPageAction = DateTime.MinValue;

        void OnContentMessage(Tab tab, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var m = Json.Parse(e.WebMessageAsJson);
            string t = Json.Str(m, "t");
            if (t == null) return;
            // the start page keeps its notes and pinned sites through the shell; web pages can't touch them
            if (t == "home-get" || t.StartsWith("notes-", StringComparison.Ordinal) || t.StartsWith("pins-", StringComparison.Ordinal))
            {
                if (IsStartPage(e.Source)) OnHomeMessage(tab, m);
                return;
            }
            // our own settings page may change settings; web pages never can
            if (t.StartsWith("settings-", StringComparison.Ordinal))
            {
                if (IsTrustedPage(e.Source)) OnSettingsMessage(tab, m);
                return;
            }
            // any tab, even in the background, may ask for its hiding rules or report a result
            if (t == "cosmetic") { OnCosmeticRequest(tab, m); return; }
            if (t == "yt-ad") { if (!ytAdLogged) { ytAdLogged = true; Log.Info("YouTube: an ad came up; muted and skipped it"); } return; }
            if (t == "reader-size")
            {
                double size = Json.Num(m, "size", 0);
                if (size >= 0.6 && size <= 2.5) Sites.Update(tab.PageHost, x => x.readerSize = Math.Round(size, 2));
                return;
            }
            if (t == "img") { if (tab == active || tab == glance) OnImageRequest(tab, m); return; }
            if (t == "tr-batch") { OnTranslateBatch(tab, m); return; } // background tabs keep translating
            if (tab == glance)
            {
                if (t == "esc" || t == "glance-close") CloseGlance();
                else if (t == "glance") OpenGlance(Json.Str(m, "url"));
                else if (t == "gesture") OnGesture(tab, Json.Str(m, "a"));
                else if (t == "home") GoHome();
                else if (t == "lookup" && S.lookup) DoLookup(tab, (int)Json.Num(m, "id", 0), Json.Str(m, "text"));
                return;
            }
            if (tab != active) return;
            if (t == "glance") { OpenGlance(Json.Str(m, "url")); return; }
            if (t == "glance-close") { CloseGlance(); return; }
            // Pages are untrusted: they can only ask for these few harmless things.
            if (t == "esc") { if (glance != null) CloseGlance(); else Holster(); }
            else if (t == "focusBar") FocusBar(true);
            else if (t == "home") GoHome();
            else if (t == "gesture") OnGesture(tab, Json.Str(m, "a"));
            else if (t == "pagelang") OnPageLang(tab, Json.Str(m, "lang"));
            else if (t == "hasvideo") { if (!tab.HasVideo) { tab.HasVideo = true; PushTabs(); } }
            else if (t == "tr-state") { tab.InPlace = Json.Bool(m, "on"); PushTabs(); if (Json.Bool(m, "failed")) Toast("Google Translate didn't answer. Try again in a minute, or switch to the page proxy in Settings", null, null); }
            else if (t == "reader-none") Toast("No article found on this page", null, null);
            else if (t == "fog-done")
            {
                int n = (int)Json.Num(m, "n", 0);
                if (!Json.Bool(m, "silent")) Toast(n > 0 ? "Cleared " + n + " overlay" + (n == 1 ? "" : "s") + " and scroll locks" : "No fog found on this page", null, null);
            }
            else if (t == "lookup" && S.lookup) DoLookup(tab, (int)Json.Num(m, "id", 0), Json.Str(m, "text"));
            else if (t == "lookup-off" && S.lookup)
            {
                string site = Sites.Key(SiteHostOf(tab));
                Log.Info("Look-up: Alt held on " + site + ", where word look-up is switched off; offered to turn it on");
                Toast("Word look-up is off for " + site, "Turn on", "lookupOn");
            }
            else if (t == "claude" || t == "search")
            {
                // opens a tab, so throttle it: a page can't spam tabs
                if ((DateTime.UtcNow - lastPageAction).TotalSeconds < 1.5) return;
                lastPageAction = DateTime.UtcNow;
                string text = Trim(Json.Str(m, "text") ?? "", 200);
                if (text.Length == 0) return;
                if (t == "claude") OpenAI("Explain \u201C" + text + "\u201D in simple terms.\n\n(I came across it on " + (tab.Url ?? "") + ")");
                else Navigate(S.searchUrl.Replace("{q}", Uri.EscapeDataString(text)), true);
            }
        }

        bool lookupLogged, ytAdLogged;

        async void DoLookup(Tab tab, int id, string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text) || !Lookup.Allow()) return;
                if (!lookupLogged) { lookupLogged = true; Log.Info("Look-up working: first word this session"); }
                var result = await Lookup.Run(text);
                result = new Dictionary<string, object>(result);
                result["t"] = "lookup-result";
                result["id"] = id;
                PostToContent(tab, result);
            }
            catch (Exception ex) { Log.Error("Look-up", ex); }
        }

        void PostToContent(Tab tab, object message)
        {
            try
            {
                if (tab.View != null && tab.View.CoreWebView2 != null)
                    tab.View.CoreWebView2.PostWebMessageAsJson(Json.Serialize(message));
            }
            catch (Exception ex) { Log.Error("PostToContent", ex); }
        }

        // ───────────────────────── Claude ─────────────────────────


        void OnContextMenu(Tab tab, CoreWebView2 core, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                var target = e.ContextMenuTarget;
                string sel = target.HasSelection ? (target.SelectionText ?? "").Trim() : "";
                string page = core.DocumentTitle + " \u2014 " + target.PageUri;
                var add = new List<CoreWebView2ContextMenuItem>();
                if (sel.Length > 0)
                {
                    string shortSel = Trim(System.Text.RegularExpressions.Regex.Replace(sel, @"\s+", " "), 26);
                    if (S.lookup && sel.Length <= 80)
                        add.Add(MenuItem("Look up \u201C" + shortSel + "\u201D", () => PostToContent(tab, new { t = "lookup-sel", text = sel })));
                    add.Add(MenuItem("Ask " + AiName + " about \u201C" + shortSel + "\u201D", () =>
                        OpenAI("\u201C" + Trim(sel, 1400) + "\u201D\n\n(from " + page + ")\n\n")));
                }
                else if (target.HasLinkUri)
                {
                    string link = target.LinkUri;
                    if (IsWebUrl(link))
                    {
                        add.Add(MenuItem("Open link in Glance", () => OpenGlance(link)));
                        string bn = BrowserName;
                        add.Add(MenuItem("Open link in " + bn, () => SendToBrowser(link, false)));
                        add.Add(MenuItem("Open link in " + bn + " (private)", () => SendToBrowser(link, true)));
                    }
                    add.Add(MenuItem("Send link to " + AiName, () => OpenAI("Here's a link: " + link + "\n\n")));
                }
                if (sel.Length == 0 && !target.HasLinkUri && (target.Kind == CoreWebView2ContextMenuTargetKind.Video || tab.HasVideo || IsVideoSite(target.PageUri)))
                    add.Add(MenuItem("Pop out video  (Alt+P)", () => PopOutVideo(tab)));
                if (sel.Length == 0)
                {
                    add.Add(MenuItem("Send page to " + AiName, () => OpenAI("I'm reading this page: " + page + "\n\n")));
                    string pageUrl = target.PageUri;
                    if (!target.HasLinkUri && IsWebUrl(pageUrl) && !pageUrl.StartsWith(UiOrigin, StringComparison.OrdinalIgnoreCase))
                    {
                        add.Add(MenuItem("Open page in " + BrowserName, () => SendToBrowser(pageUrl, false)));
                        add.Add(MenuItem("Copy page link", () => CopyLink(pageUrl)));
                        add.Add(MenuItem("Pin to start page", () => PinPage(tab)));
                    }
                }
                // the built-in "Copy link" copies the tracking junk too; ours copies the clean link
                if (target.HasLinkUri && S.cleanLinks && IsWebUrl(target.LinkUri))
                {
                    string link = target.LinkUri;
                    for (int i = 0; i < e.MenuItems.Count; i++)
                        if (e.MenuItems[i].Name == "copyLinkLocation")
                        {
                            e.MenuItems.RemoveAt(i);
                            e.MenuItems.Insert(i, MenuItem("Copy link", () => CopyLink(link)));
                            break;
                        }
                }
                if (add.Count == 0) return;
                add.Add(env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
                for (int i = add.Count - 1; i >= 0; i--) e.MenuItems.Insert(0, add[i]);
            }
            catch (Exception ex) { Log.Error("Context menu", ex); }
        }

        CoreWebView2ContextMenuItem MenuItem(string label, Action action)
        {
            var item = env.CreateContextMenuItem(Lang.T(label), null, CoreWebView2ContextMenuItemKind.Command);
            item.CustomItemSelected += (s, a) => BeginInvoke(action);
            return item;
        }

        // ───────────────────────── zoom ─────────────────────────

        Dictionary<string, double> siteZoom;
        string ZoomPath { get { return Path.Combine(Program.DataDir, "zoom.json"); } }

        Dictionary<string, double> SiteZoom
        {
            get
            {
                if (siteZoom != null) return siteZoom;
                siteZoom = new Dictionary<string, double>();
                try
                {
                    if (File.Exists(ZoomPath))
                    {
                        var d = Json.Parse(File.ReadAllText(ZoomPath));
                        if (d != null) foreach (var kv in d) siteZoom[kv.Key] = Json.Num(d, kv.Key, 1.0);
                    }
                }
                catch (Exception ex) { Log.Error("zoom.json", ex); }
                return siteZoom;
            }
        }

        void SaveSiteZoom()
        {
            try { File.WriteAllText(ZoomPath, Json.Pretty(Json.Serialize(SiteZoom))); }
            catch (Exception ex) { Log.Error("Save zoom", ex); }
        }

        /// <summary>Zoom belongs to the whole site (wiki.hoyolab.com and www.hoyolab.com share it). Entries saved
        /// per host before 0.8 still win for their host.</summary>
        bool TryGetZoom(string host, out double z)
        {
            return SiteZoom.TryGetValue(host, out z) || SiteZoom.TryGetValue(Sites.Key(host), out z);
        }

        void RememberZoom(string host, double z)
        {
            SiteZoom.Remove(host);
            SiteZoom[Sites.Key(host)] = z;
            SaveSiteZoom();
        }

        static string HostOf(string url)
        {
            Uri u;
            return Uri.TryCreate(url ?? "", UriKind.Absolute, out u) ? u.Host.ToLowerInvariant() : "";
        }

        void HookZoom(Tab tab, CoreWebView2 core)
        {
            var view = tab.View ?? FindView(core);
            if (view == null) return;
            view.ZoomFactorChanged += (s, e) =>
            {
                if (tab.View == null) return;
                double z = Math.Round(tab.View.ZoomFactor, 2);
                if (Math.Abs(z - tab.ExpectZoom) > 0.005 && !tab.IsNewTab)
                {
                    // you changed it (Ctrl + scroll, Ctrl + +/-): remember it for this site
                    string host = HostOf(tab.Url);
                    if (host.Length > 0) RememberZoom(host, z);
                    tab.ExpectZoom = z;
                }
                if (tab == active) PushTabs();
            };
        }

        WebView2 FindView(CoreWebView2 core)
        {
            foreach (Control c in content.Controls)
            {
                var v = c as WebView2;
                if (v != null && v.CoreWebView2 == core) return v;
            }
            return null;
        }

        /// <summary>
        /// On arriving at a site: its remembered zoom, or (first visit, narrow window) a little zoomed out.
        /// </summary>
        void ApplySiteZoom(Tab tab)
        {
            if (tab.View == null) return;
            string host = tab.IsNewTab ? "" : HostOf(tab.Url);
            string site = Sites.Key(host);
            if (site == tab.ZoomHost) return;
            tab.ZoomHost = site;
            double z = 1.0;
            double saved;
            if (host.Length > 0 && TryGetZoom(host, out saved)) z = saved;
            else if (host.Length > 0 && S.autoZoom) z = AutoZoom();
            SetZoom(tab, z);
        }

        double AutoZoom()
        {
            double css = content.ClientSize.Width / Native.Scale(Handle);
            if (css >= S.autoZoomBelow) return 1.0;
            if (css < 560) return 0.8;
            if (css < 720) return 0.85;
            return 0.9;
        }

        void SetZoom(Tab tab, double z)
        {
            if (tab.View == null) return;
            tab.ExpectZoom = Math.Round(z, 2);
            if (Math.Abs(tab.View.ZoomFactor - z) > 0.005) tab.View.ZoomFactor = z;
            if (tab == active) PushTabs();
        }

        void ResetZoom()
        {
            if (active == null || active.View == null) return;
            string host = HostOf(active.Url);
            if (host.Length > 0 && !active.IsNewTab) RememberZoom(host, 1.0);
            SetZoom(active, 1.0);
        }

        // ───────────────────────── telling the strip ─────────────────────────

        void PushTabs()
        {
            if (ui == null) return;
            var list = tabs.Select(t => new
            {
                id = t.Id,
                title = t.Title ?? "",
                url = t.IsNewTab ? "" : (t.Url ?? ""),
                favicon = t.Favicon ?? "",
                loading = t.Loading,
                active = t == active,
                audible = t.Audible,
                muted = t.Muted,
                asleep = t.View == null || (t.View.CoreWebView2 != null && t.View.CoreWebView2.IsSuspended)
            }).ToList();
            bool back = false, forward = false;
            try
            {
                if (active != null && active.View != null && active.View.CoreWebView2 != null)
                {
                    back = active.View.CoreWebView2.CanGoBack;
                    forward = active.View.CoreWebView2.CanGoForward;
                }
            }
            catch (Exception) { }
            double zoom = 1.0;
            try { if (active != null && active.View != null && !active.IsNewTab) zoom = active.View.ZoomFactor; } catch (Exception) { }
            PostToUi(new { type = "tabs", tabs = list, canBack = back, canForward = forward, canReopen = closed.Count > 0, zoom = Math.Round(zoom * 100), page = PageInfo(), glance = GlanceInfo() });
        }

        static string Trim(string s, int max)
        {
            s = s ?? "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
