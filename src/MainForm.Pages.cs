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
    /// <summary>Milestone 2: ad blocking, cosmetic hiding, reader view, fog, dark mode, the page panel and the settings page.</summary>
    internal sealed partial class MainForm
    {
        string readerJs = "", readabilityJs = "";
        FileSystemWatcher settingsWatcher;
        readonly Timer blockedPushTimer = new Timer { Interval = 600 };
        readonly Timer settingsReloadTimer = new Timer { Interval = 400 };

        void InitPages()
        {
            try
            {
                readabilityJs = File.ReadAllText(Path.Combine(Program.UiDir, "vendor", "Readability.js"));
                readerJs = File.ReadAllText(Path.Combine(Program.UiDir, "reader.js"));
            }
            catch (Exception ex) { Log.Error("Reader scripts missing", ex); }

            blockedPushTimer.Tick += (s, e) => { blockedPushTimer.Stop(); PushTabs(); };
            settingsReloadTimer.Tick += (s, e) => { settingsReloadTimer.Stop(); ReloadSettingsFromDisk(); };
            Adblock.Changed += () => { try { BeginInvoke(new Action(() => { PushTabs(); PushSettingsSnapshot(); })); } catch (Exception) { } };

            try
            {
                settingsWatcher = new FileSystemWatcher(Program.DataDir, "settings.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };
                FileSystemEventHandler h = (s, e) => { try { BeginInvoke(new Action(() => { settingsReloadTimer.Stop(); settingsReloadTimer.Start(); })); } catch (Exception) { } };
                settingsWatcher.Changed += h;
                settingsWatcher.Created += h;
                settingsWatcher.Renamed += (s, e) => h(s, e);
                settingsWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Error("Settings watcher", ex); }
        }

        // ───────────────────────── per page setup ─────────────────────────

        void HookPage(CoreWebView2 core, Tab tab)
        {
            try
            {
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            }
            catch (Exception)
            {
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            }
            core.WebResourceRequested += (s, e) => OnResourceRequested(tab, e);
        }

        /// <summary>A new page is starting in this tab (not a frame inside it).</summary>
        void OnMainNavigation(Tab tab, string uri)
        {
            int hash = uri.IndexOf('#');
            tab.MainNavUri = hash >= 0 ? uri.Substring(0, hash) : uri;
            tab.PageHost = Adblock.HostOf(uri);
            tab.Blocked = 0;
            tab.CosmeticSent.Clear();
            tab.NavStart = System.Diagnostics.Stopwatch.GetTimestamp();
            tab.FilterTicks = 0; tab.Requests = 0; tab.LoadMs = 0; tab.Lang = "";
            tab.InPlace = false; tab.TrBatches = 0; tab.HasVideo = false;
            tab.AdsAllowed = Sites.Get(tab.PageHost).allowAds;
        }

        void OnResourceRequested(Tab tab, CoreWebView2WebResourceRequestedEventArgs e)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { CheckRequest(tab, e); }
            finally { tab.FilterTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; tab.Requests++; }
        }

        void CheckRequest(Tab tab, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var engine = Adblock.Engine;
            if (engine == null || !S.adblock || UblockOn || tab.AdsAllowed || string.IsNullOrEmpty(tab.PageHost) || tab.PageHost == Host) return;
            string uri = e.Request.Uri;
            if (!(uri.StartsWith("https://", StringComparison.Ordinal) || uri.StartsWith("http://", StringComparison.Ordinal))) return;
            int type = MapType(e.ResourceContext);
            if (type == FilterEngine.Document)
            {
                if (uri == tab.MainNavUri || uri.StartsWith(tab.MainNavUri + "#", StringComparison.Ordinal)) return; // never the page itself
                type = FilterEngine.Subdocument;
            }
            string why;
            if (!engine.ShouldBlock(uri, Adblock.HostOf(uri), tab.PageHost, type, out why)) return;
            e.Response = env.CreateWebResourceResponse(null, 403, "Blocked by Sidearm", "");
            tab.Blocked++;
            if (tab == active && !blockedPushTimer.Enabled) blockedPushTimer.Start();
        }

        static int MapType(CoreWebView2WebResourceContext c)
        {
            switch (c)
            {
                case CoreWebView2WebResourceContext.Document: return FilterEngine.Document;
                case CoreWebView2WebResourceContext.Stylesheet: return FilterEngine.Stylesheet;
                case CoreWebView2WebResourceContext.Image: return FilterEngine.Image;
                case CoreWebView2WebResourceContext.Media: return FilterEngine.Media;
                case CoreWebView2WebResourceContext.Font: return FilterEngine.Font;
                case CoreWebView2WebResourceContext.Script: return FilterEngine.Script;
                case CoreWebView2WebResourceContext.XmlHttpRequest:
                case CoreWebView2WebResourceContext.Fetch:
                case CoreWebView2WebResourceContext.EventSource: return FilterEngine.Xhr;
                case CoreWebView2WebResourceContext.Websocket: return FilterEngine.Websocket;
                case CoreWebView2WebResourceContext.Ping:
                case CoreWebView2WebResourceContext.CspViolationReport: return FilterEngine.Ping;
                default: return FilterEngine.Other;
            }
        }

        /// <summary>The page lists the classes and ids it uses; answer with the hiding rules that apply.</summary>
        void OnCosmeticRequest(Tab tab, Dictionary<string, object> m)
        {
            var engine = Adblock.Engine;
            if (engine == null || !S.adblock || UblockOn || tab.AdsAllowed || string.IsNullOrEmpty(tab.PageHost)) return;
            var keys = new List<string>();
            object raw;
            if (m.TryGetValue("keys", out raw) && raw is System.Collections.IEnumerable && !(raw is string))
                foreach (var k in (System.Collections.IEnumerable)raw)
                {
                    var s = k as string;
                    if (s != null && s.Length < 120) keys.Add(s);
                    if (keys.Count >= 8000) break;
                }
            var selectors = engine.CosmeticFor(tab.PageHost, keys, Json.Bool(m, "first"));
            var fresh = new List<string>();
            foreach (var s in selectors) if (tab.CosmeticSent.Add(s)) fresh.Add(s);
            if (fresh.Count > 0) PostToContent(tab, new { t = "cosmetic-css", selectors = fresh });
        }

        // ───────────────────────── reader, fog, dark ─────────────────────────

        bool IsWebPage(Tab t)
        {
            return t != null && t.View != null && t.View.CoreWebView2 != null && !t.IsNewTab && IsWebUrl(t.Url);
        }

        async void ToggleReader(Tab tab)
        {
            if (!IsWebPage(tab)) { Toast("Reader view works on web pages", null, null); return; }
            try
            {
                var prefs = Json.Serialize(new { size = Sites.Get(tab.PageHost).readerSize, dict = Lang.Code == "en" ? null : Lang.Page });
                string js = "(function(){window.__sidearmReaderPrefs=" + prefs + ";\n" + readabilityJs + "\n" + readerJs + "\n})();";
                await tab.View.CoreWebView2.ExecuteScriptAsync(js);
            }
            catch (Exception ex) { Log.Error("Reader", ex); }
        }

        void ClearFog(Tab tab)
        {
            if (!IsWebPage(tab)) return;
            PostToContent(tab, new { t = "fog" });
        }

        /// <summary>The site a page belongs to; a translated page belongs to the site it came from.</summary>
        string SiteHostOf(Tab t) { return IsTranslated(t.Url) ? HostOf(OriginalOf(t.Url)) : t.PageHost; }

        void SetSiteOption(string op, bool on)
        {
            if (!IsWebPage(active)) return;
            string host = SiteHostOf(active);
            switch (op)
            {
                case "trAuto":
                    Sites.Update(host, s => s.autoTranslate = on);
                    if (on && !IsTranslated(active.Url)) ToggleTranslate(active);
                    break;
                case "ads":
                    Sites.Update(host, s => s.allowAds = !on);
                    active.AdsAllowed = !on;
                    Toast(on ? "Blocking ads on " + Sites.Key(host) + " again; reloading" : "Ads allowed on " + Sites.Key(host) + "; reloading", null, null);
                    try { active.View.CoreWebView2.Reload(); } catch (Exception) { }
                    break;
                case "fogAuto":
                    Sites.Update(host, s => s.autoFog = on);
                    if (on) ClearFog(active);
                    break;
                case "dark":
                    Sites.Update(host, s => s.forceDark = on);
                    PostToContent(active, new { t = "dark", on = on });
                    break;
                case "lookup":
                    Sites.Update(host, s => s.noLookup = !on);
                    break;
            }
            RefreshInjection();
            PushTabs();
        }

        object PageInfo()
        {
            if (!IsWebPage(active)) return new { web = false };
            string shost = SiteHostOf(active);
            var site = Sites.Get(shost);
            return new
            {
                web = true,
                site = Sites.Key(shost),
                translated = IsTranslated(active.Url) || active.InPlace,
                inPlace = active.InPlace || S.translateMode == "inplace",
                ublock = UblockOn,
                video = active.HasVideo || IsVideoSite(active.Url),
                lang = active.Lang ?? "",
                trTo = S.translateTo,
                trAuto = site.autoTranslate,
                loadMs = active.LoadMs,
                filterMs = Math.Round(TicksToMs(active.FilterTicks), 1),
                requests = active.Requests,
                host = active.PageHost,
                blocked = active.Blocked,
                adblock = S.adblock && (Adblock.Engine != null || UblockOn),
                ads = !site.allowAds,
                fogAuto = site.autoFog,
                dark = site.forceDark,
                lookup = S.lookup && !site.noLookup
            };
        }

        // ───────────────────────── the injected page script ─────────────────────────

        string onAccentTmp;

        string InjectScript()
        {
            var cfg = Json.Serialize(new
            {
                lookup = S.lookup,
                ytAds = S.youtubeAds && S.adblock && !UblockOn,
                dbl = S.lookupOnDoubleClick,
                adblock = S.adblock,
                theme = ThemeName,
                middleGlance = S.middleClickGlance,
                accent = ReadableAccent(out onAccentTmp),
                onAccent = onAccentTmp,
                aiName = AiName,
                browserName = BrowserName,
                gestures = S.gestures ? S.GestureMap() : null,
                dict = Lang.Code == "en" ? null : Lang.Page,
                fontBase = UiOrigin + "fonts/",
                dark = Sites.Where(x => x.forceDark),
                fog = Sites.Where(x => x.autoFog),
                noLookup = Sites.Where(x => x.noLookup)
            });
            return injectJs.Replace("/*SIDEARM_CFG*/{}", cfg);
        }

        /// <summary>Settings changed: future pages get the new script, open pages get the new config now.</summary>
        async void RefreshInjection()
        {
            string script = InjectScript();
            foreach (var t in tabs.ToList())
            {
                if (t.View == null || t.View.CoreWebView2 == null) continue;
                try
                {
                    var core = t.View.CoreWebView2;
                    if (t.InjectId != null) core.RemoveScriptToExecuteOnDocumentCreated(t.InjectId);
                    t.InjectId = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
                    var site = Sites.Get(t.PageHost);
                    PostToContent(t, new { t = "cfg", lookup = S.lookup && !site.noLookup, siteOff = S.lookup && site.noLookup, dbl = S.lookupOnDoubleClick, gestures = S.gestures ? S.GestureMap() : null });
                }
                catch (Exception ex) { Log.Error("Refresh injection", ex); }
            }
        }

        // ───────────────────────── settings page ─────────────────────────

        public void OpenSettingsPage()
        {
            var existing = tabs.FirstOrDefault(t => (t.Url ?? "").StartsWith(UiOrigin + "settings.html", StringComparison.OrdinalIgnoreCase));
            if (!Visible) Draw();
            if (existing != null) Forget(ActivateTab(existing, true));
            else Navigate(UiOrigin + "settings.html", true);
        }

        static bool IsTrustedPage(string source)
        {
            return source != null && source.StartsWith(UiOrigin + "settings.html", StringComparison.OrdinalIgnoreCase);
        }

        async void OnSettingsMessage(Tab tab, Dictionary<string, object> m)
        {
            string t = Json.Str(m, "t");
            try
            {
                switch (t)
                {
                    case "settings-get":
                        break;
                    case "settings-set":
                        string key = Json.Str(m, "key");
                        object value; m.TryGetValue("value", out value);
                        if (S.Set(key, value)) { ApplySetting(key); S.Save(); }
                        break;
                    case "settings-bangs":
                        object raw; m.TryGetValue("bangs", out raw);
                        var dict = raw as Dictionary<string, object>;
                        if (dict != null)
                        {
                            var bangs = new Dictionary<string, Bang>();
                            foreach (var kv in dict)
                            {
                                var b = kv.Value as Dictionary<string, object>;
                                string k = (kv.Key ?? "").Trim().ToLowerInvariant().TrimStart('!');
                                string url = Json.Str(b, "url");
                                if (k.Length == 0 || k.Length > 12 || k.Contains(" ") || url == null || !url.Contains("{q}") || !IsWebUrl(url.Replace("{q}", "x").Replace("{tl}", "uk").Replace("{sl}", "en"))) continue;
                                bangs[k] = new Bang(Json.Str(b, "name") ?? k, url);
                            }
                            S.bangs = bangs;
                            S.Save();
                            SendInit();
                        }
                        break;
                    case "settings-action":
                        await SettingsAction(tab, Json.Str(m, "name"), m);
                        break;
                }
            }
            catch (Exception ex) { Log.Error("Settings page: " + t, ex); }
            PushSettingsSnapshot(tab);
        }

        async Task SettingsAction(Tab tab, string name, Dictionary<string, object> m)
        {
            string arg = Json.Str(m, "arg");
            switch (name)
            {
                case "updateFilters": Adblock.UpdateNow(true); break;
                case "updateCheck": CheckUpdatesNow(tab); break;
                case "updateNow": UpdateNow(); break;
                case "shortcutsAdd": MakeShortcuts(); break;
                case "shortcutsRemove": Shortcuts.RemoveBoth(); break;
                case "importWiki":
                    PostToContent(tab, new { t = "settings-msg", text = "Looking for Modern for Wikipedia…" });
                    string msg = await Extensions.Import(ui.CoreWebView2.Profile);
                    wikiStatusCache = await Extensions.Status(ui.CoreWebView2.Profile);
                    PostToContent(tab, new { t = "settings-msg", text = msg });
                    break;
                case "removeWiki":
                    await Extensions.RemoveExisting(ui.CoreWebView2.Profile);
                    wikiStatusCache = "Not installed";
                    PostToContent(tab, new { t = "settings-msg", text = "Removed Modern for Wikipedia." });
                    break;
                case "openData":
                    try { Process.Start("explorer.exe", "\"" + Program.DataDir + "\""); } catch (Exception) { }
                    break;
                case "editJson": OpenSettingsFile(); break;
                case "autostart":
                    if (Json.Bool(m, "on") != IsAutostart()) ToggleAutostart();
                    break;
                case "resetSite":
                    Sites.Reset(arg);
                    foreach (var h in SiteZoom.Keys.Where(h => Sites.Key(h) == arg).ToList()) SiteZoom.Remove(h);
                    SaveSiteZoom();
                    RefreshInjection();
                    break;
                case "resetBangs":
                    S.bangs = Settings.DefaultBangs();
                    S.Save();
                    SendInit();
                    break;
                case "ublockUpdate":
                    await Ublock.Update(ui.CoreWebView2.Profile);
                    SyncUblock();
                    break;
                case "ravenAdd": OpenRaven(raven.Identity == null ? "list" : "add", null, null); break;
                case "ravenList": OpenRaven("list", null, null); break;
                case "ravenDelete": RavenDeleteEverything(); break;
                case "ublockOpen":
                    if (UblockDashboard != null) Navigate(UblockDashboard, true);
                    break;
            }
        }

        void PushSettingsSnapshot() { PushSettingsSnapshot(null); }

        async void PushSettingsSnapshot(Tab target)
        {
            var tab = target ?? tabs.FirstOrDefault(t => IsTrustedPage(t.Url) && t.View != null);
            if (tab == null) return;
            // Send what we know right away; the slow checks (scanning browser profiles) run in the background
            PostToContent(tab, SettingsSnapshot());
            if (!wikiScanned)
            {
                wikiScanned = true;
                wikiFoundCache = await Task.Run(() => WikiSource());
                PostToContent(tab, SettingsSnapshot());
            }
            try
            {
                if (ui != null && ui.CoreWebView2 != null)
                {
                    string status = await Extensions.Status(ui.CoreWebView2.Profile);
                    if (status != wikiStatusCache) { wikiStatusCache = status; PostToContent(tab, SettingsSnapshot()); }
                }
            }
            catch (Exception) { }
        }

        bool wikiScanned;
        string wikiFoundCache, wikiStatusCache = "Checking…";

        object SettingsSnapshot()
        {
            // one row per site: its page-panel choices plus any remembered zoom
            var zoomBySite = new Dictionary<string, double>();
            foreach (var kv in SiteZoom)
                if (Math.Abs(kv.Value - 1.0) > 0.01) zoomBySite[Sites.Key(kv.Key)] = Math.Round(kv.Value * 100);
            var keys = new HashSet<string>(Sites.All.Select(kv => kv.Key));
            keys.UnionWith(zoomBySite.Keys);
            var sites = new Dictionary<string, object>();
            foreach (var key in keys.OrderBy(k => k))
            {
                var ss = Sites.Get(key);
                double z; zoomBySite.TryGetValue(key, out z);
                sites[key] = new { ss.allowAds, ss.autoFog, ss.forceDark, ss.noLookup, ss.autoTranslate, zoom = z };
            }
            return new
            {
                t = "settings",
                s = S,
                version = Program.ShownVersion,
                autostart = IsAutostart(),
                filters = UblockOn ? "uBlock Origin Lite is blocking, inside the engine" : Adblock.Status,
                ublock = Ublock.Status,
                ublockOn = UblockOn,
                quickKeyOk = quickKeyOk || !S.quickBar,
                rates = Rates.Uah == null ? "Not downloaded yet" : (Rates.Source == "NBU" ? "National Bank of Ukraine, " : "open.er-api.com, ") + Rates.Date,
                wiki = wikiStatusCache,
                wikiFound = wikiFoundCache,
                browser = BrowserInfo(),
                dataDir = Program.DataDir,
                update = UpdateInfo(),
                raven = new { on = S.raven, online = ravenOnline, ready = RavenReady, title = RavenTitle, friends = raven == null ? 0 : raven.Friends.Count(f => f.status == "friend"), requests = raven == null ? 0 : raven.Friends.Count(f => f.status == "request"), key = raven != null && raven.Identity != null ? Json.Str(raven.Identity, "key") : "" },
                shortcuts = new { desktop = Shortcuts.State(Shortcuts.DesktopPath), start = Shortcuts.State(Shortcuts.StartMenuPath) },
                sites = sites
            };
        }

        static string WikiSource()
        {
            try { string from; bool ff; return Extensions.Find(out from, out ff) != null ? from : null; }
            catch (Exception) { return null; }
        }

        /// <summary>Side effects of one setting changing.</summary>
        void ApplySetting(string key)
        {
            switch (key)
            {
                case null:
                    foreach (var k in new[] { "hotkey", "opacity", "uiScale", "preferDark", "pinned", "searchUrl", "lookup", "theme", "tabsPosition" }) ApplySetting(k);
                    break;
                case "hotkey":
                    RegisterHotkey();
                    if (tray != null) { tray.Text = "Sidearm · " + S.hotkey; tray.ContextMenuStrip = BuildTrayMenu(); }
                    if (!hotkeyOk) Toast(S.hotkey + " is taken by another app", null, null);
                    break;
                case "opacity":
                    Opacity = Math.Max(0.3, Math.Min(1, S.opacity));
                    break;
                case "uiScale":
                    if (ui != null && ui.CoreWebView2 != null) ui.ZoomFactor = S.uiScale;
                    if (tabsView != null && tabsView.CoreWebView2 != null) tabsView.ZoomFactor = S.uiScale;
                    break;
                case "preferDark":
                    ApplyColorScheme();
                    break;
                case "pinned":
                    SetPinned(S.pinned);
                    break;
                case "searchUrl": case "maxTabs":
                    SendInit();
                    break;
                case "theme":
                    ApplyTheme();
                    RefreshInjection();
                    break;
                case "tabsPosition":
                    ApplyTabsPosition();
                    SendInit();
                    break;
                case "sendTo": case "browserPath": case "aiProvider":
                    exeCache.Clear();
                    SendInit();
                    RefreshInjection();
                    break;
                case "accent": case "accent2": case "tint": case "grain":
                    ApplyTheme(); // frame, border, strip, open pages and their look-up cards
                    RefreshInjection();
                    break;
                case "language":
                    Lang.Load(S.language);
                    if (tray != null) tray.ContextMenuStrip = BuildTrayMenu();
                    SendInit(); // the strip saves the new language and reloads itself
                    RefreshInjection();
                    ReloadOwnPagesSoon();
                    break;
                case "translateTo": case "autoTranslate": case "neverTranslate":
                    PushTabs();
                    break;
                case "quickBar": case "quickHotkey":
                    RegisterQuickHotkey();
                    if (S.quickBar && !quickKeyOk) Toast(S.quickHotkey + " is taken by another app", null, null);
                    if (!S.quickBar) DropQuick();
                    SendInit();
                    break;
                case "quickPosition": case "currency":
                    SendInit();
                    break;
                case "translateMode":
                    PushTabs();
                    break;
                case "blocker":
                    SyncUblock();
                    RefreshInjection();
                    break;
                case "spareTab":
                    if (S.spareTab) PrepareSpareSoon(); else DropSpare();
                    break;
                case "adblock":
                    if (S.blocker == "ublock" || Ublock.Loaded) SyncUblock();
                    RefreshInjection();
                    PushTabs();
                    break;
                case "lookup": case "lookupOnDoubleClick": case "middleClickGlance": case "youtubeAds":
                case "gestures": case "gestureLeft": case "gestureRight": case "gestureUp": case "gestureDown":
                case "gestureUpDown": case "gestureDownRight": case "gestureDownLeft":
                    RefreshInjection();
                    PushTabs();
                    break;
                case "ghostHotkey":
                    RegisterGhostHotkey(Visible);
                    if (trayGhost != null) trayGhost.Text = "Ghost mode  (" + S.ghostHotkey + ")";
                    if (Visible && !ghostKeyOk) Toast(S.ghostHotkey + " is taken by another app", null, null);
                    SendInit();
                    break;
                case "ghostOpacity":
                    if (ghost) Opacity = Math.Max(0.2, Math.Min(0.9, S.ghostOpacity));
                    break;
                case "amnesia":
                    SaveSessionSoon();
                    break;
                case "raven":
                    if (S.raven) { Log.Info(RavenLog("on")); StartRavenIfOn(); } else StopRaven();
                    RefreshRavenUi();
                    break;
                case "callsign": case "avatar":
                    ProfileChangedSoon();
                    RefreshRavenUi();
                    break;
                case "ravenTabs": case "ravenHome": case "ravenName": case "quickReplies":
                    RefreshRavenUi();
                    break;
            }
        }

        /// <summary>After the strip has saved the new language, reload the start page and settings tabs so they redraw in it.</summary>
        async void ReloadOwnPagesSoon()
        {
            await Task.Delay(700);
            foreach (var t in tabs.Where(t => (t.Url ?? "").StartsWith(UiOrigin, StringComparison.OrdinalIgnoreCase) && t.View != null && t.View.CoreWebView2 != null))
                try { t.View.CoreWebView2.Reload(); } catch (Exception) { }
        }

        void ApplyColorScheme()
        {
            try
            {
                if (ui != null && ui.CoreWebView2 != null)
                    ui.CoreWebView2.Profile.PreferredColorScheme = S.preferDark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Auto;
            }
            catch (Exception ex) { Log.Error("Color scheme", ex); }
        }

        /// <summary>settings.json was edited outside Sidearm: take the file's values instead of overwriting them.</summary>
        void ReloadSettingsFromDisk()
        {
            if ((DateTime.UtcNow - Settings.LastSelfSave).TotalSeconds < 2) return; // our own save
            try
            {
                var fresh = Settings.Load(S.FilePath);
                S.CopyFrom(fresh, false);
                ApplySetting(null);
                SendInit();
                RefreshInjection();
                PushSettingsSnapshot();
                Toast("Settings file changed: applied", null, null);
                Log.Info("settings.json changed on disk, reloaded");
            }
            catch (Exception ex) { Log.Error("Reload settings", ex); }
        }
    }
}
