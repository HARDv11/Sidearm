// 0.9: Raven's files (RavenStore), the settings it adds, and the SVG path reader behind the wing icon.
using System; using System.Reflection; using System.Collections; using System.Collections.Generic; using System.IO; using System.Linq;
class T {
  static int fails = 0;
  static void Check(string name, bool ok, object got) { Console.WriteLine((ok ? "ok   " : "FAIL ") + name + "  " + got); if (!ok) fails++; }
  static void Main() {
    var asm = Assembly.LoadFrom("Sidearm.exe");
    var BF = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var log = asm.GetType("Sidearm.Log");
    log.GetMethod("Init", BF).Invoke(null, new object[] { Path.GetFullPath("raven-test.log") });
    string dir = Path.GetFullPath("raven-data");
    if (Directory.Exists(dir)) Directory.Delete(dir, true);
    Directory.CreateDirectory(dir);

    var storeT = asm.GetType("Sidearm.RavenStore");
    var friendT = asm.GetType("Sidearm.RavenStore+Friend");
    var msgT = asm.GetType("Sidearm.RavenStore+Msg");
    Func<object> newStore = () => Activator.CreateInstance(storeT, BF, null, new object[] { dir }, null);
    Func<char, string> K = c => new string(c, 87);

    var s = newStore();
    storeT.GetMethod("Load").Invoke(s, null);
    var friends = (IList)storeT.GetField("Friends").GetValue(s);
    Func<string, string, object> friend = (key, status) => { var f = Activator.CreateInstance(friendT); friendT.GetField("key").SetValue(f, key); friendT.GetField("dh").SetValue(f, K('d')); friendT.GetField("status").SetValue(f, status); friendT.GetField("name").SetValue(f, "Оля"); return f; };
    friends.Add(friend(K('a'), "friend"));
    friends.Add(friend(K('b'), "request"));
    friends.Add(friend(K('c'), "waiting"));
    var allowed = (IList)storeT.GetMethod("Allowed").Invoke(s, null);
    Check("who may write: friends and the ones you added, not open requests", allowed.Count == 2, allowed.Count);

    var add = storeT.GetMethod("Add");
    for (int i = 0; i < 230; i++)
    {
      var m = Activator.CreateInstance(msgT);
      msgT.GetField("id").SetValue(m, "id" + i);
      msgT.GetField("text").SetValue(m, "повідомлення " + i);
      msgT.GetField("at").SetValue(m, 1000L + i);
      add.Invoke(s, new object[] { K('a'), m });
    }
    var dup = Activator.CreateInstance(msgT); msgT.GetField("id").SetValue(dup, "id229");
    Check("the same message twice is kept once", add.Invoke(s, new object[] { K('a'), dup }) == null, "");
    var hist = (IList)storeT.GetMethod("History").Invoke(s, new object[] { K('a') });
    Check("only the last 200 per friend", hist.Count == 200 && (string)msgT.GetField("id").GetValue(hist[0]) == "id30", hist.Count);
    Check("lastAt follows the newest message", (long)friendT.GetField("lastAt").GetValue(friends[0]) == 1229, friendT.GetField("lastAt").GetValue(friends[0]));

    var id = new Dictionary<string, object> { { "v", 1 }, { "key", K('z') }, { "dhPub", K('y') }, { "sign", new Dictionary<string, object> { { "kty", "EC" } } } };
    storeT.GetMethod("SaveIdentity").Invoke(s, new object[] { id });
    storeT.GetMethod("SaveFriends").Invoke(s, null);
    storeT.GetMethod("SaveHistory").Invoke(s, null);
    Check("files in data\\raven", File.Exists(Path.Combine(dir, "raven", "identity.json")) && File.Exists(Path.Combine(dir, "raven", "friends.json")) && File.Exists(Path.Combine(dir, "raven", "history.json")), "");

    var s2 = newStore();
    storeT.GetMethod("Load").Invoke(s2, null);
    var f2 = (IList)storeT.GetField("Friends").GetValue(s2);
    var id2 = (Dictionary<string, object>)storeT.GetField("Identity").GetValue(s2);
    var h2 = (IList)storeT.GetMethod("History").Invoke(s2, new object[] { K('a') });
    Check("friends come back", f2.Count == 3 && (string)friendT.GetField("status").GetValue(f2[1]) == "request" && (string)friendT.GetField("name").GetValue(f2[0]) == "Оля", f2.Count);
    Check("the identity comes back", id2 != null && (string)id2["key"] == K('z'), id2 == null ? "null" : id2["key"]);
    Check("history comes back (Cyrillic intact)", h2.Count == 200 && (string)msgT.GetField("text").GetValue(h2[199]) == "повідомлення 229", h2.Count);

    File.WriteAllText(Path.Combine(dir, "raven", "friends.json"), "[{\"key\":\"short\",\"dh\":\"x\"},{\"key\":\"" + K('q') + "\",\"dh\":\"" + K('r') + "\",\"status\":\"boss\",\"av\":9,\"name\":\"" + new string('Ж', 40) + "\"}]");
    var s3 = newStore(); storeT.GetMethod("Load").Invoke(s3, null);
    var f3 = (IList)storeT.GetField("Friends").GetValue(s3);
    Check("bad entries are dropped, odd values cleaned", f3.Count == 1 && (string)friendT.GetField("status").GetValue(f3[0]) == "friend" && (int)friendT.GetField("av").GetValue(f3[0]) == 4
      && ((string)friendT.GetField("name").GetValue(f3[0])).Length == 24, f3.Count);

    storeT.GetMethod("Wipe").Invoke(s2, null);
    Check("delete removes the files", !File.Exists(Path.Combine(dir, "raven", "identity.json")) && !File.Exists(Path.Combine(dir, "raven", "history.json")), "");

    // settings
    var st = asm.GetType("Sidearm.Settings");
    var cs = st.GetMethod("CleanCallsign");
    Check("call sign: one line, 24 characters", (string)cs.Invoke(null, new object[] { "  Лицар\n\tз   мечем " + new string('x', 40) }) == "Лицар з мечем xxxxxxxxxx", cs.Invoke(null, new object[] { "  Лицар\n\tз   мечем " + new string('x', 40) }));
    var cr = st.GetMethod("CleanReplies");
    Check("quick replies: up to 5, no empties or repeats", (string)cr.Invoke(null, new object[] { "Так| Ні |  | Так|5 хв|a|b|c" }) == "Так|Ні|5 хв|a|b", cr.Invoke(null, new object[] { "Так| Ні |  | Так|5 хв|a|b|c" }));
    File.WriteAllText(Path.GetFullPath("rs.json"), "{\"ravenName\":\"owl\",\"avatar\":12,\"ravenHome\":\"x\",\"callsign\":null}");
    var S = st.GetMethod("Load").Invoke(null, new object[] { Path.GetFullPath("rs.json") });
    Func<string, object> P = n => st.GetProperty(n).GetValue(S, null);
    Check("raven is off by default, odd values fall back", !(bool)P("raven") && (string)P("ravenName") == "raven" && (int)P("avatar") == 4 && (string)P("ravenHome") == "pinned" && (string)P("callsign") == "", P("ravenName"));
    var set = st.GetMethod("Set");
    Check("Set ravenName courier", (bool)set.Invoke(S, new object[] { "ravenName", "courier" }), P("ravenName"));
    Check("Set ravenName bad refused", !(bool)set.Invoke(S, new object[] { "ravenName", "owl" }), P("ravenName"));
    Check("Set popsHide", (bool)set.Invoke(S, new object[] { "popsHide", true }) && (bool)P("popsHide"), P("popsHide"));

    // the wing's path
    var rail = asm.GetType("Sidearm.GlanceRail");
    var path = (System.Drawing.Drawing2D.GraphicsPath)rail.GetMethod("ParsePath", BF).Invoke(null, new object[] { "M19.9767 50.9765C30.1062 51.7595 43.0724 46.3271 48.0343 26.689L0 0Z" });
    var b = path.GetBounds();
    Check("SVG path: the wing parses", path.PointCount > 4 && b.Right > 47 && b.Bottom > 50, b);

    Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
    Environment.Exit(fails == 0 ? 0 : 1);
  }
}
