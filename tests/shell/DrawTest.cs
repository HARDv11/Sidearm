// 0.9.5: drawings kept with the notes (DrawStore), pictures in Raven's store, the new settings (shapes, your own
// gestures, the joystick, folders) and the gesture defaults moving to new tab / start page.
using System; using System.Reflection; using System.Collections; using System.Collections.Generic; using System.IO; using System.Linq;
class D {
  static int fails = 0;
  static void Check(string name, bool ok, object got) { Console.WriteLine((ok ? "ok   " : "FAIL ") + name + "  " + got); if (!ok) fails++; }
  static void Main() {
    var asm = Assembly.LoadFrom("Sidearm.exe");
    var BF = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    asm.GetType("Sidearm.Log").GetMethod("Init", BF).Invoke(null, new object[] { Path.GetFullPath("draw-test.log") });
    string root = Path.GetFullPath("draw-data");
    if (Directory.Exists(root)) Directory.Delete(root, true);
    Directory.CreateDirectory(root);

    // ── DrawStore ──
    var ds = asm.GetType("Sidearm.DrawStore");
    ds.GetProperty("Dir", BF).SetValue(null, Path.Combine(root, "drawings"), null);
    byte[] png = { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 0 }, jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2 };
    string id = (string)ds.GetMethod("NewId", BF).Invoke(null, null);
    Check("a new id looks like one", (bool)ds.GetMethod("IsId", BF).Invoke(null, new object[] { id }), id);
    Check("ids can't climb out of the folder", !(bool)ds.GetMethod("IsId", BF).Invoke(null, new object[] { "..\\..\\x" }) && !(bool)ds.GetMethod("IsId", BF).Invoke(null, new object[] { "ABC" }), "");
    var save = ds.GetMethod("Save", BF);
    save.Invoke(null, new object[] { id, "  Бос\ntut  ", "{\"v\":1,\"items\":[]}", png, png, jpg, 1600, 1000 });
    var list = (IList)ds.GetMethod("List", BF).Invoke(null, null);
    var eT = asm.GetType("Sidearm.DrawStore+Entry");
    Check("kept and listed", list.Count == 1 && (string)eT.GetProperty("title").GetValue(list[0], null) == "Бос tut" && (bool)eT.GetProperty("shot").GetValue(list[0], null), list.Count);
    Check("files on disk", File.Exists(Path.Combine(root, "drawings", id + ".json")) && File.Exists(Path.Combine(root, "drawings", id + ".png")) && File.Exists(Path.Combine(root, "drawings", id + ".bg.png")) && File.Exists(Path.Combine(root, "drawings", id + ".jpg")), "");
    byte[] png2 = (byte[])png.Clone(); png2[11] = 7;
    save.Invoke(null, new object[] { id, "Бос", "{\"v\":1,\"items\":[1]}", png, png2, null, 1600, 1000 });
    Check("the screenshot under the ink is written once", File.ReadAllBytes(Path.Combine(root, "drawings", id + ".bg.png"))[11] == 0, "");
    var loaded = (Dictionary<string, object>)ds.GetMethod("Load", BF).Invoke(null, new object[] { id });
    Check("opens again with its document and screenshot", (string)loaded["doc"] == "{\"v\":1,\"items\":[1]}" && ((string)loaded["bg"]).StartsWith("data:image/png;base64,"), loaded["title"]);
    bool refused = false;
    try { save.Invoke(null, new object[] { id, "x", "{}", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, null, null, 1, 1 }); } catch (TargetInvocationException) { refused = true; }
    Check("anything but a PNG is refused", refused, "");
    Check("FromDataUrl takes only the types asked for", ds.GetMethod("FromDataUrl", BF).Invoke(null, new object[] { "data:image/png;base64,iVBORw==", new[] { "image/webp" } }) == null
      && ds.GetMethod("FromDataUrl", BF).Invoke(null, new object[] { "data:image/png;base64,iVBORw==", new[] { "image/png" } }) != null, "");
    Check("deleting takes every file", (bool)ds.GetMethod("Delete", BF).Invoke(null, new object[] { id }) && Directory.GetFiles(Path.Combine(root, "drawings"), id + "*").Length == 0 && ((IList)ds.GetMethod("List", BF).Invoke(null, null)).Count == 0, "");

