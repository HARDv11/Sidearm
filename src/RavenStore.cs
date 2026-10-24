using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sidearm
{
    /// <summary>
    /// 0.9: Raven's files in data\raven: your identity (the keys), your friends, and the last 200 messages with each.
    /// Like the rest of data\ (cookies, sign-ins), never share these files: whoever has identity.json can write as you.
    /// </summary>
    internal sealed class RavenStore
    {
        public sealed class Friend
        {
            public string key, dh;           // their public keys (base64url)
            public string name = "";         // the call sign they sent
            public string alias = "";        // what you call them (wins over name)
            public int av;                   // their avatar, 0-4
            public bool pinned;
            public string status = "friend"; // friend · waiting (you added them, their hello hasn't come) · request (they asked)
            public string wordsEn = "", wordsUk = "";
            public long added, lastAt, reqAt;
            public int unread;
            public string Shown { get { return alias.Length > 0 ? alias : name.Length > 0 ? name : "?"; } }
        }

        public sealed class Msg
        {
            public string id, text = "", url = "", title = "";
            public bool mine;
            public long at;
            public string state = "";        // mine: sending · sent · delivered · failed
        }

        public const int KeepPerFriend = 200, MaxFriends = 64;

        readonly string dir;
        public Dictionary<string, object> Identity;   // what raven-core.js made: { v, sign (JWK), dh (JWK), key, dhPub }
        public readonly List<Friend> Friends = new List<Friend>();
        readonly Dictionary<string, List<Msg>> history = new Dictionary<string, List<Msg>>();
        bool loaded;

        public RavenStore(string dataDir) { dir = Path.Combine(dataDir, "raven"); }

        string IdPath { get { return Path.Combine(dir, "identity.json"); } }
        string FriendsPath { get { return Path.Combine(dir, "friends.json"); } }
        string HistoryPath { get { return Path.Combine(dir, "history.json"); } }

        public void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (File.Exists(IdPath))
                {
                    var d = Json.Parse(File.ReadAllText(IdPath));
                    if (d != null && Json.Str(d, "key") != null) Identity = d;
                }
            }
            catch (Exception ex) { Log.Error("Raven: identity.json", ex); }
            try
            {
                if (File.Exists(FriendsPath))
                {
                    var arr = Json.ParseAny(File.ReadAllText(FriendsPath)) as object[];
                    if (arr != null)
                        foreach (var o in arr)
                        {
                            var d = o as Dictionary<string, object>;
                            var f = FriendFrom(d);
                            if (f != null && Find(f.key) == null) Friends.Add(f);
                        }
                }
            }
            catch (Exception ex) { Log.Error("Raven: friends.json", ex); }
            try
            {
                if (File.Exists(HistoryPath))
                {
                    var d = Json.Parse(File.ReadAllText(HistoryPath));
                    if (d != null)
                        foreach (var kv in d)
                        {
                            var arr = kv.Value as System.Collections.ArrayList;
                            var list = new List<Msg>();
                            if (arr != null)
                                foreach (var o in arr)
                                {
                                    var m = o as Dictionary<string, object>;
                                    if (m == null || Json.Str(m, "id") == null) continue;
                                    list.Add(new Msg
                                    {
                                        id = Json.Str(m, "id"), text = Json.Str(m, "text") ?? "", url = Json.Str(m, "url") ?? "", title = Json.Str(m, "title") ?? "",
                                        mine = Json.Bool(m, "mine"), at = (long)Json.Num(m, "at", 0), state = Json.Str(m, "state") ?? ""
                                    });
                                }
                            history[kv.Key] = list;
                        }
                }
            }
            catch (Exception ex) { Log.Error("Raven: history.json", ex); }
        }

        static Friend FriendFrom(Dictionary<string, object> d)
        {
            if (d == null) return null;
            string key = Json.Str(d, "key"), dh = Json.Str(d, "dh");
            if (!IsKey(key) || !IsKey(dh)) return null;
            var f = new Friend
            {
                key = key, dh = dh, name = Settings.CleanCallsign(Json.Str(d, "name")), alias = Settings.CleanCallsign(Json.Str(d, "alias")),
                av = Math.Max(0, Math.Min(4, (int)Json.Num(d, "av", 0))), pinned = Json.Bool(d, "pinned"),
                status = Json.Str(d, "status") ?? "friend", wordsEn = Json.Str(d, "wordsEn") ?? "", wordsUk = Json.Str(d, "wordsUk") ?? "",
                added = (long)Json.Num(d, "added", 0), lastAt = (long)Json.Num(d, "lastAt", 0), reqAt = (long)Json.Num(d, "reqAt", 0),
                unread = Math.Max(0, (int)Json.Num(d, "unread", 0))
            };
            if (f.status != "waiting" && f.status != "request") f.status = "friend";
            return f;
        }

        public static bool IsKey(string s) { return s != null && s.Length == 87 && s.All(c => char.IsLetterOrDigit(c) && c < 128 || c == '-' || c == '_'); }

        public Friend Find(string key) { return key == null ? null : Friends.FirstOrDefault(f => f.key == key); }

        /// <summary>Who may write to your inbox: friends, and those you added who haven't said hello yet.</summary>
        public List<object> Allowed()
        {
            return Friends.Where(f => f.status != "request").Select(f => (object)new { key = f.key, dh = f.dh }).ToList();
        }

        public IList<Msg> History(string key)
        {
            List<Msg> list;
            return history.TryGetValue(key, out list) ? list : (IList<Msg>)new Msg[0];
        }

        public Msg Add(string key, Msg m)
        {
            List<Msg> list;
            if (!history.TryGetValue(key, out list)) history[key] = list = new List<Msg>();
            if (list.Any(x => x.id == m.id)) return null; // already have it
            list.Add(m);
            if (list.Count > KeepPerFriend) list.RemoveRange(0, list.Count - KeepPerFriend);
            var f = Find(key);
            if (f != null) f.lastAt = Math.Max(f.lastAt, m.at);
            return m;
        }

        public Msg FindMsg(string key, string id) { return History(key).FirstOrDefault(m => m.id == id); }

        public void Forget(string key)
        {
            Friends.RemoveAll(f => f.key == key);
            history.Remove(key);
        }

        public void SaveIdentity(Dictionary<string, object> id)
        {
            Identity = id;
            Write(IdPath, Json.Pretty(Json.Serialize(id)));
        }

        public void SaveFriends() { Write(FriendsPath, Json.Pretty(Json.Serialize(Friends))); }

        public void SaveHistory()
        {
            var d = new Dictionary<string, object>();
            foreach (var kv in history) if (Find(kv.Key) != null) d[kv.Key] = kv.Value;
            Write(HistoryPath, Json.Serialize(d));
        }

        /// <summary>Raven turned off for good (Settings → Raven → Delete): keys, friends and history go.</summary>
        public void Wipe()
        {
            Identity = null;
            Friends.Clear();
            history.Clear();
            foreach (var p in new[] { IdPath, FriendsPath, HistoryPath }) try { if (File.Exists(p)) File.Delete(p); } catch (Exception) { }
        }

        void Write(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text, new System.Text.UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }
            catch (Exception ex) { Log.Error("Raven: write " + Path.GetFileName(path), ex); }
        }
    }
}
