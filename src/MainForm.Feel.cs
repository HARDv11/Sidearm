using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Sidearm
{
    /// <summary>Milestone 4: ghost mode, sound, gestures, clean links, quick copy, amnesia.</summary>
    internal sealed partial class MainForm
    {
        // ───────────────────────── ghost mode ─────────────────────────
        // See-through and click-through: a map or guide stays up while your clicks go to the game.

        const int GhostHotkeyId = 0x5A1E;
        bool ghost, ghostKeyOk;
        IntPtr gameWindow; // what had focus before Sidearm was drawn, so ghost mode can hand focus back
        ToolStripMenuItem trayGhost;

        /// <summary>The ghost key is only taken while Sidearm is on screen, so the game keeps Alt+G otherwise.</summary>
        void RegisterGhostHotkey(bool on)
        {
            if (!IsHandleCreated) return;
            Native.UnregisterHotKey(Handle, GhostHotkeyId);
            ghostKeyOk = false;
            if (!on) return;
            uint mods, vk;
            if (!Native.ParseHotkey(S.ghostHotkey, out mods, out vk)) { Log.Info("Could not parse ghost hotkey " + S.ghostHotkey); return; }
            ghostKeyOk = Native.RegisterHotKey(Handle, GhostHotkeyId, mods | Native.MOD_NOREPEAT, vk);
            if (!ghostKeyOk) Log.Info("Ghost hotkey " + S.ghostHotkey + " is taken by another app");
        }

        void RememberGameWindow()
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == Handle) return;
                uint pid;
                Native.GetWindowThreadProcessId(fg, out pid);
                if ((int)pid == ownPid || WebViewPids().Contains((int)pid)) return;
                gameWindow = fg;
            }
            catch (Exception) { }
        }

        void SetGhost(bool on) { SetGhost(on, true); }

        /// <summary>0.9: pop-outs follow Sidearm into ghost mode and out of it (<paramref name="floats"/>), except on a holster.</summary>
        void SetGhost(bool on, bool floats)
        {
            if (on == ghost) { if (floats) SetFloatsGhost(on); return; }
            if (on && (!Visible || fullscreen)) return;
            try
            {
                deactivateTimer.Stop();
                if (on)
                {
                    if (!ghostKeyOk) RegisterGhostHotkey(true);
                    CancelLookup();
                    PostToUi(new { type = "holstered" }); // closes the palette and the page panel
                    Opacity = Math.Max(0.2, Math.Min(0.9, S.ghostOpacity)); // makes the window layered first
                    ghost = true;
                    UpdateStyles();                                         // then adds click-through
                    if (gameWindow != IntPtr.Zero) Native.SetForegroundWindow(gameWindow);
                }
                else
                {
                    ghost = false;
                    Opacity = Math.Max(0.3, Math.Min(1, S.opacity));
                    UpdateStyles();
                }
                if (floats) SetFloatsGhost(ghost);
                if (ghost && ravenWin != null) ravenWin.Hide();
                UpdateRavenTabs(); // the side tabs step aside while you're a ghost
                if (trayGhost != null) trayGhost.Checked = ghost;
                PostToUi(new { type = "ghost", on = ghost, key = S.ghostHotkey });
                Log.Info(ghost ? "Ghost mode on" : "Ghost mode off");
            }
            catch (Exception ex) { Log.Error("Ghost mode", ex); }
        }

        async void ToggleGhost()
        {
            if (!Visible && AnyFloatShown()) { ToggleFloatsGhost(); return; } // holstered: just the pop-outs
            if (ghost) { SetGhost(false); Draw(); return; }
            if (!Visible)
            {
                // from the tray menu: draw first, then go see-through once the window is up
                Draw();
                await Task.Delay(500);
            }
            SetGhost(true);
        }

        // ───────────────────────── sound ─────────────────────────

        void HookAudio(Tab tab, CoreWebView2 core)
        {
            if (!tab.MuteKnown) { tab.Muted = S.muteByDefault; tab.MuteKnown = true; }
            try
            {
                core.IsMuted = tab.Muted;
                core.IsDocumentPlayingAudioChanged += (s, e) =>
                {
                    tab.Audible = core.IsDocumentPlayingAudio;
                    if (tab.Audible && tab.Muted && !tab.MuteHinted && (tab == active || tab == glance))
                    {
                        tab.MuteHinted = true;
                        Toast("This page is playing sound, muted", "Unmute", "unmute");
                    }
                    PushTabs();
                };
            }
            catch (Exception ex) { Log.Error("Audio hooks", ex); }
        }

        void SetMuted(Tab tab, bool muted)
        {
            if (tab == null) return;
            tab.Muted = muted;
            tab.MuteKnown = true;
            try { if (tab.View != null && tab.View.CoreWebView2 != null) tab.View.CoreWebView2.IsMuted = muted; }
            catch (Exception ex) { Log.Error("Mute", ex); }
            PushTabs();
        }

        void ToggleMute()
        {
            var tab = glance ?? active;
            if (tab == null) return;
            SetMuted(tab, !tab.Muted);
            Toast(tab.Muted ? "Muted this tab" : "Sound on for this tab", null, null);
        }

        void OnMuteMessage(System.Collections.Generic.Dictionary<string, object> m)
        {
            int id = (int)Json.Num(m, "id", -1);
            var tab = tabs.FirstOrDefault(t => t.Id == id) ?? (glance != null && glance.Id == id ? glance : null);
            if (tab == null) return;
            bool muted = m.ContainsKey("on") ? Json.Bool(m, "on") : !tab.Muted;
            SetMuted(tab, muted);
        }

        // ───────────────────────── gestures ─────────────────────────

        DateTime lastGesture = DateTime.MinValue;

        /// <summary>A page reports a finished stroke; only these actions exist, and only the tab in front can ask.</summary>
        void OnGesture(Tab tab, string a)
        {
            if (!S.gestures || string.IsNullOrEmpty(a)) return;
            if ((DateTime.UtcNow - lastGesture).TotalMilliseconds < 250) return;
            lastGesture = DateTime.UtcNow;
            bool inGlance = tab == glance;
            var core = tab.View != null ? tab.View.CoreWebView2 : null;
            try
            {
                switch (a)
                {
                    case "back":
                        if (core != null && core.CanGoBack) core.GoBack();
                        else if (inGlance) CloseGlance();
                        break;
                    case "forward": if (core != null && core.CanGoForward) core.GoForward(); break;
                    case "reload": if (core != null) core.Reload(); break;
                    case "close": if (glance != null) CloseGlance(); else Holster(); break;
                    case "closeTab": if (inGlance) CloseGlance(); else CloseTab(tab, true); break;
                    case "newTab": NewTabAndFocus(); break;
                    case "reopen": ReopenClosed(); break;
                    case "home": GoHome(); break;
                    case "zen":
                    case "zenPrivate":
                        if ((DateTime.UtcNow - lastPageAction).TotalSeconds < 1.5) return; // a page can't spam Zen windows
                        lastPageAction = DateTime.UtcNow;
                        SendToBrowser(tab.IsNewTab ? null : tab.Url, a == "zenPrivate");
                        break;
                    // 0.9.5: the new ones, for strokes and shapes
                    case "popPage": if (!inGlance) PopOutPage(tab); break;
                    case "shot": StartShot(inGlance ? tab : active); break;
                    case "draw": OpenDrawing(null); break;
                    case "clips": ShowClips(); break;
                    case "send": if (!tab.IsNewTab && IsWebUrl(tab.Url)) SendToFriend(tab.Url, tab.Title); break;
                    case "raven": if (S.raven) ToggleRavenList(); else Toast(RavenTitle + " is off. Turn it on in Settings", "Settings", "settings"); break;
                    case "ghost": ToggleGhost(); break;
                    case "mute": ToggleMute(); break;
                    case "reader": if (!inGlance) ToggleReader(tab); break;
                    case "translate": if (!inGlance) ToggleTranslate(tab); break;
                }
            }
            catch (Exception ex) { Log.Error("Gesture " + a, ex); }
        }

        // ───────────────────────── clean links, quick copy ─────────────────────────

        string ForClipboard(string url) { return S.cleanLinks ? CleanLinks.Clean(url) : url; }

        void CopyLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            string clean = ForClipboard(url);
            try { Clipboard.SetText(clean); } catch (Exception) { return; }
            Toast(clean.Length < url.Length ? "Copied link, tracking removed" : "Copied link", null, null);
        }

        /// <summary>Ctrl+Shift+C: the selection as plain text; nothing selected copies the page's clean link.</summary>
        async void QuickCopy()
        {
            var tab = glance ?? active;
            if (tab == null || tab.View == null || tab.View.CoreWebView2 == null) return;
            try
            {
                string raw = await tab.View.CoreWebView2.ExecuteScriptAsync("(function(){var s=window.getSelection();return s?s.toString():''})()");
                string text = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<string>(raw) ?? "";
                if (text.Trim().Length == 0)
                {
                    if (!tab.IsNewTab && IsWebUrl(tab.Url)) CopyLink(tab.Url);
                    return;
                }
                Clipboard.SetText(text.Trim());
                Toast("Copied as plain text", null, null);
            }
            catch (Exception ex) { Log.Error("Quick copy", ex); }
        }

        // ───────────────────────── amnesia ─────────────────────────

        async void AmnesiaWipe()
        {
            try
            {
                closed.Clear();
                Lookup.ClearCache();
                PostToUi(new { type = "amnesia" });
                var core = ui != null ? ui.CoreWebView2 : null;
                if (core != null)
                {
                    var kinds = CoreWebView2BrowsingDataKinds.AllSite | CoreWebView2BrowsingDataKinds.BrowsingHistory
                        | CoreWebView2BrowsingDataKinds.DownloadHistory | CoreWebView2BrowsingDataKinds.GeneralAutofill
                        | CoreWebView2BrowsingDataKinds.PasswordAutosave;
                    await core.Profile.ClearBrowsingDataAsync(kinds);
                }
                SendInit(); // our own start page keeps its settings in site storage too; put them back
                SaveSession();
                Log.Info("Amnesia: history, cookies, site data and recent searches wiped");
            }
            catch (Exception ex) { Log.Error("Amnesia", ex); }
        }
    }
}
