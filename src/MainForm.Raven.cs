using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sidearm
{
    /// <summary>
    /// 0.9: Raven (Ворон), messages between friends' Sidearms. The engine (keys, encryption, the connection to the post
    /// office) is ui\raven-core.js running in the strip, which stays awake while Raven is on. The shell keeps the files
    /// (RavenStore) and the windows: the side tabs (RavenTabs), the chat (RavenWindow, ui\raven.html), the start
    /// page's block and the bar's @name.
    /// </summary>
    internal sealed partial class MainForm
    {
        const string DefaultPostOffice = "https://sidearm-post.hard11.workers.dev";
        RavenStore raven;
        bool ravenStarted, ravenOnline, ravenOnlineLogged;
        int ravenReq;
        readonly Dictionary<int, TaskCompletionSource<Dictionary<string, object>>> ravenCalls = new Dictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        readonly Timer ravenSave = new Timer { Interval = 1500 };
        readonly Timer ravenHello = new Timer { Interval = 4000 };
        bool historyDirty, friendsDirty;

        bool RavenReady { get { return S.raven && ravenStarted && raven != null && raven.Identity != null; } }

        static readonly Dictionary<string, string[]> RavenTitles = new Dictionary<string, string[]>
        {
            // what the feature is called, in English and in Ukrainian (Settings → Raven → Name)
            { "raven", new[] { "Raven", "Ворон" } }, { "courier", new[] { "Courier", "Гонець" } }, { "radio", new[] { "Radio", "Рація" } },
            { "pigeon", new[] { "Pigeon", "Голуб" } }, { "dispatch", new[] { "Dispatch", "Депеша" } }
        };
        string RavenTitle { get { string[] t; return RavenTitles.TryGetValue(S.ravenName ?? "raven", out t) ? t[Lang.Code == "uk" ? 1 : 0] : "Raven"; } }

        string[] QuickReplies
        {
            get
            {
                var mine = (S.quickReplies ?? "").Split('|').Where(x => x.Length > 0).ToArray();
                if (mine.Length > 0) return mine;
                return Lang.Code == "uk" ? new[] { "Так", "Ні", "5 хв" } : new[] { "Yes", "No", "5 min" };
            }
        }

        void InitRaven()
        {
            raven = new RavenStore(Program.DataDir);
            ravenSave.Tick += (s, e) => { ravenSave.Stop(); FlushRaven(); };
            ravenHello.Tick += (s, e) => { ravenHello.Stop(); SayHelloToAll(); };
        }

        void FlushRaven()
        {
            if (raven == null) return;
            if (friendsDirty) { friendsDirty = false; raven.SaveFriends(); }
            if (historyDirty) { historyDirty = false; raven.SaveHistory(); }
        }

        void RavenChanged(bool friends, bool history)
        {
            friendsDirty |= friends; historyDirty |= history;
            ravenSave.Stop(); ravenSave.Start();
            RefreshRavenUi();
        }

        // ───────────── the engine in the strip ─────────────

        /// <summary>On the strip's "ready", and when Raven is switched on.</summary>
        void StartRavenIfOn()
        {
            if (!S.raven || raven == null) return;
            raven.Load();
            ravenStarted = true;
            PostToUi(new
            {
                type = "raven", op = "start", server = PostOffice,
                identity = raven.Identity, friends = raven.Allowed()
            });
            RefreshRavenUi();
        }

        string PostOffice
        {
            get
            {
                string s = (S.ravenServer ?? "").Trim().TrimEnd('/');
                return s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ? s : DefaultPostOffice;
            }
        }

        void StopRaven()
        {
            if (!ravenStarted) return;
            ravenStarted = false;
            ravenOnline = false;
            PostToUi(new { type = "raven", op = "stop" });
            FlushRaven();
            HideRavenWindows();
            Log.Info(RavenLog("off"));
        }

        static string RavenLog(string what) { return "Raven: " + what; }

        /// <summary>A question for the engine, answered with op "result" and the same req.</summary>
        async Task<Dictionary<string, object>> RavenCall(string op, Dictionary<string, object> fields, int timeoutMs = 15000)
        {
            if (!ravenStarted) throw new InvalidOperationException("Raven is off");
            int req = ++ravenReq;
            var tcs = new TaskCompletionSource<Dictionary<string, object>>();
            ravenCalls[req] = tcs;
            var m = fields ?? new Dictionary<string, object>();
            m["type"] = "raven"; m["op"] = op; m["req"] = req;
            PostToUi(m);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            ravenCalls.Remove(req);
            if (done != tcs.Task) throw new TimeoutException(op + " took too long");
            var r = tcs.Task.Result;
            if (!Json.Bool(r, "ok")) throw new Exception(Json.Str(r, "error") ?? "failed");
            object v; r.TryGetValue("value", out v);
            return v as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        /// <summary>Everything the engine reports.</summary>
        void OnRavenEvent(Dictionary<string, object> m)
        {
            string op = Json.Str(m, "op");
            switch (op)
            {
                case "result":
                    int req = (int)Json.Num(m, "req", 0);
                    TaskCompletionSource<Dictionary<string, object>> tcs;
                    if (ravenCalls.TryGetValue(req, out tcs)) tcs.TrySetResult(m);
                    break;
                case "identity":
                    object id; m.TryGetValue("identity", out id);
                    var d = id as Dictionary<string, object>;
                    if (d != null && RavenStore.IsKey(Json.Str(d, "key")))
                    {
                        raven.SaveIdentity(d);
                        Log.Info(RavenLog("made this Sidearm's keys (data\\raven\\identity.json)"));
                        PushSettingsSnapshot();
                    }
                    break;
                case "online":
                    bool on = Json.Bool(m, "on");
                    if (on != ravenOnline)
                    {
                        ravenOnline = on;
                        if (on && !ravenOnlineLogged) { ravenOnlineLogged = true; Log.Info(RavenLog("connected to the post office")); }
                        else if (!on) Log.Info(RavenLog("connection closed (" + Json.Num(m, "code", 0) + "), will reconnect"));
                        RefreshRavenUi();
                        PushSettingsSnapshot();
                    }
                    if (on) PushAllowedSoon();
                    break;
                case "incoming": OnRavenIncoming(m); break;
                case "request": OnRavenRequest(m); break;
                case "log": Log.Info(RavenLog(Json.Str(m, "text") ?? "")); break;
            }
        }

        bool allowedPushed;
        /// <summary>Once a session (and after every change), the post office learns who may write.</summary>
        async void PushAllowedSoon()
        {
            if (allowedPushed) return;
            allowedPushed = true;
            try { await RavenCall("friends", new Dictionary<string, object> { { "friends", raven.Allowed() }, { "push", true } }); }
            catch (Exception ex) { allowedPushed = false; Log.Info(RavenLog("couldn't update the friend list on the post office: " + ex.Message)); }
        }

        async Task PushAllowed()
        {
            allowedPushed = true;
            await RavenCall("friends", new Dictionary<string, object> { { "friends", raven.Allowed() }, { "push", true } });
        }

        void OnRavenIncoming(Dictionary<string, object> m)
        {
            string from = Json.Str(m, "from");
            var f = raven.Find(from);
            if (f == null || f.status == "request") return;
            object bo; m.TryGetValue("body", out bo);
            var b = bo as Dictionary<string, object> ?? new Dictionary<string, object>();
            string kind = Json.Str(b, "t") ?? "msg";
            string name = Settings.CleanCallsign(Json.Str(b, "name"));
            if (name.Length > 0) f.name = name;
            f.av = Math.Max(0, Math.Min(4, (int)Json.Num(b, "av", f.av)));
            bool wasWaiting = f.status == "waiting";
            if (f.status == "waiting") f.status = "friend";
            if (wasWaiting)
            {
                Log.Info(RavenLog("a friend accepted"));
                Toast(f.Shown + " is your friend now", null, null);
            }
            if (kind == "msg")
            {
                var msg = raven.Add(from, new RavenStore.Msg
                {
                    id = Json.Str(m, "id"), at = (long)Json.Num(m, "at", NowMs()), mine = false,
                    text = Json.Str(b, "text") ?? "", url = Json.Str(b, "url") ?? "", title = Json.Str(b, "title") ?? ""
                });
                if (msg != null)
                {
                    bool looking = ravenWin != null && ravenWin.Visible && ravenWin.Mode == "chat" && ravenWin.Key == from && ravenWin.ContainsFocus;
                    if (!looking) f.unread++;
                    RavenChanged(true, true);
                    if (!looking) OnRavenNewMessage(f, msg);
                    return;
                }
            }
            RavenChanged(true, false);
        }

        void OnRavenRequest(Dictionary<string, object> m)
        {
            string from = Json.Str(m, "from"), dh = Json.Str(m, "dh");
            if (!RavenStore.IsKey(from) || !RavenStore.IsKey(dh)) return;
            var f = raven.Find(from);
            if (f == null)
            {
                if (raven.Friends.Count >= RavenStore.MaxFriends) { Log.Info(RavenLog("a friend request came, but the list is full (64)")); return; }
                f = new RavenStore.Friend { key = from, dh = dh, status = "request", added = NowMs() };
                raven.Friends.Add(f);
            }
            else if (f.status == "friend" && f.dh == dh) return; // already friends: an old request, nothing to do
            else if (f.status == "waiting")
            {
                // you added them with their code, and they used yours: that's both sides, you're friends
                f.dh = dh;
                f.status = "friend";
                RavenChanged(true, false);
                SayHelloTo(f.key);
                return;
            }
            f.dh = dh;
            if (f.status != "friend") f.status = "request";
            f.name = Settings.CleanCallsign(Json.Str(m, "name"));
            f.av = Math.Max(0, Math.Min(4, (int)Json.Num(m, "av", 0)));
            f.reqAt = (long)Json.Num(m, "at", NowMs());
            object wo; m.TryGetValue("words", out wo);
            var w = wo as Dictionary<string, object>;
            if (w != null) { f.wordsEn = JoinWords(w, "en"); f.wordsUk = JoinWords(w, "uk"); }
            Log.Info(RavenLog("a friend request came in"));
            RavenChanged(true, false);
            if (Visible) Toast(RavenTitle + ": " + (f.name.Length > 0 ? f.name : "someone") + " wants to be friends", "Show", "ravenRequests");
        }

        static string JoinWords(Dictionary<string, object> w, string lang)
        {
            object o; w.TryGetValue(lang, out o);
            var arr = o as System.Collections.IEnumerable;
            return arr == null ? "" : string.Join(" · ", arr.Cast<object>().Select(x => Convert.ToString(x)));
        }

        static long NowMs() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

        /// <summary>A new message while you weren't looking at that chat: a side tab, and a quick-reply card if Sidearm is up.</summary>
        void OnRavenNewMessage(RavenStore.Friend f, RavenStore.Msg msg)
        {
            if (!Visible || ghost) return; // it waits in the side tabs and on the start page until you draw
            if (ravenWin != null && ravenWin.Visible && ravenWin.Mode != "peek") return; // the chat is open: its list shows it
            ShowRavenPeek(f.key);
        }

        // ───────────── what you do ─────────────

        object Profile { get { return new { name = S.callsign ?? "", av = S.avatar }; } }

        async Task<RavenStore.Msg> RavenSend(string key, string text, string url, string title)
        {
            var f = raven.Find(key);
            if (f == null || f.status == "request") throw new InvalidOperationException("not a friend");
            text = (text ?? "").Trim();
            if (text.Length > 2000) text = text.Substring(0, 2000);
            if (!IsWebUrl(url)) url = ""; else if (IsTranslated(url)) url = OriginalOf(url);
            title = Trim(title ?? "", 200);
            if (text.Length == 0 && url.Length == 0) return null;
            var msg = raven.Add(key, new RavenStore.Msg { id = NewMsgId(), mine = true, at = NowMs(), text = text, url = url, title = url.Length > 0 ? title : "", state = "sending" });
            f.unread = 0;
            RavenChanged(true, true);
            try
            {
                var r = await RavenCall("send", new Dictionary<string, object>
                {
                    { "to", key }, { "id", msg.id },
                    { "body", new Dictionary<string, object> { { "t", "msg" }, { "text", text }, { "url", url }, { "title", msg.title }, { "name", S.callsign ?? "" }, { "av", S.avatar } } }
                });
                msg.state = Json.Bool(r, "delivered") ? "delivered" : "sent";
            }
            catch (Exception ex)
            {
                msg.state = "failed";
                string why = ex.Message;
                Log.Info(RavenLog("send failed: " + why));
                Toast(why == "slow down" ? "Too many messages at once. Wait a minute" : why == "not a friend" ? f.Shown + " hasn't added you back yet" : "Couldn't send: " + why, null, null);
            }
            RavenChanged(false, true);
            return msg;
        }

        static string NewMsgId()
        {
            var b = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Your call sign or avatar changed: friends hear about it with a hello (a few seconds later, once).</summary>
        void ProfileChangedSoon() { if (RavenReady) { ravenHello.Stop(); ravenHello.Start(); } }

        async void SayHelloTo(string key)
        {
            try
            {
                await PushAllowed();
                await RavenCall("send", new Dictionary<string, object> { { "to", key }, { "body", new Dictionary<string, object> { { "t", "hello" }, { "name", S.callsign ?? "" }, { "av", S.avatar } } } });
            }
            catch (Exception ex) { Log.Info(RavenLog("hello failed: " + ex.Message)); }
        }

        async void SayHelloToAll()
        {
            if (!RavenReady) return;
            foreach (var f in raven.Friends.Where(x => x.status == "friend").ToList())
                try { await RavenCall("send", new Dictionary<string, object> { { "to", f.key }, { "body", new Dictionary<string, object> { { "t", "hello" }, { "name", S.callsign ?? "" }, { "av", S.avatar } } } }); }
                catch (Exception ex) { Log.Info(RavenLog("hello to a friend failed: " + ex.Message)); }
        }

        async Task<Dictionary<string, object>> RavenInvite()
        {
            var r = await RavenCall("invite", null);
            Log.Info(RavenLog("made an invite code"));
            return r;
        }

        /// <summary>A code from a friend: become friends on your side, and ask them.</summary>
        async Task RavenAddByCode(string code, string alias)
        {
            var c = await RavenCall("readCode", new Dictionary<string, object> { { "code", code } });
            string key = Json.Str(c, "key"), dh = Json.Str(c, "dh");
            var f = raven.Find(key);
            if (f != null && f.status == "friend") throw new Exception("already a friend");
            if (f == null)
            {
                if (raven.Friends.Count >= RavenStore.MaxFriends) throw new Exception("64 friends is the limit");
                f = new RavenStore.Friend { key = key, added = NowMs() };
                raven.Friends.Add(f);
            }
            f.dh = dh;
            f.status = f.status == "request" ? "friend" : "waiting"; // they asked first and you had their code: done
            f.alias = Settings.CleanCallsign(alias);
            object wo; c.TryGetValue("words", out wo);
            var w = wo as Dictionary<string, object>;
            if (w != null) { f.wordsEn = JoinWords(w, "en"); f.wordsUk = JoinWords(w, "uk"); }
            RavenChanged(true, false);
            await PushAllowed();
            await RavenCall("request", new Dictionary<string, object> { { "code", code }, { "profile", Profile } });
            Log.Info(RavenLog("sent a friend request"));
        }

        async Task RavenAccept(string key, string alias)
        {
            var f = raven.Find(key);
            if (f == null) return;
            f.status = "friend";
            f.alias = Settings.CleanCallsign(alias);
            RavenChanged(true, false);
            await PushAllowed();
            await RavenCall("send", new Dictionary<string, object> { { "to", key }, { "body", new Dictionary<string, object> { { "t", "hello" }, { "name", S.callsign ?? "" }, { "av", S.avatar } } } });
            Log.Info(RavenLog("accepted a friend request"));
        }

        async Task RavenRemove(string key)
        {
            raven.Forget(key);
            RavenChanged(true, true);
            try { await PushAllowed(); } catch (Exception ex) { Log.Info(RavenLog("couldn't update the friend list: " + ex.Message)); }
        }

        /// <summary>Raven switched off with "delete": the inbox on the post office and the files here go.</summary>
        async void RavenDeleteEverything()
        {
            try { if (ravenStarted) await RavenCall("delete", null); }
            catch (Exception ex) { Log.Info(RavenLog("couldn't delete the inbox: " + ex.Message)); }
            StopRaven();
            raven.Wipe();
            S.raven = false;
            SaveSettingsSoon();
            Log.Info(RavenLog("keys, friends and messages deleted"));
            RefreshRavenUi();
            PushSettingsSnapshot();
        }

        // ───────────── views for the pages ─────────────

        static string Preview(RavenStore.Msg m)
        {
            if (m == null) return "";
            string t = (m.text ?? "").Replace('\n', ' ').Trim();
            if (t.Length == 0 && m.url.Length > 0) t = m.title.Length > 0 ? m.title : HostOf(m.url);
            return Trim(t, 120);
        }

        List<object> RavenFriendsView()
        {
            return raven.Friends.OrderByDescending(f => f.status == "request").ThenByDescending(f => f.unread > 0).ThenByDescending(f => f.lastAt)
                .Select(f =>
                {
                    var h = raven.History(f.key);
                    var last = h.Count > 0 ? h[h.Count - 1] : null;
                    return (object)new
                    {
                        key = f.key, name = f.Shown, callsign = f.name, alias = f.alias, av = f.av, pinned = f.pinned, status = f.status,
                        unread = f.unread, lastAt = f.lastAt, last = Preview(last), lastMine = last != null && last.mine,
                        wordsEn = f.wordsEn, wordsUk = f.wordsUk
                    };
                }).ToList();
        }

        List<object> RavenChatView(string key)
        {
            return raven.History(key).Select(m => (object)new { id = m.id, mine = m.mine, text = m.text, url = m.url, title = m.title, at = m.at, state = m.state }).ToList();
        }

        /// <summary>The start page's block: unread from pinned friends first; nobody pinned has written → everyone.</summary>
        object RavenHomeView()
        {
            if (!S.raven || raven == null || S.ravenHome == "off") return new { on = false };
            var unread = raven.Friends.Where(f => f.unread > 0 && f.status == "friend").ToList();
            var pinned = unread.Where(f => f.pinned).ToList();
            var show = S.ravenHome == "pinned" && pinned.Count > 0 ? pinned : unread;
            var requests = raven.Friends.Count(f => f.status == "request");
            return new
            {
                on = true, title = RavenTitle, requests = requests, total = unread.Sum(f => f.unread),
                list = show.OrderByDescending(f => f.lastAt).Take(5).Select(f =>
                {
                    var h = raven.History(f.key);
                    var last = h.LastOrDefault(x => !x.mine);
                    return (object)new { key = f.key, name = f.Shown, av = f.av, unread = f.unread, at = last != null ? last.at : f.lastAt, text = Preview(last), url = last != null ? last.url : "" };
                }).ToList()
            };
        }

        /// <summary>Friends for the bar's @name (names only).</summary>
        object RavenBarView()
        {
            if (!RavenReady) return new object[0];
            return raven.Friends.Where(f => f.status == "friend").OrderByDescending(f => f.pinned).ThenByDescending(f => f.lastAt)
                .Select(f => (object)new { key = f.key, name = f.Shown, av = f.av }).ToArray();
        }

        void RefreshRavenUi()
        {
            if (raven == null) return;
            UpdateRavenTabs();
            if (ravenWin != null && ravenWin.Ready) ravenWin.Post(RavenStateMessage());
            if (ravenWin != null && ravenWin.Ready && ravenWin.Key != null) ravenWin.Post(new { t = "chat", key = ravenWin.Key, msgs = RavenChatView(ravenWin.Key) });
            var home = RavenHomeView();
            foreach (var t in tabs) if (t.View != null && t.IsNewTab) PostToContent(t, new { t = "raven-home", raven = home });
            var bar = RavenBarView();
            PostToUi(new { type = "raven-friends", list = bar });
            if (tray != null)
            {
                int unread = S.raven ? raven.Friends.Where(f => f.status == "friend").Sum(f => f.unread) : 0;
                string tip = "Sidearm · " + S.hotkey + (unread > 0 ? " · " + RavenTitle + ": " + unread : "");
                try { tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip; } catch (Exception) { }
            }
            PostToQuick(new { type = "raven-friends", list = bar });
        }

        object RavenStateMessage()
        {
            return new
            {
                t = "state", on = S.raven, online = ravenOnline, ready = RavenReady, title = RavenTitle,
                me = new { name = S.callsign ?? "", av = S.avatar, key = raven.Identity != null ? Json.Str(raven.Identity, "key") : "" },
                friends = RavenFriendsView(), replies = QuickReplies, lang = Lang.Code,
                page = active != null && !active.IsNewTab && IsWebUrl(active.Url) ? (object)new { url = IsTranslated(active.Url) ? OriginalOf(active.Url) : active.Url, title = active.Title ?? "" } : null
            };
        }

        // ───────────── entry points from the rest of Sidearm ─────────────

        /// <summary>Right-click → Send to a friend, Glance's wing, the shield panel: one friend gets it at once, more pick.</summary>
        void SendToFriend(string url, string title)
        {
            if (!S.raven) { Toast(RavenTitle + " is off. Turn it on in Settings", "Settings", "settings"); return; }
            if (!IsWebUrl(url)) return;
            var friends = raven.Friends.Where(f => f.status == "friend").ToList();
            if (friends.Count == 0) { OpenRaven("add", null, null); return; }
            OpenRaven("pick", null, new { url = IsTranslated(url) ? OriginalOf(url) : url, title = title ?? "" });
        }

        /// <summary>The bar's "@name text": with the page (Enter) or text only (Shift+Enter).</summary>
        async void RavenFromBar(string key, string text, bool withPage)
        {
            try
            {
                string url = "", title = "";
                if (withPage && active != null && !active.IsNewTab && IsWebUrl(active.Url)) { url = active.Url; title = active.Title; }
                var f = raven.Find(key);
                var msg = await RavenSend(key, text, url, title);
                if (msg != null && msg.state != "failed" && f != null) Toast("Sent to " + f.Shown, null, null);
            }
            catch (Exception ex) { Log.Error("Raven: bar", ex); }
        }
    }
}
