using System; using System.Reflection; using System.Collections.Generic; using System.Threading.Tasks; using System.IO;
class T { 
  static int fails = 0;
  static void Check(string name, bool ok, object got) { Console.WriteLine((ok ? "ok   " : "FAIL ") + name + "  " + got); if (!ok) fails++; }
  static void Main() {
  var asm = Assembly.LoadFrom("Sidearm.exe");
  var BF = BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
  var prog = asm.GetType("Sidearm.Program");
  prog.GetField("UiDir", BF).SetValue(null, Environment.GetEnvironmentVariable("SIDEARM_UI"));
  prog.GetField("AppDir", BF).SetValue(null, Path.GetFullPath("."));
  Directory.CreateDirectory(Path.GetFullPath("data"));
  prog.GetField("DataDir", BF).SetValue(null, Path.GetFullPath("data"));
  // ── language ──
  var lang = asm.GetType("Sidearm.Lang");
  lang.GetMethod("Load").Invoke(null, new object[]{"uk"});
  var Tr = lang.GetMethod("T");
  foreach (var s in new[]{"Timer set: 5:00 boss","Time's up: Your 5:00 timer","Your 1:30 timer","Zen wouldn't start, so it opened in your default browser. Reinstalling it usually fixes this","On: uBlock Origin Lite 2026.1012.1545","Pop out video  (Alt+P)","Opacity 70%","Alt+Space is taken by another app"})
  { var r = (string)Tr.Invoke(null, new object[]{s}); Check("T " + s, r != s, r); }
  // ── settings ──
  var st = asm.GetType("Sidearm.Settings");
  File.WriteAllText(Path.GetFullPath("data/settings.json"), "{\"blocker\":\"nope\",\"quickPosition\":\"weird\",\"currency\":\"usd\",\"translateMode\":\"x\",\"quickHotkey\":\"\"}");
  var S = st.GetMethod("Load").Invoke(null, new object[]{Path.GetFullPath("data/settings.json")});
  Func<string, object> P = n => st.GetProperty(n).GetValue(S, null);
  Check("blocker fallback", (string)P("blocker") == "sidearm", P("blocker"));
  Check("quickPosition fallback", (string)P("quickPosition") == "mouse", P("quickPosition"));
  Check("currency fallback", (string)P("currency") == "UAH", P("currency"));
  Check("translateMode fallback", (string)P("translateMode") == "proxy", P("translateMode"));
  Check("quickHotkey fallback", (string)P("quickHotkey") == "Alt+Space", P("quickHotkey"));
  Check("quickBar default on", (bool)P("quickBar"), P("quickBar"));
  var set = st.GetMethod("Set");
  Check("Set blocker ublock", (bool)set.Invoke(S, new object[]{"blocker","ublock"}) && (string)P("blocker") == "ublock", P("blocker"));
  Check("Set blocker bad refused", !(bool)set.Invoke(S, new object[]{"blocker","adguard"}), P("blocker"));
  Check("Set currency EUR", (bool)set.Invoke(S, new object[]{"currency","EUR"}), P("currency"));
  Check("Set currency bad refused", !(bool)set.Invoke(S, new object[]{"currency","eur!"}), P("currency"));
  Check("Set empty hotkey refused", !(bool)set.Invoke(S, new object[]{"quickHotkey"," "}), P("quickHotkey"));
  // ── video embeds ──
  var mf = asm.GetType("Sidearm.MainForm");
  var emb = mf.GetMethod("VideoEmbedUrl", BF);
  foreach (var c in new[]{
    new[]{"https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=x","pop.html?yt=dQw4w9WgXcQ&t=42"},
    new[]{"https://youtu.be/dQw4w9WgXcQ?t=90","pop.html?yt=dQw4w9WgXcQ&t=42"},
    new[]{"https://m.youtube.com/shorts/dQw4w9WgXcQ","pop.html?yt=dQw4w9WgXcQ&t=42"},
    new[]{"https://www.twitch.tv/videos/123456","pop.html?twv=123456&t=42"},
    new[]{"https://www.twitch.tv/Shroud","pop.html?twc=shroud"},
    new[]{"https://www.twitch.tv/directory","null"},
    new[]{"https://arc-raiders.fandom.com/wiki/Arc_Raiders_Wiki","null"}}) {
    var r = (string)emb.Invoke(null, new object[]{c[0], 42.7});
    Check("embed " + c[0], (r ?? "null").EndsWith(c[1]), r ?? "null");
  }
  Check("youtu.be t= used when time is 0", ((string)emb.Invoke(null, new object[]{"https://youtu.be/dQw4w9WgXcQ?t=90", 0.0})).EndsWith("&t=90"), emb.Invoke(null, new object[]{"https://youtu.be/dQw4w9WgXcQ?t=90", 0.0}));
  // ── batch translation with a fake Google ──
  var lk = asm.GetType("Sidearm.Lookup");
  var fetchField = lk.GetField("Fetch", BF);
  var calls = new List<string>();
  fetchField.SetValue(null, (Func<string, Task<string>>)(url => { calls.Add(url);
    var q = Uri.UnescapeDataString(url.Substring(url.IndexOf("&q=") + 3));
    var lines = q.Split('\n');
    var sb = new System.Text.StringBuilder("[[");
    for (int i = 0; i < lines.Length; i++) sb.Append((i > 0 ? "," : "") + "[\"" + lines[i].ToUpper() + (i < lines.Length - 1 ? "\\n" : "") + "\",\"" + lines[i] + "\",null,null,1]");
    sb.Append("],null,\"en\"]");
    return Task.FromResult(sb.ToString()); }));
  var tb = lk.GetMethod("TranslateBatch", BF);
  var res = ((Task<List<string>>)tb.Invoke(null, new object[]{ new List<string>{"Hello world","This is a","bold","  spaced\n text "}, "uk"})).Result;
  Check("batch one request", calls.Count == 1, calls.Count);
  Check("batch result", string.Join("|", res) == "HELLO WORLD|THIS IS A|BOLD|SPACED TEXT", string.Join("|", res));
  // a reply that merges lines falls back to one by one
  calls.Clear();
  fetchField.SetValue(null, (Func<string, Task<string>>)(url => { calls.Add(url);
    return Task.FromResult(url.Contains("%0A") ? "[[[\"MERGED ALL\",\"x\",null,null,1]],null,\"en\"]" : "[[[\"ONE\",\"x\",null,null,1]],null,\"en\"]"); }));
  res = ((Task<List<string>>)tb.Invoke(null, new object[]{ new List<string>{"a","b","c"}, "uk"})).Result;
  Check("fallback one by one", res.Count == 3 && res[0] == "ONE" && calls.Count == 4, calls.Count + " calls, " + string.Join("|", res));
  fetchField.SetValue(null, (Func<string, Task<string>>)(url => Task.FromResult<string>(null)));
  res = ((Task<List<string>>)tb.Invoke(null, new object[]{ new List<string>{"a"}, "uk"})).Result;
  Check("offline gives null", res == null, res == null ? "null" : "list");
  // ── rates ──
  var rt = asm.GetType("Sidearm.Rates");
  rt.GetField("Fetch", BF).SetValue(null, (Func<string, string>)(url => url.Contains("bank.gov.ua")
     ? "[{\"r030\":36,\"txt\":\"x\",\"rate\":27.3,\"cc\":\"AUD\",\"exchangedate\":\"16.10.2026\"},{\"r030\":840,\"txt\":\"y\",\"rate\":41.47,\"cc\":\"USD\",\"exchangedate\":\"16.10.2026\"},{\"r030\":978,\"rate\":48.3,\"cc\":\"EUR\",\"exchangedate\":\"16.10.2026\"},{\"cc\":\"PLN\",\"rate\":11.3},{\"cc\":\"GBP\",\"rate\":55},{\"cc\":\"CZK\",\"rate\":1.9},{\"cc\":\"JPY\",\"rate\":0.27},{\"cc\":\"CAD\",\"rate\":30},{\"cc\":\"CHF\",\"rate\":51},{\"cc\":\"SEK\",\"rate\":4}]" : null));
  bool got = (bool)rt.GetMethod("FromNbu", BF).Invoke(null, null);
  var uah = (Dictionary<string,double>)rt.GetField("Uah", BF).GetValue(null);
  Check("NBU parse", got && uah["USD"] == 41.47 && (string)rt.GetField("Date", BF).GetValue(null) == "16.10.2026", got + " " + (uah == null ? "" : uah.Count.ToString()));
  rt.GetField("Fetch", BF).SetValue(null, (Func<string, string>)(url => "{\"result\":\"success\",\"rates\":{\"UAH\":1,\"USD\":0.0241,\"EUR\":0.0207}}"));
  got = (bool)rt.GetMethod("FromErApi", BF).Invoke(null, null);
  uah = (Dictionary<string,double>)rt.GetField("Uah", BF).GetValue(null);
  Check("ER-API parse", got && Math.Abs(uah["USD"] - 41.49) < 0.01, got + " " + (uah == null ? "" : uah["USD"].ToString()));
  // ── chime ──
  var wav = (byte[])asm.GetType("Sidearm.Chime").GetMethod("Make", BF).Invoke(null, null);
  Check("chime wav", wav.Length > 40000 && System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && BitConverter.ToInt32(wav, 4) == wav.Length - 8, wav.Length);
  File.WriteAllBytes(Path.GetFullPath("chime.wav"), wav);
  // ── clock ──
  var clock = mf.GetMethod("Clock", BF);
  Check("clock", (string)clock.Invoke(null, new object[]{300}) == "5:00" && (string)clock.Invoke(null, new object[]{5400}) == "1:30:00" && (string)clock.Invoke(null, new object[]{45}) == "0:45", clock.Invoke(null, new object[]{5400}));
  Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
}}