    // ── Raven's pictures ──
    var storeT = asm.GetType("Sidearm.RavenStore");
    var msgT = asm.GetType("Sidearm.RavenStore+Msg");
    var friendT = asm.GetType("Sidearm.RavenStore+Friend");
    var s = Activator.CreateInstance(storeT, BF, null, new object[] { root }, null);
    storeT.GetMethod("Load").Invoke(s, null);
    string K = new string('a', 87);
    var f = Activator.CreateInstance(friendT); friendT.GetField("key").SetValue(f, K); friendT.GetField("dh").SetValue(f, new string('d', 87));
    ((IList)storeT.GetField("Friends").GetValue(s)).Add(f);
    byte[] webp = { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P', 1, 2, 3 };
    Check("a WebP is known by its bytes", (string)storeT.GetMethod("PicExt").Invoke(null, new object[] { webp }) == "webp" && storeT.GetMethod("PicExt").Invoke(null, new object[] { new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 } }) == null, "");
    string mid = "AbCdEfGhIjKlMnOpQrSt";
    string name = (string)storeT.GetMethod("SavePic").Invoke(s, new object[] { mid, webp });
    Check("a picture is kept as its own file", name == mid + ".webp" && File.Exists(Path.Combine(root, "raven", "pics", name)), name);
    Check("an id with a path in it isn't a file name", storeT.GetMethod("SavePic").Invoke(s, new object[] { "..\\..\\evil-name-here-1234", webp }) == null, "");
    var m = Activator.CreateInstance(msgT); msgT.GetField("id").SetValue(m, mid); msgT.GetField("pic").SetValue(m, name); msgT.GetField("w").SetValue(m, 800); msgT.GetField("at").SetValue(m, 5L);
    storeT.GetMethod("Add").Invoke(s, new object[] { K, m });
    storeT.GetMethod("SaveFriends").Invoke(s, null); storeT.GetMethod("SaveHistory").Invoke(s, null);
    var s2 = Activator.CreateInstance(storeT, BF, null, new object[] { root }, null);
    storeT.GetMethod("Load").Invoke(s2, null);
    var h = (IList)storeT.GetMethod("History").Invoke(s2, new object[] { K });
    Check("the history keeps the picture's name and size", h.Count == 1 && (string)msgT.GetField("pic").GetValue(h[0]) == name && (int)msgT.GetField("w").GetValue(h[0]) == 800, h.Count);
    Check("and gives it back as a data URL", ((string)storeT.GetMethod("PicDataUrl").Invoke(s2, new object[] { name })).StartsWith("data:image/webp;base64,"), "");
    storeT.GetMethod("Forget").Invoke(s2, new object[] { K });
    Check("forgetting a friend takes their pictures too", !File.Exists(Path.Combine(root, "raven", "pics", name)), "");

    // ── settings ──
    var st = asm.GetType("Sidearm.Settings");
    var clean = st.GetMethod("CleanMyGestures", BF);
    var pts = string.Join(",", Enumerable.Range(0, 32).Select(i => "[" + (i * 0.01).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + (-i * 0.02).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]"));
    string good = (string)clean.Invoke(null, new object[] { "[{\"name\":\"Блискавка\",\"action\":\"raven\",\"pts\":[" + pts + "]},{\"name\":\"bad\",\"action\":\"format c:\",\"pts\":[" + pts + "]},{\"name\":\"few\",\"action\":\"shot\",\"pts\":[[0,0],[1,1]]}]" });
    Check("your own gestures: a good one kept, a bad action and too few points dropped", good.Contains("Блискавка") && !good.Contains("bad") && !good.Contains("few"), good.Length);
    Check("nonsense becomes nothing", (string)clean.Invoke(null, new object[] { "{not json" }) == "", "");
    string file = Path.Combine(root, "settings.json");
    File.WriteAllText(file, "{\"defaultsVersion\":6,\"gestureDownRight\":\"zen\",\"gestureDownLeft\":\"zenPrivate\",\"gestureLeft\":\"back\",\"joystickKey\":\"Ctrl+Alt+Del\",\"selectColor\":\"red\"}");
    var S = st.GetMethod("Load", BF).Invoke(null, new object[] { file });
    Func<string, object> P = n => st.GetProperty(n).GetValue(S, null);
    Check("↓→ moves to the start page and ↓← to a new tab", (string)P("gestureDownRight") == "home" && (string)P("gestureDownLeft") == "newTab", P("gestureDownRight") + " / " + P("gestureDownLeft"));
    Check("shapes get their defaults", (string)P("gestureCircle") == "shot" && (string)P("gestureSquare") == "popPage" && (string)P("gestureZ") == "ghost", P("gestureCircle"));
    Check("a key that isn't on the list, and a color that isn't one, are dropped", (string)P("joystickKey") == "" && (string)P("selectColor") == "", P("joystickKey"));
    Check("the joystick is on, at speed 5", (bool)P("joystick") && (int)P("joystickSpeed") == 5, P("joystickSpeed"));
    File.WriteAllText(file, "{\"defaultsVersion\":6,\"gestureDownRight\":\"reopen\",\"gestureDownLeft\":\"off\"}");
    S = st.GetMethod("Load", BF).Invoke(null, new object[] { file });
    Check("your own choices for ↓→ / ↓← stay", (string)P("gestureDownRight") == "reopen" && (string)P("gestureDownLeft") == "off", P("gestureDownRight"));
    var map = (Dictionary<string, string>)st.GetMethod("GestureMap").Invoke(S, null);
    Check("the pages get the shapes too", map.ContainsKey("@circle") && map["@triangle"] == "reader" && !map.ContainsKey("DL"), map.Count);
    var set = st.GetMethod("Set");
    Check("a shape can do any gesture action", (bool)set.Invoke(S, new object[] { "gestureCheck", "joystick" }) && !(bool)set.Invoke(S, new object[] { "gestureCheck", "rm -rf" }), P("gestureCheck"));
    Check("joystick speed stays 1-10", (bool)set.Invoke(S, new object[] { "joystickSpeed", 42 }) && (int)P("joystickSpeed") == 10, P("joystickSpeed"));
    Check("joystick key from the list only", (bool)set.Invoke(S, new object[] { "joystickKey", "Alt+J" }) && !(bool)set.Invoke(S, new object[] { "joystickKey", "Win+R" }), P("joystickKey"));
    Check("the box's color must be a color", (bool)set.Invoke(S, new object[] { "selectColor", "#33aa66" }) && !(bool)set.Invoke(S, new object[] { "selectColor", "javascript:" }), P("selectColor"));

    Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
  }
}
