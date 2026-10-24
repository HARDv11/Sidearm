using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sidearm
{
    /// <summary>0.9: Raven's windows: the side tabs outside Sidearm's edge and the chat window beside them.</summary>
    internal sealed partial class MainForm
    {
        RavenTabs ravenTabs;
        RavenWindow ravenWin;
        Task ravenWinInit;
        readonly Timer ravenSleep = new Timer { Interval = 30000 };
        bool ravenSleepHooked;

        bool RavenSideRight
        {
            get
            {
                var wa = Screen.FromHandle(Handle).WorkingArea;
                double s = Native.Scale(Handle);
                return Left - wa.Left < RavenTabs.TabW(s) + ToPx(8); // against the left edge: the tabs go right
            }
        }

        void HideRavenWindows()
        {
            if (ravenTabs != null && ravenTabs.Visible) ravenTabs.Hide();
            if (ravenWin != null && ravenWin.Visible) ravenWin.Hide();
        }

        /// <summary>Who gets a side tab: whoever wrote and hasn't been read, the open chat, then the wing for everyone.</summary>
        void UpdateRavenTabs()
        {
            bool show = S.raven && S.ravenTabs && raven != null && Visible && !ghost && !fullscreen && WindowState == FormWindowState.Normal;
            if (!show) { if (ravenTabs != null && ravenTabs.Visible) ravenTabs.Hide(); return; }
            if (ravenTabs == null)
            {
                ravenTabs = new RavenTabs();
                ravenTabs.Picked += key => { if (key == "*") ToggleRavenList(); else OpenRaven("chat", key, null); };
                var h = ravenTabs.Handle;
            }
            string open = ravenWin != null && ravenWin.Visible && ravenWin.Mode == "chat" ? ravenWin.Key : null;
            var list = raven.Friends.Where(f => f.status == "friend" && (f.unread > 0 || f.key == open))
                .OrderByDescending(f => f.lastAt).Take(4)
                .Select(f => new RavenTabs.Item { Key = f.key, Avatar = f.av, Unread = f.unread, Open = f.key == open, Tip = f.Shown + (f.unread > 0 ? " · " + f.unread : "") }).ToList();
            int requests = raven.Friends.Count(f => f.status == "request");
            bool listOpen = ravenWin != null && ravenWin.Visible && ravenWin.Mode != "chat" && ravenWin.Mode != "peek";
            list.Add(new RavenTabs.Item { Key = "*", Unread = requests, Open = listOpen, Tip = Lang.T(RavenTitle + ": all chats") });
            ravenTabs.Back = IsLight ? Color.FromArgb(0xF6, 0xF5, 0xF1) : Color.FromArgb(0x17, 0x1B, 0x1D);
            ravenTabs.Line = IsLight ? Color.FromArgb(0xC4, 0xBD, 0xAE) : Color.FromArgb(0x34, 0x3C, 0x3F);
            ravenTabs.Ink = IsLight ? Color.FromArgb(0x3B, 0x3B, 0x3B) : Color.FromArgb(0xC9, 0xCC, 0xC5);
            ravenTabs.Accent = AccentColor;
            ravenTabs.Badge = IsLight ? Color.FromArgb(0x4D, 0x67, 0x27) : Color.FromArgb(0x8A, 0x5A, 0x12);
            ravenTabs.SetItems(list);
            PositionRaven();
            if (!ravenTabs.Visible) ravenTabs.Show();
            ravenTabs.ShapeIt();
        }

        /// <summary>The tabs hug the window's edge below the strip; the chat window sits beyond them.</summary>
        void PositionRaven()
        {
            if (ravenTabs == null && ravenWin == null) return;
            double s = Native.Scale(Handle);
            bool right = RavenSideRight;
            var wa = Screen.FromHandle(Handle).WorkingArea;
            int tw = RavenTabs.TabW(s), ty = Top + ToPx(104);
            if (ravenTabs != null)
            {
                ravenTabs.RightSide = right;
                var want = ravenTabs.Want(s);
                var r = new Rectangle(right ? Right : Left - tw, ty, tw, Math.Max(1, want.Height));
                if (ravenTabs.Bounds != r) { ravenTabs.Bounds = r; ravenTabs.ShapeIt(); }
            }
            if (ravenWin != null && ravenWin.Visible) ravenWin.ShowAt(RavenWindowBounds(), false);
        }

        Rectangle RavenWindowBounds()
        {
            double s = Native.Scale(Handle);
            bool right = RavenSideRight;
            var wa = Screen.FromHandle(Handle).WorkingArea;
            int tw = S.ravenTabs ? RavenTabs.TabW(s) : 0, gap = ToPx(12);
            int w = (int)(320 * s);
            int h = ravenWin != null && ravenWin.Mode == "peek" ? (int)(260 * s) : Math.Min((int)(560 * s), Math.Max((int)(380 * s), Height - ToPx(60)));
            int x = right ? Right + tw + gap : Left - tw - gap - w;
            if (x < wa.Left) x = wa.Left + ToPx(4);
            if (x + w > wa.Right) x = wa.Right - w - ToPx(4);
            int y = Top + ToPx(84);
            if (y + h > wa.Bottom) y = Math.Max(wa.Top, wa.Bottom - h);
            return new Rectangle(x, y, w, h);
        }

        async Task EnsureRavenWindow()
        {
            if (ravenWin != null && !ravenWin.IsDisposed) { if (ravenWinInit != null) await ravenWinInit; return; }
            await EnsureInitialized();
            if (env == null) return;
            ravenWin = new RavenWindow(PageColor);
            ravenWin.Message += OnRavenWindowMessage;
            ravenWin.VisibleChanged += (s, e) =>
            {
                if (ravenWin == null) return;
                if (!ravenWin.Visible) { ravenSleep.Stop(); ravenSleep.Start(); }
                UpdateRavenTabs();
            };
            if (!ravenSleepHooked)
            {
                ravenSleepHooked = true;
                ravenSleep.Tick += async (s, e) => { ravenSleep.Stop(); if (ravenWin != null && !ravenWin.Visible) await TrySuspend(ravenWin.View); };
            }
            var h = ravenWin.Handle;
            Native.StyleFrame(ravenWin.Handle, ToBgr(FrameLine), !IsLight);
            ravenWinInit = ravenWin.Init(env, S.uiScale);
            await ravenWinInit;
            for (int i = 0; i < 40 && !ravenWin.Ready; i++) await Task.Delay(50);
        }

        void DropRavenWindow()
        {
            if (ravenWin == null || ravenWin.Visible) return;
            try { ravenWin.Message -= OnRavenWindowMessage; ravenWin.Dispose(); } catch (Exception) { }
            ravenWin = null; ravenWinInit = null;
        }

        void ToggleRavenList()
        {
            if (ravenWin != null && ravenWin.Visible && ravenWin.Mode != "chat" && ravenWin.Mode != "peek") { ravenWin.Hide(); return; }
            OpenRaven(raven.Friends.Count == 0 ? "add" : "list", null, null);
        }

        /// <summary>Opens Raven's window in a mode: chat (key), list, add, review (key), pick (page), peek (key).</summary>
        async void OpenRaven(string mode, string key, object page)
        {
            try
            {
                if (!S.raven) { Toast(RavenTitle + " is off. Turn it on in Settings", "Settings", "settings"); return; }
                if (!Visible) await DrawAsync();
                await EnsureRavenWindow();
                if (ravenWin == null) return;
                ravenSleep.Stop();
                Resume(ravenWin.View);
                bool peek = mode == "peek";
                ravenWin.Mode = mode;
                ravenWin.Key = key;
                ravenWin.Post(RavenStateMessage());
                if (key != null) ravenWin.Post(new { t = "chat", key = key, msgs = RavenChatView(key) });
                ravenWin.Post(new { t = "show", mode = mode, key = key, page = page });
                if (mode == "chat" && key != null) MarkRavenRead(key);
                ravenWin.ShowAt(RavenWindowBounds(), !peek);
                SetViewVisible(ravenWin.View, true);
                UpdateRavenTabs();
            }
            catch (Exception ex) { Log.Error("Raven window", ex); }
        }

        void ShowRavenPeek(string key) { OpenRaven("peek", key, null); }

        void MarkRavenRead(string key)
        {
            var f = raven.Find(key);
            if (f == null || f.unread == 0) return;
            f.unread = 0;
            RavenChanged(true, false);
        }

        /// <summary>What Raven's window asks for. It's Sidearm's own page (checked by RavenWindow), so it may do all of this.</summary>
        async void OnRavenWindowMessage(Dictionary<string, object> m)
        {
            string t = Json.Str(m, "t"), key = Json.Str(m, "key");
            try
            {
                switch (t)
                {
                    case "ready":
                        ravenWin.Post(RavenStateMessage());
                        break;
                    case "close":
                        bool had = ravenWin.ContainsFocus;
                        ravenWin.Hide();
                        if (had && Visible && active != null && active.View != null) active.View.Focus(); // only if it had the focus: the card closing by itself takes nothing from you
                        break;
                    case "mode":
                        ravenWin.Mode = Json.Str(m, "mode") ?? "list";
                        ravenWin.Key = key;
                        if (ravenWin.Mode == "chat" && key != null) { ravenWin.Post(new { t = "chat", key = key, msgs = RavenChatView(key) }); MarkRavenRead(key); }
                        if (Json.Bool(m, "focus") && !ravenWin.ContainsFocus) { Native.ForceForeground(ravenWin.Handle); ravenWin.Activate(); }
                        ravenWin.ShowAt(RavenWindowBounds(), Json.Bool(m, "focus"));
                        UpdateRavenTabs();
                        break;
                    case "read":
                        if (key != null) MarkRavenRead(key);
                        break;
                    case "send":
                        if (!RavenReady) { ravenWin.Post(new { t = "error", text = Lang.T(RavenTitle + " isn't connected yet") }); break; }
                        await RavenSend(key, Json.Str(m, "text"), Json.Str(m, "url"), Json.Str(m, "title"));
                        if (ravenWin != null && ravenWin.Mode == "pick") { ravenWin.Hide(); Toast("Sent", null, null); }
                        break;
                    case "link":
                        string url = Json.Str(m, "url");
                        if (IsWebUrl(url)) { if (ravenWin.Mode == "peek") ravenWin.Hide(); Navigate(url, true); }
                        break;
                    case "copy":
                        try { Clipboard.SetText(Json.Str(m, "text") ?? ""); } catch (Exception) { }
                        break;
                    case "invite":
                        var inv = await RavenInvite();
                        ravenWin.Post(new { t = "invite", code = Json.Str(inv, "code"), expires = Json.Num(inv, "expires", 0) });
                        break;
                    case "readCode":
                        var c = await RavenCall("readCode", new Dictionary<string, object> { { "code", Json.Str(m, "code") ?? "" } });
                        var known = raven.Find(Json.Str(c, "key"));
                        ravenWin.Post(new { t = "code", ok = true, words = c.ContainsKey("words") ? c["words"] : null, known = known != null ? known.status : "" });
                        break;
                    case "add":
                        await RavenAddByCode(Json.Str(m, "code") ?? "", Json.Str(m, "alias"));
                        ravenWin.Post(new { t = "added" });
                        break;
                    case "accept":
                        await RavenAccept(key, Json.Str(m, "alias"));
                        ravenWin.Post(new { t = "accepted", key = key });
                        break;
                    case "reject":
                    case "remove":
                        await RavenRemove(key);
                        break;
                    case "rename":
                        var fr = raven.Find(key);
                        if (fr != null) { fr.alias = Settings.CleanCallsign(Json.Str(m, "alias")); RavenChanged(true, false); }
                        break;
                    case "pin":
                        var fp = raven.Find(key);
                        if (fp != null) { fp.pinned = Json.Bool(m, "on"); RavenChanged(true, false); }
                        break;
                    case "settings":
                        ravenWin.Hide();
                        OpenSettingsPage();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Info(RavenLog(t + " failed: " + ex.Message));
                if (ravenWin != null) ravenWin.Post(new { t = "error", op = t, text = ex.Message });
            }
        }
    }
}
